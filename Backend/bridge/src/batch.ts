import { z } from 'zod';
import { lineSchema, pointRadius, pointDirection } from './lines.js';
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
  const base = { key, parentKey: key.optional(), name: creation.name, transform, relativeTo: relativeTo.optional() };
  const node = z.discriminatedUnion('type', [
    z.object({ ...base, type: z.literal('empty') }).strict(),
    z.object({ ...base, type: z.literal('primitive'), primitiveType: (schemaOf('create_primitive') as z.ZodObject).shape.primitiveType, appearance: appearanceWithoutEntityId.optional() }).strict(),
    z.object({ ...base, type: z.literal('panel'), panel: panelSchema }).strict(),
    z.object({ key, name: creation.name, type: z.literal('point'), targetKey: key.optional(), targetId: id.optional(), direction: pointDirection(vector).optional(), radius: pointRadius.optional(), appearance: appearanceWithoutEntityId.optional() }).strict(),
    z.object({ key, name: creation.name, type: z.literal('line'), parentKey: key.optional(), line: lineSchema(id, vector, key) }).strict(),
  ]);
  const { entityId: _entityId, ...placementFields } = placement;
  const build = z.object({ name: creation.name, parentId: id.optional(), nodes: z.array(node).min(1).max(64), placement: z.object(placementFields).strict().refine(p => p.destination !== 'air' || p.physicalId === undefined, 'Air has no physical support.').optional() }).strict().superRefine((args, context) => {
    const invalid = (message: string) => context.addIssue({ code: 'custom', message });
    const nodes = new Map<string, { node: z.infer<typeof node>; ancestors: string[] }>();
    for (const item of args.nodes) {
      if (nodes.has(item.key)) invalid('Duplicate node key.');
      let ancestors: string[] = [];
      if ('parentKey' in item && item.parentKey !== undefined) {
        const parent = nodes.get(item.parentKey);
        if (!parent || item.parentKey === item.key) invalid('parentKey must reference an earlier node.');
        else {
          if (['panel', 'point', 'line'].includes(parent.node.type)) invalid('A panel cannot be a parent.');
          ancestors = [...parent.ancestors, item.parentKey];
        }
      }
      if (item.type === 'point') {
        if ((item.targetKey === undefined) === (item.targetId === undefined)) invalid('Use exactly one point target.');
        if (item.targetKey !== undefined) {
          const target = nodes.get(item.targetKey);
          if (!target || target.node.type !== 'primitive') invalid('targetKey must reference an earlier primitive.');
          else ancestors = [...target.ancestors, item.targetKey];
        }
      }
      if (ancestors.length >= 6) invalid('Maximum node depth is 6.');
      nodes.set(item.key, { node: item, ancestors });
    }
    for (const [key, { node, ancestors }] of nodes) {
      if (node.type === 'line') {
        for (const endpoint of node.line.points as { key?: string }[]) {
          if (endpoint.key !== undefined && (!nodes.has(endpoint.key) || nodes.get(endpoint.key)!.node.type === 'line')) invalid('Line keys must reference non-line nodes.');
        }
      }
      const reference = 'relativeTo' in node ? node.relativeTo?.key : undefined;
      if (reference === undefined) continue;
      const target = nodes.get(reference);
      if (!target || reference === key || ancestors.includes(reference) || target.ancestors.includes(key)) invalid('relativeTo must reference a disjoint node.');
      if (target && ['point', 'line'].includes(target.node.type)) invalid('relativeTo cannot target points or lines.');
      if (target && 'relativeTo' in target.node && target.node.relativeTo) invalid('relativeTo chains are not supported.');
    }
  });
  const operationSchemas = ['set_transform', 'set_appearance', 'rename_object', 'update_panel', 'update_line', 'set_parent', 'delete_object', 'place_relative'].map(name => z.object({ tool: z.literal(name), arguments: schemaOf(name) }).strict());
  const operation = z.discriminatedUnion('tool', [operationSchemas[0]!, ...operationSchemas.slice(1)]);
  return [
    { name: 'scene_build', readOnly: false, schema: build, description: 'Creates a complete 3D composition in one atomic call: containers, primitives with appearance, text labels, points and lines, as a flat node list with unique local keys. Optional parentKey must identify an earlier container or primitive node; omit it to parent under the composition root. Maximum 64 nodes and depth 6. Positions are local to the parent node. relativeTo places disjoint non-point/non-line nodes by bounds. Points are created next on earlier targetKey primitives (or targetId); lines last, with endpoints key, entityId or position. Point nodes omit parentKey/transform/relativeTo; line nodes omit transform/relativeTo. Lines cannot be endpoints or parents. Points count towards depth. All support persistence v6. placement puts the whole group in the air in front of the user, on a table or on a wall, scaling it down to minScale if needed. Returns key to entityId. One undoable operation; on any error nothing is created. Prefer this over repeated create_primitive/set_appearance/create_panel calls. Never retry ambiguous mutations.' },
    { name: 'scene_edit', readOnly: false, schema: z.object({ operations: z.array(operation).min(1).max(64) }).strict(), description: 'Atomically edits 1..64 live Generated descendants in order using the existing tool argument contracts. Allowed: set_transform, set_appearance, rename_object, update_panel, update_line, set_parent, delete_object, place_relative. Returns indexed results. One undoable operation; any failure rolls back all changes and includes the failing index. Never retry ambiguous mutations.' },
  ];
}
