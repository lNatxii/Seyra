# Captured room references (22a)

`room_get({})` returns `runtimeId`, `roomRevision`, `roomId`, `state`,
`simulated`, `viewerReady`, surface/object counts and a diagnostic `reason`.
States are `AirOnly`, `WaitingForPermission`, `Loading`, `Ready`,
`PermissionDenied`, `Unavailable`, `Suspended`, `Error`. Only `Ready` exposes
geometry. Every published snapshot has an increasing revision. Room data is
immutable and rebuilt on MRUK room/anchor changes, not every frame.

`RoomContext` owns permission and loading. Android requests
`com.oculus.permission.USE_SCENE` once, then calls
`LoadSceneFromDevice(false, true, MRUK.SceneModel.V1)`. There is no automatic
scan, retry loop or prefab fallback. MRUK startup loading and OVRManager's
startup scene permission request are disabled. Scene Support is Supported,
Anchor Support enabled. World Lock is enabled; unavailable positional tracking
or localization suspends geometry. Room membership is polled every 0.5 seconds.
Editor test prefabs require explicit `LoadTestRoomAsync`; those snapshots always
declare `simulated:true` and bypass device tracking with an explicit test viewer.
The Editor exit hook invalidates async continuations and unsubscribes before
MRUK releases native resources.

## Inventory

`room_objects({offset?:0, limit?:32, kind?, label?})` returns `objects`,
`total`, `nextOffset`, `state`, `runtimeId`, `roomRevision`, `simulated` and
`geometryKnownOnly:true`. Maximum limit 64; offset 0..1000000. Kind is
`wall|floor|ceiling|opening|tabletop|object`. Label uses exact case-insensitive
matching of original MRUK labels. Sort order is ordinal physicalId, then kind.
Compare revisions between pages; restart after any change.

Each entry includes `physicalId:"physical:<anchor UUID>"`, `kind`, `labels`,
`readOnly:true`, world AABB `bounds`, optional known `facing` and
`viewerRelative:{position,distance,side,depth}` when a horizontal tracked viewer
frame exists. Viewer positions are in metres, +X right, +Y up, +Z away from the
viewer. Surfaces include `pose`, local polygon in metres and optional `supportId`;
volumes include oriented `box:{center,size,rotation}`. Rotations use Euler degrees.
The tabletop and its supporting volume share physicalId; filter kind to distinguish
them. Bounds resolution for such an ID uses the volume, so `above, gap=0` rests
the content's bounding envelope on the top of the captured box.

Door/window frames are openings. Invisible walls and global mesh are excluded.
Every other volume is furniture/obstacle geometry. Table/storage tops select the
box axis closest to world up; normal must be within 20 degrees. Storage must be
0.4..1.3 m above the captured floor. Beds and couches never become tabletops.
Boxes use transformed corners; unsupported shear/singular or non-finite geometry
fails the snapshot instead of producing misleading dimensions.

## Bounds and relative placement

Existing `scene_bounds`/`place_relative` calls keep their world-axis semantics.
Both accept physical references (only referenceId for placement). Physical IDs
belong to the current runtime and current room. Refresh inventory after roomRevision
changes. An unchanged anchor UUID in the current snapshot remains resolvable;
these calls do not take an expected revision token.

Optional `frame` is `world` (default), `viewer` (head yaw) or `reference` (known
reference front projected horizontally). Reference front is the wall normal
on the viewer's side, MRUK's furniture facing for couch/bed/screen, or managed
Transform forward. Tables without a front reject reference frame; use viewer.
All corners are projected into frame axes before computing face gaps and aligned
centres. `above/below` remain world vertical. In viewer frame, `right/left` are
the user's right/left and `front` moves away from the user.

Non-world bounds have `kind:"frame_aligned_box"`, `space` equal to the frame and
`frameRotation` (world Euler degrees). Their centre/min/max are coordinates projected
about the world origin; multiply by frame rotation to reconstruct world points.
World bounds retain `{space:"world",kind:"aabb",center,size,min,max}`.
Physical responses add roomRevision, simulated and geometryKnownOnly.

Optional placement `orient`: `keep` (default), `face_viewer`, `match_reference`.
Orientation is yaw only and applied before computing placement bounds. UIXRWindow
has its readable front along local -Z: face_viewer points local +Z away from the
viewer; match_reference makes a panel's readable side face the reference front.
Other content aligns +Z to the reference front. Ancestor scales must be positive
and uniform for orientation. Parent and local scale remain unchanged. Rejected
placements restore local position/rotation; successful position and rotation share
one existing history operation. Undo and HISTORY_CONFLICT use the existing circuit.

Errors: `ROOM_UNAVAILABLE` (no Ready snapshot), `ROOM_REFERENCE_STALE` (unknown
UUID in current room), `FRAME_UNDEFINED` (no horizontal viewer/reference front),
`ROOM_READ_ONLY` (physical mutation/parent). Geometry/argument errors use
`INVALID_ARGUMENT`. Room nodes in hierarchy/find carry physical/readOnly flags;
their generic IDs and ancestor transforms are also protected against mutation.

Room snapshots, IDs and revisions are never persisted. Generated content continues
to save its local pose using the existing content format; no physical attachment
is created. Placement uses captured envelopes and does not check free space,
visibility, collisions or moved furniture. Planner, reservations, snapping and
chat following belong to 22b–22d.

## Validation

Run `npm test --prefix Backend/bridge` for strict schemas, transport and profiles.
With a configured scene in Play Mode, run `RoomChecks.Run` through
`unity command script run` against `test-support/RoomChecks.cs`; this explicitly
loads Bedroom00, Office00 and OtherRoom07_Cluttered. Run unchanged `BoundsChecks.Run`
for legacy geometry/placement regression. Headset permission, real capture and
World Lock behavior require device validation after roadmap 07.

Official Meta references checked on 2026-10-27 (exact API signatures verified
against the installed 207.0.1 package):
[Scene data and World Lock](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/),
[Spatial data permission](https://developers.meta.com/vr/documentation/unity/unity-spatial-data-perm/).
