# Scene batches

`scene_build` and `scene_edit` are atomic, recorded scene mutations. Both use the
existing bridge validation, runtime handlers, deduplication and snapshot rollback.
Never retry a mutation after an ambiguous timeout or disconnect; inspect first.

## scene_build

Arguments: `name` (trimmed, 1..256), optional `parentId`, `nodes` (1..64 total,
maximum depth 6 via parentKey), optional `placement`. Creates an identity-transform container
under the specified Generated parent, or the open request container by default.
When no request is open, opens and closes an automatic request around the build.

Each node requires `key` (`^[a-z][a-z0-9_]{0,47}$`, unique), `name`, `type` and
`transform`. Transform requires local `position`; `rotation` defaults to zero
Euler degrees and `scale` to one. Vectors reuse the existing finite float contract.

- `empty`: an empty container.
- `primitive`: existing `primitiveType`, optional `appearance` (the existing
  set_appearance contract without entityId).
- `panel`: complete existing [panel contract](PANELS.md).

`nodes` is a flat list. Optional `parentKey` uses the same format as `key` and
references an earlier non-panel node; omission parents the node under the
composition root. Self, missing, forward and panel parent references are rejected.
`children` is an unknown field and is rejected. The JSON Schema is non-recursive:
Meta rejects turns whose tool catalog includes recursive schemas.

Optional `relativeTo` has exactly one of `key` or existing `entityId`, `direction`
(above/below/right/left/front/behind), and `gap` (0..10 meters). After all creation,
these run in list order through `place_relative` with `frame=world`. Local key
references must be disjoint (ancestry follows parentKey): no self, ancestor, descendant or reference chains.
External entity references retain the existing place_relative protections.

Optional `placement` reuses spatial placement: `destination` air/tabletop/wall,
optional `physicalId`, `allowAirFallback` (default true), `minScale` (.1..1,
default 1). Air rejects physicalId. Reading panels retain spatial_place's size
restrictions. `applied=false` raises `PLACEMENT_REJECTED` with the placement reason
and rolls back the entire build.

Returns `rootId`, `entityId`, `requestGroupId`, `nodes` (key to entityId), optional
confirmed `placement`, `operationId`, `operationIds`. The requestGroupId supplies
the history group even after the automatic request closes.

## scene_edit

Arguments: `operations`, an ordered array of 1..64 `{ tool, arguments }` objects.
Allowed tools: `set_transform`, `set_appearance`, `rename_object`, `update_panel`,
`set_parent`, `delete_object`, `place_relative`. Each arguments schema is the same
registered schema used by the individual tool; no creation or nested batches.
Each entityId must resolve to a live descendant of Generated. External transform
targets and Generated itself are rejected. Room and spatial application guards
run for each operation, including parentId checks.

Returns `count`, indexed `results` with their `tool` and handler result,
`operationId`, `operationRequestGroupId` (the current request, if any).
An operation failure preserves its existing error code/message and adds zero-based
`index`. The bridge accepts this structured error without waiting for a timeout.

## History, rollback and persistence

Each successful batch creates exactly one history entry (maximum 128 entries).
Suboperations never record individual entries. Any failure restores the prior
snapshot and adds no history; restoration failure is `ROLLBACK_FAILED`.
Operation/request undo reverses the complete batch; creations enter recoverable
trash. Content uses the existing persistence v5 without a format change, including
containers, primitive appearance, panels and spatial placement metadata.

Validation: `npm run build --prefix Backend/bridge`,
`npm test --prefix Backend/bridge`, C# compilation, then Play Mode
`SceneBatchChecks.Run` and `LayoutChecks.Run`. SceneBatchChecks reports one timed
64-primitive build including dispatch, snapshots and response serialization;
stop if it exceeds 5 seconds. Device timing and the Muse prompt workflow remain
manual validation. The bridge timeout remains unchanged (10 seconds).
