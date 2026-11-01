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

`mediaCard` requires exactly one of `image` (a registered photo) or `generatedImage`,
and non-empty `body` (at most 4000
characters), plus the common `id`, `title` and optional `description`. Optional
`facts` has at most 8 `UIPropertyData` label/value entries (unit defaults to empty);
`tags` has at most 6 entries (model schema: non-empty, at most 40 characters each).
Recommended size is 0.42 × 0.62 metres. The image header is outside the ScrollRect,
with preferred height 45% of the canvas and zero flexible height. It preserves
aspect ratio, offers no zoom control and keeps its height when the image is
missing. Description (Label), tags separated by ` · ` (Help), body and
`label: value` facts are inside the scroll. Image resolution uses `TryMedia` so
`missingMedia` includes failures. See [media](MEDIA.md) for sources and limits.

Only `mediaCard` supports `generatedImage: { subject, category, alt }`, including
cards in `layout_create`. The bridge validates the policy in [MEDIA.md](MEDIA.md),
rewrites to an internal photo with `source: "AI-generated illustration"`, queries
disk availability and adds root `pendingMedia` for missing IDs. Generation starts
only after a confirmed Unity success. Generated-image results retain scene data
and add `images: { queued, reused, skipped, reason? }`. Other calls keep their
previous result contract. Surface layouts do not support these presentations.

The header shows `Creating image…`, then the image or `Image unavailable` plus
alt, with caption `Source: AI-generated illustration`. `RefreshMedia` rebuilds
through `ApplyPresentation(Presentation, InteractionState)` to preserve mark and
scroll, including hidden/trash panels. Delivery, failure and pending expiry are
outside undo history. Persistence retains the existing reference fields and
version; loading never starts generation. See MEDIA.md for the internal
`media_status` and `media_deliver` contracts; neither is an MCP tool.

Steps 1–7 are implemented pending review. Step 8 (system prompt) is pending with
the user, and the feature remains inactive in the intended assistant flow until
that prompt is approved and applied. Real Muse and Quest validation are pending.

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

Photos accept only a registered `imageRef`. The model cannot supply `web:` or `gen:` references,
`source` or `pendingMedia`. Missing references
render alternate content and appear in `missingMedia`. See [the media contract](MEDIA.md).
`ui_media_list` lists only registered catalog sprites. The document viewer keeps
registered `imageRef` only and does not parse PDFs. Work plans do not execute
tasks. Physical headset comfort and interaction remain to be validated.

## Checkable cards

Text panels of kind `panel`, and presentations of type `infoPanel`, `technicalSheet` or `mediaCard`, accept optional `check: { label, completed? }`. Labels reject it with `Labels cannot have a check.`; other presentation types reject it as an unknown field. Unknown check fields are rejected. The required label is trimmed and must contain 1–24 characters. Creation defaults completed to false.

The toggle stays below the scroll and changes locally without a chat turn or history entry. Omit check.completed to keep the user's mark; set it only to change the mark. Omit check to remove the toggle. A supplied completed value overrides the current mark. Full updates still require all ordinary content fields. Panels expose the current mark in `scene_get.panel.check`; presentations expose it in `interaction.values.checked` as the string `true` or `false` (presentation.check.completed is the initial value).

Moving, hiding, layout reapplication and trash restoration preserve marks. Undo of update_panel/ui_update restores content and the previous mark; undo of a move preserves the latest local mark. Undo of scene_edit changing only panel completed does not revert that mark. Slots use version 7, store current marks and accept older content without check.

`card_checks({ rootId? })` is read-only and returns `{ cards, completedCount, total }`. Each card contains entityId, name, title, label, completed (boolean) and kind (panel/presentation). Results follow hierarchy order and include hidden cards, excluding trash. Optional rootId scopes to a live managed subtree, including its root; unknown IDs use the usual lookup error. Omission searches all live content. Use this single query when the user asks what is done or left.
