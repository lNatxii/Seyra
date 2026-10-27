import assert from 'node:assert/strict';
import test from 'node:test';
import { tools } from '../dist/tools.js';
import { panelSchema } from '../dist/panels.js';

const panel = { version: 1, kind: 'panel', title: 'Datos', text: '  <b>Texto literal</b>\nSegunda línea  ', width: 0.8, height: 0.6, fontSize: 24 };
test('panel contracts are complete, literal and bounded', () => {
  assert.deepEqual(panelSchema.parse(panel), panel);
  const label = { ...panel, kind: 'label', title: '', width: 0.16, height: 0.04, fontSize: 12 };
  assert.deepEqual(panelSchema.parse(label), label);
  for (const changes of [
    { version: 2 }, { version: '1' }, { kind: 'gallery' }, { width: 0.159 }, { width: Infinity },
    { width: 2.001 }, { height: 0.239 }, { fontSize: 11.9 }, { fontSize: 73 },
    { text: '' }, { text: '   ' }, { text: 'x'.repeat(16001) }, { title: 'x'.repeat(257) },
    { path: 'Content/Label' }, { html: '<div>test</div>' }, { text: null }, { fontSize: '24' },
  ]) assert.equal(panelSchema.safeParse({ ...panel, ...changes }).success, false, JSON.stringify(changes));
  for (const changes of [{ title: 'Unsupported title' }, { height: 0.039 }, { height: 0.04, fontSize: 72 }])
    assert.equal(panelSchema.safeParse({ ...label, ...changes }).success, false);
  for (const key of Object.keys(panel)) {
    const incomplete = { ...panel }; delete incomplete[key];
    assert.equal(panelSchema.safeParse(incomplete).success, false, key);
  }
  const create = tools.find(tool => tool.name === 'create_panel').schema;
  const creation = { name: 'Panel', parentId: 'Generated', position: { x: 0, y: 1.4, z: 1.5 }, rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 }, panel };
  assert.deepEqual(create.parse(creation), creation);
  assert.equal(create.safeParse({ ...creation, parentId: null }).success, false);
  const update = tools.find(tool => tool.name === 'update_panel').schema;
  assert.equal(update.safeParse({ entityId: 'go_panel', panel }).success, true);
  assert.equal(update.safeParse({ entityId: 'go_panel', text: 'partial' }).success, false);
});
