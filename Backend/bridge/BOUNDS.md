# World bounds and relative placement

Optional physical references, `frame` and `orient` are documented in
[SPATIAL.md](SPATIAL.md). The world-axis contract below remains the default;
without those optional arguments existing calls keep their behavior.

`scene_bounds({entityId})` queries a live object's entire subtree. It returns
`entityId`, `runtimeId` and `bounds: {space:"world", kind:"aabb", center, size, min, max}`.
Vectors use `{x,y,z}`. Dimensions and gaps use Unity world units (metres under the
project's normal XR scale convention).

## Geometry contract

- Include the root's own geometry AND every descendant, including inactive objects
  and disabled renderers. Exclude the service-owned trash subtree. Empty transform
  containers contribute no point at their pivot; empty geometry is an error.
- Static `MeshRenderer` geometry uses all eight corners of `MeshFilter.sharedMesh.bounds`,
  each transformed by that object's complete `localToWorldMatrix`, then accumulated
  directly in world space. Do not aggregate local boxes under different parents and
  transform the resulting box, or multiply dimensions by `localScale`/`lossyScale`.
  The matrix includes nested rotations, nonuniform/negative scale and shear.
- `RectTransform` contributes its four `GetWorldCorners` points, including container
  and Canvas rectangles. Its root Canvas must use World Space. This includes
  rectangular panel dimensions independently of pivot, anchors or graphic type.
  Query after the application's layout has resolved; no layout or Canvas settings
  are changed by the query. Structural rectangles ignore masks, clipping and alpha.
- Other Renderer types, missing meshes, screen-space UI and RectTransforms without
  a Canvas fail explicitly. Collider-only objects do not have visual bounds under
  this contract. There is no silent switch to collider dimensions or pivot fallback.
- Managed panels are atomic typed geometry: use their Canvas and authored grab-border
  envelope, transformed with full matrices. Scroll-clipped text and internal animated
  meshes do not expand that layout envelope. Labels contribute their Canvas rectangle.
  See [panels](PANELS.md); unrelated unsupported renderers still fail explicitly.
- Zero thickness (plane, quad, UI) is valid. Bounds are conservative mesh-box and
  rectangle envelopes, not exact mesh silhouettes, collision shapes or shader-deformed
  surfaces. Mesh bounds must be maintained by any future mesh-editing feature.

## Placement contract

`place_relative({entityId, referenceId, direction, gap})` translates a managed live
descendant of Generated relative to another live object's subtree. The reference
may be external. Generated itself cannot be moved by this tool. Target and reference
must be disjoint: self and either ancestor relationship are rejected.

| Direction | World axis | Required face separation |
| --- | --- | --- |
| above | +Y | moving.min.y = reference.max.y + gap |
| below | -Y | moving.max.y = reference.min.y - gap |
| right | +X | moving.min.x = reference.max.x + gap |
| left | -X | moving.max.x = reference.min.x - gap |
| front | +Z | moving.min.z = reference.max.z + gap |
| behind | -Z | moving.max.z = reference.min.z - gap |

Other axes align bounds centers. Gap is required, finite and non-negative. Zero
means touching envelopes; curved/rotated surfaces may still have visible separation.
This operation does not attach content, track reference motion or solve physics.

Compute fresh bounds for both subtrees, subtract their faces/centers to obtain a
world translation, and add it to the target's world position. Never assign a desired
bounds center as the Transform position: the pivot offset must survive. Parent,
local rotation and local scale remain unchanged. A singular parent matrix, non-finite
result or actual placement error greater than 0.0001 world units rejects the change.
Rejected operations restore the old local position and create no history entry.

Results include inspected object state, resulting `bounds`, `referenceId`,
`referenceBounds`, `direction`, `gap` and the standard operation/request IDs. The
existing dispatcher provides request deduplication. Operation and request undo use
the existing snapshot/conflict mechanism. Deleting/restoring preserves placement.
The resulting local Transform already persists in content format v2; no new stored
bounds or format migration is needed. Bounds are always recomputed after load.

## UI scope

The bounds service is ready for world-space UI rectangles and mixed mesh/UI
containers. This delivery does not add panel authoring or UI persistence: those
remain roadmap 05. Existing persistence rejects unsupported UI components rather
than discarding them. Layout/animation/tracking can overwrite a later Transform;
the UI integration must move an undriven wrapper when layout owns panel geometry.
Bounds queried before a later layout pass describe the current resolved geometry,
not a prediction of that pass.

## Verification

- `npm test` in `Backend/bridge`: schema validation, provider parity and local MCP
  transport behavior. The permission assertion reflects the already configured
  read/grep/glob rules; implementation changes no permission rules.
- In Play Mode, run `test-support/BoundsChecks.cs`, entry `BoundsChecks.Run` through
  Unity Pipeline. Uses an isolated service/root and temporary slots, cleaned in
  `finally`; tests all six native primitives, each direction as target/reference,
  transformed mesh vertex containment, nested geometry, extreme aspect ratios,
  negative scale/shear, noncentral pivots, inactive/trash, rejected mutations,
  operation/request undo, deduplication, persistence and world-space UI.
- Run `test-support/RunUnityIntegration.cs`, entry `RunUnityIntegration.Run` for
  MCP → bridge → Unity tests, including world bounds and placement/undo for each
  native primitive. Uses separate port 4101 and isolated objects/storage.

Unity API references: [GetWorldCorners](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RectTransform.GetWorldCorners.html),
[Mesh.bounds](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh-bounds.html).
