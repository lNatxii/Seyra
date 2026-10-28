
## Secure connection transport (feature 07)

The bridge remains bound to `127.0.0.1`. When `SEYRA_BRIDGE_INTERNAL_TOKEN` is set,
WebSocket connections must supply `Authorization: Bearer <internal token>` and no
Origin header. Without the variable, the direct Editor development mode remains
available. A valid new runtime replaces the old one; pending requests finish with
`UNITY_DISCONNECTED` and `outcomeUnknown: true`. They are never replayed. Ping/pong
runs every 10 seconds; an unresponsive socket is terminated on the next tick.

Remote devices use the HTTPS gateway polling contract in `GATEWAY.md`; they never
connect directly to this WebSocket or to OpenCode.
