# Web presentation media (26)

The bridge resolves web images before forwarding `ui_present`, `ui_update` or
`layout_create`. Unity receives JPEG bytes through the existing authenticated
tool transport and never downloads images from the internet.

## Model input

Each photo requires `title`, accepts `description` and `alt`, and contains exactly
one source:

| Source | Contract |
| --- | --- |
| `imageSearch` | Trimmed text, 2–200 characters, identifying an English Wikipedia article, including disambiguation when useful |
| `imageUrl` | Valid HTTP/HTTPS URL, at most 2048 characters; direct image or HTML page |
| `imageRef` | Non-empty registered catalog reference; the model cannot supply a reference starting with `web:` |

This applies to `mediaCard.image` and existing photo fields in `infoPanel`,
`technicalSheet`, `gallery`, `stepGuide` and `choiceSelector`. `documentViewer`
continues to accept only registered `imageRef` values. `ui_media_list` lists
registered catalog sprites, not web images.

At most 12 distinct sources (`imageSearch` text or `imageUrl`) may appear in one
call. Exceeding this limit returns `success: false` with `INVALID_ARGUMENT` and
message `At most 12 images per call`, before sending anything to Unity.

## Wikipedia and download pipeline

`imageSearch` uses `https://en.wikipedia.org/w/api.php` with `action=query`,
`format=json`, `generator=search`, `gsrsearch=<text>`, `gsrlimit=1`,
`gsrnamespace=0` and `prop=info`. The first result title is URL-encoded into
`https://en.wikipedia.org/api/rest_v1/page/summary/<title>`. Use
`originalimage.source`, falling back to `thumbnail.source` only when absent.
No search result or image produces `MEDIA_NOT_FOUND`. JSON responses are limited
to 256 KiB. No API key is required.

All requests use `User-Agent: SeyraBridge/1.0 (personal assistant)`. Sources are
resolved in parallel with an 8-second total deadline and 4 seconds per download,
including its redirect chain. Only HTTP/HTTPS is accepted; credentials in URLs
are rejected. Redirects are manual, at most three, and each destination is checked.

SSRF protection resolves all DNS addresses and rejects a host if any address is
blocked: loopback, private, link-local, CGNAT, multicast or unspecified addresses,
including IPv4-mapped IPv6. IPv6 is restricted to global unicast with transition
ranges excluded. The connection lookup is pinned to a checked IP to prevent a
second DNS lookup from selecting a different address. HTTP Host and HTTPS
certificate validation still use the URL hostname. Tests can inject `checkHost`
and `download` through `createMediaResolver`; production uses the real checker.

Image bodies are limited to 4 MiB. Magic bytes identify JPEG, PNG, WebP or GIF;
the Content-Type alone cannot authorize image decoding. HTML is limited to 1 MiB
and may resolve one `og:image`, `twitter:image` or `link rel="image_src"` URL,
relative to the final page URL. Its image download uses the same protections;
there is no recursive HTML traversal.

Pinned dependency `sharp` 0.35.5 decodes at most 40 megapixels, uses the first
animation frame, applies EXIF orientation and resizes inside 512×512 without
enlargement. Inputs with a longest side below 200 pixels are rejected. Output is
JPEG quality 82 without source metadata. The ID is
`web:` + `sha256(jpeg).hex.slice(0, 32)`.

Two in-memory LRU caches hold up to 64 entries each for 15 minutes: source URL to
converted JPEG and search text to image URL. Repeated references in a call share
the download/conversion, and outgoing media entries are deduplicated by ID.

## Internal transport and failures

The bridge replaces successful `imageSearch`/`imageUrl` fields with `imageRef`.
Failures become `imageRef: "web:unavailable"`; the rest of the card is preserved.
The model never supplies the internal optional `media` argument:

```json
{
  "media": [
    { "id": "web:<32 lowercase hex>", "mime": "image/jpeg", "width": 512, "height": 341, "data": "<base64 JPEG>" }
  ]
}
```

Only the three tools above accept this argument in Unity. It is omitted when
empty. Each call contains at most 12 entries. Unity validates every entry before
changing scene content: exact fields, ID pattern, MIME, base64 and decoded size
up to 1 MiB, JPEG magic, hash prefix, dimensions in 1–512 and agreement between
declared and decoded dimensions. Invalid entries produce `INVALID_ARGUMENT`.
Files left by later content validation failures are harmless cache entries.

After a successful Unity result only, the bridge adds `data.mediaFailures` when
non-empty: `[{ "search": "<original query>", "code": "..." }]` or
`[{ "url": "<original URL>", "code": "..." }]`. A Unity error is returned
unchanged. Image failures do not turn an otherwise successful tool into an error.

| Code | Meaning |
| --- | --- |
| `MEDIA_NOT_FOUND` | Wikipedia has no matching article or image |
| `MEDIA_BLOCKED_HOST` | Forbidden host, scheme or URL credentials |
| `MEDIA_TIMEOUT` | Per-download or total deadline exceeded |
| `MEDIA_TOO_LARGE` | Response exceeds its byte limit |
| `MEDIA_TOO_SMALL` | Longest input side is below 200 pixels |
| `MEDIA_UNSUPPORTED` | Unsupported/invalid image or response, decode failure, or unusable redirect chain |
| `MEDIA_HTTP_<status>` | HTTP response is unsuccessful |
| `MEDIA_NO_IMAGE` | HTML has no supported image reference |

Do not retry failed images automatically. Preserve the cards and their alternate
text. OpenCode's MCP execution timeout is 25000 ms, covering bridge resolution
and Unity's default 10-second confirmation deadline.

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
