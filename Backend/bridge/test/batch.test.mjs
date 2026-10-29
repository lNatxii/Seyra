import test from 'node:test';
import assert from 'node:assert/strict';
import { tools } from '../dist/tools.js';
const schema = name => tools.find(t => t.name === name).schema;
const transform = { position: { x: 0, y: 0, z: 0 } };
const empty = key => ({ key, name: key, type: 'empty', transform });
const appearance = { style: 'unlit', color: { r: 1, g: 0, b: 0, a: 1 } };
const tree = () => ({ name: 'Composition', nodes: [
  { ...empty('group'), children: [{ ...empty('sun'), type: 'primitive', primitiveType: 'sphere', appearance, children: [empty('child')] }] },
  { ...empty('label'), type: 'panel', panel: { version: 1, kind: 'label', title: '', text: 'Sun', width: .2, height: .06, fontSize: 24 }, relativeTo: { key: 'sun', direction: 'above', gap: .01 } },
] });
test('scene_build accepts a three-level composition and rejects invalid contracts', () => {
  const build = schema('scene_build');
  assert.equal(build.safeParse(tree()).success, true);
  const cases = [];
  let args = tree(); args.nodes.push(empty('sun')); cases.push(args);
  cases.push({ name: 'Too many', nodes: Array.from({ length: 65 }, (_, i) => empty('n' + i)) });
  let node = empty('n6'); for (let i = 5; i >= 0; i--) node = { ...empty('n' + i), children: [node] };
  cases.push({ name: 'Too deep', nodes: [node] });
  args = tree(); args.nodes[1].relativeTo.key = 'missing'; cases.push(args);
  args = tree(); args.nodes[0].children[0].appearance = { ...appearance, color: { ...appearance.color, a: .5 } }; cases.push(args);
  args = tree(); args.placement = { destination: 'air', physicalId: 'physical:00000000-0000-0000-0000-000000000000' }; cases.push(args);
  for (const invalid of cases) assert.equal(build.safeParse(invalid).success, false);
});
test('scene_edit reuses existing schemas and excludes creation', () => {
  assert.equal(schema('scene_edit').safeParse({ operations: [{ tool: 'set_appearance', arguments: { entityId: 'go_test', ...appearance } }] }).success, true);
  assert.equal(schema('scene_edit').safeParse({ operations: [{ tool: 'create_primitive', arguments: {} }] }).success, false);
});
