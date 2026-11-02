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

Implementation stage: saving interactive content remains explicitly blocked until
format v8 is implemented in step 8; ordinary content continues using v7. Physical
hand/controller input and Quest behavior remain pending validation.
