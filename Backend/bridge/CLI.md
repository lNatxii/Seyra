# Backend CLI

## OpenCode configuration

OpenCode 2.1.1 resolves `{env:SEYRA_BACKEND_DIR}` in both the local MCP
command and its working directory. Verified on Windows on 2026-10-29 using
an isolated configuration outside the repository, placeholder credentials,
and a server on loopback port 4196. The server log reported
`message="mcp connected" server=seyra tools=39`.

Port 4100 was free before and after the probe. Only the probe process tree
was stopped. No runtime configuration fallback is required.
