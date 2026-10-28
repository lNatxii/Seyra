import assert from 'node:assert/strict';
import test from 'node:test';
import { tools } from '../dist/tools.js';

test('room queries and optional placement frames have strict bounded schemas', () => {
  const tool = name => tools.find(t => t.name === name);
  for (const name of ['room_get', 'room_objects']) assert.equal(tool(name).readOnly, true);
  assert.deepEqual(tool('room_get').schema.parse({}), {});
  assert.equal(tool('room_get').schema.safeParse({ scan: true }).success, false);
  for (const args of [{ limit: 65 }, { offset: -1 }, { limit: 0 }, { kind: 'sofa' }, { label: '' }, { extra: true }])
    assert.equal(tool('room_objects').schema.safeParse(args).success, false);
  for (const kind of ['wall', 'floor', 'ceiling', 'opening', 'tabletop', 'object'])
    assert.equal(tool('room_objects').schema.safeParse({ kind, label: 'TABLE', limit: 64 }).success, true);
  const legacy = { entityId: 'go_box', referenceId: 'go_reference', direction: 'above', gap: 0 };
  assert.deepEqual(tool('place_relative').schema.parse(legacy), legacy);
  for (const frame of ['world', 'viewer', 'reference']) {
    assert.equal(tool('scene_bounds').schema.safeParse({ entityId: 'physical:uuid', frame }).success, true);
    for (const orient of ['keep', 'face_viewer', 'match_reference'])
      assert.equal(tool('place_relative').schema.safeParse({ ...legacy, referenceId: 'physical:uuid', frame, orient }).success, true);
  }
  for (const args of [{ frame: 'head' }, { orient: 'auto' }, { gap: Infinity }, { gap: NaN }, { roomRevision: 3 }])
    assert.equal(tool('place_relative').schema.safeParse({ ...legacy, ...args }).success, false);
});

 test('spatial placement schemas remain bounded and distinguish query from commit', () => {
  const query=tools.find(t=>t.name==='spatial_candidates'), commit=tools.find(t=>t.name==='spatial_place');
  assert.equal(query.readOnly,true);assert.equal(commit.readOnly,false);
  assert.equal(query.schema.safeParse({entityId:'go_test',destination:'air'}).success,true);
  assert.equal(query.schema.safeParse({entityId:'go_test',destination:'air',expectedRoomRevision:1}).success,false);
  assert.equal(commit.schema.safeParse({entityId:'go_test',destination:'wall',expectedRoomRevision:1}).success,true);
  assert.equal(commit.schema.safeParse({entityId:'go_test',destination:'air',minScale:0}).success,false);
 });
