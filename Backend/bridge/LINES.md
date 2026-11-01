# Managed guide lines and points

`create_point`: `{name,targetId,direction?,radius?,appearance?}`. Creates a normal managed sphere child centered on a native primitive collider surface. Direction is a finite non-zero world vector, default up, used once. Radius is 0.002–0.1 m, default 0.01; the initial world diameter is exact per axis. Default appearance is unlit white; explicit appearance follows `set_appearance` without entityId. Target ancestors require positive uniform scales; the target may be nonuniform. Physics transforms are synchronized before collider raycasting. Cylinder uses CapsuleCollider (rounded caps); plane/quad raycasts are one-sided. Later target scaling scales the point. Returns inspected sphere, primitiveType, targetId and operationId.

`create_line`: `{name,parentId,line}`; Generated routes to the open request. `update_line`: `{entityId,line}`, full replacement preserving identity, pose, parent and request.

`line`: `{points:Endpoint[2..16],width?,color?}`. An endpoint has exactly one field: `{entityId}` for a live managed Generated descendant other than a line or `{position:{x,y,z}}` local to the line parent. Consecutive identical endpoints are invalid. Width 0.001–0.05 m (default 0.01); sRGB color channels 0–1, alpha exactly 1 (default white). At most 128 live lines. Unknown fields and invalid numeric contracts are rejected.

ManagedLine follows active endpoint transforms in LateUpdate (order 150), terminates on the nearest panel surface perimeter segment, and updates renderer positions only after a change above 0.0001 m. Fixed endpoints follow the line transform, initially local identity. Width scales with absolute world X scale. View-aligned caps, no shadows, owned Seyra/Unlit material released on destruction.

`scene_get` adds `line`, `status` (`visible` / `target_missing`) and `missingTargets` IDs. A missing/inactive/trashed endpoint hides the renderer; restore/undo or ui_open makes it visible again. Lines do not contribute structural bounds. A line alone has no supported visual geometry; relative placement against it fails. Fixed endpoints outside a composition can exceed its reserved bounds.

Errors: `POINT_TARGET_INVALID`, `POINT_SURFACE_NOT_FOUND`, `LINE_ENDPOINT_INVALID`, `LINE_LIMIT`; malformed arguments use `INVALID_ARGUMENT`. All three tools use the existing history, requests, deduplication and trash circuit. Calculated renderer positions and material state are excluded from component comparison; semantic content and live target references are restored by undo.

Persistence v7 writes node `kind:"line"` and `line` (null on other kinds). Points persist as normal primitives. Loading accepts v1–v7 and resolves persistent endpoint IDs only against saved nodes after hierarchy reconstruction. Missing endpoints are legitimate and load hidden with target_missing; references to saved lines or the root are rejected. Allowed line components: Transform, GeneratedObject, ManagedLine, LineRenderer. Trash/history are not saved.

Batch nodes and order are documented in [BATCH.md](BATCH.md). Lines and points can be created by scene_build; scene_edit only adds update_line, without creations.
