using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Seyra.UI.Library;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Seyra.Editor
{
    /// <summary>Thin authoring adapters over installed Meta assets. Never copies SDK source or changes PackageCache.</summary>
    public static class SeyraMetaUIAuthoring
    {
        public const string UISet = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet";
        public const string WindowPath = SeyraUILibraryBuilder.Root + "/Panels/UIXRWindow.prefab";
        public static GameObject Source(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(UISet + "/Prefabs/" + path + ".prefab")
            ?? throw new InvalidOperationException("Missing Meta UI Set component: " + path);
        static RectTransform Native(string path, string name)
        {
            var source = Source(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.name = name;
            go.AddComponent<UIMetaSource>().Configure(source);
            UIAssetResources.EnsureTextMaterials(go);
            UIKeyboardNavigation.EnableAutomatic(go);
            return (RectTransform)go.transform;
        }
        public static RectTransform Button(string name, string variant, string label)
        {
            var root = Native("Button/UnityUIButtonBased/" + variant + "Button_IconAndLabel_UnityUIButton", name);
            var text = root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "Label");
            text.text = label; text.richText = false;
            var icon = root.GetComponentsInChildren<UnityEngine.UI.Image>(true).First(t => t.name == "Icon");
            icon.gameObject.SetActive(false);
            var gap = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Gap");
            if (gap != null) gap.gameObject.SetActive(false);
            root.gameObject.AddComponent<UIActionButton>().Configure(root.GetComponent<UnityEngine.UI.Button>(), text, icon, icon.gameObject);
            var element = Get<UnityEngine.UI.LayoutElement>(root.gameObject);
            element.minWidth = element.preferredWidth = root.rect.width;
            element.minHeight = element.preferredHeight = root.rect.height;
            element.flexibleWidth = 1; element.flexibleHeight = 0;
            return root;
        }
        public static RectTransform Input(string name, bool growing)
        {
            var root = Native("TextInputField/TextInputField", name);
            var layout = root.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var field = root.GetComponentInChildren<TMP_InputField>(true);
            var title = root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "Title");
            var help = root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "HelperText");
            title.text = "Etiqueta"; help.text = ""; help.gameObject.SetActive(false);
            field.richText = false; field.characterLimit = 4000;
            var icon = field.transform.Find("Icon"); if (icon != null) icon.gameObject.SetActive(false);
            var size = Get<UnityEngine.UI.LayoutElement>(field.gameObject); size.minHeight = 48; size.flexibleWidth = 1;
            field.textComponent.raycastTarget = false;
            ((TMP_Text)field.placeholder).text = "Escribe aquí…";
            if (growing)
            {
                field.lineType = TMP_InputField.LineType.MultiLineNewline;
                field.textComponent.alignment = ((TMP_Text)field.placeholder).alignment = TextAlignmentOptions.TopLeft;
                field.gameObject.AddComponent<Seyra.UI.AutoGrowingInputField>();
            }
            root.gameObject.AddComponent<UIField>().Configure(title, help, field);
            return root;
        }
        public static RectTransform Toggle(string name, TMP_Text label, TMP_Text help, RectTransform root)
        {
            var control = Native("Button/UnityUIToggleBased/ToggleButton_Switch", "Switch");
            control.SetParent(root, false);
            Get<UnityEngine.UI.LayoutElement>(control.gameObject).minHeight = 48;
            root.gameObject.AddComponent<UIMetaSource>().Configure(Source("Button/UnityUIToggleBased/ToggleButton_Switch"));
            var field = root.gameObject.AddComponent<UIField>();
            field.Configure(label, help, control.GetComponent<UnityEngine.UI.Toggle>());
            FitToggleLayout(field, AssetDatabase.LoadAssetAtPath<SeyraUITheme>(SeyraUILibraryBuilder.ThemePath));
            return root;
        }
        public static void FitToggleLayout(UIField field, SeyraUITheme theme)
        {
            var control = (RectTransform)field.Control.transform;
            var nativeSize = field.GetComponent<UIMetaSource>().SourcePrefab.GetComponent<RectTransform>().rect.size;
            RectTransform slot;
            if (control.parent == field.transform)
            {
                slot = new GameObject("ControlContainer", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement)).GetComponent<RectTransform>();
                slot.SetParent(field.transform, false); slot.SetSiblingIndex(control.GetSiblingIndex()); control.SetParent(slot, false);
            }
            else slot = (RectTransform)control.parent;
            var layout = Get<UnityEngine.UI.HorizontalLayoutGroup>(slot.gameObject);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            Get<UIStyle>(slot.gameObject).Configure(theme, size: UISizeRole.Control);
            var size = Get<UnityEngine.UI.LayoutElement>(control.gameObject);
            size.minWidth = size.preferredWidth = nativeSize.x;
            size.minHeight = size.preferredHeight = nativeSize.y;
            size.flexibleWidth = size.flexibleHeight = 0;
        }
        public static void Slider(RectTransform root, TMP_Text label, TMP_Text help, TMP_Text value)
        {
            var control = Native("Slider/MediumSlider", "Slider"); control.SetParent(root, false);
            Get<UnityEngine.UI.LayoutElement>(control.gameObject).minHeight = 48;
            var slider = control.GetComponent<UnityEngine.UI.Slider>();
            root.gameObject.AddComponent<UIMetaSource>().Configure(Source("Slider/MediumSlider"));
            root.gameObject.AddComponent<UIField>().Configure(label, help, slider);
            root.gameObject.AddComponent<UISliderValue>().Configure(slider, value);
        }
        public static RectTransform Dropdown()
        {
            var root = Native("DropDown/DropDown1LineTextOnly", "UIDropdown");
            var sample = root.GetComponent<Oculus.Interaction.Samples.DropDownGroup>();
            var serialized = new SerializedObject(sample);
            var header = (UnityEngine.UI.Toggle)serialized.FindProperty("_headerToggle").objectReferenceValue;
            var title = (TMP_Text)serialized.FindProperty("_title").objectReferenceValue;
            var group = (UnityEngine.UI.ToggleGroup)serialized.FindProperty("_toggleGroup").objectReferenceValue;
            var options = group.GetComponentsInChildren<UnityEngine.UI.Toggle>(true);
            // The SDK sample owns a fixed list. This adapter supplies arbitrary choices and events.
            UnityEngine.Object.DestroyImmediate(sample);
            var prototype = options.First();
            foreach (var option in options) option.gameObject.SetActive(false);
            var adapter = root.gameObject.AddComponent<UIMetaDropdown>();
            adapter.Configure(header, prototype, (RectTransform)group.transform, title);
            // SetChoices is called by OnEnable at runtime; keep the authoring sample readable.
            root.gameObject.AddComponent<UIField>().Configure(title, null, header);
            return root;
        }
        public static void Backplate(RectTransform root)
        {
            var source = Source("Backplate/EmptyUIBackplateWithCanvas");
            var graphic = source.transform.Find("CanvasRoot/UIBackplate");
            var plate = UnityEngine.Object.Instantiate(graphic.gameObject, root, false);
            plate.name = "MetaBackplate"; plate.transform.SetAsFirstSibling();
            foreach (Transform child in plate.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var layout = plate.GetComponent<UnityEngine.UI.LayoutGroup>(); if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
            var mask = plate.GetComponent<UnityEngine.UI.Mask>(); if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
            var rect = (RectTransform)plate.transform; Stretch(rect);
            Get<UnityEngine.UI.LayoutElement>(plate).ignoreLayout = true;
            plate.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            root.gameObject.AddComponent<UIMetaSource>().Configure(source);
        }
        public static void ConfigureTheme(GameObject root)
        {
            var manager = Get<UIThemeManager>(root);
            var serialized = new SerializedObject(manager);
            var themes = serialized.FindProperty("_themes"); themes.arraySize = 2;
            themes.GetArrayElementAtIndex(0).objectReferenceValue = Theme("Dark");
            themes.GetArrayElementAtIndex(1).objectReferenceValue = Theme("Light");
            serialized.FindProperty("_currentThemeIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.ApplyCurrentTheme();
        }
        public static UITheme Theme(string name) => AssetDatabase.LoadAssetAtPath<UITheme>(UISet + "/Themes/UIThemeQuest_" + name + ".asset");
        public static UIXRWindow BuildWindow()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Source("Backplate/EmptyUIBackplateWithCanvas"));
            go.name = "UIXRWindow";
            var canvas = go.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.localScale = Vector3.one * .001f;
            // Retain Meta's scale and interaction surfaces; anchors/layouts own all child placement.
            var backplate = canvas.transform.Find("UIBackplate");
            var canvasLayout = canvas.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            canvasLayout.childControlWidth = canvasLayout.childControlHeight = true;
            canvasLayout.childForceExpandWidth = canvasLayout.childForceExpandHeight = true;
            var plateSize = Get<UnityEngine.UI.LayoutElement>(backplate.gameObject);
            plateSize.flexibleWidth = plateSize.flexibleHeight = 1;
            plateSize.minWidth = plateSize.preferredWidth = plateSize.minHeight = plateSize.preferredHeight = 0;
            var plateLayout = backplate.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            plateLayout.childControlWidth = plateLayout.childControlHeight = true;
            plateLayout.childForceExpandWidth = plateLayout.childForceExpandHeight = true;
            var content = new GameObject("Content", typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup)).GetComponent<RectTransform>();
            content.SetParent(backplate, false); Stretch(content);
            var layout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            var contentSize = Get<UnityEngine.UI.LayoutElement>(content.gameObject);
            contentSize.flexibleWidth = contentSize.flexibleHeight = 1;
            contentSize.minWidth = contentSize.preferredWidth = contentSize.minHeight = contentSize.preferredHeight = 0;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(24,24,24,24); layout.spacing = 12;
            var factory = go.AddComponent<UIFactory>();
            var window = go.AddComponent<UIXRWindow>(); window.Configure(canvas, content, go.GetComponent<UIThemeManager>(), factory);
            ConfigureTheme(go);
            SeyraWindowGrabAuthoring.Ensure(window);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, WindowPath);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab.GetComponent<UIXRWindow>();
        }
        public static void EnsureSceneInput()
        {
            // The native backplate already contains ray/poke surfaces. Quick Actions would duplicate them.
            if (UnityEngine.Object.FindFirstObjectByType<PointableCanvasModule>() != null) return;
            var events = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (events == null) events = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
            foreach (var module in events.GetComponents<UnityEngine.EventSystems.BaseInputModule>()) module.enabled = false;
            events.gameObject.AddComponent<PointableCanvasModule>();
        }
        public static void AddNativeCatalog(List<UITemplateEntry> entries)
        {
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[]{UISet + "/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!(prefab.transform is RectTransform)) continue; // world-space host has its own typed API
                var relative = path.Substring((UISet + "/Prefabs/").Length);
                var id = "meta." + relative.Substring(0, relative.Length - ".prefab".Length).Replace('/', '.');
                entries.Add(new UITemplateEntry(id, prefab.name, UITemplateKind.MetaComponent, prefab));
            }
        }
        public static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
        static T Get<T>(GameObject target) where T : Component => target.GetComponent<T>() ?? target.AddComponent<T>();
    }
}
