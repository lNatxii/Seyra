import { z } from 'zod';
import { panelSchema } from './panels.js';
import { placement } from './spatial.js';

type Tool = { name: string; schema: z.ZodType };
type Shared = { id: z.ZodType<string>; vector: z.ZodType; creation: { name: z.ZodString }; appearanceWithoutEntityId: z.ZodType };
export function batchTools(existing: readonly Tool[], shared: Shared) {
  const { id, vector, creation, appearanceWithoutEntityId } = shared;
  const schemaOf = (name: string) => existing.find(tool => tool.name === name)!.schema;
  const key = z.string().regex(/^[a-z][a-z0-9_]{0,47}$/);
  const transform = z.object({ position: vector, rotation: vector.default({ x: 0, y: 0, z: 0 }), scale: vector.default({ x: 1, y: 1, z: 1 }) }).strict();
  const relativeTo = z.object({ key: key.optional(), entityId: id.optional(), direction: z.enum(['above', 'below', 'right', 'left', 'front', 'behind']), gap: z.number().finite().min(0).max(10) }).strict().refine(r => (r.key === undefined) !== (r.entityId === undefined), 'Use key or entityId.');
  type Node = { key: string; children?: Node[]; relativeTo?: { key?: string } };
  const base = { key, name: creation.name, transform, relativeTo: relativeTo.optional() };
  const node: z.ZodType<Node> = z.lazy(() => z.discriminatedUnion('type', [
    z.object({ ...base, type: z.literal('empty'), children: z.array(node).max(64).optional() }).strict(),
    z.object({ ...base, type: z.literal('primitive'), primitiveType: (schemaOf('create_primitive') as z.ZodObject).shape.primitiveType, appearance: appearanceWithoutEntityId.optional(), children: z.array(node).max(64).optional() }).strict(),
    z.object({ ...base, type: z.literal('panel'), panel: panelSchema }).strict(),
  ]));
  const { entityId: _entityId, ...placementFields } = placement;
  const build = z.object({ name: creation.name, parentId: id.optional(), nodes: z.array(node).min(1).max(64), placement: z.object(placementFields).strict().refine(p => p.destination !== 'air' || p.physicalId === undefined, 'Air has no physical support.').optional() }).strict().superRefine((args, context) => {
    const invalid = (message: string) => context.addIssue({ code: 'custom', message });
    const nodes = new Map<string, { node: Node; ancestors: string[] }>(); let count = 0;
    function visit(items: Node[], ancestors: string[]) {
      for (const item of items) {
        count++;
        if (ancestors.length >= 6) invalid('Maximum node depth is 6.');
        if (nodes.has(item.key)) invalid('Duplicate node key.');
        nodes.set(item.key, { node: item, ancestors });
        if (item.children) visit(item.children, [...ancestors, item.key]);
      }
    }
    visit(args.nodes, []);
    if (count > 64) invalid('Maximum total node count is 64.');
    for (const [key, { node, ancestors }] of nodes) {
      const reference = node.relativeTo?.key;
      if (reference === undefined) continue;
      const target = nodes.get(reference);
      if (!target || reference === key || ancestors.includes(reference) || target.ancestors.includes(key)) invalid('relativeTo must reference a disjoint node.');
      if (target?.node.relativeTo) invalid('relativeTo chains are not supported.');
    }
  });
  const operationSchemas = ['set_transform', 'set_appearance', 'rename_object', 'update_panel', 'set_parent', 'delete_object', 'place_relative'].map(name => z.object({ tool: z.literal(name), arguments: schemaOf(name) }).strict());
  const operation = z.discriminatedUnion('tool', [operationSchemas[0]!, ...operationSchemas.slice(1)]);
  return [
    { name: 'scene_build', readOnly: false, schema: build, description: 'Creates a complete 3D composition in one atomic call: containers, primitives with appearance and text labels, as a tree with local keys. Positions are local to the parent node. relativeTo places a node against another by bounds after creation. placement puts the whole group in the air in front of the user, on a table or on a wall, scaling it down to minScale if needed. Returns key to entityId. One undoable operation; on any error nothing is created. Prefer this over repeated create_primitive/set_appearance/create_panel calls. Never retry ambiguous mutations.' },
    { name: 'scene_edit', readOnly: false, schema: z.object({ operations: z.array(operation).min(1).max(64) }).strict(), description: 'Atomically edits 1..64 live Generated descendants in order using the existing tool argument contracts. Allowed: set_transform, set_appearance, rename_object, update_panel, set_parent, delete_object, place_relative. Returns indexed results. One undoable operation; any failure rolls back all changes and includes the failing index. Never retry ambiguous mutations.' },
  ];
}
