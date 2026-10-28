# Captured room references (22a)

## Planned placement (22b)

`spatial_candidates({entityId,destination,physicalId?,allowAirFallback?,minScale?})`
plans without moving or reserving content. Destination is wall/tabletop/air;
fallback defaults true, relative scale .1..1 defaults 1 and must be 1 for any
reading panel/presentation subtree. `spatial_place` accepts the same arguments
plus `expectedRoomRevision`, replans against current reservations and revalidates
before synchronous commit. Only managed Generated descendants are movable;
held objects return OBJECT_MANIPULATED, stale revisions ROOM_REVISION_CONFLICT.
Positive uniform ancestor scales are required. Task yaw is fixed only inside an
open request; unscoped calls use the current yaw. Current viewer position is
used for each call.

Results include applied, code, reason, actual destination/support, pose (world
position/Euler degrees), relative scale, score, distance, yaw, elevation,
angularHeight, roomRevision, simulated, physicalValidation and up to 40 rejected
supports/reasons. A query returns applied=false and CANDIDATE for a valid plan.
PLACEMENT_REJECTED with applied=false is a confirmed refusal, not an unknown
mutation, and creates no history entry. A successful commit returns PLACED and
one existing Transform history operation; undo restores position/rotation/scale.

Candidates sample captured walls/tabletops and a fixed air grid. Conservative
corner footprints (including edges across concave cutouts), oriented-box SAT,
captured planes, openings, floor containment, visibility rays, comfort sector,
reading angle and current reservations determine validity. Physical geometry is
captured data only. A missing map permits air with physicalValidation=false;
loading/suspended tracking does not permit placement. A failed named support
can fall back to checked air when requested. Settings defaults are centralized
in SpatialPlacementSettings; SAT and polygon predicates are pure SpatialCollision.

Reservations are derived from current live transforms of placed content, panels,
presentations, chat and menu, omitting hidden presentations and inactive/trash
roots. Planning never reserves. Restore revalidates through scene_get/ui_get
`spatial:{surface,physicalId,needsPlacement,lastValidPose}` without moving anything.
Metadata lives in the runtime service, adds no persisted components and never
survives content_load (new instances). Only Transform is recorded in history;
after undo/external movement, stale placement metadata is marked needsPlacement
when it fails geometric validation. `ValidateGeometry` checks finite geometry,
support footprint, openings, floor containment, furniture, planes and reservations
without using the viewer. Thus turning or walking away never sets needsPlacement
by itself. Planning/commit additionally use `ValidatePose` for distance, visibility,
comfort and readability; wall incidence applies to both reading and 3D content.
Surface attachment/snapping belongs to 22c.

## Application following (22d)

`SpatialFollowController` on SeyraAssistantApp owns only the chat. The menu's
SeyraLazyFollow independently owns its dock motion, including with the chat closed.
Each native grab frame targets its own window. Releasing the chat pins only the chat; Seguirme restores adaptive following. Application
windows and their ancestors reject generic scene mutation tools; they are not
Generated content and have no scene-tool placement history or persistence.

Modes are Adaptive, Pinned and Suspended; states Stable, Waiting, Plan, Moving,
Cooldown, Recovery. Motion requires being beyond 45 degrees or outside .8..2.1 m
for 1.5 seconds, with head speed below 20 degrees/s for .6 seconds. Returning to
30 degrees and .9..1.8 m resets the wait. Maximum chat speed is .35 m/s and
25 degrees/s, with time-based damping, followed by a 3-second cooldown. Native
grabs, focused TMP inputs, pointer/scroll activity, scrolling velocity, dictation
and the permission panel block motion; activity imposes a further 1-second wait.
Tracking/focus loss and room loading/localization suspend following. Long turns
beyond 90 degrees or room changes require explicit Reubicar.

The planner captures one air destination at motion start. A conservative swept
envelope checks captured furniture/planes and live reservations throughout the
move; no automatic slide or jump is used when blocked. Reubicar explicitly commits
a fresh air plan measuring only the chat (the menu is excluded from reservations); failure keeps the current pose.
No-map success reports that physical validation was unavailable. Room state is
shown in the menu. Escanear habitación is visible only on Android when AirOnly
or Unavailable, and invokes the existing RoomContext loader with capture enabled
only on that user action. No separate permission requester was added.
While a scan is in progress or has failed, its nonempty action status takes
precedence over the general following status in the menu.

The authoring setup patches the menu prefab and saves scene references without
rebuilding the chat. Menu visibility calls its enabled lazy follower before reappearing;
a centered chat farther than 2.1 m does not hide the menu. Physical comfort, simultaneous native grabs, scrolling/input
on the headset and the system scan dialog need manual device validation; the
basic Editor checks do not establish these results.

Geometry is deliberately conservative: footprints are projected bounding rectangles;
this can reject a fit that a tighter silhouette would accept. Hidden holes are
represented by captured opening surfaces, not invented polygon-hole data.

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
matching of original MRUK labels. With a tracked viewer, sort by world distance
from the viewer to the world AABB centre, then ordinal physicalId and kind.
Without a tracked viewer, sort by ordinal physicalId, then kind.
Compare revisions between pages; restart after any change.

Each entry includes `physicalId:"physical:<anchor UUID>"`, `kind`, `labels`,
`readOnly:true`, world AABB `bounds`, optional known `facing` and
`viewerRelative:{position,distance,bearing,side,depth}` when a horizontal tracked viewer
frame exists. Viewer positions are in metres, +X right, +Y up, +Z away from the
viewer. Bearing is horizontal degrees from forward, positive to the right
(`atan2(x,z)`, -180..180). `side` is center when abs(x)<0.1 m, otherwise left/right;
`depth` is front when z>=0, otherwise behind. These two fields retain the existing
split direction contract instead of a single front/left/right/behind field.
Wall `facing` points to the viewer's side of the plane using the same rule as
physical reference resolution; without tracking it remains the captured normal.
Surfaces include `pose`, local polygon in metres and optional `supportId`;
wall/opening entries also include `width` and `height` in metres, the polygon's
local X/Y extents (not a guarantee that a rectangle fits a concave polygon).
`kind=tabletop` implicitly declares supportsContent and its pose/polygon provides
the top height; no separate `supportsContent` or `topHeight` fields are emitted;
volumes include oriented `box:{center,size,rotation}`. Rotations use Euler degrees.
The tabletop and its supporting volume share physicalId; filter kind to distinguish
them. Bounds resolution for such an ID uses the volume, so `above, gap=0` rests
the content's bounding envelope on the top of the captured box.

Door/window frames are openings. Invisible walls and global mesh are excluded.
Every other volume is furniture/obstacle geometry. Table/storage tops select the
box axis closest to world up; normal must be within 20 degrees. Storage must be
0.4..1.3 m above the captured floor. Beds and couches never become tabletops.
Boxes use transformed corners. A volume anchor with a singular or sheared
transform is skipped as a whole (box, tabletop and plane) instead of producing
misleading dimensions; the rest of the room stays `Ready` and `room_get.reason`
ends with `Skipped <n> invalid anchor(s) with singular or sheared transforms.`
Other geometry errors still fail the snapshot.

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
is created by relative placement. `place_relative` uses captured envelopes without
free-space, visibility or collision validation. `spatial_place` adds the captured
geometry checks described above; moved furniture remains unknown. Snapping is 22c.

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
