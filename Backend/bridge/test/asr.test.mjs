import assert from 'node:assert/strict';
import test from 'node:test';
import { once } from 'node:events';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import https from 'node:https';
import { createHash } from 'node:crypto';
import { WebSocketServer } from 'ws';
import { createGateway } from '../dist/gateway.js';

async function fixture(t, apiKey = 'fixture-key') {
  const root = mkdtempSync(join(tmpdir(), 'seyra-asr-'));
  execFileSync(process.execPath, ['Backend/seyra.mjs', 'init-tls', '--root', root], { cwd: new URL('../../..', import.meta.url), stdio: 'pipe' });
  const meta = new WebSocketServer({ host: '127.0.0.1', port: 0 }); await once(meta, 'listening');
  const frames = []; let socket; let handshake;
  meta.on('connection', peer => {
    socket = peer;
    peer.on('message', (data, binary) => {
      if (binary) frames.push(Buffer.from(data));
      else {
        const message = JSON.parse(data.toString());
        if (message.authorization) { handshake = message; peer.send(JSON.stringify({ sessionId: 'meta-session' })); }
        if (message.type === 'endStream') peer.send(JSON.stringify({ type: 'transcript', transcript: 'Hola Seyra.', final: true }));
      }
    });
  });
  const devicesFile = join(root, 'test-devices.json');
  writeFileSync(devicesFile, JSON.stringify(['fixture-device', 'other-device'].map(id => ({ id, tokenSha256: createHash('sha256').update(id).digest('hex'), revoked: false }))));
  const previous = { META_API_KEY: process.env.META_API_KEY, SEYRA_ASR_URL: process.env.SEYRA_ASR_URL, NODE_ENV: process.env.NODE_ENV };
  process.env.META_API_KEY = apiKey; process.env.SEYRA_ASR_URL = `ws://127.0.0.1:${meta.address().port}`; process.env.NODE_ENV = 'test';
  let gateway;
  try {
    gateway = createGateway({ cert: readFileSync(join(root, '.seyra/tls/gateway.cert.pem')), key: readFileSync(join(root, '.seyra/tls/gateway.key.pem')), devicesFile, password: 'fixture-password', internalToken: 'fixture-internal' });
  } finally {
    for (const [key, value] of Object.entries(previous)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; }
  }
  gateway.listen(0, '127.0.0.1'); await once(gateway, 'listening');
  t.after(async () => {
    gateway.closeAllConnections(); await new Promise(resolve => gateway.close(resolve));
    for (const peer of meta.clients) peer.terminate(); await new Promise(resolve => meta.close(resolve));
    rmSync(root, { recursive: true, force: true });
  });
  const request = (action, { id, method = action === 'events' ? 'GET' : 'POST', body = Buffer.from('{}'), token = 'fixture-device', after = -1, wait = 0 } = {}) => new Promise((resolve, reject) => {
    const req = https.request({ host: '127.0.0.1', port: gateway.address().port, rejectUnauthorized: false, method,
      path: `/_seyra/asr/${action}?asrId=${id ?? ''}&after=${after}&wait=${wait}`, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), Connection: 'close' } }, res => {
      let text = ''; res.on('data', data => text += data); res.on('end', () => resolve({ status: res.statusCode, body: JSON.parse(text || '{}') }));
    }); req.on('error', reject); req.end(method === 'GET' ? undefined : body);
  });
  return { request, frames, get socket() { return socket; }, get handshake() { return handshake; } };
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
test('ASR requires configuration and device authentication', async t => {
  const f = await fixture(t, '');
  assert.equal((await f.request('start', { token: null })).status, 401);
  assert.deepEqual(await f.request('start'), { status: 503, body: { error: 'ASR_NOT_CONFIGURED' } });
});
test('ASR relays handshake, binary audio, partial and final snapshots', async t => {
  const f = await fixture(t); const start = await f.request('start'); assert.equal(start.status, 200); const id = start.body.asrId;
  assert.deepEqual(f.handshake, { authorization: { accessToken: 'Bearer fixture-key' }, model: 'muse-voice-transcribe-1.0', audioEncoding: 'PCM_16KHZ', mode: 'PUSH_TO_TALK', partialMode: 'CUMULATIVE', emitAudioProgress: false, languageBias: ['English'], keywords: ['Seyra'] });
  const initial = await f.request('events', { id });
  const audio = Buffer.alloc(6400, 7);
  assert.equal((await f.request('audio', { id, body: audio })).status, 200); await delay(120);
  assert.equal((await f.request('audio', { id, body: audio })).status, 200); await delay(240);
  assert.ok(f.frames.filter(frame => frame.equals(audio)).length >= 2);
  const poll = f.request('events', { id, after: initial.body.version, wait: 10 });
  f.socket.send(JSON.stringify({ type: 'transcript', transcript: 'Hola', final: false }));
  const partial = await poll; assert.equal(partial.body.partial, 'Hola'); assert.ok(partial.body.version > initial.body.version);
  assert.equal((await f.request('stop', { id })).status, 200);
  const final = await f.request('events', { id, after: partial.body.version, wait: 10 });
  assert.equal(final.body.done, true); assert.equal(final.body.final, 'Hola Seyra.');
});
test('ASR sends real-time silence while waiting for client audio', async t => {
  const f = await fixture(t); const id = (await f.request('start')).body.asrId; await delay(500);
  assert.ok(f.frames.length >= 3); assert.ok(f.frames.every(frame => frame.length === 3200 && frame.every(byte => byte === 0)));
  const audio = Buffer.alloc(3200, 7);
  for (let count = 0; count < 2; count++) {
    assert.equal((await f.request('audio', { id, body: audio })).status, 200); await delay(120);
  }
  assert.ok(f.frames.every(frame => frame.every(byte => byte === 0)));
  assert.equal((await f.request('audio', { id, body: audio })).status, 200); await delay(240);
  assert.deepEqual(Buffer.concat(f.frames.filter(frame => frame.some(byte => byte !== 0))), Buffer.concat([audio, audio, audio]));
});
test('ASR enforces chunk limits and session ownership/expiration', async t => {
  const f = await fixture(t); const id = (await f.request('start')).body.asrId;
  assert.equal((await f.request('audio', { id, body: Buffer.alloc(65 * 1024) })).status, 413);
  assert.equal((await f.request('audio', { id, body: Buffer.alloc(3) })).status, 400);
  assert.equal((await f.request('events', { id, token: 'other-device' })).status, 410);
  await f.request('cancel', { id }); assert.equal((await f.request('events', { id })).status, 410);
});
test('ASR maps upstream 1013 closure to rate limiting', async t => {
  const f = await fixture(t); const id = (await f.request('start')).body.asrId;
  const poll = f.request('events', { id, after: 0, wait: 10 }); f.socket.close(1013);
  assert.equal((await poll).body.error, 'ASR_RATE_LIMITED');
});
