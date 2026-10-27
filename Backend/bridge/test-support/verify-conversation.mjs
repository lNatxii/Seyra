import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';

// Verify recorded runtime confirmations independently of the assistant's prose.
const source = process.argv[2];
if (!source) throw new Error('Usage: node verify-conversation.mjs <record.json> [summary.json]');
const record = JSON.parse(await readFile(source, 'utf8'));
const calls = record.messages.flatMap(message => (message.content ?? [])
  .filter(part => part.type === 'tool' && part.name.startsWith('seyra_'))
  .map(part => {
    assert.equal(part.state.status, 'completed', part.name);
    const result = JSON.parse(part.state.content.find(item => item.type === 'text').text);
    assert.equal(result.success, true, `${part.name}: ${JSON.stringify(result.error)}`);
    return { messageId: message.id, tool: part.name.slice(6), input: part.state.input, data: result.data };
  }));
for (const tool of ['scene_hierarchy', 'request_begin', 'request_end', 'create_primitive', 'create_gameobject',
  'rename_object', 'set_transform', 'set_parent', 'delete_object', 'restore_object', 'history_list', 'undo', 'content_save', 'content_load', 'scene_get'])
  assert(calls.some(call => call.tool === tool), `Missing confirmation: ${tool}`);
const created = calls.find(call => call.tool === 'create_primitive').data;
const id = created.entityId;
const target = calls.filter(call => call.input.entityId === id);
const renamed = target.find(call => call.tool === 'rename_object').data;
const moved = target.find(call => call.tool === 'set_transform').data;
const reparented = target.find(call => call.tool === 'set_parent').data;
const deleted = target.find(call => call.tool === 'delete_object').data;
const restored = target.find(call => call.tool === 'restore_object').data;
for (const data of [renamed, moved, reparented, deleted, restored]) assert.equal(data.entityId, id);
assert.equal(deleted.deleted, true);
assert.equal(restored.deleted, false);
assert.deepEqual(restored.components, created.components, 'Soft deletion/restoration changed component IDs');
assert.equal(restored.parentId, reparented.parentId);
assert.deepEqual(restored.transform.position, moved.transform.position);
const loadIndex = calls.findIndex(call => call.tool === 'content_load');
const undoIndex = calls.findIndex(call => call.tool === 'undo');
assert(calls.slice(undoIndex + 1, loadIndex).some(call => call.tool === 'scene_get' && call.input.entityId === id && call.data.deleted === true),
  'Missing confirmation that undo restored the deleted state');
for (let index = 0; index < calls.length; index++) {
  if (calls[index].tool !== 'undo') continue;
  const history = calls.slice(0, index).reverse().find(call => call.tool === 'history_list');
  assert.equal(calls[index].input.expectedOperationId, history.data.operations[0].operationId, 'Undo did not use the latest inspected operation');
}
const loaded = calls.slice(loadIndex + 1).find(call => call.tool === 'scene_get' && call.input.entityId === id)?.data;
assert(loaded, 'Missing post-load inspection');
assert.equal(loaded.name, renamed.name);
assert.equal(loaded.parentId, reparented.parentId);
assert.deepEqual(loaded.transform.position, moved.transform.position);
assert.notDeepEqual(loaded.components, created.components, 'Loading should reconstruct components');
const summary = {
  sessionId: record.sessionId,
  models: [...new Set(record.messages.filter(m => m.type === 'assistant' && m.model).map(m => `${m.model.providerID}/${m.model.id}`))],
  turns: record.messages.filter(m => m.type === 'user').map(m => ({ id: m.id, text: m.text })),
  outcomes: record.messages.filter(m => m.type === 'idle').map(m => m.outcome),
  checks: ['required tools confirmed', 'stable object ID', 'deleted state', 'restored component IDs', 'undo precondition and deleted state',
    'restored parent and transform', 'loaded name, parent, transform and object ID', 'reconstructed component IDs'],
  calls: calls.map(({ messageId, tool, input, data }) => ({ messageId, tool, input,
    result: Object.fromEntries(Object.entries(data).filter(([key]) => ['entityId', 'name', 'parentId', 'deleted',
      'transform', 'operationId', 'slot', 'version', 'nodeCount', 'closed', 'undone', 'runtimeId'].includes(key))) }))
};
assert(summary.outcomes.length > 0 && summary.outcomes.every(outcome => outcome === 'succeeded'));
if (process.argv[3]) await writeFile(process.argv[3], JSON.stringify(summary, null, 2) + '\n');
console.log(JSON.stringify({ sessionId: summary.sessionId, models: summary.models, confirmedCalls: calls.length, checks: summary.checks }, null, 2));
