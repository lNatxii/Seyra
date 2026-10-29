# Semantic presentations

There are thirteen complete typed contracts: `mediaCard`, `infoPanel`, `technicalSheet`,
`gallery`, `stepGuide`, `documentViewer`, `comparison`, `checklist`, `diagnostic`,
`choiceSelector`, `parameterForm`, `dataView` and `workPlan`.
One request fills the entire presentation. Prefab children are implementation
details; each window is one managed scene entity.

## Tools

| Tool | Contract |
| --- | --- |
| `ui_catalog` | Available types, dimensions and capacity |
| `ui_media_list` | Registered image references; explicit offset and limit |
| `ui_present` | name, parentId, local position/rotation/scale, width, height, presentation |
| `ui_update` | entityId, width, height, complete replacement presentation |
| `ui_list` | Live presentations, including hidden windows |
| `ui_get` | Content, interaction state, missingMedia and integrity |
| `ui_close`, `ui_open` | Hide/show by entityId |
| `ui_remove` | Recoverable scene deletion by entityId |

Presentation dimensions are 0.4–2 metres per axis. The contract is limited to
128 KiB and the scene to sixteen live presentations, including hidden windows.
The existing physical object budget also applies during replacement preparation.
`presentation.id` is a unique semantic content key. Subsequent tools use the
returned scene `entityId`, never that semantic key. Creation requires a confirmed
Generated parent and explicit local pose, following the existing request system.
See [shared examples](test/presentation-corpus.json) and the strict schemas in
`src/presentations.ts`; Unity independently validates the same relationships.

## Interaction and history

`ui_get` returns `interaction` with version, revision, index, nodeId, values,
submitted, typed submission, lastAction, scroll, zoom and visible. Draft numeric
input remains literal; submission validates required fields, choices and numeric
values. Editing a submitted form invalidates its result. Exclusive selections,
diagnostic navigation, checklist/guide progress and media paging remain local.
Controls do not execute tools or automatically start a conversation turn.
Query after meaningful interaction instead of polling every frame.

Creation, full replacement and removal use existing scene history. Replacement
resets interaction while preserving visibility. Undo of replacement restores the
previous contract and its interaction snapshot, even for identical content.
Ordinary transform undo preserves newer local input. Local input and visibility
do not create separate scene undo entries. Deletion uses the existing trash and
restores the original window with its current local state.

## Media cards

`mediaCard` requires `image` (a photo) and non-empty `body` (at most 4000
characters), plus the common `id`, `title` and optional `description`. Optional
`facts` has at most 8 `UIPropertyData` label/value entries (unit defaults to empty);
`tags` has at most 6 entries (model schema: non-empty, at most 40 characters each).
Recommended size is 0.42 × 0.62 metres. The image header is outside the ScrollRect,
with preferred height 45% of the canvas and zero flexible height. It preserves
aspect ratio, offers no zoom control and keeps its height when the image is
missing. Description (Label), tags separated by ` · ` (Help), body and
`label: value` facts are inside the scroll. Image resolution uses `TryMedia` so
`missingMedia` includes failures. See [web media](MEDIA.md) for sources and limits.

## Persistence v5

Scene slots write v5 and still read v1–v4. Each presentation is one node with
`panel: null` and `presentation: {content, state}`. Content contains version=1,
width, height and the typed presentation. State has its own version=1. Unity
validates both before staging a load; unknown keys, invalid references, invalid
submissions and future versions reject the load without replacing live content.
The existing save/load path, GUIDs, request groups and load undo remain shared.
UI internals are reconstructed from the catalog; they are never serialized as a
hierarchy of separately editable generated objects.

## Assets and scope

The thirteen presentation prefabs keep stable GUIDs and serialized bindings, with
their renderers and builder. Build the UI library to regenerate its presentation
catalog too. Presentations use the existing scene services and slot persistence;
remote JPEGs use the separate disk cache described in [MEDIA.md](MEDIA.md).
Replacement prepares an inactive view before retiring the old one.

Photos accept exactly one registered `imageRef`, `imageSearch` (short web search
query) or HTTP/HTTPS `imageUrl`. The bridge downloads, validates and converts web
images; Unity receives internal JPEG `media` only on `ui_present`, `ui_update`
and `layout_create`. The model cannot supply `web:` references. Missing references
render alternate content and appear in `missingMedia`; successful tool results
may include `data.mediaFailures`. See [the media contract](MEDIA.md).
`ui_media_list` lists only registered catalog sprites. The document viewer keeps
registered `imageRef` only and does not parse PDFs. Work plans do not execute
tasks. Physical headset comfort and interaction remain to be validated.
