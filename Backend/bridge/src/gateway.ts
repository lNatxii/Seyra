import https from 'node:https';
import http from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { createHash, randomUUID, timingSafeEqual } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { join } from 'node:path';
import { WebSocket } from 'ws';
import { createAsrRelay } from './asr.js';
import type { IncomingMessage, ServerResponse } from 'node:http';

type Device = { id: string; tokenSha256: string; revoked: boolean };
type Session = { id: string; device: string; socket: WebSocket; lastPoll: number; queue: string[]; bytes: number; delivered: Set<string>; poll?: ServerResponse; timer?: NodeJS.Timeout };
type Options = ({ cert: Buffer; key: Buffer; pfx?: never; passphrase?: never } | { pfx: Buffer; passphrase: string; cert?: never; key?: never })
  & { devicesFile: string; password: string; internalToken: string; openCodePort?: number; bridgePort?: number };
const hash = (text: string) => createHash('sha256').update(text).digest();
const send = (response: ServerResponse, status: number, body?: unknown) => {
  if (response.destroyed || response.writableEnded) return;
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  response.end(body === undefined ? undefined : JSON.stringify(body));
};
export function allowed(method: string, path: string) {
  const id = '[A-Za-z0-9_-]+';
  return method === 'GET' && path === '/api/agent'
    || (method === 'GET' || method === 'POST') && path === '/api/session'
    || method === 'GET' && new RegExp(`^/api/session/${id}(/(message|permission))?$`).test(path)
    || method === 'POST' && new RegExp(`^/api/session/${id}/(prompt|interrupt|permission/${id}/reply)$`).test(path);
}
async function body(request: IncomingMessage, maximum: number) {
  const parts: Buffer[] = []; let length = 0;
  for await (const part of request) {
    length += part.length;
    if (length > maximum) throw new Error('BODY_TOO_LARGE');
    parts.push(part);
  }
  return Buffer.concat(parts);
}
export function createGateway(options: Options) {
  if (!options.password || !options.internalToken) throw new Error('Gateway credentials are missing.');
  const relay = createAsrRelay({ apiKey: process.env.META_API_KEY, url: process.env.NODE_ENV === 'test' ? process.env.SEYRA_ASR_URL : undefined });
  let active: Session | undefined;
  const failures = new Map<string, { start: number; count: number; blockedUntil: number }>();
  function closeSession(session: Session) {
    if (active === session) active = undefined;
    clearTimeout(session.timer); if (session.poll) send(session.poll, 410);
    session.socket.terminate(); session.queue.length = 0; session.delivered.clear();
  }
  function deliver(session: Session) {
    if (!session.poll || !session.queue.length) return;
    const text = session.queue.shift()!; session.bytes -= Buffer.byteLength(text);
    session.delivered.add(JSON.parse(text).requestId);
    const response = session.poll; session.poll = undefined; clearTimeout(session.timer);
    response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); response.end(text);
  }
  async function authenticate(request: IncomingMessage, response: ServerResponse) {
    const ip = request.socket.remoteAddress ?? 'unknown'; const now = Date.now();
    const record = failures.get(ip);
    if (record && record.blockedUntil > now) { send(response, 429); return; }
    const header = request.headers.authorization ?? '';
    let devices: Device[] = [];
    try { devices = JSON.parse(readFileSync(options.devicesFile, 'utf8')); } catch { /* Fail closed. */ }
    const tokenHash = hash(header.startsWith('Bearer ') ? header.slice(7) : '');
    const device = devices.find(item => !item.revoked && /^[a-f0-9]{64}$/i.test(item.tokenSha256)
      && timingSafeEqual(Buffer.from(item.tokenSha256, 'hex'), tokenHash));
    if (header.startsWith('Bearer ') && device) return device;
    const failure = record && now - record.start < 60000 ? record : { start: now, count: 0, blockedUntil: 0 };
    failure.count++;
    if (failure.count > 20) failure.blockedUntil = now + 300000;
    failures.set(ip, failure);
    await new Promise(resolve => setTimeout(resolve, 500));
    send(response, failure.blockedUntil > now ? 429 : 401);
  }
  async function handle(request: IncomingMessage, response: ServerResponse) {
    const device = await authenticate(request, response); if (!device) return;
    const url = new URL(request.url ?? '/', 'https://gateway');
    if (await relay.handle(device, request, response, url)) return;
    const path = url.pathname;
    if (path === '/_seyra/bridge/hello' && request.method === 'POST') {
      await body(request, 1024);
      if (active) closeSession(active);
      const socket = new WebSocket(`ws://127.0.0.1:${options.bridgePort ?? 4100}`, {
        headers: { Authorization: `Bearer ${options.internalToken}` }, maxPayload: 8388608, perMessageDeflate: false
      });
      const session: Session = { id: randomUUID(), device: device.id, socket, lastPoll: Date.now(), queue: [], bytes: 0, delivered: new Set() };
      active = session;
      socket.on('close', () => closeSession(session));
      socket.on('error', () => closeSession(session));
      socket.on('message', (bytes, binary) => {
        if (active !== session || binary) return;
        const text = bytes.toString();
        try { const message = JSON.parse(text); if (message.type !== 'tool_request' || typeof message.requestId !== 'string') throw new Error(); }
        catch { closeSession(session); return; }
        session.bytes += Buffer.byteLength(text);
        if (session.queue.length >= 64 || session.delivered.size >= 64 || session.bytes > 8388608) { closeSession(session); return; }
        session.queue.push(text); deliver(session);
      });
      const opened = await new Promise<boolean>(resolve => {
        const timer = setTimeout(() => { socket.terminate(); resolve(false); }, 5000);
        socket.once('open', () => { clearTimeout(timer); resolve(true); });
        socket.once('error', () => { clearTimeout(timer); resolve(false); });
        socket.once('close', () => { clearTimeout(timer); resolve(false); });
      });
      if (!opened || active !== session) { closeSession(session); send(response, 503); return; }
      send(response, 200, { sessionId: session.id }); return;
    }
    if (path === '/_seyra/bridge/next' || path === '/_seyra/bridge/result') {
      const isNext = path.endsWith('/next');
      if (request.method !== (isNext ? 'GET' : 'POST')) { send(response, 404); return; }
      const session = active;
      if (!session || session.id !== url.searchParams.get('sessionId') || session.device !== device.id || session.socket.readyState !== WebSocket.OPEN) { send(response, 410); return; }
      if (isNext) {
        if (session.poll) { send(response, 409); return; }
        session.lastPoll = Date.now(); session.poll = response;
        const wait = Math.min(25, Math.max(0, Number(url.searchParams.get('wait') ?? 25) || 0));
        session.timer = setTimeout(() => { if (session.poll === response) { session.poll = undefined; send(response, 204); } }, wait * 1000);
        response.once('close', () => { if (session.poll === response) { session.poll = undefined; clearTimeout(session.timer); } });
        deliver(session); return;
      }
      const text = (await body(request, 8388608)).toString('utf8');
      const result = JSON.parse(text);
      if (active !== session || result.type !== 'tool_result' || !session.delivered.has(result.requestId)) { send(response, 409); return; }
      session.delivered.delete(result.requestId);
      await new Promise<void>((resolve, reject) => session.socket.send(text, error => error ? reject(error) : resolve()));
      send(response, 200, {}); return;
    }
    if (!allowed(request.method ?? '', path)) { request.resume(); send(response, 404); return; }
    const content = await body(request, 1048576);
    // Construct a minimal header set: device authorization, cookies and Origin never reach OpenCode.
    await new Promise<void>(resolve => {
      const upstream = http.request({ host: '127.0.0.1', port: options.openCodePort ?? 4096, path: path + url.search,
        method: request.method, headers: { Authorization: `Basic ${Buffer.from(`opencode:${options.password}`).toString('base64')}`,
          'Content-Type': 'application/json', 'Content-Length': content.length, Connection: 'close' } }, remote => {
        response.writeHead(remote.statusCode ?? 502, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
        remote.pipe(response); remote.once('end', resolve); remote.once('error', () => { response.destroy(); resolve(); });
      });
      upstream.setTimeout(120000, () => upstream.destroy());
      upstream.on('error', () => { send(response, 502); resolve(); });
      response.once('close', () => { upstream.destroy(); resolve(); });
      upstream.end(content);
    });
  }
  const server = https.createServer({ cert: options.cert, key: options.key, pfx: options.pfx, passphrase: options.passphrase, minVersion: 'TLSv1.2', maxHeaderSize: 16384 },
    (request, response) => { void handle(request, response).catch(error => send(response, error.message === 'BODY_TOO_LARGE' ? 413 : error instanceof SyntaxError ? 400 : 502)); });
  server.on('upgrade', (_, socket) => { socket.end('HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n'); });
  const sweep = setInterval(() => {
    if (active && Date.now() - active.lastPoll > 40000) closeSession(active);
    for (const [ip, record] of failures) if (Date.now() - Math.max(record.start + 60000, record.blockedUntil) > 0) failures.delete(ip);
  }, 2000);
  sweep.unref();
  server.on('close', () => { relay.close(); clearInterval(sweep); if (active) closeSession(active); });
  return server;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const root = process.env.SEYRA_DATA_ROOT ?? join(process.cwd(), '.seyra');
  const port = Number(process.env.SEYRA_GATEWAY_PORT ?? 8443);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid gateway port.');
  const certificate = join(root, 'tls/gateway.cert.pem');
  const key = join(root, 'tls/gateway.key.pem');
  const credentials = existsSync(certificate) && existsSync(key)
    ? { cert: readFileSync(certificate), key: readFileSync(key) }
    : { pfx: readFileSync(join(root, 'tls/gateway.pfx')), passphrase: readFileSync(join(root, 'tls/gateway.pass'), 'utf8').trim() };
  const server = createGateway({ ...credentials,
    devicesFile: join(root, 'devices.json'), password: process.env.OPENCODE_SERVER_PASSWORD ?? '', internalToken: process.env.SEYRA_BRIDGE_INTERNAL_TOKEN ?? '',
    openCodePort: Number(process.env.SEYRA_OPENCODE_PORT ?? 4096) });
  server.listen(port, process.env.SEYRA_GATEWAY_HOST ?? '0.0.0.0', () => console.log(`Seyra HTTPS gateway ready on port ${port}`));
  const stop = () => { server.closeAllConnections(); server.close(); };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
}
