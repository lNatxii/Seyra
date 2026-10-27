# Managed text panels and labels — UI phase 2

`create_panel` and `update_panel` use the existing MCP transport, strict validation,
Unity dispatch, request deduplication, history, trash and versioned local slots.
No separate scene store or backend persistence service is introduced.

## Complete content contract

```json
{
  "name": "Instructions",
  "parentId": "Generated",
  "position": { "x": 0, "y": 1.4, "z": 1.5 },
  "rotation": { "x": 0, "y": 0, "z": 0 },
  "scale": { "x": 1, "y": 1, "z": 1 },
  "panel": {
    "version": 1,
    "kind": "panel",
    "title": "Instructions",
    "text": "First step\n\nSecond step",
    "width": 0.8,
    "height": 0.6,
    "fontSize": 24
  }
}
```

All fields are required; unknown fields are rejected. The usual creation parent,
request-group routing and local pose contract applies. Creation returns confirmed
`entityId`, `panel`, `panelIntact`, `operationId` and request provenance.

`update_panel({entityId,panel})` replaces the complete `panel` object in one operation.
It preserves kind, identity, provenance, hierarchy and pose. To change between a
window and a label, create another entity. It does not patch individual widgets.

| Field | Contract |
| --- | --- |
| version | Exactly 1; independent of the scene-file format version |
| kind | `panel` or `label` |
| title | Required string, at most 256 characters; labels require `""` |
| text | Required non-whitespace string, at most 16000 characters; whitespace is preserved |
| width | 0.16–2 local meters |
| height | Panels: 0.24–2 local meters; labels: 0.04–2 local meters |
| fontSize | 12–72 millimeter Canvas units; label height must fit at least one nominal text line |

Dimensions describe the local layout at unit scale. Ancestor scales and the entity's
own scale affect world dimensions, as with primitives. Query `scene_bounds` for the
actual world envelope. Numbers must be finite. Rendered UI uses float coordinates,
while content stores double values to retain validated numeric boundaries.

Panels reuse the recovered Meta window, fixed two-line title region, and scrollable
body. `fontSize` controls body/label typography; the title retains its semantic
Title role. Labels use a transparent World Space Canvas, light semantic ink and a dark
outline. Label overflow uses ellipsis inside the requested rectangle. Panels and
labels wrap text; TMP rich text is disabled. No HTML, URLs, arbitrary callbacks,
component paths or executable code are interpreted.

## Identity and existing tools

Each panel/label has one `GeneratedObject` with kind `panel` and one `ManagedPanel`.
Its internal visuals have runtime IDs for inspection but are not independent managed
content. Normal queries can inspect the rendered hierarchy; persistence and history
traverse the panel as one atomic content node. Limits count both logical content
(2048 including Generated) and physical retained objects (8192 including trash/UI).

- `scene_get` returns the complete contract and `panelIntact`, including for trash.
- `set_transform`, `rename_object`, `set_parent`, `delete_object`, `restore_object`
  and `undo` work on the logical root.
- Creating/reparenting content into a panel, or mutating internal visual objects,
  is rejected. They are implementation details, not destinations for new content.
- `set_appearance` remains a primitive-only tool.
- Native grab targets the logical entity's Transform. Manual movement is observable
  as an external change and causes `HISTORY_CONFLICT` when undo would overwrite it.

Managed bounds use the Canvas rectangle and the four grab-border boxes transformed
by their full matrices. This typed envelope includes the grab frame while excluding
scroll-clipped text and internal animated mesh implementation details. It is a
structural layout envelope, not a pixel silhouette. Generic geometry rules still
reject unrelated unsupported renderers. Relative placement reuses the same six
world directions, gap validation and history as primitives.

## History, deletion and integrity

Creation, full content replacement, pose, parent, rename, deletion, restoration and
load reuse the existing recorded-operation mechanism. Grouped request undo includes
panel creation and content edits. Undoing load restores the previous actual objects
and their component IDs; loading reconstructs new components with saved entity IDs.

Snapshots include the semantic contract and visual integrity signature. External
text, font, structure or controlled layout changes are not silently saved or
overwritten by `update_panel`; undo compares the current signature before applying
its snapshot. SDK-created `InteractableTriggerBroadcaster` and
`MoveTowardsTargetProvider` components are transient infrastructure and excluded
from the structural signature. Runtime layout and local scrolling do not create
scene operations.

Scroll position is session-local: content replacement, undoing a content replacement
and loading reset it to the top. Phase 2 does not persist user form state or a chat
session. The twelve richer presentation contracts belong to phase 3.

## Persistence v3

The existing `content_save`, `content_load` and `content_list` tools and paths remain
in use. New writes use scene format 3. Each node retains the existing fields and
adds required `panel`: the full version-1 contract for panel nodes, `null` otherwise.
The `appearance` field remains required: an appearance for primitives, `null` for
other kinds. Root `panel` is also `null`.

Internal UI trees, scene cameras, rigs, SDK components and transient interaction
state are not serialized. On load the semantic panel reconstructs its owned view
using the configured catalog. Entity IDs, request groups, parent ordering, name,
tag/layer, activeSelf and local pose retain their existing semantics.

The new reader accepts v1 and v2. V1 primitives retain their matte-white migration;
v2 appearances remain unchanged. Older application versions may reject v3 files;
compatibility means that the new runtime reads existing slots, not that old
runtimes can read new features. Unknown scene/content versions are rejected.

Loading validates the full graph, including the prohibition on panel parents, and
reconstructs in inactive staging before replacing live content. Missing catalogs,
invalid contracts and unsupported versions fail without replacing the current
content. Save rejects unsupported changes before atomic file replacement. Trash,
history, secrets and external objects remain outside the file.

## Runtime configuration and verification

Both runtime scenes have the recovered `UIPrefabCatalog` assigned to
`SeyraSceneRuntime.uiCatalog` and one scene-level `PointableCanvasModule` on their
EventSystem. The editor menu **Seyra > UI > Configure Panel Runtime Scenes**
reapplies those bindings and preserves the previously open scenes; save open scene
changes first. No new rig or room-recognition service is required.

Tests can inject the catalog as the third `SceneToolService` constructor argument.
Primitive-only callers remain compatible with the original constructor arguments.

With a configured scene in Play Mode:

```powershell
unity command script run --caller plugin --skill unity-cli C:/Proyectos/Seyra/Backend/bridge/test-support/PanelChecks.cs --project-path C:/Proyectos/Seyra/SeyraUnity -- --entry PanelChecks.Run
unity command script run --caller plugin --skill unity-cli C:/Proyectos/Seyra/Backend/bridge/test-support/RunUnityIntegration.cs --project-path C:/Proyectos/Seyra/SeyraUnity -- --entry RunUnityIntegration.Run
```

`PanelChecks` isolates its objects and temporary slots. `RunUnityIntegration` uses
an isolated service and the actual MCP stdio/WebSocket path on port 4101. Backend
schema and transport tests run with `npm test`. See
[the phase-2 evidence](../validation/05-ui-phase-2.md).

## Phase 3 compatibility

Current saves write v4 and read v1/v2/v3. Text panel contracts remain version 1. Rich presentations add a separate node field and local interaction state; see [presentations](PRESENTATIONS.md).
