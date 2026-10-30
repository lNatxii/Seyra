import assert from 'node:assert/strict';
import test from 'node:test';
import { once } from 'node:events';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import https from 'node:https';
import { createGateway } from '../dist/gateway.js';

test('TTS config requires authentication and configuration and forbids caching', async () => {
  const root = mkdtempSync(join(tmpdir(), 'seyra-tts-'));
  execFileSync('powershell.exe', ['-NoProfile', '-Command', `
    $key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=seyra-test',$key,[Security.Cryptography.HashAlgorithmName]::SHA256)
    $cert=$request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1),[DateTimeOffset]::Now.AddDays(1))
    [IO.File]::WriteAllBytes((Join-Path $env:SEYRA_TEST_CERT_ROOT 'test.pfx'),$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,'fixture'))
    $cert.Dispose();$key.Dispose()
  `], { env: { ...process.env, SEYRA_TEST_CERT_ROOT: root } });
  const devicesFile = join(root, 'devices.json');
  writeFileSync(devicesFile, JSON.stringify([{ id: 'fixture', tokenSha256: createHash('sha256').update('device-fixture').digest('hex'), revoked: false }]));
  const previous = process.env.WITAI_CLIENT_TOKEN;
  const fixture = randomUUID();
  const gateway = createGateway({ pfx: readFileSync(join(root, 'test.pfx')), passphrase: 'fixture', devicesFile, password: 'fixture', internalToken: 'fixture' });
  gateway.listen(0, '127.0.0.1'); await once(gateway, 'listening');
  function request(authenticated) {
    return new Promise((resolve, reject) => {
      const req = https.get({ host: '127.0.0.1', port: gateway.address().port, path: '/_seyra/tts/config', rejectUnauthorized: false,
        headers: { Connection: 'close', ...(authenticated ? { Authorization: 'Bearer device-fixture' } : {}) } }, res => {
        let body = ''; res.on('data', part => body += part); res.on('end', () => resolve({ status: res.statusCode, cache: res.headers['cache-control'], body }));
      }); req.on('error', reject);
    });
  }
  try {
    process.env.WITAI_CLIENT_TOKEN = fixture;
    const configured = await request(true);
    assert.equal(configured.status, 200); assert.equal(configured.cache, 'no-store');
    assert.ok(JSON.parse(configured.body).clientToken === fixture, 'Expected the synthetic credential.');
    delete process.env.WITAI_CLIENT_TOKEN;
    const missing = await request(true);
    assert.equal(missing.status, 503); assert.equal(missing.cache, 'no-store'); assert.deepEqual(JSON.parse(missing.body), { error: 'TTS_NOT_CONFIGURED' });
    const unauthenticated = await request(false);
    assert.equal(unauthenticated.status, 401); assert.equal(unauthenticated.cache, 'no-store');
  } finally {
    if (previous === undefined) delete process.env.WITAI_CLIENT_TOKEN; else process.env.WITAI_CLIENT_TOKEN = previous;
    gateway.closeAllConnections(); await new Promise(resolve => gateway.close(resolve));
    rmSync(root, { recursive: true, force: true });
  }
});
