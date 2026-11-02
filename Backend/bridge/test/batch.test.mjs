import test from 'node:test';
import assert from 'node:assert/strict';
import { z } from 'zod';
import { toJsonSchemaCompat } from '@modelcontextprotocol/sdk/server/zod-json-schema-compat.js';
import { tools } from '../dist/tools.js';
import { interactionSetSchema, interactionFieldsSchema } from '../dist/interaction.js';
import { id } from '../dist/tools.js';
test('interaction schemas accept details and reject unsupported fields', () => {
  const set = interactionSetSchema(id);
  const panel = { version: 1, kind: 'panel', title: 'Planet', text: 'A planet.', width: .4, height: .3, fontSize: 24 };
  for (const fields of [{}, { grabbable: true, returnOnRelease: false, exclusive: false, detailSide: 'right', detailGap: .5, detailOpen: true },
    { detail: { panel } }, { detail: { presentation: { type: 'infoPanel', id: 'facts', title: 'Facts' }, width: .4, height: .4 } }]) {
    assert.equal(interactionFieldsSchema.safeParse(fields).success, true);
    assert.equal(set.safeParse({ entityId: 'go_target', interactive: true, ...fields }).success, true);
  }
  assert.equal(set.safeParse({ entityId: 'go_target', interactive: false }).success, true);
  for (const fields of [{ detailGap: -.01 }, { detailGap: .51 }, { detailGap: Infinity }, { detailSide: 'below' }, { grabbable: 1 },
    { detail: { panel: { ...panel, kind: 'label', title: '' } } }, { detail: { panel, width: .4 } },
    { detail: { presentation: { type: 'infoPanel', id: 'facts', title: 'Facts' }, width: .3, height: .4 } }, { detail: null }, { unknown: true }])
    assert.equal(set.safeParse({ entityId: 'go_target', interactive: true, ...fields }).success, false, JSON.stringify(fields));
  assert.equal(set.safeParse({ entityId: 'go_target', interactive: false, grabbable: false }).success, false);
});
const schema = name => tools.find(t => t.name === name).schema;
const transform = { position: { x: 0, y: 0, z: 0 } };
const empty = key => ({ key, name: key, type: 'empty', transform });
test('scene_build motion contracts validate pivots, ranges and position channels', () => {
  const build = schema('scene_build');
  const args = motions => ({ name: 'Animated model', nodes: [
    { ...empty('carrier'), motions },
    { ...empty('child'), parentKey: 'carrier' },
    { ...empty('sun'), type: 'primitive', primitiveType: 'sphere' },
    { key: 'point', name: 'Point', type: 'point', targetKey: 'sun' },
    { key: 'line', name: 'Line', type: 'line', line: { points: [{ key: 'sun' }, { key: 'point' }] } },
  ] });
  for (const motions of [
    [{ kind: 'orbit', pivotKey: 'sun', speed: 360 }],
    [{ kind: 'orbit', pivotKey: 'point', speed: -360 }],
    [{ kind: 'spin', speed: -720 }, { kind: 'orbit', pivotPoint: { x: 0, y: 0, z: 0 }, speed: 20 }],
    [{ kind: 'spin', speed: 720 }, { kind: 'bob', amplitude: .005, period: .2 }],
    [{ kind: 'bob', amplitude: .5, period: 60 }],
    [{ kind: 'orbit', pivotId: 'go_existing', speed: 20 }],
  ]) assert.equal(build.safeParse(args(motions)).success, true, JSON.stringify(motions));
  for (const motions of [
    [], Array.from({ length: 3 }, () => ({ kind: 'spin', speed: 20 })),
    ...['missing', 'line', 'carrier', 'child', 'Invalid key'].map(pivotKey => [{ kind: 'orbit', pivotKey, speed: 20 }]),
    [{ kind: 'orbit', speed: 20 }],
    [{ kind: 'orbit', speed: 20, pivotKey: 'sun', pivotId: 'go_existing' }],
    [{ kind: 'orbit', speed: 20, pivotKey: 'sun', pivotPoint: { x: 0, y: 0, z: 0 } }],
    [{ kind: 'orbit', speed: 20, pivotId: 'go_existing', pivotPoint: { x: 0, y: 0, z: 0 } }],
    [{ kind: 'orbit', speed: 0, pivotKey: 'sun' }],
    [{ kind: 'orbit', speed: 361, pivotKey: 'sun' }],
    [{ kind: 'orbit', speed: -361, pivotKey: 'sun' }],
    [{ kind: 'orbit', speed: 20, pivotKey: 'sun', axis: { x: 0, y: 0, z: 0 } }],
    [{ kind: 'spin', speed: 721 }], [{ kind: 'spin', speed: -721 }],
    [{ kind: 'spin', speed: 20, pivotKey: 'sun' }],
    [{ kind: 'bob', amplitude: .004, period: 1 }], [{ kind: 'bob', amplitude: .501, period: 1 }],
    [{ kind: 'bob', amplitude: .1, period: .199 }], [{ kind: 'bob', amplitude: .1, period: 61 }],
    [{ kind: 'orbit', pivotKey: 'sun', speed: 20 }, { kind: 'bob', amplitude: .1, period: 1 }],
    [{ kind: 'bob', amplitude: .1, period: 1 }, { kind: 'orbit', pivotKey: 'sun', speed: 20 }],
  ]) assert.equal(build.safeParse(args(motions)).success, false, JSON.stringify(motions));
  for (const index of [3, 4]) {
    const invalid = args([{ kind: 'spin', speed: 20 }]);
    invalid.nodes[index].motions = [{ kind: 'spin', speed: 20 }];
    assert.equal(build.safeParse(invalid).success, false);
  }
  const descendantPoint = args([{ kind: 'orbit', pivotKey: 'point', speed: 20 }]);
  descendantPoint.nodes[3].targetKey = 'sun'; descendantPoint.nodes[2].parentKey = 'carrier';
  assert.equal(build.safeParse(descendantPoint).success, false);
});

