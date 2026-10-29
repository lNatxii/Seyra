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
