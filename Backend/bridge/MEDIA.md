# Presentation media

Photos accept only registered `imageRef` values, require `title`, and accept
`description` and `alt`. The model cannot supply `web:` references.
`ui_media_list` lists registered catalog sprites. The bridge forwards tool
arguments unchanged and does not send image bytes to Unity.

## Retained image loader

`createImageLoader` returns an asynchronous URL loader with no production callers.
It is kept for a future licensed source and used directly by its tests. Each
call returns `{ entry, sourceUrl }`, with the final URL after redirects, or throws
`MediaError`. Abort and timeout failures become `MEDIA_TIMEOUT`.

All requests use `User-Agent: SeyraBridge/1.0 (https://github.com/lNatxii/Seyra; personal assistant)` (overridable with `SEYRA_MEDIA_USER_AGENT`). Each load has a 10-second total deadline and 4 seconds per download,
including its redirect chain. Only HTTP/HTTPS is accepted; credentials in URLs
are rejected. Redirects are manual, at most three, and each destination is checked. At most three downloads per hostname run concurrently across calls, including redirect hops. One retry is allowed after HTTP 429, waiting Retry-After seconds capped at 2 seconds (default 1 second), inside the total deadline.

SSRF protection resolves all DNS addresses and rejects a host if any address is
blocked: loopback, private, link-local, CGNAT, multicast or unspecified addresses,
including IPv4-mapped IPv6. IPv6 is restricted to global unicast with transition
ranges excluded. The connection lookup is pinned to a checked IP to prevent a
second DNS lookup from selecting a different address. HTTP Host and HTTPS
certificate validation still use the URL hostname. Tests can inject `checkHost`
and `download` through `createImageLoader`; production uses the real checker.

All download bodies are limited to 4 MiB. Magic bytes identify JPEG, PNG, WebP
or GIF; the Content-Type alone cannot authorize image decoding. HTML and other
non-image responses return `MEDIA_UNSUPPORTED`.

Pinned dependency `sharp` 0.35.5 decodes at most 40 megapixels, uses the first
animation frame, applies EXIF orientation and resizes inside 512×512 without
enlargement. Inputs with a longest side below 200 pixels are rejected. Output is
JPEG quality 82 without source metadata. The ID is
`web:` + `sha256(jpeg).hex.slice(0, 32)`.

One isolated in-memory LRU cache per factory holds up to 64 converted JPEGs and
final source URLs for 15 minutes, keyed by input URL.

## Loader errors

| Code | Meaning |
| --- | --- |
| `MEDIA_BLOCKED_HOST` | Forbidden host, scheme or URL credentials |
| `MEDIA_TIMEOUT` | Per-download or total deadline exceeded |
| `MEDIA_TOO_LARGE` | Response exceeds its byte limit |
| `MEDIA_TOO_SMALL` | Longest input side is below 200 pixels |
| `MEDIA_UNSUPPORTED` | Unsupported/invalid image or response, decode failure, or unusable redirect chain |
| `MEDIA_HTTP_<status>` | HTTP response is unsuccessful |

## Retained Unity media contract

Unity continues to accept optional internal `media` on `ui_present`, `ui_update`
and `layout_create`, covered by its own checks. Each call accepts at most 12
entries with exact fields: id, mime, width, height and base64 data. Unity validates
JPEG magic, hash prefix, dimensions (1–512), matching decoded dimensions and a
1 MiB decoded byte limit. The bridge currently supplies no such argument.

## Unity storage and persistence v5

`SceneToolService` creates or reuses `RemoteMediaStore` on a separate
`SeyraRemoteMedia` GameObject in the same scene, outside `Generated` and all its
descendants. `RemoteMediaStore.For(generatedRoot)` pairs stores with roots.
`ManagedPanel` finds its owning Generated ancestor and uses a composite resolver:
`web:` references go to the store; other references go to the sprite catalog.
No persistence component whitelist or v5 format change is required.

Files live at `Path.Combine(persistenceDirectory, "Media", "<hex>.jpg")`.
Registration writes a temporary file and atomically moves it into place if the
final file is absent. `TryResolve` loads missing memory entries lazily from disk,
checking size, JPEG and hash again. Textures use no mipmaps, `markNonReadable`
and Clamp; sprites and textures are released together.

The memory target is 48 textures. When exceeded, the store evicts IDs not used
by a presentation under its Generated root, including inactive views and trash.
Content being prepared is also protected. If all remaining images are in use,
loading is allowed and a warning is emitted. Stores leave the static registry
and release resources when their root is destroyed or their last service owner
is disposed. Disk files are not automatically removed on deletion or undo.

Slots remain format v5 and save `imageRef` by reference. Loading reconstructs
the view and reads its JPEG from the disk cache. A missing file or
`web:unavailable` renders `Imagen no disponible` plus `alt`, without exposing the
ID in the card; `ui_get.missingMedia` identifies unresolved references. Moving a
slot to another device does not transfer its cached JPEGs.

See [presentations](PRESENTATIONS.md) and [layouts](LAYOUTS.md). After deployment,
restart both the backend and OpenCode so `sharp`, the 25-second timeout and the
updated Images prompt are loaded.
