# Spatial layouts (23a / 23b)

`layout_plan` is a query; `layout_create` and `layout_apply` use the existing
recorded scene mutation circuit. All schemas are strict. Use layouts for two
or more related cards, with a common size. Plan before writing card content.
When capacity is insufficient, select or group items that have their own
content. Do not replace them with one long list.

## Requests

```ts
type Size = { width: number; height: number };
type Layout = 'arc' | 'plane' | 'hub' | 'surround' | 'anchor' | 'wall' | 'surface';
type Behavior = 'fixed_facing' | 'fixed_aligned' | 'billboard';
type Params = {
  radius?: number; rows?: number; distance?: number;
  arrangement?: 'row' | 'column' | 'grid'; columns?: number;
  physicalId?: string; allowSpill?: boolean; pose?: 'standing' | 'lying';
};
type Spec = { layout: Layout; behavior?: Behavior; itemSize: Size;
  centerSize?: Size; gap?: number; params?: Params };
// layout_plan: Spec & {count: number; targets?: {targetId: string;
//   side?: 'above' | 'front' | 'left' | 'right'}[]}
// layout_create: Spec & {name: string; parentId?: string; items: Item[]}
// layout_apply: {rootId: string; layout?: Layout; behavior?: Behavior;
//   gap?: number; params?: Params; order?: string[]}
```

An Item has `name` and exactly one `panel` (the create_panel contract without
width/height) or `presentation` (the ui_present presentation object).
For image collections use `mediaCard` presentations with `itemSize` around
0.42 × 0.62 metres: fixed image header and scrollable description, body and facts.
Photos accept only a registered `imageRef`. The bridge adds optional internal `media` to
`layout_create` only and reports `data.mediaFailures` on successful results.
See [MEDIA.md](MEDIA.md) for resolution, errors, caching and persistence.
Anchor items additionally require `targetId` and accept `side` (default above).
Size is in meters: width .16..2, height .04..2; panel height >=.24,
presentation width/height >=.4. Labels retain their title and font constraints.
Count is 1..24; hub requires 3..9 including its center. Presentation IDs must
be unique across the runtime and at most 16 presentations may be live.

Default behavior is fixed_facing, except anchor (billboard) and wall/surface
(fixed_aligned, their only valid behavior). Billboard is
valid only for arc, hub and anchor. The model cannot request `lazy_follow` or
`params.side` in plan/create/apply; new planes use `fixed_facing`, centered ahead
of the user. Anchor item/target `side` remains available for relative placement.
No room is required for floating layouts;
physical anchor targets require current room references.

Arc: radius 1.2..2.6, rows 1..3; automatic radius increases to 2.2 before
adding a row, within 50 degrees each side and 1.4m total height. Plane:
distance .8..3 (default 1.6), row/column/grid, grid columns 1..6 (automatic
ceil(sqrt(count*1.6)), capped at 6); width within +/-40 degrees and height
<=1.4m. Hub: distance default 1.7, centerSize defaults to itemSize, satellites
start above and proceed clockwise; ellipse grows until the rectangles have
the requested gap, with total height <=1.5m and width within +/-45 degrees.
Surround: radius 1.4..3, rows 1..2, automatic minimum 1.6; starts ahead and
proceeds right around the user. Gap is 0.. .5, default .08 (.05 for anchor).
The root uses the working view from SpatialPlacementService.View().

Plan returns `fits,layout,behavior,count,maxItems,rows,columns,radius,items,
warnings,reason,perWall,roomRevision,simulated`. Room plans add each item
physicalId and perWall entries `{physicalId,capacity,used}`. Each item has index and world position/rotation (Euler
degrees). Capacity is computed with the same constraints, including explicit
parameters. Failed planning creates nothing. Anchor plans use the declared card rectangle; creation refines placement with the real panel/grab envelope through the shared relative offset calculation. Anchor overlap warnings identify
potentially overlapping indices; anchor placement does not resolve collisions.

Create/apply return `rootId,entityId,requestGroupId,layout,behavior,items,
warnings,operationId,operationIds`. Each item has index, entityId and name.
An existing request is reused; otherwise a request container is created and
closed in finally. Creation validates content, identities, catalogs, sizes and
capacity before allocation. Failures use the existing snapshot rollback.
Apply records one group operation, preserves card content/IDs, and accepts a
permutation of all direct children. Containers must have 1..24 equally sized
managed cards. Positive uniform ancestor scales are required. Parameters are
replaced as an object; changing layout resets its incompatible defaults.

## History and runtime behavior

`scene_get.layout` exposes the specification on the root; sibling order is
the reading order. `SeyraLayoutGroup` reconstructs enabled behavior from that
specification. Billboard runs at execution order +50, yaw only, .15s damping,
2 degree dead zone, and pauses while grabbed. Trash disables behavior;
restore and undo reconstruct it from the captured specification.

