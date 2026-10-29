# HTTPS gateway and polling contract

Remote device traffic uses TLS >= 1.2 and `Authorization: Bearer <device token>` on
**every** request. Pin the SHA-256 of the certificate DER. No remote WebSocket
upgrade is supported. OpenCode and the MCP bridge remain on loopback.

TLS loading prefers `tls/gateway.cert.pem` plus `tls/gateway.key.pem` when
both exist under the data root. Otherwise it uses the legacy `gateway.pfx`
and `gateway.pass`. New certificates are created by the portable backend
CLI; existing PFX certificates and device records remain valid. See
[CLI files and startup](CLI.md).

- `POST /_seyra/bridge/hello`, `{}` -> `{ "sessionId": "..." }`. Replaces the old
  runtime and opens an authenticated local WS to the bridge. Never retry a tool.
- `GET /_seyra/bridge/next?sessionId=...&wait=25` -> the unchanged version 1
  `tool_request` envelope, or 204 after at most 25 seconds. Only one outstanding
  poll is allowed. No poll for 40 seconds closes the local WS and marks pending
  calls disconnected, with unknown outcomes.
- `POST /_seyra/bridge/result?sessionId=...`, the unchanged version 1 `tool_result`
  -> `{}`. Results are accepted only for request IDs delivered in that session.
  An expired/replaced session returns 410; the client opens a new session, never
  resending an ambiguous result or mutation.

- `/_seyra/asr/*` provides authenticated speech transcription; see
  [ASR.md](ASR.md) for routes, polling, limits and errors.

Only the authenticated session owner can poll/respond. The queue is bounded to
64 messages and 8 MiB total. Results have the bridge's 8 MiB limit. Other HTTP
bodies are at most 1 MiB. Authentication is checked against devices.json for each
request, so revocation takes effect on the next request (an existing poll may
finish within 25 seconds). Invalid auth: 401 after 500 ms; >20 failures/IP/minute
block for 5 minutes with 429. Logs never contain headers or tokens.

OpenCode allowlist (identifiers use ASCII letters, digits, underscore and hyphen):

| Method | Path |
| --- | --- |
| GET | `/api/agent` |
| GET, POST | `/api/session` |
| GET | `/api/session/{id}` |
| GET | `/api/session/{id}/message` (query preserved) |
| GET | `/api/session/{id}/permission` |
| POST | `/api/session/{id}/prompt` |
| POST | `/api/session/{id}/interrupt` |
| POST | `/api/session/{id}/permission/{pid}/reply` |

All other routes return 404. The gateway generates the upstream Basic header
using the PC-only OpenCode password, discarding incoming Cookie, Origin and all
other device headers. It never retries upstream requests.
