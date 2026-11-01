# Presentation media

Photos accept only registered `imageRef` values, require `title`, and accept
`description` and `alt`. The model cannot supply `web:` or `gen:` references,
`source`, `media` or `pendingMedia`. `ui_media_list` lists registered catalog
sprites. Only `mediaCard` can instead request `generatedImage`; the bridge
rewrites it before invoking Unity. Calls without generated images keep their
original arguments and results.

## Generated illustrations (feature 19)

`generatedImage` is a strict object with required `subject`, `category`, `alt`.
The English subject has 2–80 characters matching
`^[A-Za-z0-9][A-Za-z0-9 ,'-]*$`; alt has 1–200 characters. Categories are
`ingredient`, `dish`, `tool`, `mechanical_part`, `electrical_component`,
`material`, `plant`, `animal`, `household_object`. Only `mediaCard` accepts it,
with exactly one of `image` or `generatedImage`. Surface layouts do not support
presentations because their height limit is below 0.4 metres.

`image-policy.ts` contains the blocked terms and compares whole words,
case-insensitively after whitespace normalization. Only `apple` is exempt for
`ingredient`, `dish` and `plant`. This is not a complete rights filter. Subject
policy runs in the handler after MCP structural validation so rejection returns
`success: false`, code `IMAGE_SUBJECT_REJECTED`, and the exact message
`This subject cannot have a generated image. Use a text panel or a card without an image.`
No Unity invoke or fetch occurs. More than 12 generated images per call returns
`IMAGE_LIMIT_EXCEEDED`: `At most 12 generated images per call.`

The ID is `gen:` plus the first 32 hex digits of
`sha256("muse-image-1.0|" + category + "|" + normalizedSubject)`, where subject
normalization trims, collapses whitespace and lowercases. The fixed prompt is:

`A plain generic example of {subject}. Clean, simple illustration, clearly not a photograph. One centered subject on a plain light background. No text, letters, numbers, logos, brand marks, labels, packaging designs, watermarks, people or fictional characters.`

`createImageService({ fetch, invoke, env, now })` handles `ui_present`, `ui_update`
and `layout_create`. It queries `media_status`, rewrites to
`image: { imageRef, title, alt, source: "AI-generated illustration" }`, and adds
root `pendingMedia` for missing IDs. These fields are added after MCP validation,
not accepted from the model. Only confirmed Unity success queues generation;
failure or unknown outcome never does. Results add `data.images` with unique-ID
counts `{ queued, reused, skipped, reason? }`; reason is
`IMAGE_NOT_CONFIGURED` or `IMAGE_BUDGET_EXHAUSTED` when applicable.

API: `POST https://api.meta.ai/v1/images/generations` with Bearer `META_API_KEY`.
The body uses `model: "muse-image-1.0"`, fixed `prompt`, `n: 1`,
`size: "1024x1024"`, `response_format: "b64_json"`, `output_format: "jpeg"`,
`reasoning_strength: "low"`, and `tool_enablement` with `enable_image_search`,
`enable_web_search`, `enable_shell` all false. Responses are capped at 16 MiB;
generation has a 60-second total deadline. Sharp limits input to 40,000,000 pixels,
applies orientation, resizes inside 512×512 and writes JPEG quality 82, at most
1 MiB. Provider error bodies and credentials are not logged or returned.

At most three generations run concurrently. Same IDs deduplicate through delivery;
disk hits are reused. Limits default to 30 sends per ten minutes
(`SEYRA_IMAGE_WINDOW_LIMIT`) and 100 per process (`SEYRA_IMAGE_SESSION_LIMIT`,
maximum 100). Only actual API sends count, including retries. Queued work reserves
capacity without incrementing counters. HTTP 429 retries once with Retry-After
capped at two seconds within the original deadline. Other API errors, including
content-policy rejection, fail without generation retry.

Missing key or exhausted budget still creates the card and delivers failure.
Delivery retries transport/disconnection failures every five seconds for up to
two minutes, then discards bytes. It does not occupy a generation slot. Internal
Unity tools stay absent from the MCP registry and outside scene undo history:

- `media_status { ids: string[] }` → `{ existing: string[] }`.
- `media_deliver { media?: MediaEntry[], failed?: string[] }` → `{ refreshed: number }`.

Delivery accepts at most one JPEG per call and is idempotent. `refreshed` counts
panel rebuilds. IDs match `(?:web|gen):[0-9a-f]{32}`. Pending state is marked only
after content applies. Pending cards show `Creating image…`; registration/failure
rebuilds affected panels including inactive/trash views, preserving mark and scroll.
Failure or expiry after 120 seconds shows `Image unavailable` plus alt. LateUpdate
returns before LINQ when there are no pending IDs. `missingMedia` still includes
unresolved references. Undo/deletion do not regenerate or cancel generation.
Loading resolves disk files or shows unavailable; it never generates.

Steps 1–7 are implemented pending review. Step 8 (system prompt) remains pending
with the user; the feature is inactive in the intended assistant flow until then.
This is a prompt gate, not a server-side feature flag. Real Muse calls, quality,
latency and Quest 3 validation remain pending.

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
JPEG magic, dimensions (1–512), matching decoded dimensions and a
1 MiB JPEG byte limit. `web:` validates the JPEG hash prefix; `gen:` does not.

## Unity storage and persistence v5

`SceneToolService` creates or reuses `RemoteMediaStore` on a separate
`SeyraRemoteMedia` GameObject in the same scene, outside `Generated` and all its
descendants. `RemoteMediaStore.For(generatedRoot)` pairs stores with roots.
`ManagedPanel` finds its owning Generated ancestor and uses a composite resolver:
`web:` and `gen:` references go to the store; other references go to the sprite catalog.
No persistence component whitelist or v5 format change is required.

Files live at `Path.Combine(persistenceDirectory, "Media", "<hex>.jpg")`.
Registration writes a temporary file and atomically moves it into place if the
final file is absent. `TryResolve` loads missing memory entries lazily from disk,
checking size, JPEG dimensions and the hash for `web:` again. `Exists` checks disk
presence or a loaded sprite without decoding or changing LRU. Textures use no mipmaps, `markNonReadable`
and Clamp; sprites and textures are released together.

The memory target is 48 textures. When exceeded, the store evicts IDs not used
by a presentation under its Generated root, including inactive views and trash.
Content being prepared is also protected. If all remaining images are in use,
loading is allowed and a warning is emitted. Stores leave the static registry
and release resources when their root is destroyed or their last service owner
is disposed. Disk files are not automatically removed on deletion or undo.

Slots remain format v5 and save `imageRef` by reference. Loading reconstructs
the view and reads its JPEG from the disk cache. A missing file or
`web:unavailable` renders `Image unavailable` plus `alt`, without exposing the
ID in the card; `ui_get.missingMedia` identifies unresolved references. Moving a
slot to another device does not transfer its cached JPEGs.

See [presentations](PRESENTATIONS.md) and [layouts](LAYOUTS.md). After deployment,
restart both the backend and OpenCode. The Images system prompt is still pending
with the user and has not been modified for feature 19.
