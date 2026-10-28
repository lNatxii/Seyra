# Floating spatial layouts (23a)

`layout_plan` is a query; `layout_create` and `layout_apply` use the existing
recorded scene mutation circuit. All schemas are strict. Use layouts for two
or more related cards, with a common size. Plan before writing card content.
When capacity is insufficient, select or group items that have their own
content. Do not replace them with one long list.

## Requests

```ts
type Size = { width: number; height: number };
type Layout = 'arc' | 'plane' | 'hub' | 'surround' | 'anchor';
type Behavior = 'fixed_facing' | 'billboard' | 'lazy_follow';
type Params = {
  radius?: number; rows?: number; distance?: number;
  arrangement?: 'row' | 'column' | 'grid'; columns?: number;
  side?: 'left' | 'center' | 'right';
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
Anchor items additionally require `targetId` and accept `side` (default above).
Size is in meters: width .16..2, height .04..2; panel height >=.24,
presentation width/height >=.4. Labels retain their title and font constraints.
Count is 1..24; hub requires 3..9 including its center. Presentation IDs must
be unique across the runtime and at most 16 presentations may be live.

Default behavior is fixed_facing, except anchor (billboard). Billboard is
valid only for arc, hub and anchor. Lazy follow is valid only for plane with
1..3 items; one live following layout per runtime, including inactive content.
It follows the root in yaw, pauses while a child is grabbed, and defaults to
.9m forward, .35m right and .1m below eye height. Fixed aligned, wall and
surface are deferred to 23b. No room is required for floating layouts;
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
warnings,reason`. Each item has index and world position/rotation (Euler
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
never saved. Anchor target references are retained only during the runtime;
after load, applying anchor retains saved poses and reports
`anchor_targets_not_persisted`, until recreated with new targets.

## Errors and validation

Layout-specific failures include LAYOUT_DOES_NOT_FIT (message includes
maxItems), LAYOUT_BEHAVIOR_INVALID, LAYOUT_SIZE_INVALID,
LAYOUT_TOO_MANY_PRESENTATIONS, LAYOUT_FOLLOW_BUSY (structured rootId),
and LAYOUT_NOT_A_GROUP, plus existing room/identity errors. Schema failures
are invalid arguments. Never repeat ambiguous mutations automatically.

Basic checks: `npm test --prefix Backend/bridge`, LayoutPlannerChecks in Edit
Mode, LayoutChecks in Play Mode, then the existing basic scene/UI suites.
Readability, comfort and physical interaction remain headset validation work.
