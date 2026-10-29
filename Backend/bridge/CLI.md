# Backend CLI

## OpenCode configuration

OpenCode 2.1.1 resolves `{env:SEYRA_BACKEND_DIR}` in both the local MCP
command and its working directory. Verified on Windows on 2026-10-29 using
an isolated configuration outside the repository, placeholder credentials,
and a server on loopback port 4196. The server log reported
`message="mcp connected" server=seyra tools=39`.

Port 4100 was free before and after the probe. Only the probe process tree
was stopped. No runtime configuration fallback is required.

## Local files and TLS

`node Backend/seyra.mjs <command> --root <directory>` places `.seyra` under
the selected directory. The default root is `Backend`, independent of the
current working directory. Tests must always select a temporary root.

`init-tls` creates an ECDSA P-256 self-signed certificate, valid for two
years, in `tls/gateway.cert.pem` and a PKCS#8 key in `tls/gateway.key.pem`.
The gateway prefers a complete PEM pair and otherwise loads the existing
`gateway.pfx` and `gateway.pass`. Fingerprints are lowercase SHA-256 of the
leaf certificate DER in either format. Rotation requires `--force` and
devices must be paired again afterwards.

Device records and pairing environment files retain the existing formats.
`pair` stores only the token hash in `devices.json`; `list` omits hashes.
`push` uses argument arrays for adb, verifies the exact installed package
and remote file, and deletes the local pairing file only after success.
Secrets are created with mode 0600 on Unix systems; Windows inherits the
user's filesystem permissions.

The certificate dependency is pinned to `@peculiar/x509` 2.1.0, published
2026-09-04 (npm registry metadata checked on 2026-10-29). The 2.2.0 release
had not yet reached fourteen full days at the time of selection.

## Starting and stopping

`start` reads `.env.local` from the selected root, compiles the bridge,
and runs OpenCode on loopback. The password must be present in that file;
`OPENCODE_PASSWORD` is set to the same value, overriding inherited values.
The CLI passes its own `opencode.json` explicitly through `OPENCODE_CONFIG`.
It supplies `SEYRA_BACKEND_DIR` and `SEYRA_OPENCODE_DATA_DIR` to resolve
bridge paths and the existing truncated-output permission rules.

With `--root`, OpenCode's working directory is the selected root as well.
The MCP working directory remains the backend code directory. This lets
tests use placeholder credentials and temporary device files without
loading the backend's real environment file or device store.

The gateway starts automatically when TLS exists, or explicitly with
`--gateway`; `--no-gateway` selects the direct Editor connection. Its
internal token is generated only in memory. OpenCode logs are inherited
so that MCP connection evidence is visible. On interruption or child exit,
the CLI stops its children; Windows stops the OpenCode process tree and
uses `taskkill /T` as a gateway fallback after two seconds. On Unix,
OpenCode and its MCP descendants share a separate process group.