`DrivenTransform.Register(Transform, DrivenChannels, object owner)` and
`Unregister(owner)` share the Position/Rotation flags with future motion and
target following. Registrations use actual Transform identity, separating
runtime roots and permitting multiple owners. Disabled behavior unregisters.
History still compares hierarchy, scale, unmanaged channels, content and
component configuration; controlled channels retain their captured poses for
undo but do not cause HISTORY_CONFLICT when behavior moves them. Undo restores
the captured pose and behavior resumes from there.

## Persistence

Saves format 5 and reads formats 1..5. Container nodes add optional `layout`
with layout, behavior, itemSize, centerSize, gap and params. Cards and sibling
order use the existing format. Loading reconstructs behavior after the entire
hierarchy is ready. Physical targets, room revisions and reservations are
never saved; params.physicalId is stripped while other parameters remain.
Loaded wall/surface layouts retain their saved world poses without revalidation;
layout_apply chooses a current support when physicalId is omitted. Anchor target references are retained only during the runtime;
after load, applying anchor retains saved poses and reports
`anchor_targets_not_persisted`, until recreated with new targets.

Legacy saved slots may still contain `lazy_follow` and its `params.side`.
Unity's unchanged LayoutPlanner and SeyraLazyFollow continue to load them and
restore their runtime behavior. These fields are compatibility data, not model
inputs. Use `layout_apply` with explicit `behavior: "fixed_facing"` to stop an
old following layout; do not request a new following layout.

## Errors and validation

`MOTION_DRIVEN` rejects writes to registered Position/Rotation channels with
"Pause or remove the behavior first." `set_transform` checks only the supplied
position/rotation fields; scale is unaffected. `place_relative` checks position
and, when orient is not keep, rotation. `spatial_place` checks both channels;
the read-only `spatial_candidates` query remains available. Checks apply only
to the target Transform, so moving a parent is permitted. `layout_apply` is
exempt because it owns layout reconfiguration.

Layout-specific failures include LAYOUT_DOES_NOT_FIT (structured maxItems and
perWall fields in the error object), LAYOUT_BEHAVIOR_INVALID, LAYOUT_SIZE_INVALID,
LAYOUT_TOO_MANY_PRESENTATIONS,
and LAYOUT_NOT_A_GROUP, plus existing room/identity errors. Schema failures
are invalid arguments. Never repeat ambiguous mutations automatically.
Legacy runtime operations can still report LAYOUT_FOLLOW_BUSY (structured rootId)
when restoring saved following content.

Basic checks: `npm test --prefix Backend/bridge`, LayoutPlannerChecks in Edit
Mode, LayoutChecks in Play Mode, then the existing basic scene/UI suites.
Readability, comfort and physical interaction remain headset validation work.

## Room layouts (23b)

Both layouts require a Ready RoomSnapshot. ROOM_UNAVAILABLE rejects missing or
suspended room data; ROOM_REFERENCE_STALE rejects a missing physicalId;
INVALID_ARGUMENT rejects a reference of the wrong support kind. Plans are pure,
using captured geometry and reservations. They do not detect newly moved furniture.

Wall uses a centered grid with 10 cm wall margins and centers its useful vertical
range between floor + .8 m and floor + 2.2 m. Every oriented .05 m deep card box
sits 3 cm off the wall and passes SpatialPlacementPlanner.ValidatePose, including
openings, furniture, occlusion, reservations and the unchanged minimum 12 degree
angular height. A .2 m high card at 2.5 m cannot pass; use taller cards or group
items. A blocked anchor does not discard a wall's remaining free cells. Wall
scores use the work view and the shared Score function; cell validation turns
the view toward the projected anchor, or the shared corner for spill walls.

physicalId selects the preferred wall; otherwise the best scored wall with free
cells and incidence <=45 degrees is preferred. The most compact complete window
nearest the anchor is used, falling back to nearest free cells. allowSpill defaults
to true and traverses adjacent walls sharing a corner within .3 m, by score.
Reading order is left to right, top to bottom, wall by wall. Capacity includes
reachable free cells even when count is smaller, with the existing 5 cm reservation
separation applied within and across walls. Failed plans allocate nothing.

Surface uses captured tabletop supports. Without physicalId the existing spatial
planner selects a support for the complete block envelope. Standing cards are at
most .35 m high, lean backward 15 degrees and occupy at most two rows parallel
to the near edge. Lying cards are at most .30 x .30 m and rise 30 degrees from
horizontal with their top away from the viewer. Both sit 2 cm above the support.
Full rotated boxes go through ValidatePose and the unchanged oriented support
footprint rule. Its 10 cm edge margin also applies to lying cards (rather than
relaxing it to 5 cm). Small or low tables may fail comfort or reading constraints.

Create/apply register each card with wall/tabletop and its physicalId for
scene_get.spatial and needsPlacement. Applying a floating layout clears these
support registrations; undo restores the captured registrations with the poses.
Apply excludes its entire root from reservations and permits transitions between
floating and room layouts, resetting behavior defaults when the layout changes.
Persistence stays at version 5. Physical comfort and readability need headset
validation; synthetic and Bedroom00 checks are Editor-only evidence.
