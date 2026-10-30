import { randomUUID } from 'node:crypto';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { WebSocket } from 'ws';

type Options = { apiKey?: string; url?: string; tickMs?: number; languageBias?: string[]; keywords?: string[] };
type Session = {
  id: string; device: string; socket: WebSocket; buffer: Buffer[]; bytes: number;
  version: number; partial: string; final: string | null; error: string | null;
  done: boolean; stopping: boolean; ended: boolean; buffering: boolean; lastAudio: number; lastPoll: number;
  tick?: NodeJS.Timeout; deadline?: NodeJS.Timeout; retention?: NodeJS.Timeout;
  poll?: ServerResponse; pollTimer?: NodeJS.Timeout; acknowledge?: (ok: boolean) => void;
};
const send = (response: ServerResponse, status: number, value: unknown = {}) => {
  if (response.destroyed || response.writableEnded) return;
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  response.end(JSON.stringify(value));
};
async function readBody(request: IncomingMessage, maximum: number) {
  const parts: Buffer[] = []; let length = 0;
  // Drain oversized requests so the HTTP error can still reach the caller.
  for await (const part of request) { length += part.length; if (length <= maximum) parts.push(part); }
  return length > maximum ? null : Buffer.concat(parts);
}
export function createAsrRelay(options: Options) {
  let active: Session | undefined;
  const snapshot = (s: Session) => ({ version: s.version, partial: s.partial, final: s.final, done: s.done, error: s.error });
  function flush(s: Session) {
    if (!s.poll) return;
    const response = s.poll; s.poll = undefined; clearTimeout(s.pollTimer);
    send(response, 200, snapshot(s));
  }
  function dispose(s: Session) {
    if (active === s) active = undefined;
    clearInterval(s.tick); clearTimeout(s.deadline); clearTimeout(s.retention); clearTimeout(s.pollTimer);
    s.acknowledge?.(false);
    if (s.poll) { send(s.poll, 410); s.poll = undefined; }
    s.buffer = []; s.bytes = 0; s.socket.terminate();
  }
  function finish(s: Session, error: string | null, final: string | null = null) {
    if (s.done || s.error || active !== s) return;
    s.error = error; s.final = final; s.done = error === null; s.version++;
    clearInterval(s.tick); clearTimeout(s.deadline); s.buffer = []; s.bytes = 0;
    s.acknowledge?.(false); flush(s);
    if (s.socket.readyState === WebSocket.OPEN) s.socket.close(1000);
    else s.socket.terminate();
    s.retention = setTimeout(() => dispose(s), 30000); s.retention.unref();
  }
  function tick(s: Session, started: number) {
    if (active !== s || s.done || s.error) return;
    const now = Date.now();
    if (now - s.lastPoll >= 30000) { dispose(s); return; }
    if (now - started >= 60000) s.stopping = true;
    if (!s.stopping && now - s.lastAudio >= 5000) { finish(s, 'ASR_IDLE'); return; }
    if (s.socket.readyState !== WebSocket.OPEN) return;
    if (!s.stopping && s.buffering) {
      if (s.bytes < 9600) { s.socket.send(Buffer.alloc(3200)); return; }
      s.buffering = false;
    }
    if (s.bytes) {
      let remaining = Math.min(6400, s.bytes); const chunks: Buffer[] = [];
      while (remaining) {
        const head = s.buffer[0]; const count = Math.min(head.length, remaining);
        chunks.push(head.subarray(0, count)); remaining -= count; s.bytes -= count;
        if (count === head.length) s.buffer.shift(); else s.buffer[0] = head.subarray(count);
      }
      s.socket.send(Buffer.concat(chunks));
      if (!s.bytes && !s.stopping) s.buffering = true;
    } else if (!s.stopping) s.socket.send(Buffer.alloc(3200));
    else if (!s.ended) {
      s.ended = true; s.socket.send(JSON.stringify({ type: 'endStream' }));
      s.deadline = setTimeout(() => finish(s, 'ASR_TIMEOUT'), 15000);
    }
  }
  async function handle(device: { id: string }, request: IncomingMessage, response: ServerResponse, url: URL): Promise<boolean> {
    if (!url.pathname.startsWith('/_seyra/asr/')) return false;
    const action = url.pathname.slice('/_seyra/asr/'.length);
    if (!['start', 'audio', 'events', 'stop', 'cancel'].includes(action) || request.method !== (action === 'events' ? 'GET' : 'POST')) {
      request.resume(); send(response, 404); return true;
    }
    if (action === 'start') {
      if (!options.apiKey?.trim()) { request.resume(); send(response, 503, { error: 'ASR_NOT_CONFIGURED' }); return true; }
      const body = await readBody(request, 1024);
      if (!body) { send(response, 413); return true; }
      try { JSON.parse(body.toString()); } catch { send(response, 400); return true; }
      if (active) dispose(active);
      const id = randomUUID(); const endpoint = new URL(options.url ?? 'wss://api.meta.ai/v1/asr/realtime'); endpoint.searchParams.set('sessionId', id);
      const socket = new WebSocket(endpoint, { maxPayload: 1048576, perMessageDeflate: false, handshakeTimeout: 8000 });
      const now = Date.now();
      const s: Session = { id, device: device.id, socket, buffer: [], bytes: 0, version: 0, partial: '', final: null, error: null, done: false, stopping: false, ended: false, buffering: true, lastAudio: now, lastPoll: now };
      active = s;
      const accepted = new Promise<boolean>(resolve => {
        const timer = setTimeout(() => { finish(s, 'ASR_UPSTREAM'); resolve(false); }, 8000);
        s.acknowledge = ok => { clearTimeout(timer); s.acknowledge = undefined; resolve(ok); };
      });
      socket.on('open', () => {
        if (active !== s) return;
        socket.send(JSON.stringify({ authorization: { accessToken: `Bearer ${options.apiKey}` }, model: 'muse-voice-transcribe-1.0',
          audioEncoding: 'PCM_16KHZ', mode: 'PUSH_TO_TALK', partialMode: 'CUMULATIVE', emitAudioProgress: false,
          languageBias: options.languageBias ?? ['English'], keywords: options.keywords ?? ['Seyra'] }));
      });
      socket.on('message', (bytes, binary) => {
        if (active !== s || s.done || s.error || binary) return;
        let message; try { message = JSON.parse(bytes.toString()); } catch { finish(s, 'ASR_UPSTREAM'); return; }
        if (s.acknowledge && message.type === undefined && typeof message.sessionId === 'string') {
          s.acknowledge(true); s.tick = setInterval(() => tick(s, now), options.tickMs ?? 100); s.tick.unref();
        } else if (message.type === 'transcript' && typeof message.transcript === 'string') {
          if (message.final === true) finish(s, null, message.transcript);
          else { s.partial = message.transcript; s.version++; flush(s); }
        } else if (message.type === 'error') {
          // Keep logs limited to correlation, code and duration (privacy contract).
          console.error(`ASR ${s.id} ASR_UPSTREAM ${Date.now() - now}ms`); finish(s, 'ASR_UPSTREAM');
        }
      });
      socket.on('error', () => finish(s, 'ASR_UPSTREAM'));
      socket.on('close', code => finish(s, code === 1013 ? 'ASR_RATE_LIMITED' : 'ASR_UPSTREAM'));
      const ok = await accepted;
      send(response, ok && active === s && !s.error ? 200 : 502, ok && active === s && !s.error ? { asrId: id } : { error: 'ASR_UPSTREAM' });
      return true;
    }
    const s = active;
    if (!s || s.id !== url.searchParams.get('asrId') || s.device !== device.id) {
      request.resume(); send(response, action === 'cancel' ? 200 : 410); return true;
    }
    if (action === 'events') {
      if (s.poll) { send(response, 409); return true; }
      s.lastPoll = Date.now();
      const after = Number(url.searchParams.get('after') ?? -1);
      const wait = Math.min(10, Math.max(0, Number(url.searchParams.get('wait') ?? 10) || 0));
      if (s.version > after || s.done || s.error || !wait) send(response, 200, snapshot(s));
      else {
        s.poll = response; s.pollTimer = setTimeout(() => flush(s), wait * 1000);
        response.once('close', () => { if (s.poll === response) { s.poll = undefined; clearTimeout(s.pollTimer); } });
      }
      return true;
    }
    const content = await readBody(request, action === 'audio' ? 65536 : 1024);
    if (!content) { send(response, 413); return true; }
    if (active !== s) { send(response, action === 'cancel' ? 200 : 410); return true; }
    if (action === 'cancel') { dispose(s); send(response, 200); return true; }
    if (action === 'stop') { s.stopping = true; send(response, 200); return true; }
    if (!content.length || content.length % 2) { send(response, 400); return true; }
    if (s.stopping || s.done || s.error) { send(response, 409); return true; }
    s.lastAudio = Date.now();
    if (s.bytes + content.length > 96000) { finish(s, 'ASR_BACKLOG'); send(response, 409, { error: 'ASR_BACKLOG' }); return true; }
    s.buffer.push(content); s.bytes += content.length; send(response, 200); return true;
  }
  return { handle, close() { if (active) dispose(active); } };
}
