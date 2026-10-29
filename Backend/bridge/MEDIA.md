# Web presentation media (26)

The bridge resolves web images before forwarding `ui_present`, `ui_update` or
`layout_create`. Unity receives JPEG bytes through the existing authenticated
tool transport and never downloads images from the internet.

## Model input

Each photo requires `title`, accepts `description` and `alt`, and contains exactly
one source:

| Source | Contract |
| --- | --- |
| `imageSearch` | Trimmed text, 2–200 characters, containing a web search query (film title/year, dish name, etc.) |
| `imageUrl` | Valid HTTP/HTTPS URL, at most 2048 characters; direct image or HTML page |
| `imageRef` | Non-empty registered catalog reference; the model cannot supply a reference starting with `web:` |

This applies to `mediaCard.image` and existing photo fields in `infoPanel`,
`technicalSheet`, `gallery`, `stepGuide` and `choiceSelector`. `documentViewer`
continues to accept only registered `imageRef` values. `ui_media_list` lists
registered catalog sprites, not web images.

At most 12 distinct sources (`imageSearch` text or `imageUrl`) may appear in one
call. Exceeding this limit returns `success: false` with `INVALID_ARGUMENT` and
message `At most 12 images per call`, before sending anything to Unity.

## Meta search and download pipeline

`imageSearch` sends a POST to `https://api.meta.ai/v1/responses`, authenticated
with `META_API_KEY`. The body contains `model` (from `SEYRA_MEDIA_SEARCH_MODEL`,
default `muse-spark-1.3`), `tools: [{type: "web_search", search_context_size: "low"}]`,
`include: ["web_search_call.results"]`, `reasoning: {effort: "low"}`,
`max_output_tokens: 2048` and
`input: "Use web search now to find: <query>. Then reply with the single word done."`.
No `tool_choice` is supplied. Responses are limited to 1 MiB and 6 seconds per
search, with at most four searches active at once across resolver calls.

Read `output[]` entries of type `web_search_call`, taking the first eight
`results[]` entries of type `text_result` with HTTP/HTTPS URLs. Download these
pages and their images in parallel; choose the first decodable image in search
order, skipping missing, unsupported (including SVG) or small images. An earlier
result is awaited before choosing a later one, within the total deadline. No
usable image produces `MEDIA_NOT_FOUND`. Wikipedia has no special API route;
it can appear as an ordinary search result.

For `youtube.com/watch?v=<id>` and `youtu.be/<id>`, fetch
`https://img.youtube.com/vi/<id>/hqdefault.jpg` instead of the page. Its source
remains the original YouTube page.

The launcher passes its local environment to OpenCode. The MCP server now also
explicitly sets `environment.META_API_KEY: "{env:META_API_KEY}"` in
`Backend/opencode.json`, so MCP environment filtering does not remove the key.
An isolated probe using a fictitious key verified that OpenCode's child bridge
receives it; no real key or authenticated search is needed for tests.
`createMediaResolver` accepts an injected `metaRequest` (fetch-compatible) and
`env` in addition to the existing downloader and host checker.

All requests use `User-Agent: SeyraBridge/1.0 (https://github.com/lNatxii/Seyra; personal assistant)` (overridable with `SEYRA_MEDIA_USER_AGENT`). Sources are
resolved in parallel with a 10-second total deadline and 4 seconds per download,
including its redirect chain. Only HTTP/HTTPS is accepted; credentials in URLs
are rejected. Redirects are manual, at most three, and each destination is checked. At most three downloads per hostname run concurrently across calls, including redirect hops. One retry is allowed after HTTP 429, waiting Retry-After seconds capped at 2 seconds (default 1 second), inside the total deadline.

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
converted JPEG/source and search text to the selected JPEG/source. Repeated references in a call share
the download/conversion, and outgoing media entries are deduplicated by ID.

## Internal transport and failures

The bridge replaces successful `imageSearch`/`imageUrl` fields with `imageRef`.
Failures become `imageRef: "web:unavailable"`; the rest of the card is preserved.

Successful photos also gain bridge-only `source` and `sourceUrl`. `source` is
`<hostname without www> · <page title, at most 80 characters>`. For search results,
the title and URL come from the selected search page, not the JPEG URL. Direct
imageUrl uses its final URL and photo title; HTML uses its page URL and HTML title
(falling back to the photo title). The strict model photo schema excludes both
fields. Unity validates source text up to 300 characters and sourceUrl up to
2048 characters. Photo captions show Fuente: <source>; mediaCard places this
Help line first in the scroll, immediately below the fixed image. The URL is
preserved in v5 and ui_get, but is never displayed or opened.
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
| `MEDIA_NOT_CONFIGURED` | META_API_KEY is missing |
| `MEDIA_SEARCH_<status>` | Meta search returned an unsuccessful HTTP status |
| `MEDIA_NOT_FOUND` | No search result provides a usable image |
| `MEDIA_BLOCKED_HOST` | Forbidden host, scheme or URL credentials |
| `MEDIA_TIMEOUT` | Per-download or total deadline exceeded |
| `MEDIA_TOO_LARGE` | Response exceeds its byte limit |
| `MEDIA_TOO_SMALL` | Longest input side is below 200 pixels |
| `MEDIA_UNSUPPORTED` | Unsupported/invalid image or response, decode failure, or unusable redirect chain |
| `MEDIA_HTTP_<status>` | HTTP response is unsuccessful |
| `MEDIA_NO_IMAGE` | HTML has no supported image reference |

Do not retry failed images automatically. Preserve the cards and their alternate
text. OpenCode's MCP execution timeout is 25000 ms, covering the 10-second bridge resolution budget
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
