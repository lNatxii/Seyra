using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Seyra.UI.Library;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Seyra.Editor
{
    /// <summary>Checks imported references, composition isolation, local events and window geometry through Unity.</summary>
    public static class SeyraUILibraryValidation
    {
        [MenuItem("Seyra/UI/Validate Library and Windows")]
        public static void ValidateFromMenu() => Debug.Log(Run().ToString());

        public static JObject RunPreview()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Run preview interaction checks in Play Mode.");
            int checks = 0;
            void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
            var preview = Object.FindFirstObjectByType<UILibraryPreview>();
            Require(preview != null && preview.ControlsWindow != null && preview.GalleryWindow != null, "The preview did not create both windows.");
            Require(Object.FindObjectsByType<PointableCanvasModule>(FindObjectsSortMode.None).Length == 1, "The preview needs one canvas input module.");
            var primary = preview.ControlsWindow; var secondary = preview.GalleryWindow;
            UIActionButton Button(string label) => primary.GetComponentsInChildren<UIActionButton>(true).Single(b => b.Label == label);
            bool HasText(string value) => primary.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == value);
            var click = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            UnityEngine.EventSystems.ExecuteEvents.Execute(Button("Probar acción").gameObject, click, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            Require(HasText("Acción recibida."), "Pointer click did not reach the local action.");
            var toggle = primary.GetComponentsInChildren<UIField>().Where(f => f.GetComponent<UIMetaDropdown>() == null)
                .Select(f => f.Control).OfType<Toggle>().Single();
            toggle.isOn = true; Require(HasText("Opción activada."), "Toggle did not update the preview.");
            var slider = primary.GetComponentsInChildren<UIField>().Select(f => f.Control).OfType<Slider>().Single();
            slider.value = 75; Require(HasText("Valor: 75"), "Slider did not update the preview.");
            var input = primary.GetComponentsInChildren<TMP_InputField>().Single();
            input.text = "Texto de ejemplo"; Require(HasText("Texto: Texto de ejemplo"), "Input did not update the preview.");
            var dropdown = primary.GetComponentInChildren<UIMetaDropdown>();
            dropdown.SetValue(1, true); dropdown.Header.isOn = false;
            Require(dropdown.Value == 1 && HasText("Opción: 2"), "Dropdown did not update the preview.");
            int previousTheme = primary.Themes.CurrentThemeIndex;
            Button("Cambiar tema").OnClick.Invoke();
            Require(primary.Themes.CurrentThemeIndex != previousTheme, "Theme action failed.");
            Button("Cambiar tema").OnClick.Invoke();
            var pose = primary.transform.position;
            Button("Cambiar tamaño").OnClick.Invoke();
            Require(((RectTransform)primary.Canvas.transform).rect.width == 900 && primary.transform.position == pose, "Resize action changed pose or failed.");
            Button("Cambiar tamaño").OnClick.Invoke();
            Require(((RectTransform)primary.Canvas.transform).rect.width == 800, "Resize did not restore the initial dimensions.");
            int galleryInstances = secondary.GetComponentsInChildren<UIInstance>(true).Length;
            Button("Mostrar / ocultar catálogo").OnClick.Invoke();
            Require(!secondary.gameObject.activeSelf && primary.gameObject.activeSelf, "Close action affected the wrong window.");
            Button("Mostrar / ocultar catálogo").OnClick.Invoke();
            Require(secondary.gameObject.activeSelf && secondary.GetComponentsInChildren<UIInstance>(true).Length == galleryInstances, "Reopen recreated the gallery.");
            Require(input.text == "Texto de ejemplo", "Local input was lost when closing another window.");
            var next = secondary.GetComponentsInChildren<UIActionButton>().Single(b => b.Label == "Siguiente");
            for (int i = 0; i < 39; i++) next.OnClick.Invoke();
            Require(secondary.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.StartsWith("1 / 39 · ", StringComparison.Ordinal)), "Native gallery navigation did not wrap correctly.");
            return new JObject { ["checks"] = checks, ["passed"] = true, ["frame"] = Time.frameCount, ["windows"] = 2 };
        }

        public static JObject Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run library validation in Edit Mode.");
            int checks = 0;
            void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
            void Reject(Action operation, string message)
            {
                try { operation(); } catch (ArgumentException) { checks++; return; }
                throw new InvalidOperationException(message);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(SeyraUIPrefabCatalogBuilder.CatalogPath);
            Require(catalog != null, "Missing UI catalog."); catalog.Validate();
            Require(catalog.Entries.Count(e => e.Kind != UITemplateKind.MetaComponent) == 20, "Expected twenty semantic component aliases.");
            Require(catalog.MetaThemes.Count == 2 && catalog.MetaThemes.All(t => t != null), "Missing Meta themes.");
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { SeyraUILibraryBuilder.Root }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Missing prefab: " + path);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing script: " + path + "/" + transform.name);
                foreach (var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                    Require(text.font != null && text.fontSharedMaterial != null, "Missing font/material: " + path);
            }
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var anchor = new GameObject("ValidationAnchor");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(anchor, scene);
                var window = UIXRWindow.Create(catalog, anchor.transform, new Vector2(800, 600));
                var other = UIXRWindow.Create(catalog, anchor.transform, new Vector2(640, 480), 1);
                Require(window.Canvas.renderMode == RenderMode.WorldSpace, "The window must use World Space.");
                Require(window.Canvas.transform.localScale == Vector3.one * .001f, "Canvas units must remain millimeters.");
                Require(window.GrabFrame != null && window.GrabFrame.Grabbable != null, "Missing window grab frame.");
                Require(window.GetComponentsInChildren<PointableCanvas>(true).Length > 0, "Missing canvas ray/poke integration.");
                Require(window.GrabFrame.GetComponentsInChildren<PointableCanvas>(true).Length == 0, "Obsolete example canvas integration survived.");
                var borders = window.GrabFrame.GetComponentsInChildren<BoxCollider>(true);
                Require(borders.Length == 4 && borders.All(c => c.isTrigger), "Expected four trigger borders.");
                var position = window.transform.position; var rotation = window.transform.rotation;
                var canvasPosition = window.Canvas.transform.localPosition;
                var canvasRotation = window.Canvas.transform.localRotation;
                window.Resize(new Vector2(1000, 750));
                Require(window.transform.position == position && window.transform.rotation == rotation &&
                    window.Canvas.transform.localPosition == canvasPosition && window.Canvas.transform.localRotation == canvasRotation,
                    "Resizing moved the window or canvas pose.");
                Require(borders.Any(c => Mathf.Abs(c.size.x - 1.16f) < .0001f), "The grab frame did not follow the canvas width.");
                var rail = window.GrabFrame.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Require(rail != null && AssetDatabase.Contains(rail.sharedMesh), "The grab rail must use a shared persisted mesh.");
                window.Close(); window.Open();
                Require(window.gameObject.activeSelf && other.gameObject.activeSelf, "Window visibility is not independent.");
                window.SetTheme(1); Require(window.Themes.CurrentThemeIndex == 1, "Theme change failed.");
                int children = anchor.transform.childCount;
                Reject(() => UIXRWindow.Create(catalog, null, Vector2.one), "A missing anchor was accepted.");
                Reject(() => UIXRWindow.Create(catalog, anchor.transform, new Vector2(float.NaN, 600)), "NaN dimensions were accepted.");
                Reject(() => UIXRWindow.Create(catalog, anchor.transform, new Vector2(800, 0)), "Zero dimensions were accepted.");
                Reject(() => UIXRWindow.Create(catalog, anchor.transform, new Vector2(800, 600), 20), "An invalid theme was accepted.");
                Require(anchor.transform.childCount == children, "Invalid creation leaked a window.");
                var factory = window.Factory;
                var column = factory.InstantiateColumn();
                var label = factory.InstantiateText("<b>Literal text</b>", column);
                Require(!label.richText, "Model text must remain literal.");
                int clicks = 0;
                var button = factory.InstantiateAcceptButton(column, onClick: () => clicks++);
                button.OnClick.Invoke(); Require(clicks == 1, "Button callback failed.");
                Reject(() => factory.InstantiateText("Foreign", other.Content), "A foreign parent was accepted.");
                var toggle = factory.InstantiateToggle(column, new UIToggleOptions { IsOn = true });
                Require(((Toggle)toggle.Control).isOn, "Toggle initialization failed.");
                var field = factory.InstantiateGrowingInputField(column, new UIInputOptions { Text = "Initial", CharacterLimit = 128 });
                var fieldId = factory.InstanceOf(field).Id;
                factory.SetInputText(fieldId, "Updated");
                Require(((TMP_InputField)field.Control).text == "Updated", "Input update failed.");
                var foldout = factory.InstantiateFoldout(column, new UIFoldoutOptions { Title = "Details" });
                foldout.SetExpanded(false); Require(!foldout.Content.gameObject.activeSelf, "Foldout did not close.");
                var scroll = factory.InstantiateScrollView(column);
                factory.InstantiateText("Scrollable", scroll.content);
                factory.ScrollTo(factory.InstanceOf(scroll).Id, Vector2.up);
                var id = factory.InstanceOf(column).Id;
                factory.Remove(id); Require(!factory.TryGet(id, out _), "Removed content remained indexed.");
                foreach (var entry in catalog.Entries.Where(e => e.Kind == UITemplateKind.MetaComponent))
                {
                    var instance = factory.InstantiateMetaComponent(entry.Id);
                    Require(instance.ViewRoot != null, "Native component failed: " + entry.Id);
                    factory.Remove(instance.Id);
                }
                factory.Clear(); Require(factory.DescribeInstances().Length == 0, "Clear left owned instances.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return new JObject { ["checks"] = checks, ["prefabs"] = prefabPaths.Length, ["aliases"] = 20,
                ["nativeComponents"] = catalog.Entries.Count(e => e.Kind == UITemplateKind.MetaComponent), ["passed"] = true };
        }
    }
}