test('scene_edit accepts motion mutations and excludes control and batch pivot keys', () => {
  const edit = operations => schema('scene_edit').safeParse({ operations }).success;
  assert.equal(edit([
    { tool: 'motion_set', arguments: { entityId: 'go_target', motion: { kind: 'orbit', pivotId: 'go_sun', speed: 20 } } },
    { tool: 'motion_remove', arguments: { entityId: 'go_target', kind: 'orbit' } },
  ]), true);
  assert.equal(edit([{ tool: 'motion_control', arguments: { action: 'pause' } }]), false);
  assert.equal(edit([{ tool: 'motion_set', arguments: { entityId: 'go_target', motion: { kind: 'orbit', pivotKey: 'sun', speed: 20 } } }]), false);
});
const appearance = { style: 'unlit', color: { r: 1, g: 0, b: 0, a: 1 } };
test('motion tool schemas enforce kinds, pivot exclusivity and ranges', () => {
  const set = motion => schema('motion_set').safeParse({ entityId: 'go_target', motion }).success;
  for (const motion of [
    { kind: 'spin', speed: 720 }, { kind: 'spin', speed: -720, axis: { x: 0, y: 2, z: 0 } },
    { kind: 'orbit', speed: -360, pivotId: 'go_pivot' },
    { kind: 'orbit', speed: 360, pivotPoint: { x: 0, y: 0, z: 0 } },
    { kind: 'bob', amplitude: .005, period: .2 }, { kind: 'bob', amplitude: .5, period: 60 },
  ]) assert.equal(set(motion), true, JSON.stringify(motion));
  for (const motion of [
    { kind: 'orbit', speed: 20 },
    { kind: 'orbit', speed: 20, pivotId: 'go_pivot', pivotPoint: { x: 0, y: 0, z: 0 } },
    { kind: 'orbit', speed: 20, pivotKey: 'sun' },
    { kind: 'orbit', speed: 361, pivotId: 'go_pivot' },
    { kind: 'orbit', speed: -361, pivotId: 'go_pivot' },
    { kind: 'orbit', speed: 0, pivotId: 'go_pivot' },
    { kind: 'orbit', speed: 20, pivotId: null },
    { kind: 'spin', speed: 721 }, { kind: 'spin', speed: -721 }, { kind: 'spin', speed: 0 },
    { kind: 'spin', speed: Infinity }, { kind: 'spin', speed: 20, axis: { x: 0, y: 0, z: 0 } },
    { kind: 'spin', speed: 20, axis: { x: NaN, y: 1, z: 0 } },
    { kind: 'spin', speed: 20, period: 1 },
    { kind: 'bob', amplitude: .004, period: 1 }, { kind: 'bob', amplitude: .501, period: 1 },
    { kind: 'bob', amplitude: .1, period: .199 }, { kind: 'bob', amplitude: .1, period: 61 },
  ]) assert.equal(set(motion), false, JSON.stringify(motion));
  assert.equal(schema('motion_set').safeParse({ entityId: 'go_target', motion: { kind: 'spin', speed: 30 }, paused: true }).success, true);
  assert.equal(schema('motion_remove').safeParse({ entityId: 'go_target', kind: 'orbit' }).success, true);
  assert.equal(schema('motion_remove').safeParse({ entityId: 'go_target', kind: 'all' }).success, false);
  for (const action of ['pause', 'resume', 'reset']) {
    assert.equal(schema('motion_control').safeParse({ action }).success, true);
    assert.equal(schema('motion_control').safeParse({ entityId: 'go_root', action }).success, true);
  }
  assert.equal(schema('motion_control').safeParse({ action: 'stop' }).success, false);
});
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

test('guide point and line batch contracts', () => {
  const build = schema('scene_build');
  const valid = () => { const args = composition(); args.nodes.push(
    { key: 'point', name: 'Point', type: 'point', targetKey: 'sun' },
    { key: 'line', name: 'Line', type: 'line', line: { points: [{ key: 'point' }, { key: 'label' }], width: .003 } }); return args; };
  assert.equal(build.safeParse(valid()).success, true);
  assert.equal(schema('create_point').safeParse({ name: 'Point', targetId: 'go_test' }).success, true);
  assert.equal(schema('create_line').safeParse({ name: 'Line', parentId: 'Generated', line: { points: [{ entityId: 'go_test' }, { position: { x: 0, y: 1, z: 0 } }] } }).success, true);
  assert.equal(schema('scene_edit').safeParse({ operations: [{ tool: 'update_line', arguments: { entityId: 'go_line', line: { points: [{ entityId: 'go_point' }, { entityId: 'go_label' }] } } }] }).success, true);
  const cases = [];
  let args = valid(); args.nodes[5].line.points[0].entityId = 'go_test'; cases.push(args);
  args = valid(); [args.nodes[1], args.nodes[4]] = [args.nodes[4], args.nodes[1]]; cases.push(args);
  args = valid(); args.nodes.push({ ...empty('bad_parent'), parentKey: 'line' }); cases.push(args);
  for (const radius of [.001, .101]) { args = valid(); args.nodes[4].radius = radius; cases.push(args); }
  for (const width of [.0009, .051]) { args = valid(); args.nodes[5].line.width = width; cases.push(args); }
  args = valid(); args.nodes[5].line.color = { r: 1, g: 1, b: 1, a: .5 }; cases.push(args);
  for (const invalid of cases) assert.equal(build.safeParse(invalid).success, false, JSON.stringify(invalid));
});
