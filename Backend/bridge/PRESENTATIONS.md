# Semantic presentations

Phase 3 adds twelve complete typed contracts: `infoPanel`, `technicalSheet`,
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

## Persistence v4

Scene slots now write v4 and still read v1–v3. Each presentation is one node with
`panel: null` and `presentation: {content, state}`. Content contains version=1,
width, height and the typed presentation. State has its own version=1. Unity
validates both before staging a load; unknown keys, invalid references, invalid
submissions and future versions reject the load without replacing live content.
The existing save/load path, GUIDs, request groups and load undo remain shared.
UI internals are reconstructed from the catalog; they are never serialized as a
hierarchy of separately editable generated objects.

## Assets and scope

The twelve presentation prefabs keep stable GUIDs and serialized bindings, with
their renderers and builder. Build the UI library to regenerate its presentation
catalog too. No dedicated presentation host, room service or separate persistence store
is required. Replacement prepares an inactive view before retiring the old one.

Images resolve only from registered catalog sprites. Missing references render
alternate content and appear in missingMedia. The document viewer displays
provided structured content; it does not parse PDFs. Work plans do not execute
tasks. There is no remote download, CAD renderer, room recognition or spatial chat
in this phase. Physical headset comfort and interaction remain to be validated.
