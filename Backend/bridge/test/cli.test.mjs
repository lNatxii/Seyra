import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { createHash } from 'node:crypto';
import tls from 'node:tls';
import { fileURLToPath } from 'node:url';
import { parseEnvironment, loadTls } from '../../seyra.mjs';
import { createGateway } from '../dist/gateway.js';

test('environment reader handles quotes, comments and invalid entries', () => {
  assert.deepEqual(parseEnvironment('# comment\n\n A="one"\nB=\'two\'\nC = three\n'), { A: 'one', B: 'two', C: 'three' });
  assert.throws(() => parseEnvironment('invalid'), /Invalid .env.local format/);
});

test('CLI PEM fingerprint matches gateway TLS and pairing can be revoked and renewed', async () => {
  const root = mkdtempSync(join(tmpdir(), 'seyra-cli-'));
  const cli = fileURLToPath(new URL('../../seyra.mjs', import.meta.url));
  const run = (...args) => execFileSync(process.execPath, [cli, ...args, '--root', root], { encoding: 'utf8' });
  let gateway;
  try {
    const printed = run('init-tls').trim().split(': ').at(-1);
    gateway = createGateway({ ...loadTls(root), devicesFile: join(root, '.seyra/devices.json'), password: 'test', internalToken: 'test' });
    gateway.listen(0, '127.0.0.1'); await once(gateway, 'listening');
    const fingerprint = await new Promise((resolve, reject) => {
      const socket = tls.connect({ host: '127.0.0.1', port: gateway.address().port, rejectUnauthorized: false }, () => {
        resolve(createHash('sha256').update(socket.getPeerCertificate().raw).digest('hex')); socket.end();
      });
      socket.once('error', reject);
    });
    assert.equal(printed, fingerprint);
    run('pair', '--name', 'test', '--url', 'https://localhost:8443');
    const pairing = parseEnvironment(readFileSync(join(root, '.seyra/pairing/test.env'), 'utf8'));
    const devices = readFileSync(join(root, '.seyra/devices.json'), 'utf8');
    assert.equal(devices.includes(pairing.SEYRA_DEVICE_TOKEN), false);
    assert.equal(JSON.parse(devices)[0].tokenSha256, createHash('sha256').update(pairing.SEYRA_DEVICE_TOKEN).digest('hex'));
    assert.equal(pairing.SEYRA_GATEWAY_CERT_SHA256, fingerprint);
    run('revoke', '--name', 'test');
    run('pair', '--name', 'test', '--url', 'https://localhost:8443');
    const renewed = JSON.parse(readFileSync(join(root, '.seyra/devices.json'), 'utf8'));
    assert.equal(renewed.length, 2); assert.equal(renewed[0].revoked, true); assert.equal(renewed[1].revoked, false);
  } finally {
    if (gateway) { gateway.closeAllConnections(); await new Promise(resolve => gateway.close(resolve)); }
    rmSync(root, { recursive: true, force: true });
  }
});
