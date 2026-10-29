import test from 'node:test';
import assert from 'node:assert/strict';
import { tools } from '../dist/tools.js';
const schema = name => tools.find(t => t.name === name).schema;
const base = { layout: 'arc', itemSize: { width: .5, height: .3 } };
test('layout schemas are strict and enforce layout behavior contracts', () => {
  assert.equal(schema('layout_plan').safeParse({ ...base, count: 6 }).success, true);
  for (const extra of [{ behavior: 'lazy_follow' }, { layout: 'wall', behavior: 'billboard' }, { count: 25 }, { params: { distance: 2 } }, { mystery: true }])
    assert.equal(schema('layout_plan').safeParse({ ...base, count: 6, ...extra }).success, false);
  assert.equal(schema('layout_plan').safeParse({ ...base, layout: 'wall', count: 6, params: { physicalId: 'physical:wall', allowSpill: true } }).success, true);
  assert.equal(schema('layout_plan').safeParse({ ...base, layout: 'surface', count: 4, itemSize: { width: .3, height: .3 }, behavior: 'fixed_aligned', params: { pose: 'lying' } }).success, true);
  assert.equal(schema('layout_plan').safeParse({ ...base, layout: 'surface', count: 4, params: { pose: 'lying' } }).success, false);
  assert.equal(schema('layout_plan').safeParse({ ...base, layout: 'anchor', count: 1 }).success, false);
  const item = { name: 'Card', panel: { version: 1, kind: 'panel', title: 'Title', text: 'Content', fontSize: 24 } };
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', items: [item] }).success, true);
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', items: [{ ...item, presentation: {} }] }).success, false);
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', itemSize: { width: .5, height: .1 }, items: [item] }).success, false);
  assert.equal(schema('layout_apply').safeParse({ rootId: 'go_test', layout: 'plane' }).success, true);
  assert.equal(schema('layout_apply').safeParse({ rootId: 'go_test', layout: 'plane', behavior: 'billboard' }).success, false);
  const planeRequests = [
    ['layout_plan', { ...base, layout: 'plane', count: 2 }],
    ['layout_create', { ...base, layout: 'plane', name: 'Recipe', items: [item, item] }],
    ['layout_apply', { rootId: 'go_test', layout: 'plane' }],
  ];
  for (const [name, args] of planeRequests) {
    assert.equal(schema(name).safeParse({ ...args, behavior: 'fixed_facing' }).success, true);
    assert.equal(schema(name).safeParse({ ...args, behavior: 'lazy_follow' }).success, false);
    assert.equal(schema(name).safeParse({ ...args, params: { side: 'right' } }).success, false);
  }
  assert.equal(schema('layout_create').safeParse({ ...base, layout: 'anchor', name: 'Labels', items: [{ ...item, targetId: 'go_target', side: 'left' }] }).success, true);
});
