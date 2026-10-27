import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tools } from '../dist/tools.js';

const schema = tools.find(t => t.name === 'set_appearance').schema;
const color = { r: 0.2, g: 0.4, b: 0.8, a: 1 };
test('appearance styles and RGBA validation', () => {
  for (const style of ['matte_opaque', 'colored_translucent', 'unlit', 'ui_unlit', 'plastic', 'transparent']) {
    assert.equal(schema.safeParse({ entityId: 'go_test', style, color }).success, true);
    assert.equal(schema.safeParse({ entityId: 'go_test', style, color: { ...color, a: 0 } }).success,
      ['colored_translucent', 'ui_unlit', 'transparent'].includes(style));
  }
  for (const invalid of [
    { style: 'glass' }, { entityId: null }, { color: { ...color, r: -0.1 } },
    { color: { ...color, g: 1.1 } }, { color: { ...color, a: NaN } },
    { color: { r: 1, g: 1, b: 1 } }, { color: { ...color, extra: 1 } }, { shader: 'arbitrary' },
  ]) assert.equal(schema.safeParse({ entityId: 'go_test', style: 'matte_opaque', color, ...invalid }).success, false);
  assert.equal(tools.find(t => t.name === 'set_appearance').readOnly, false);
});
