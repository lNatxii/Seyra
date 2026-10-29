import test from 'node:test';
import assert from 'node:assert/strict';
import { z } from 'zod';
import { toJsonSchemaCompat } from '@modelcontextprotocol/sdk/server/zod-json-schema-compat.js';
import { tools } from '../dist/tools.js';
const schema = name => tools.find(t => t.name === name).schema;
const transform = { position: { x: 0, y: 0, z: 0 } };
const empty = key => ({ key, name: key, type: 'empty', transform });
const appearance = { style: 'unlit', color: { r: 1, g: 0, b: 0, a: 1 } };
const composition = () => ({ name: 'Composition', nodes: [
  empty('group'),
  { ...empty('sun'), parentKey: 'group', type: 'primitive', primitiveType: 'sphere', appearance },
  { ...empty('child'), parentKey: 'sun' },
  { ...empty('label'), type: 'panel', panel: { version: 1, kind: 'label', title: '', text: 'Sun', width: .2, height: .06, fontSize: 24 }, relativeTo: { key: 'sun', direction: 'above', gap: .01 } },
] });
test('scene_build accepts a three-level flat composition and rejects invalid contracts', () => {
  const build = schema('scene_build');
  assert.equal(build.safeParse(composition()).success, true);
  const cases = [];
  let args = composition(); args.nodes.push(empty('sun')); cases.push(args);
  cases.push({ name: 'Too many', nodes: Array.from({ length: 65 }, (_, i) => empty('n' + i)) });
  cases.push({ name: 'Too deep', nodes: Array.from({ length: 7 }, (_, i) => ({ ...empty('n' + i), ...(i ? { parentKey: 'n' + (i - 1) } : {}) })) });
  for (const parentKey of ['child', 'missing', 'sun']) {
    args = composition(); args.nodes[1].parentKey = parentKey; cases.push(args);
  }
  args = composition(); args.nodes.push({ ...empty('panel_child'), parentKey: 'label' }); cases.push(args);
  args = composition(); args.nodes[0].children = []; cases.push(args);
  args = composition(); args.nodes[3].relativeTo.key = 'missing'; cases.push(args);
  args = composition(); args.nodes[3].parentKey = 'sun'; cases.push(args);
  args = composition(); args.nodes[0].relativeTo = { key: 'child', direction: 'above', gap: 0 }; cases.push(args);
  args = composition(); args.nodes[1].relativeTo = { key: 'label', direction: 'above', gap: 0 }; cases.push(args);
  args = composition(); args.nodes[1].appearance = { ...appearance, color: { ...appearance.color, a: .5 } }; cases.push(args);
  args = composition(); args.placement = { destination: 'air', physicalId: 'physical:00000000-0000-0000-0000-000000000000' }; cases.push(args);
  for (const invalid of cases) assert.equal(build.safeParse(invalid).success, false, JSON.stringify(invalid));
});
test('scene_edit reuses existing schemas and excludes creation', () => {
  assert.equal(schema('scene_edit').safeParse({ operations: [{ tool: 'set_appearance', arguments: { entityId: 'go_test', ...appearance } }] }).success, true);
  assert.equal(schema('scene_edit').safeParse({ operations: [{ tool: 'create_primitive', arguments: {} }] }).success, false);
});

// McpServer's tools/list uses this SDK converter with these options (Zod 4:
// zod/v4-mini.toJSONSchema, draft-7, io=input). Check the emitted reference graph.
const mcpJsonSchema = input => toJsonSchemaCompat(input, { strictUnions: true, pipeStrategy: 'input' });
function assertNonRecursive(root, name) {
  const active = new Set();
  const visited = new Set();
  function visit(value) {
    if (value === null || typeof value !== 'object') return;
    assert.equal(active.has(value), false, `${name}: recursive JSON Schema`);
    if (visited.has(value)) return;
    active.add(value);
    if (typeof value.$ref === 'string') {
      assert.ok(value.$ref === '#' || value.$ref.startsWith('#/'), `${name}: unsupported external $ref`);
      const target = value.$ref === '#' ? root : value.$ref.slice(2).split('/').reduce((node, part) => node?.[decodeURIComponent(part).replace(/~1/g, '/').replace(/~0/g, '~')], root);
      assert.notEqual(target, undefined, `${name}: unresolved $ref ${value.$ref}`);
      visit(target);
    }
    for (const child of Object.values(value)) visit(child);
    active.delete(value);
    visited.add(value);
  }
  visit(root);
}
test('the entire MCP tool catalog emits non-recursive JSON Schemas', () => {
  for (const tool of tools) assertNonRecursive(mcpJsonSchema(tool.schema), tool.name);
  // Guard the detector itself: a recursive lazy node must fail, including root refs.
  const recursive = z.object({ children: z.array(z.lazy(() => recursive)) });
  assert.throws(() => assertNonRecursive(mcpJsonSchema(recursive), 'recursive control'), /recursive JSON Schema/);
});
