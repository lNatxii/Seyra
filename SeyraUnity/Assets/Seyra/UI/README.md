# UI library and windows — phase 1

Unity and Meta assets keep stable GUIDs; native SDK assets remain package
references, not local copies.

## Included

- Twenty semantic component aliases and thirty-nine native Meta UI Set entries.
- Layout containers, text, four action buttons, panels, confirmation, inputs,
  dropdown, toggle, slider, foldout, scroll view and list item.
- Semantic theme roles, scoped composition, local events and keyboard navigation.
- World Space window with a millimeter Canvas, independent content and theme,
  four grab borders, shared baked rail mesh, ray/poke surfaces and native grab.
- Twenty-one library prefabs plus one new two-window demonstration prefab.
- Editor generators and executable validation commands.

The library does not create a camera, rig, EventSystem or backend connection.
Supply an explicit parent in a loaded scene. XR scenes need one configured
`PointableCanvasModule` on their EventSystem and the existing interaction rig.
The desktop window preview supplies its own EventSystem with both desktop and
Meta canvas modules, plus a non-XR URP camera. Its camera is not part of the
reusable demonstration prefab.

## Try it

1. Open **Seyra > UI > Open Window Preview** and enter Play Mode.
2. Test actions, fields, the dropdown and the native component gallery.
3. Change the theme and window size. Hide and reopen the gallery.

The preview is `Catalog/UIWindowPreview.unity`. For a visual catalog of native
components alone, open `Catalog/UIComponentGallery.unity`.

`Catalog/UILibraryPreview.prefab` contains the demonstration and its two anchors,
without a camera, rig or EventSystem. It can be placed in a test scene with the
existing XR rig and canvas input module to test native grab, ray and poke. Its
default anchors are authored around a standing eye height; adjust the prefab
instance's pose for the test environment. Do not put this demonstration under
`Generated`: generated content and persistence arrive in phase 2.

## Compose locally

```csharp
var window = UIXRWindow.Create(catalog, sceneAnchor, new Vector2(800, 600));
var factory = window.Factory;
var panel = factory.InstantiateTitledPanel(options: new UIPanelOptions {
    Title = "Example", Description = "Local presentation", ShowActions = true
});
factory.InstantiateText("Content", panel.Content);
factory.InstantiateAcceptButton(panel.Actions,
    new UIButtonOptions { Label = "Accept" }, () => HandleLocalAction());
window.Resize(new Vector2(900, 700));
window.Close();
window.Open();
```

`UIFactory` owns instances and their layout slots inside its configured scope.
Consumers use typed adapters and serialized content/actions references rather
than hierarchy-name searches. `UIXRWindow.Create` is available without creating
a temporary factory; `UIFactory.InstantiateXRWindow` delegates to the same path.
Dimensions are Canvas units, not world meters. Native grab preserves scale;
explicit resizing changes the Canvas dimensions and refits the grab borders.
Closing hides a window and preserves its local controls. It does not define
chat-session lifetime; that belongs to phase 4.

## Regenerate and validate

- **Seyra > UI > Build Component Library** rebuilds the library, both preview
  scenes and the demonstration prefab. Existing GUIDs are preserved by Unity.
- **Rebuild Prefab Catalog**, **Update Window Grab Frame**, and **Build Window
  Preview** support narrower updates.
- Close both preview scenes before rebuilding; the generators refuse to
  overwrite an open preview scene. Other open scenes are preserved.
- **Validate Library and Windows** runs `SeyraUILibraryValidation.Run()` in Edit
  Mode, including actual instantiation of all registered native components.
- `SeyraUILibraryValidation.RunPreview()` tests local interactions while the
  window preview is running in Play Mode.

Technical diagnostics, menus and comments use English. Preview text is deliberately
localized in Spanish.

## Integration boundaries

Room-placement services, geometry-query markers, chart hosts, chat docks,
presentation hosts and backend tools are not dependencies of this library.
The window keeps native Meta interaction and visuals; it does not auto-register
with a global room or scene service. The library generator also builds the twelve phase-3 presentations.

Phase 2 now adds managed panels, identity, history, trash and versioned persistence
to the existing `SceneToolService`; see `Backend/bridge/PANELS.md` at the repository root.
The phase-1 demonstration remains isolated from Generated. Phase 3 adds the twelve full presentation contracts with shared history and persistence v4. Phase 4 will connect spatial chat to the current conversation logic.

Editor validation is not physical headset validation. See the evidence and
remaining checks in `Progress/Completed/05-ui-phase-1.md` at the repository root.
