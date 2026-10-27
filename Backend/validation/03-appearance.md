# 03 — Managed object appearance

Validated on 27 October 2026 with Unity 6000.3.21f1, URP 17.3.0 and the existing scene MCP bridge.

## Delivered

Six custom URP shaders and matching material assets: matte opaque, colored translucent, opaque unlit, UI unlit, plastic and transparent. The shaders use stereo/instancing macros and texture-independent geometry coloring. Lit styles use diffuse lighting/ambient SH; plastic adds fixed direct-light highlights. The UI shader supports stencil masks and soft rectangular clipping. Transparent styles use alpha blending without depth writes or shadow casting; no refraction or physical transmission is claimed.

`AppearanceStyle` and `ManagedAppearance.SetAppearance` provide the C# contract. `set_appearance` exposes the same closed style list and required RGBA through the existing MCP/bridge dispatcher. `scene_get` returns the stored style/color and an integrity flag. Each primitive owns one runtime material reused across changes and destroyed with the object. External material edits are detected rather than silently persisted. Soft deletion retains the material for recovery.

History restores style/color values and validates conflicts. Persistence v2 stores managed appearance; v1 loads as white matte. Invalid saved appearance is rejected before replacing live content. Arbitrary components and materials remain unsupported.

Full payload, limits and commands: [appearance contract](../bridge/APPEARANCE.md).

## Results

| Check | Result |
| --- | --- |
| TypeScript build and `npm test` | PASS: appearance schema/RGBA validation, existing permissions/contracts and MCP transport tests |
| Unity script compilation | PASS: no C# compilation errors |
| Six shaders imported and compiled in the Editor | PASS: supported shaders, no shader errors |
| Android strict shader asset bundle | PASS: all six shaders compiled for configured Vulkan and OpenGLES3; artifact in `.utmp/appearance-android` |
| `AppearanceChecks.Run` | PASS: six style changes, material isolation/reuse, invalid RGBA/styles/opaque alpha, alpha zero, deduplication, undo, six persistence round trips, v1 migration, malformed v2 rejection, delete/restore, external edit conflicts, protected saved files and material cleanup |
| `RunUnityIntegration.Run` | PASS: real MCP stdio → WebSocket → Unity circuit; appearance mutation/query, invalid alpha rejection, persistence and existing scene operations |
| `LifecycleChecks.Run` | PASS: existing request/history/persistence regression checks, including external edits, atomic overwrite, invalid files and path rejection |
| `AppearanceVisualCheck.Run` | PASS: rendered six swatches and an actual world-space Canvas with soft RectMask2D clipping while runtime frames advanced |
| Fully transparent rendering | PASS: alpha-zero output matches a disabled renderer pixel-for-pixel |

[Rendered comparison](03-appearance-preview.png). Translucent swatches use alpha 0.35 to show the background; the separate pixel comparison verifies alpha 0. The orange UI sample demonstrates clipping on a real Canvas Graphic.

## Scope and remaining validation

The material contract currently modifies managed primitives. UiUnlit is also ready to assign to uGUI Graphics through its material asset or C# component; managed panel creation remains roadmap item 05. It does not replace TextMesh Pro SDF shaders.

Android compilation verifies shader compilation, not stereo rendering or performance on a physical headset. Device visual checks, overlapping transparencies, world Canvas masks, passthrough composition and frame cost remain part of item 07. No device validation, GPU timing or physical-environment reflection is claimed. Alpha blending retains conventional sorting limits, and alpha zero leaves colliders active.

The Meta documentation CLI lookup failed because its local credential/network access was unavailable. Shader stereo conventions were checked against [Unity 6000.3 documentation](https://docs.unity.com/en-us/engine/6000.3/manual/xr/graphics/stereo-rendering/single-pass-instancing) and the installed URP source; Android compilation supplies project-specific evidence.
