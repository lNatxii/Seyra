# 04 — World bounds and relative placement

Validated on 27 October 2026 in Unity Editor 6000.3.21f1, Play Mode.

## Results

- Unity runtime scripts compiled without errors.
- `npm test`: 4 tests passed, 0 failures, including new bounds/placement schemas,
  existing appearance schemas, provider permission parity and MCP transport.
- `BoundsChecks.Run`: 5,165 assertions passed; 96 directional placement cases
  plus request-group placements. Covers cube, sphere, capsule, cylinder, plane and
  quad as moving geometry and reference geometry in all six directions.
- `RunUnityIntegration.Run`: passed through actual MCP stdio, bridge WebSocket and
  Unity dispatch, including world bounds and relative placement/undo for all six
  primitives, alongside existing hierarchy, lifecycle and appearance checks.

The geometric fixture validates transformed mesh vertices against returned world
envelopes, nested/negative/nonuniform transforms with shear, a local-unit cube under
a parent scaled to `(100,0.001,25)`, aggregate bounds including root and child meshes,
offset container pivots, inactive objects, trash exclusion, invalid relationships,
singular parents, float precision rollback, mutation deduplication, operation and
request undo, and saved/loaded placement.

World-space UI checks include rotated/scaled Canvas and Image RectTransforms with
different pivots, child content extending outside the parent, all world corners,
and six directions as both moving container and reference. Screen-space Canvas is
rejected. UI panel authoring/persistence remains 05; no existing user scene content
was used as the fixture or saved by the tests.

The earlier permission test expected fewer rules than the committed configuration;
its assertion was updated to reflect existing read/grep/glob permissions. No
permissions were changed. Local transport tests require access to loopback sockets.

## Limits

See [the contract](../bridge/BOUNDS.md). Distances are between conservative world
AABB faces, not physical contact points. Structural bounds include inactive and
transparent content and ignore UI clipping. UI queries use the currently resolved
layout; future panel integration must avoid moving layout-driven elements directly.
No headset validation was performed; that remains roadmap 07.
