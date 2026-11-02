# Scene batches

`scene_build` and `scene_edit` are atomic, recorded scene mutations. Both use the
existing bridge validation, runtime handlers, deduplication and snapshot rollback.
Never retry a mutation after an ambiguous timeout or disconnect; inspect first.

## scene_build

Arguments: `name` (trimmed, 1..256), optional `parentId`, `nodes` (1..64 total,
maximum depth 6 via parentKey), optional `placement`, optional boolean `movable` (default true). Creates an identity-transform container
under the specified Generated parent, or the open request container by default.
When no request is open, opens and closes an automatic request around the build.

Each empty, primitive or panel node requires `key` (`^[a-z][a-z0-9_]{0,47}$`, unique), `name`, `type` and
`transform`. Transform requires local `position`; `rotation` defaults to zero
Euler degrees and `scale` to one. Vectors reuse the existing finite float contract.

- `empty`: an empty container.
- `primitive`: existing `primitiveType`, optional `appearance` (the existing
  set_appearance contract without entityId).
- `panel`: complete existing [panel contract](PANELS.md).

`nodes` is a flat list. Optional `parentKey` uses the same format as `key` and
references an earlier container or primitive node; omission parents the node under the
composition root. Self, missing, forward and panel parent references are rejected.
`children` is an unknown field and is rejected. The JSON Schema is non-recursive:
Meta rejects turns whose tool catalog includes recursive schemas.

Optional `relativeTo` has exactly one of `key` or existing `entityId`, `direction`
(above/below/right/left/front/behind), and `gap` (0..10 meters). After container/primitive/panel creation and before points/lines,
these run in list order through `place_relative` with `frame=world`. Local key
references must be disjoint (ancestry follows parentKey): no self, ancestor, descendant or reference chains.
External entity references retain the existing place_relative protections.

Empty, primitive and panel nodes accept optional `motions`, an array of 1..2
motion specifications from [MOTION.md](MOTION.md). Points and lines reject it.
`spin` can coexist with `orbit` or `bob`; `orbit` and `bob` on the same node are
rejected during validation, before creating content. Setting the same kind twice
uses the individual tool's replacement behavior.

Orbit takes exactly one of `pivotId`, parent-local `pivotPoint` or batch-only
`pivotKey`. A key uses the node key format and can refer to a later non-line node,
including a point. Missing keys, lines, self and descendants are rejected;
descendant checks include point ancestry through `targetKey`. The key is resolved
to its confirmed entityId after all nodes exist. Axis, speed, amplitude and period
reuse the standalone motion contract and ranges. Unity repeats the structural
validation; live pivot validity, non-zero orbit radius, dependency cycles and the
global motion limit are checked by `motion_set` and roll back the batch on failure.

Optional `placement` reuses spatial placement: `destination` air/tabletop/wall,
optional `physicalId`, `allowAirFallback` (default true), `minScale` (.1..1,
default 1). Air rejects physicalId. Reading panels retain spatial_place's size
restrictions. `applied=false` raises `PLACEMENT_REJECTED` with the placement reason
and rolls back the entire build. When rejected candidates are available, the
message appends `Rejected: CODE xN (ids...); CODE xN (ids...).`, grouped by reason
code, with the number of rejected candidates and at most four distinct support
IDs per reason (`air` for candidates without a physicalId). The entire message
is limited to 1024 characters. With no rejected candidates, only the placement
reason is included. The error code and complete rollback remain unchanged.

Returns `rootId`, `entityId`, `requestGroupId`, `nodes` (key to entityId), optional
confirmed `placement`, `movable` (actual applied state), `operationId`, `operationIds`. The requestGroupId supplies
the history group even after the automatic request closes.

## scene_edit

Arguments: `operations`, an ordered array of 1..64 `{ tool, arguments }` objects.
Allowed tools: `set_transform`, `set_appearance`, `rename_object`, `update_panel`, `update_line`,
`set_parent`, `delete_object`, `place_relative`, `motion_set`, `motion_remove`, `interaction_set`.
`motion_control` is excluded because it does not record history. `pivotKey` is
not accepted in scene_edit; use confirmed pivotId or pivotPoint.
Each arguments schema is the same
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
trash. Content uses persistence v8 (reads v1–v8), including managed lines and
containers, primitive appearance, panels and spatial placement metadata.
Motion changes are part of batch history and rollback. Persistence v8 saves interaction and motion configuration, base, phase and pause state.

Validation: `npm run build --prefix Backend/bridge`,
`npm test --prefix Backend/bridge`, C# compilation, then Play Mode
`SceneBatchChecks.Run` and `LayoutChecks.Run`. SceneBatchChecks reports one timed
64-primitive build including dispatch, snapshots and response serialization;
stop if it exceeds 5 seconds. Device timing and the Muse prompt workflow remain
manual validation. The bridge timeout remains unchanged (10 seconds).

## Points and lines (feature 29)

Point: {key,name,type:"point",targetKey? OR targetId?,direction?,radius?,appearance?}. Exactly one target; targetKey must identify an earlier primitive and counts towards depth 6. No parentKey, transform or relativeTo.

Line: {key,name,type:"line",parentKey?,line:{points:[{key} OR {entityId} OR {position}],color?,width?}}. 2..16 endpoints, exactly one endpoint field. Keys can identify any non-line node, including later points. No transform or relativeTo. Neither points nor lines can be parentKey or relativeTo targets.

Creation order: validate nodes, create containers/primitives/panels with appearance, apply relativeTo, create points, create lines, apply motions with resolved pivots, apply placement using the swept envelope. Motion phases start at zero; no Step runs during the call. The whole call remains atomic and undoable once. Lines do not contribute bounds. See [LINES.md](LINES.md) for defaults, errors, lifecycle and persistence. scene_edit accepts update_line using its full replacement contract; creations remain excluded.

## Interactive nodes (feature 10a)

Only empty and primitive nodes accept optional `interaction`, the fields of
[interaction_set](INTERACTION.md) without entityId or interactive. Configuration
runs after placement, so the selection box accounts for the final scale. Details
do not consume nodes; capacity preflight reserves 64 additional objects per
declared detail. Grabbable ancestry is rejected before creation; non-grabbable details may be nested. Runtime target,
nesting, geometry, interaction and presentation limits still apply atomically.
The result adds `details`, mapping node keys with a detail to its entityId.
scene_edit accepts interaction_set using the registered standalone schema.
Interactive content is saved in v8, including owned detail IDs and visibility; v1–v7 remain readable.

## Movable composition roots

After node interactions, scene_build configures the root with grabbable true and
returnOnRelease false by default. Set movable false for physical-world annotations,
including camera segmentation marks. create_primitive remains unchanged; use a
one-node scene_build for a movable primitive. Panels and lines alone do not enable
root grabbing. An existing grabbable ancestor or the 32-interaction limit leaves
the composition created with movable false. Parts requesting grabbable true in a
movable build fail with INTERACTION_NESTED. Non-grabbable detail parts are valid.
Automatic root grabbing belongs to the same atomic history entry as the build.
