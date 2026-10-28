import https from 'node:https';
import { readFileSync } from 'node:fs';
import { WebSocketServer } from 'ws';
// Test-only server. Keep certificate material outside Assets and source control.
const root = process.argv[2];
const server = https.createServer({ pfx: readFileSync(`${root}/gateway.pfx`),
  passphrase: readFileSync(`${root}/gateway.pass`, 'utf8').trim(), minVersion: 'TLSv1.2' },
  (_, response) => response.end('{}'));
const sockets = new WebSocketServer({ server, maxPayload: 8388608 });
sockets.on('connection', socket => socket.on('message', text => socket.send(text.toString())));
server.listen(18443, '127.0.0.1', () => console.log('Pinning fixture ready on loopback:18443'));
