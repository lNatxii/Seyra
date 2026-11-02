import { existsSync, readFileSync, writeFileSync, mkdirSync, unlinkSync, chmodSync } from 'node:fs';
import { resolve, join, dirname, delimiter } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { homedir, networkInterfaces } from 'node:os';
import { createRequire } from 'node:module';
import { createHash, randomBytes, randomUUID, webcrypto, X509Certificate } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { parseArgs } from 'node:util';
import tls from 'node:tls';

const backend = dirname(fileURLToPath(import.meta.url));
const require = createRequire(new URL('./bridge/package.json', import.meta.url));
const windows = process.platform === 'win32';
const hash = value => createHash('sha256').update(value).digest('hex');
const fail = message => { throw new Error(message); };
const readJson = file => existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : [];

export function parseEnvironment(text) {
  const values = {};
  for (const line of text.split(/\r?\n/)) {
    const entry = line.trim();
    if (!entry || entry.startsWith('#')) continue;
    const match = /^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(entry);
    if (!match) fail('Invalid .env.local format: use NAME=value, one variable per line.');
    let value = match[2].trim();
    if (value.length >= 2 && (value[0] === '"' && value.at(-1) === '"' || value[0] === "'" && value.at(-1) === "'")) value = value.slice(1, -1);
    values[match[1]] = value;
  }
  return values;
}

function privateWrite(file, text) {
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, text, { mode: 0o600 });
  if (!windows) chmodSync(file, 0o600);
}

export function loadTls(root) {
  const directory = join(root, '.seyra', 'tls');
  const cert = join(directory, 'gateway.cert.pem');
  const key = join(directory, 'gateway.key.pem');
  if (existsSync(cert) && existsSync(key)) return { cert: readFileSync(cert), key: readFileSync(key) };
  return { pfx: readFileSync(join(directory, 'gateway.pfx')), passphrase: readFileSync(join(directory, 'gateway.pass'), 'utf8').trim() };
}

export async function certificateFingerprint(options) {
  if (options.cert) return hash(new X509Certificate(options.cert).raw);
  // Let Node's TLS implementation decode legacy PKCS#12 certificates.
  const server = tls.createServer(options, socket => socket.end());
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    return await new Promise((resolve, reject) => {
      const socket = tls.connect({ host: '127.0.0.1', port: server.address().port, rejectUnauthorized: false }, () => {
        const raw = socket.getPeerCertificate().raw;
        socket.end(); resolve(hash(raw));
      });
      socket.setTimeout(5000, () => socket.destroy(new Error('Certificate inspection timed out.')));
      socket.once('error', reject);
    });
  } finally { await new Promise(resolve => server.close(resolve)); }
}

function validName(name) {
  if (!/^[A-Za-z0-9_-]{1,64}$/.test(name ?? '')) fail('Name must contain 1-64 ASCII letters, digits, underscores or hyphens.');
}

