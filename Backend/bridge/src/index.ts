import { randomUUID, createHash, timingSafeEqual } from 'node:crypto';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { WebSocket, WebSocketServer } from 'ws';
import { z } from 'zod';
import { tools } from './tools.js';
import { createImageService } from './images.js';
import { cameraTool, cameraSegmentTool, createCameraService } from './camera.js';

const log = (message: string) => console.error(`[seyra] ${message}`);
function integer(name: string, fallback: number, maximum: number) {
  const value = Number(process.env[name] ?? fallback);
  if (!Number.isSafeInteger(value) || value < 1 || value > maximum) throw new Error(`Invalid ${name}`);
  return value;
}
const port = integer('SEYRA_UNITY_PORT', 4100, 65535);
const timeoutMs = integer('SEYRA_UNITY_TIMEOUT_MS', 10000, 60000);
type Outcome = { success: true; data: Record<string, unknown> } |
  { success: false; error: { code: string; message: string; maxItems?: number; perWall?: Record<string, unknown>[]; rootId?: string; index?: number }; outcomeUnknown?: boolean };
const failure = (code: string, message: string, outcomeUnknown = false): Outcome => ({ success: false, error: { code, message }, ...(outcomeUnknown ? { outcomeUnknown } : {}) });
const resultSchema = z.discriminatedUnion('success', [
  z.object({ version: z.literal(1), type: z.literal('tool_result'), requestId: z.uuid(), success: z.literal(true), data: z.record(z.string(), z.unknown()) }).strict(),
  z.object({ version: z.literal(1), type: z.literal('tool_result'), requestId: z.uuid(), success: z.literal(false), error: z.object({
    code: z.string().min(1).max(100), message: z.string().min(1).max(4096),
    maxItems: z.number().int().min(0).optional(),
    perWall: z.array(z.record(z.string(), z.unknown())).max(64).optional(),
    rootId: z.string().min(1).max(128).optional(),
    index: z.number().int().min(0).optional(),
  }).strict() }).strict(),
]);
const pending = new Map<string, { socket: WebSocket; timer: NodeJS.Timeout; resolve: (value: Outcome) => void }>();
let unity: WebSocket | undefined;
let closing = false;
const server = new McpServer({ name: 'seyra', version: '0.2.0' });
const transport = new StdioServerTransport();
const sockets = new WebSocketServer({ host: '127.0.0.1', port, maxPayload: 8388608, perMessageDeflate: false });

function settle(id: string, result: Outcome) {
  const entry = pending.get(id);
  if (!entry) return;
  clearTimeout(entry.timer); pending.delete(id); entry.resolve(result);
}
function disconnected(socket: WebSocket) {
  if (unity === socket) { unity = undefined; log('Unity disconnected'); }
  for (const [id, entry] of pending) if (entry.socket === socket)
    settle(id, failure('UNITY_DISCONNECTED', 'Unity disconnected before confirmation. Outcome unknown; do not retry mutations automatically.', true));
}
const internalToken = process.env.SEYRA_BRIDGE_INTERNAL_TOKEN;
function authorized(header: string | undefined) {
  if (!internalToken) return true;
  return timingSafeEqual(createHash('sha256').update(header ?? '').digest(),
    createHash('sha256').update(`Bearer ${internalToken}`).digest());
}
const alive = new WeakSet<WebSocket>();
const heartbeat = setInterval(() => {
  for (const socket of sockets.clients) {
    if (!alive.has(socket)) { disconnected(socket); socket.terminate(); continue; }
    alive.delete(socket); socket.ping();
  }
}, 10000);
heartbeat.unref();
sockets.on('connection', (socket, request) => {
  socket.on('error', () => { disconnected(socket); socket.terminate(); });
  // Only an authenticated native runtime can replace the current connection.
  if (closing || request.headers.origin || !authorized(request.headers.authorization)) { socket.terminate(); return; }
  const previous = unity;
  if (previous) { disconnected(previous); previous.terminate(); }
  alive.add(socket); socket.on('pong', () => alive.add(socket));
  unity = socket; log('Unity connected');
  socket.on('close', () => disconnected(socket));
  socket.on('message', (bytes, binary) => {
    if (unity !== socket || closing || binary) return;
    let raw;
    try { raw = JSON.parse(bytes.toString()); } catch { return; }
    const parsed = resultSchema.safeParse(raw);
    if (!parsed.success) {
      const code = raw?.error?.code;
      log(`Ignored invalid tool_result (${code ?? 'no code'})`);
      if (raw?.type === 'tool_result' && typeof raw.requestId === 'string' && pending.get(raw.requestId)?.socket === socket)
        settle(raw.requestId, failure('BRIDGE_INVALID_RESULT', 'Unity returned a result the bridge could not validate. Outcome unknown; inspect state before any new mutation.', true));
      return;
    }
    const result = parsed.data;
    if (pending.get(result.requestId)?.socket !== socket) return;
    settle(result.requestId, result.success ? { success: true, data: result.data } : { success: false, error: result.error, ...(result.error.code === 'ROLLBACK_FAILED' ? { outcomeUnknown: true } : {}) });
  });
});

