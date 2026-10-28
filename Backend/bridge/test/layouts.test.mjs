import test from 'node:test';
import assert from 'node:assert/strict';
import { tools } from '../dist/tools.js';
const schema = name => tools.find(t => t.name === name).schema;
const base = { layout: 'arc', itemSize: { width: .5, height: .3 } };
test('layout schemas are strict and enforce floating behavior contracts', () => {
  assert.equal(schema('layout_plan').safeParse({ ...base, count: 6 }).success, true);
  for (const extra of [{ behavior: 'lazy_follow' }, { layout: 'wall' }, { count: 25 }, { params: { distance: 2 } }, { mystery: true }])
    assert.equal(schema('layout_plan').safeParse({ ...base, count: 6, ...extra }).success, false);
  assert.equal(schema('layout_plan').safeParse({ ...base, layout: 'anchor', count: 1 }).success, false);
  const item = { name: 'Card', panel: { version: 1, kind: 'panel', title: 'Title', text: 'Content', fontSize: 24 } };
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', items: [item] }).success, true);
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', items: [{ ...item, presentation: {} }] }).success, false);
  assert.equal(schema('layout_create').safeParse({ ...base, name: 'Books', itemSize: { width: .5, height: .1 }, items: [item] }).success, false);
  assert.equal(schema('layout_apply').safeParse({ rootId: 'go_test', layout: 'plane' }).success, true);
  assert.equal(schema('layout_apply').safeParse({ rootId: 'go_test', layout: 'plane', behavior: 'billboard' }).success, false);
});
