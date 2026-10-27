# Managed appearance contract

The scene MCP exposes `set_appearance` (OpenCode: `seyra_set_appearance`). The existing bridge registers the schema and forwards the validated command to Unity; no separate transport or permission path is introduced. `seyra_*: allow` remains sufficient.

```json
{
  "entityId": "go_<confirmed-object-id>",
  "style": "colored_translucent",
  "color": { "r": 0.2, "g": 0.4, "b": 0.8, "a": 0.35 }
}
```

`scene_get` and the mutation result return `appearance: { style, color }` and `appearanceIntact`. A false integrity flag means the stored managed values no longer describe an unmodified supported material. All four channels are required, finite sRGB values in [0,1]. The schema and Unity independently reject unknown styles, extra fields, missing channels and invalid alpha. The mutation requires a live managed primitive below Generated, not an external renderer, container, root or deleted object.

| style | Shader | Alpha | Lighting |
| --- | --- | --- | --- |
| `matte_opaque` | Seyra/MatteOpaque | Requires 1 | Diffuse direct lighting and ambient spherical harmonics; no specular |
| `colored_translucent` | Seyra/ColoredTranslucent | 0..1 | Diffuse lighting with alpha blending |
| `unlit` | Seyra/Unlit | Requires 1 | Flat uniform color |
| `ui_unlit` | Seyra/UiUnlit | 0..1 | Unlit; uGUI vertex tint, sprite atlas, stencil Mask and soft RectMask2D clipping |
| `plastic` | Seyra/Plastic | Requires 1 | Diffuse and fixed Blinn–Phong direct-light highlights; no reflection probe dependency |
| `transparent` | Seyra/Transparent | 0..1 | Unlit alpha blending without specular, refraction or distortion |

Transparent styles use the transparent queue, depth testing and no depth writes or shadow casting. Alpha=0 contributes no visible color; it does not disable colliders, selection or object identity. Transparency is conventional sorted blending: intersecting/nested surfaces can show ordering artifacts. `colored_translucent` is not physical subsurface scattering or frosted glass. `plastic` has a fixed highlight; smoothness is not part of this contract.

Shaders are UV-independent for generated geometry. The UI shader additionally samples the texture supplied by uGUI so sprite atlases and legacy UI graphics continue to work. It is not a replacement for TextMesh Pro's SDF text shader. UI vertex tint and CanvasGroup alpha multiply the material color. The UI shader includes both sides, stencil masking and rectangular clipping. No always-on-top depth override is added for world canvases.

## Runtime and asset ownership

Shaders are in `SeyraUnity/Assets/Seyra/Resources/Seyra/Appearance`, ensuring runtime lookup survives player shader stripping. Six authoring materials are in `SeyraUnity/Assets/Seyra/Materials`.

`AppearanceStyle` is the C# enum. `ManagedAppearance.SetAppearance(style, color)` is the shared script entry point. Generated primitives receive white `MatteOpaque` at creation. Each primitive owns one runtime material; changing its style or color reuses that material, leaving every other object and material asset untouched. The material is destroyed with its owner, including failed staging and scene teardown. Soft deletion retains it for restore/undo. Do not use `Renderer.material` to inspect it, since that API can implicitly clone materials.

The component also supports a uGUI `Graphic` with `UiUnlit` when used by a panel implementation. MCP currently manages primitives; this feature does not add panel creation or generic edits to existing UI. Use the UiUnlit material asset directly on a world-space Canvas graphic, or invoke the component from the future managed panel service.

History stores style/color values and compares the renderer configuration and managed appearance integrity before undo. It restores the same material instance. Direct edits to the shader, material assignment, color, queue, keywords, shadow mode or property block are outside the contract and cause save/set rejection or a history conflict. Material color comparisons allow Unity's color-space round-trip rounding.

## Persistence

New saves use format v2 and include `appearance` on every node: a style/color object for primitives and null for containers/root. Loading v1 remains supported and migrates primitives to white `matte_opaque`. Unknown versions, styles, invalid RGBA or appearance attached to a non-primitive are rejected. The file is fully parsed before reconstruction, and the loaded content is staged before replacing the live hierarchy. Saves reject arbitrary component/material modifications before overwriting a slot. Previous content and its materials remain recoverable through runtime undo.

## Verification commands

From `Backend/bridge`, run `npm test`. With the configured Unity Editor connected through Pipeline:

```powershell
unity command editor.playmode start --caller plugin --skill unity-cli --project-path C:\Proyectos\Seyra\SeyraUnity
unity command script run --file C:\Proyectos\Seyra\Backend\bridge\test-support\AppearanceChecks.cs --entry AppearanceChecks.Run --caller plugin --skill unity-cli --project-path C:\Proyectos\Seyra\SeyraUnity
unity command script run --file C:\Proyectos\Seyra\Backend\bridge\test-support\RunUnityIntegration.cs --entry RunUnityIntegration.Run --caller plugin --skill unity-cli --project-path C:\Proyectos\Seyra\SeyraUnity
```

`AppearanceChecks` uses an isolated generated root and temporary slots. `RunUnityIntegration` exercises the actual MCP stdio/WebSocket/runtime circuit, including appearance and persistence, on port 4101.

Exit Play Mode before `AppearanceAssets.BuildAndroidShaders`. That fixture builds a strict Android shader asset bundle in `.utmp/appearance-android` using the project's configured graphics APIs; it does not install a player or validate device rendering. `AppearanceAssets.CreateMaterials` recreates missing material assets without overwriting existing ones.

## Panel persistence compatibility

New scene saves use format v4, which retains this appearance contract and reads v1/v2/v3 files. Panels and labels use a separate semantic content field and do not accept set_appearance. See [panels and format v4](PANELS.md).

Phase 3 adds twelve semantic presentations and persisted local interaction through the same scene service. See [presentation contracts](PRESENTATIONS.md).
