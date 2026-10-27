import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { presentationSchema } from '../dist/presentations.js';
import { tools } from '../dist/tools.js';

test('shared presentation corpus covers twelve types and strict relationship failures', async () => {
  const corpus = JSON.parse(await readFile(new URL('./presentation-corpus.json', import.meta.url), 'utf8'));
  assert.equal(new Set(corpus.filter(c => c.valid).map(c => c.content.type)).size, 12);
  for (const entry of corpus) assert.equal(presentationSchema.safeParse(entry.content).success, entry.valid, entry.name);
  const example = corpus.find(c => c.valid).content;
  assert.equal(presentationSchema.safeParse({ ...example, title: ' ' }).success, false);
  assert.equal(presentationSchema.safeParse({ ...example, description: 'x'.repeat(4001) }).success, false);
  const args = { name: 'Presentation', parentId: 'Generated', position: { x: 0, y: 1.4, z: 1.5 }, rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 }, width: .8, height: .7, presentation: example };
  const create = tools.find(t => t.name === 'ui_present').schema;
  assert.equal(create.safeParse(args).success, true);
  for (const change of [{ parentId: null }, { width: .39 }, { height: Infinity }, { placement: { destination: 'wall' } }])
    assert.equal(create.safeParse({ ...args, ...change }).success, false);
  assert.equal(tools.find(t => t.name === 'ui_update').schema.safeParse({ entityId: 'go_test', width: .8, height: .7, presentation: example }).success, true);
  assert.equal(tools.find(t => t.name === 'ui_get').schema.safeParse({ id: 'semantic-id' }).success, false);
  assert.equal(tools.find(t => t.name === 'ui_get').schema.safeParse({ entityId: 'go_test' }).success, true);
  assert.equal(tools.find(t => t.name === 'ui_media_list').schema.safeParse({ offset: 0, limit: 100 }).success, true);
});
