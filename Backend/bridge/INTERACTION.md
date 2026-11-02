# Interactive objects

`interaction_set` accepts `entityId`, required boolean `interactive`, and optional
`grabbable` (false), `returnOnRelease` (true), `exclusive` (true), `detailSide`
(`above`, `right`, `left`; default above), `detailGap` (0..0.5 m; default 0.05),
`detailOpen` (false), and `detail`. Disabling accepts only entityId and interactive.
Detail is exactly `{panel: PanelContract}` (kind panel only) or
`{presentation, width, height}` with the ui_present contracts and dimensions.

Targets are live Generated primitives and empty containers with geometry, outside
layouts and direct layout children. Nested interactive objects are rejected.
At most 32 live objects may be interactive. Configuration replaces defaults;
omitted detail retains the existing entity. Same-type detail updates preserve its
entityId; changing type trashes the previous detail. Presentation updates preserve
the presentation content ID. Existing panel/presentation limits apply.

`scene_get` and mutation results report `interaction: {grabbable, returnOnRelease,
exclusive, detailId, detailSide, detailGap, detailVisible, status}`; status is idle
or held. Details report `followTarget: {targetId, side, gap}`. Details live under
the target's request container when available, otherwise Generated, and follow
without inheriting its scale or rotation. Hover and local detail visibility do
not record history. Close hides a detail; Listen and content updates retain their
normal behavior. Detail windows cannot be grabbed.

Errors: `INTERACTION_UNSUPPORTED`, `INTERACTION_NESTED`, `INTERACTION_LIMIT`,
`INTERACTION_DETAIL_OWNED`. Direct delete_object, ui_remove and set_parent on a
detail are owned operations and rejected. Position/rotation changes use existing
`MOTION_DRIVEN` protection. Deleting/restoring a target or ancestor also moves its
details to/from recoverable trash. interaction_set is one recorded operation;
undo and rollback restore configuration and detail content together.

Physical hand/controller input and Quest behavior remain pending validation.

With `grabbable: true`, ray input uses Grabbable and GrabFreeTransformer on the
auxiliary child, targets the managed object, and requires no Rigidbody. Releasing
after a drag returns in 0.4 seconds by default, restoring exact local scale and
the saved pose (or the motion pose at its frozen phase, relative to the current
pivot). Motion on the object and its ancestors stays held through the return.
With returnOnRelease=false, the released pose stays and own motion is rebased;
this external pose change can produce HISTORY_CONFLICT on undo. During a grab
with return enabled, history captures the saved pose rather than the dragged pose.

## Persistence v8

All nodes, including the Generated root, carry required `interaction`: null or
`{grabbable, returnOnRelease, exclusive, detailId, detailSide, detailGap,
detailVisible}`. detailId is null when there is no detail. The detail is saved as
a normal panel/presentation entity; its follower, billboard and disabled grab
frame are reconstructed from the owning target after hierarchy and layouts.
SDK auxiliaries and highlight materials are reconstructed, never serialized.

Loads accept v1–v8; v1–v7 have no interactive configuration. Validation rejects
unsupported targets, nested interaction, layout children, missing/shared details,
label details, motion on details and more than 32 interactive objects before
replacing content. Presentation limits remain 16. Invalid geometry or transforms
during reconstruction trigger the existing recorded rollback. Local visibility
round-trips without adding an undo entry. Slots retain existing 2048-object and
5 MiB limits, atomic writes and content_load undo.

## Scene composition and validation

scene_build accepts interaction on empty/primitive nodes after placement and
returns details keyed by node key; scene_edit reuses interaction_set. See
[BATCH.md](BATCH.md). InteractionChecks.Run exercises tap/exclusivity, hover
integrity, follow and scale, SDK drag/return with undo, ancestor held status,
owned-detail protection, joint trash/undo, v8 round-trip and v7 compatibility.
Run it in SeyraAssistant Play Mode through Unity Pipeline. Real ray targeting,
hand/controller feel, Listen output, overlap and frame cost require Quest testing.