async function invoke(name: string, args: unknown) {
  const respond = (result: Outcome) => ({ isError: !result.success, content: [{ type: 'text' as const, text: JSON.stringify(result) }] });
  const socket = unity;
  let result: Outcome;
  if (closing || !socket || socket.readyState !== WebSocket.OPEN) result = failure('UNITY_NOT_CONNECTED', 'Open a configured Unity scene in Play Mode. No request was sent.');
  else if (pending.size >= 64) result = failure('BRIDGE_BUSY', 'Too many pending requests. No request was sent.');
  else {
    const requestId = randomUUID();
    const request = JSON.stringify({ version: 1, type: 'tool_request', requestId, tool: name, arguments: args, deadlineUtc: new Date(Date.now() + timeoutMs).toISOString() });
    result = await new Promise<Outcome>(resolve => {
      const timer = setTimeout(() => settle(requestId, failure('UNITY_TIMEOUT', 'Unity did not confirm before timeout. Outcome unknown; inspect state before any new mutation. Never retry automatically.', true)), timeoutMs);
      pending.set(requestId, { socket, timer, resolve });
      socket.send(request, error => { if (error) { disconnected(socket); socket.terminate(); } });
    });
  }
  return respond(result);
}
const images = createImageService({ invoke });
const imageTools = new Set(['ui_present', 'ui_update', 'layout_create']);
for (const tool of tools) server.registerTool(tool.name, {
  description: tool.description,
  inputSchema: tool.schema,
  annotations: { readOnlyHint: tool.readOnly, destructiveHint: false, idempotentHint: tool.readOnly },
}, (args: unknown) => imageTools.has(tool.name) ? images.execute(tool.name, args) : invoke(tool.name, args));
const camera = createCameraService({ invoke });
server.registerTool(cameraTool.name, {
  description: cameraTool.description,
  inputSchema: cameraTool.schema,
  annotations: { readOnlyHint: cameraTool.readOnly, destructiveHint: false, idempotentHint: false },
}, args => camera.look(args));
server.registerTool(cameraSegmentTool.name, {
  description: cameraSegmentTool.description,
  inputSchema: cameraSegmentTool.schema,
  annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
}, args => camera.segment(args));

async function shutdown(code = 0) {
  if (closing) return;
  closing = true; clearInterval(heartbeat);
  for (const id of pending.keys()) settle(id, failure('UNITY_DISCONNECTED', 'Bridge stopped before confirmation. Outcome unknown.', true));
  for (const socket of sockets.clients) socket.terminate();
  sockets.close(); await server.close(); process.exitCode = code; process.stdin.destroy();
}
process.once('SIGINT', () => { void shutdown(); });
process.once('SIGTERM', () => { void shutdown(); });
process.stdin.once('end', () => { void shutdown(); });
process.stdin.once('close', () => { void shutdown(); });
sockets.on('error', error => { log(error.message); void shutdown(1); });
sockets.once('listening', () => {
  log(`Listening on 127.0.0.1:${port}`);
  void server.connect(transport).catch(error => { log(String(error)); void shutdown(1); });
});