export function findExecutable(name) {
  const extensions = windows ? (process.env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD').split(';') : [''];
  for (const directory of (process.env.PATH ?? '').split(delimiter)) {
    if (!directory) continue;
    for (const extension of extensions) {
      const file = join(directory.replace(/^"|"$/g, ''), name + extension.toLowerCase());
      if (existsSync(file)) return file;
    }
  }
}

export function findAdb() {
  const executable = windows ? 'adb.exe' : 'adb';
  const found = findExecutable('adb');
  if (found) return found;
  const candidates = [process.env.ANDROID_SDK_ROOT, process.env.ANDROID_HOME].filter(Boolean).map(root => join(root, 'platform-tools', executable));
  const versionFile = join(backend, '..', 'SeyraUnity', 'ProjectSettings', 'ProjectVersion.txt');
  const version = existsSync(versionFile) && /^m_EditorVersion: ([\w.]+)$/m.exec(readFileSync(versionFile, 'utf8').replace(/\r/g, ''))?.[1];
  if (version) {
    const editor = windows ? join(process.env.ProgramFiles ?? 'C:/Program Files', 'Unity/Hub/Editor', version, 'Editor/Data')
      : process.platform === 'darwin' ? join('/Applications/Unity/Hub/Editor', version) : join(homedir(), 'Unity/Hub/Editor', version, 'Editor/Data');
    candidates.push(join(editor, 'PlaybackEngines/AndroidPlayer/SDK/platform-tools', executable));
  }
  return candidates.find(file => existsSync(file)) ?? fail('adb was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.');
}

function pushPairing(root, values) {
  validName(values.name);
  if (!/^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$/.test(values.package ?? '')) fail('Package must be an Android application ID.');
  const adb = findAdb();
  const run = args => execFileSync(adb, args, { encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let devices;
  try { devices = run(['devices']).split(/\r?\n/).map(line => /^(\S+)\s+(device|unauthorized|offline)\b/.exec(line)).filter(Boolean); }
  catch { fail('Could not list adb devices. Check Android platform-tools.'); }
  const available = devices.filter(item => item[2] === 'device');
  if (values.serial && devices.some(item => item[1] === values.serial && item[2] === 'unauthorized')) fail('Accept USB debugging on the device, then try again.');
  if (!values.serial && !available.length && devices.some(item => item[2] === 'unauthorized')) fail('Accept USB debugging on the device, then try again.');
  const serial = values.serial ?? (available.length === 1 ? available[0][1] : undefined);
  if (!serial) fail(available.length > 1 ? 'Multiple devices connected. Specify --serial.' : 'No authorized adb device connected. Connect a device and enable USB debugging.');
  if (!available.some(item => item[1] === serial)) fail('Selected device is not connected and authorized.');
  const execute = args => run(['-s', serial, ...args]);
  const file = join(root, '.seyra', 'pairing', `${values.name}.env`);
  if (!existsSync(file)) fail('Pairing file is missing. Run pair first.');
  try {
    if (!execute(['shell', 'pm', 'list', 'packages', values.package]).split(/\r?\n/).includes(`package:${values.package}`)) fail('Package is not installed. Install the APK first.');
  } catch (error) { fail(error.message === 'Package is not installed. Install the APK first.' ? error.message : 'Could not inspect installed packages. Check the device connection.'); }
  const destination = `/sdcard/Android/data/${values.package}/files`;
  try {
    execute(['shell', 'mkdir', '-p', destination]);
    execute(['push', file, `${destination}/.env.local`]);
    execute(['shell', 'ls', `${destination}/.env.local`]);
  } catch { fail('Could not push or verify pairing. Open the app once and retry; check device permissions. Local copy retained.'); }
  unlinkSync(file);
  console.log(`Paired file pushed to ${serial}; local copy deleted.`);
}

function portNumber(value) {
  if (!/^\d+$/.test(value) || Number(value) < 1 || Number(value) > 65535) fail('Port must be an integer from 1 to 65535.');
  return String(Number(value));
}

function launch(command, args, options) {
  const child = spawn(command, args, { stdio: 'inherit', windowsHide: true, ...options });
  child.completion = new Promise(resolve => {
    child.once('error', () => resolve(1));
    child.once('exit', (code, signal) => resolve(code ?? (signal === 'SIGINT' ? 130 : 1)));
  });
  return child;
}

async function stopChild(child, tree = false) {
  if (!child?.pid || child.exitCode !== null || child.signalCode !== null) return;
  if (windows && tree) {
    // Terminate the shell, OpenCode and its stdio MCP descendants together.
    try { execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); } catch { /* Already stopped. */ }
  } else if (!windows && tree) {
    try { process.kill(-child.pid, 'SIGTERM'); } catch { /* Already stopped. */ }
  } else child.kill();
  if (await Promise.race([child.completion.then(() => true), delay(2000).then(() => false)])) return;
  if (windows) {
    try { execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); } catch { /* Already stopped. */ }
  } else {
    try { tree ? process.kill(-child.pid, 'SIGKILL') : child.kill('SIGKILL'); } catch { /* Already stopped. */ }
  }
  await child.completion;
}

async function start(root, values) {
  const envPath = join(root, '.env.local');
  if (!existsSync(envPath)) fail('Backend/.env.local is missing. See README.md to create the file.');
  const local = parseEnvironment(readFileSync(envPath, 'utf8'));
  const env = { ...process.env, ...local };
  if (!env.META_API_KEY?.trim() && env.MODEL_API_KEY?.trim()) env.META_API_KEY = env.MODEL_API_KEY;
  if (!env.META_API_KEY?.trim() || env.META_API_KEY === 'TU_CLAVE') fail('Add META_API_KEY (Meta Model API key from dev.meta.ai) to Backend/.env.local');
  if (!local.OPENCODE_SERVER_PASSWORD?.trim()) fail('OPENCODE_SERVER_PASSWORD is missing from Backend/.env.local. Add your own HTTP password; it is separate from provider API keys. OpenCode was not started.');
  env.OPENCODE_PASSWORD = local.OPENCODE_SERVER_PASSWORD;
  if (!findExecutable('opencode2')) fail('opencode2 was not found in PATH. Install the OpenCode v2 CLI before continuing.');
  if (!existsSync(join(backend, 'bridge/node_modules'))) fail('Bridge dependencies are missing. Run npm ci --prefix Backend/bridge from the repository root.');
  const port = portNumber(values.port);
  const gatewayPort = portNumber(values['gateway-port']);
  if (values.gateway && values['no-gateway']) fail('Use either --gateway or --no-gateway.');
  const build = launch('npm', ['run', 'build'], { cwd: join(backend, 'bridge'), shell: windows, env });
  if (await build.completion !== 0) fail('Could not build the bridge. OpenCode was not started.');
  env.SEYRA_BACKEND_DIR = backend.replace(/\\/g, '/');
  env.SEYRA_OPENCODE_DATA_DIR = join(homedir(), '.local/share/opencode').replace(/\\/g, '/');
  // Explicit config keeps an alternate root isolated from local repository secrets.
  env.OPENCODE_CONFIG = join(backend, 'opencode.json');
  const directory = join(root, '.seyra/tls');
  const hasCertificate = existsSync(join(directory, 'gateway.pfx')) || existsSync(join(directory, 'gateway.cert.pem')) && existsSync(join(directory, 'gateway.key.pem'));
  const useGateway = values.gateway || !values['no-gateway'] && hasCertificate;
  delete env.SEYRA_BRIDGE_INTERNAL_TOKEN;
  let gateway;
  let openCode;
  let stopping;
  let requestedSignal;
  const stop = () => stopping ??= (async () => { await stopChild(openCode, true); await stopChild(gateway); })();
  const interrupt = signal => { requestedSignal = signal; void stop(); };
  const onInterrupt = () => interrupt('SIGINT');
  const onTerminate = () => interrupt('SIGTERM');
  process.on('SIGINT', onInterrupt); process.on('SIGTERM', onTerminate);
  try {
    if (useGateway) {
      if (!hasCertificate) fail('Gateway TLS is missing. Run init-tls first.');
      const fingerprint = await certificateFingerprint(loadTls(root));
      if (requestedSignal) return 130;
      env.SEYRA_BRIDGE_INTERNAL_TOKEN = randomBytes(32).toString('base64url');
      Object.assign(env, { SEYRA_GATEWAY_HOST: values['gateway-host'], SEYRA_GATEWAY_PORT: gatewayPort, SEYRA_OPENCODE_PORT: port, SEYRA_DATA_ROOT: join(root, '.seyra') });
      gateway = launch(process.execPath, [join(backend, 'bridge/dist/gateway.js')], { cwd: root, env });
      const earlyExit = await Promise.race([gateway.completion.then(() => true), delay(500).then(() => false)]);
      if (earlyExit) fail('Gateway failed to start.');
      if (requestedSignal) return 130;
      console.log(`Gateway: https://${values['gateway-host']}:${gatewayPort}`);
      for (const addresses of Object.values(networkInterfaces())) for (const address of addresses ?? []) {
        if (address.family === 'IPv4' && !address.internal && !address.address.startsWith('169.254.')) console.log(`LAN candidate: https://${address.address}:${gatewayPort}`);
      }
      console.log(`Certificate SHA-256: ${fingerprint}`);
    }
    if (requestedSignal) return 130;
    // Arguments are fixed strings and validated numeric ports. No user text enters a shell.
    openCode = launch('opencode2', ['serve', '--hostname', '127.0.0.1', '--port', port, '--print-logs', '--log-level', values.verbose ? 'info' : 'warn'], { cwd: root, env, shell: windows, detached: !windows });
    const result = await Promise.race([
      openCode.completion,
      ...(gateway ? [gateway.completion.then(() => { if (!requestedSignal) console.error('Gateway stopped unexpectedly.'); return 1; })] : []),
    ]);
    return requestedSignal ? requestedSignal === 'SIGINT' ? 130 : 143 : result;
  } finally {
    await stop();
    process.off('SIGINT', onInterrupt); process.off('SIGTERM', onTerminate);
  }
}

export async function main(args = process.argv.slice(2)) {
  const { values, positionals } = parseArgs({ args, allowPositionals: true, options: {
    root: { type: 'string', default: backend }, name: { type: 'string' }, url: { type: 'string' },
    force: { type: 'boolean' }, package: { type: 'string' }, serial: { type: 'string' }, verbose: { type: 'boolean' },
    port: { type: 'string', default: '4096' }, gateway: { type: 'boolean' }, 'no-gateway': { type: 'boolean' },
    'gateway-host': { type: 'string', default: '0.0.0.0' }, 'gateway-port': { type: 'string', default: '8443' },
  } });
  const [command] = positionals;
  if (positionals.length !== 1) fail('Usage: node Backend/seyra.mjs <start|init-tls|pair|list|revoke|push> [options]');
  const root = resolve(values.root);
  const data = join(root, '.seyra');
  if (command === 'start') { process.exitCode = await start(root, values); return; }
  if (command === 'init-tls') {
    const directory = join(data, 'tls');
    if (!values.force && ['gateway.cert.pem', 'gateway.key.pem', 'gateway.pfx'].some(file => existsSync(join(directory, file)))) fail('TLS already exists. Use --force explicitly to rotate it, then pair devices again.');
    require('reflect-metadata');
    const { X509CertificateGenerator, cryptoProvider } = require('@peculiar/x509');
    cryptoProvider.set(webcrypto);
    const algorithm = { name: 'ECDSA', namedCurve: 'P-256', hash: 'SHA-256' };
    const keys = await webcrypto.subtle.generateKey(algorithm, true, ['sign', 'verify']);
    const now = new Date(); const expires = new Date(now); expires.setFullYear(expires.getFullYear() + 2);
    const cert = await X509CertificateGenerator.createSelfSigned({ serialNumber: randomBytes(16).toString('hex'), name: 'CN=seyra-gateway', notBefore: now, notAfter: expires, signingAlgorithm: algorithm, keys });
    const key = Buffer.from(await webcrypto.subtle.exportKey('pkcs8', keys.privateKey));
    privateWrite(join(directory, 'gateway.key.pem'), `-----BEGIN PRIVATE KEY-----\n${key.toString('base64').match(/.{1,64}/g).join('\n')}\n-----END PRIVATE KEY-----\n`);
    privateWrite(join(directory, 'gateway.cert.pem'), cert.toString('pem'));
    console.log(`Gateway certificate SHA-256: ${hash(Buffer.from(cert.rawData))}`);
    return;
  }
  if (command === 'push') return pushPairing(root, values);
  if (!['pair', 'list', 'revoke'].includes(command)) fail('Unknown command. Use start, init-tls, pair, list, revoke or push.');
  const devicesPath = join(data, 'devices.json');
  const devices = readJson(devicesPath);
  if (command === 'list') { console.log(JSON.stringify(devices.map(({ id, name, createdAt, revoked }) => ({ id, name, createdAt, revoked })), null, 2)); return; }
  validName(values.name);
  if (command === 'revoke') {
    const matches = devices.filter(device => device.name === values.name);
    if (!matches.length) fail('Device not found.');
    for (const device of matches) device.revoked = true;
    privateWrite(devicesPath, JSON.stringify(devices, null, 2));
    console.log(`Revoked device: ${values.name}`); return;
  }
  let endpoint;
  try { endpoint = new URL(values.url); } catch { /* Rejected below. */ }
  if (!endpoint || endpoint.protocol !== 'https:' || endpoint.username || endpoint.password || endpoint.pathname !== '/' || endpoint.search || endpoint.hash) fail('Url must be an HTTPS origin with no credentials, path, query or fragment.');
  if (devices.some(device => device.name === values.name && !device.revoked)) fail('An active device with that name exists. Revoke it before pairing again.');
  const fingerprint = await certificateFingerprint(loadTls(root));
  const token = randomBytes(32).toString('base64url');
  devices.push({ id: randomUUID(), name: values.name, tokenSha256: hash(token), createdAt: new Date().toISOString(), revoked: false });
  const file = join(data, 'pairing', `${values.name}.env`);
  privateWrite(file, `SEYRA_GATEWAY_URL=${endpoint.origin}\nSEYRA_DEVICE_TOKEN=${token}\nSEYRA_GATEWAY_CERT_SHA256=${fingerprint}\n`);
  privateWrite(devicesPath, JSON.stringify(devices, null, 2));
  console.log(`Pairing file: ${file}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch(error => {
    // Do not echo arguments, file contents or child stderr, which may contain secrets.
    const message = error.code ? `Operation failed (${error.code}). Check paths, permissions and dependencies.` : error.message;
    console.error(message.replace(/[\r\n]+/g, ' ')); process.exitCode = 1;
  });
}
