import https from 'node:https';
import { readFileSync } from 'node:fs';
import { WebSocketServer } from 'ws';
// Test-only server. Keep certificate material outside Assets and source control.
const root = process.argv[2];
const host = process.argv[3] ?? '127.0.0.1';
const server = https.createServer({ pfx: readFileSync(`${root}/gateway.pfx`),
  passphrase: readFileSync(`${root}/gateway.pass`, 'utf8').trim(), minVersion: 'TLSv1.2' },
  (request, response) => {
    request.resume();
    const url = new URL(request.url, 'https://pinning-fixture');
    response.setHeader('Content-Type', 'application/json');
    if (request.method === 'GET' && url.pathname === '/') return response.end('{}');
    if (request.method === 'POST' && url.pathname === '/_seyra/bridge/hello')
      return response.end(JSON.stringify({ sessionId: 'pinning-fixture' }));
    if (request.method === 'GET' && url.pathname === '/_seyra/bridge/next') {
      response.writeHead(204); return response.end();
    }
    if (request.method === 'POST' && url.pathname === '/_seyra/bridge/result') return response.end('{}');
    response.writeHead(404); response.end('{}');
  });
const sockets = new WebSocketServer({ noServer: true, maxPayload: 8388608 });
server.on('upgrade', (request, socket, head) => {
  if (request.url !== '/_seyra/bridge') { socket.destroy(); return; }
  sockets.handleUpgrade(request, socket, head, ws => sockets.emit('connection', ws, request));
});
sockets.on('connection', socket => socket.on('message', text => socket.send(text.toString())));
server.listen(18443, host, () => console.log(`Pinning fixture ready on ${host}:18443 (test-only, no backend proxy)`));
