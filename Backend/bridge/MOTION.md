# Parametric motion contract

## Implementation status

Feature 24 implements configuration/state types, the motion engine, Unity dispatch, history integration, swept placement, batch motion and persistence. `scene_get` includes motion state. Set/remove return `operationId` and support undo; control is not recorded. Matching motion signatures preserve live controlled channels during unrelated undo; failed operations force restoration of bases, phases and pause state even if configuration signatures match. Persistence uses v7 and reads versions 1–7. Play Mode MotionChecks passes all ten design cases (95 assertions), including dynamic direct-placement validity. SceneBatchChecks, LineChecks, LayoutChecks and SpatialPlacementChecks also pass. Muse and headset validation remain pending.

## Configuration

`motion_set` takes `{ entityId, motion, paused? }`:

| kind | Channel | Fields |
| --- | --- | --- |
| `spin` | Local rotation | `speed`: non-zero degrees/s, −720..720; optional `axis` |
| `orbit` | Parent-local position | Exactly one `pivotId` or `pivotPoint`; `speed`: non-zero degrees/s, −360..360; optional `axis` |
| `bob` | Parent-local position | `amplitude`: 0.005..0.5 parent-local units; `period`: 0.2..60 seconds; optional `axis` |

Vectors have exactly finite float-representable `x`, `y`, `z` numbers. Axis defaults to `(0,1,0)`, must be non-zero and is normalized by Unity. Unknown fields, null optional fields and irrelevant parameters are rejected. `pivotPoint` is local to the target's parent. `pivotId` must identify live Generated content, excluding lines, self and descendants; dependency cycles are invalid. Scene-dependent checks belong to the runtime tools.

One object can have `spin` plus either `orbit` or `bob`. Setting the same kind replaces it, capturing the current pose as base and resetting phase. `orbit` and `bob` conflict. Root Generated, lines, layouts and their direct children are invalid motion targets. At most 64 live objects may have motion.

## Tools and results

- `motion_set`: `{ entityId, motion, paused?: boolean }`. Recorded; returns confirmed object inspection with `motions` and `operationId`.
- `motion_remove`: `{ entityId, kind: 'spin' | 'orbit' | 'bob' }`. Recorded; removes that kind, keeps the current pose and releases its channel. An absent kind returns `MOTION_NOT_FOUND`.
- `motion_control`: `{ entityId?: string, action: 'pause' | 'resume' | 'reset' }`. Affects the object and descendants, or all Generated content if omitted. Returns `{ affected: number }`; zero is valid. Not recorded. Pause/resume preserve phase; reset restores base pose and phase zero. Pause does not release controlled channels.

Do not retry ambiguous mutations. Runtime errors use `RoomToolException`: `MOTION_CHANNEL_BUSY`, `MOTION_DRIVEN`, `MOTION_LIMIT`, `MOTION_PIVOT_INVALID`, `MOTION_AXIS_INVALID`, `MOTION_TARGET_INVALID`, `MOTION_NOT_FOUND`.

The planned `scene_get.motions` contract is:

```json
{
  "paused": false,
  "spin": { "axis": { "x": 0, "y": 1, "z": 0 }, "speed": 30, "angle": 12.5 },
  "position": { "kind": "orbit", "pivotId": "go_example", "axis": { "x": 0, "y": 1, "z": 0 }, "speed": 20, "angle": 40.1 },
  "status": "running"
}
```

Status is `running`, `paused`, `held`, `pivot_missing` or `pivot_invalid`. Missing/inactive pivots freeze phase until restored. Trash freezes phase. A held grab frame in the subtree freezes motion; releasing the object's own frame rebases its motion.

## Evaluation and state

Spin uses `baseRotation * AngleAxis(angle, axis)`. Orbit uses `pivotLocal + AngleAxis(angle, axis) * offset`, without rotating the object. Bob uses `basePosition + axis * amplitude * sin(2π * time / period)`. Phase is bounded by modulo 360 or period. Parent transforms carry and scale the motion; non-uniform parent scales yield elliptical orbits.

`MotionSpec.Parse`, `ToJson` and `Copy` cover declared configuration through `SpinSpec` and `PositionSpec`. `MotionState` adds pause, base rotation, offset/base position, angles/time and a runtime `Transform Pivot` reference. Copies preserve that reference and copy configuration independently, retaining exact float axes without renormalizing. Runtime Restore copies snapshots directly; saved JSON still uses strict parsing. JSON does not serialize the Transform reference; loading resolves `pivotId`.

The prepared state JSON uses v7: `{ paused, spin, position }`, where absent channels are null. Spin contains axis, speed, angle and baseRotation quaternion (no kind); orbit contains kind, axis, speed, pivotId/pivotPoint (unused pivot null), angle and offset; bob contains kind, axis, amplitude, period, time and basePosition. Parsing validates fields, numeric ranges and unit base quaternions. History captures the complete state, including the runtime pivot reference, but its signature compares only declared configuration. `SeyraMotion` is excluded from component integrity checks and accepted by PersistNode. Saved v7 slots include motion on every node (null if absent). Loading accepts versions 1–7; older slots have no motion. More than 64 animated nodes returns MOTION_LIMIT. Root, lines, layouts and direct layout children reject motion. Pivot IDs are resolved after rebuilding the hierarchy: absent pivots load as pivot_missing; root, line, self and descendant pivots are rejected. Motion is restored before activation, retaining base, phase and pause.

## Placement and batch integration

Placement planning uses swept geometry for animated descendants. The final containment check uses actual current geometry: recomputing world-aligned sweep boxes after rotating the root can enlarge the approximation and falsely reject a valid placement. Each recursive motion subtree is reduced to its world AABB's eight corners; spin is swept first, followed by orbit or bob. Rotation samples twelve angles, 30 degrees apart; bob adds both parent-scaled amplitude offsets. The current recursion root is excluded from its own sweep. Nested motion is evaluated recursively; a sibling orbit pivot is sampled at its current position. A destroyed pivot retains frozen geometry instead of inventing a pivot location. `scene_bounds` and `place_relative` continue to measure the current pose.

`NeedsPlacement` validates geometry against the current room and reservations, with no saved-pose comparison. MotionChecks dynamically confirms that bob on a directly spatial-placed object moves it without invalidating placement in unobstructed air. Motion that leaves the support or intersects geometry can still require placement; tabletop and physical-room validation remain pending.

`scene_build` accepts 1..2 motions on empty/primitive/panel nodes, with `pivotKey` available only there. It applies motion after lines and before placement. `scene_edit` accepts set/remove, excluding control. See [BATCH.md](BATCH.md) for key validation and ordering. Persistence v7 reads v1–v7 and tolerates missing saved pivots. The assistant prompt includes motion guidance and carrier examples.
