using System;
using System.IO;
using Seyra.UI.Library;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.Editor
{
    /// <summary>Authors real prefabs and variants through Unity's APIs, in an isolated authoring scene.</summary>
    public static class SeyraUILibraryBuilder
    {
        public const string Root = "Assets/Seyra/UI";
        public const string ThemePath = Root + "/Themes/SeyraUITheme.asset";
        public const string GalleryPath = Root + "/Catalog/UIComponentGallery.prefab";
        public const string GalleryScenePath = Root + "/Catalog/UIComponentGallery.unity";
        static SeyraUITheme theme;
        static Scene authoringScene;
        static Sprite check, arrow, knob;

        [MenuItem("Seyra/UI/Build Component Library")]
        public static void BuildLibrary()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before generating prefabs.");
            if (SceneManager.GetSceneByPath(GalleryScenePath).isLoaded || SceneManager.GetSceneByPath(SeyraUIWindowPreviewBuilder.ScenePath).isLoaded)
                throw new InvalidOperationException("Close the library preview scenes before rebuilding the library.");
            var previous = SceneManager.GetActiveScene();
            authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(authoringScene);
            try
            {
                Directory.CreateDirectory(Root + "/Themes");
                Directory.CreateDirectory(Root + "/Layout");
                Directory.CreateDirectory(Root + "/Controls");
                Directory.CreateDirectory(Root + "/Panels");
                Directory.CreateDirectory(Root + "/Catalog");
                AssetDatabase.Refresh();
                theme = AssetDatabase.LoadAssetAtPath<SeyraUITheme>(ThemePath);
                if (theme == null)
                {
                    theme = ScriptableObject.CreateInstance<SeyraUITheme>();
                    theme.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                    if (theme.font == null) throw new InvalidOperationException("The existing TMP font is missing.");
                    AssetDatabase.CreateAsset(theme, ThemePath);
                }
                check = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
                arrow = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                BuildLayout();
                BuildText();
                BuildButton();
                BuildPanels();
                BuildInput();
                BuildScrollView();
                BuildDropdown();
                BuildToggle();
                BuildSlider();
                BuildFoldout();
                BuildListItem();
                BuildConfirmation();
                BuildGallery();
                SeyraMetaUIAuthoring.BuildWindow();
                SeyraUIPrefabCatalogBuilder.BuildCatalog();
                SeyraUIPresentationBuilder.Build();

                BuildPreviewScene();
                SeyraUIWindowPreviewBuilder.Build();
                AssetDatabase.SaveAssets();
                Debug.Log("Seyra UI: library and catalog generated in " + Root);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(authoringScene, true);
            }
        }

        [MenuItem("Seyra/UI/Open Component Gallery")]
        public static void OpenGallery()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before opening the catalog.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save scene changes before opening the catalog.");
            EditorSceneManager.OpenScene(GalleryScenePath);
        }

        static void BuildLayout()
        {
            var root = Rect("UIContainer");
            Element(root);
            Save(root, "Layout/UIContainer");
            root = Instance("Layout/UIContainer");
            root.name = "UIRow";
            Horizontal(root);
            Save(root, "Layout/UIRow");
            root = Instance("Layout/UIContainer");
            root.name = "UIColumn";
            Vertical(root);
            Save(root, "Layout/UIColumn");
        }

        static void BuildText()
        {
            var root = Rect("UIText");
            var text = root.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = "Text";
            text.richText = false;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            Style(root, UITextRole.Body);
            Save(root, "Controls/UIText");
        }

        static void BuildButton()
        {
            Save(SeyraMetaUIAuthoring.Button("UIButton", "Borderless", "Interact"), "Controls/UIButton");
            Save(SeyraMetaUIAuthoring.Button("UIButtonPrimary", "Primary", "Accept"), "Controls/UIButtonPrimary");
            Save(SeyraMetaUIAuthoring.Button("UIButtonSecondary", "Secondary", "Cancel"), "Controls/UIButtonSecondary");
            Save(SeyraMetaUIAuthoring.Button("UIButtonDanger", "Destructive", "Delete"), "Controls/UIButtonDanger");
        }

        static void BuildPanels()
        {
            var root = Instance("Layout/UIContainer");
            root.name = "UIPanel";
            SeyraMetaUIAuthoring.Backplate(root);
            Vertical(root, UISpacing.Large);
            Style(root, padding: UISpacing.Large, spacing: UISpacing.Standard);
            var content = Instance("Layout/UIColumn", root);
            content.name = "BodyContainer";
            root.gameObject.AddComponent<UIPanelView>().Configure(content);
            Save(root, "Panels/UIPanel");

            root = Instance("Panels/UIPanel");
            root.name = "UITitledPanel";
            var header = Column(root, "HeaderContainer");
            header.SetSiblingIndex(root.GetComponent<UIPanelView>().Content.GetSiblingIndex());
            var title = TextSlot(header, "TitleContainer", "Panel title", UITextRole.Heading);
            var description = TextSlot(header, "DescriptionContainer", "Optional content description.", UITextRole.Help, UIInkRole.Muted);
            content = root.GetComponent<UIPanelView>().Content;
            var actions = Row(root, "ActionsContainer");
            actions.gameObject.SetActive(false);
            root.GetComponent<UIPanelView>().Configure(content, actions, title, description);
            Save(root, "Panels/UITitledPanel");
        }

        static void BuildInput()
        {
            Save(SeyraMetaUIAuthoring.Input("UIInputField", false), "Controls/UIInputField");
            Save(SeyraMetaUIAuthoring.Input("UIInputFieldGrowing", true), "Controls/UIInputFieldGrowing");
        }

        static void BuildScrollView()
        {
            var root = Rect("UIScrollView");
            Image(root, false);
            Element(root).flexibleHeight = 1;
            Style(root, surface: UISurfaceRole.Surface, size: UISizeRole.ScrollRegion);
            var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = theme.controlHeight;
            var viewport = Rect("Viewport", root);
            Image(viewport, true);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Column(viewport, "Content");
            content.anchorMin = Vector2.up;
            content.anchorMax = Vector2.one;
            content.pivot = Vector2.up;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            Save(root, "Controls/UIScrollView");
        }

        static void BuildDropdown()
        {
            Save(SeyraMetaUIAuthoring.Dropdown(), "Controls/UIDropdown");
        }

        static void BuildToggle()
        {
            var root = Instance("Layout/UIColumn"); root.name = "UIToggle";
            var label = TextSlot(root, "LabelContainer", "Enable option", UITextRole.Label);
            var help = TextSlot(root, "HelpContainer", "", UITextRole.Help, UIInkRole.Muted); help.transform.parent.gameObject.SetActive(false);
            SeyraMetaUIAuthoring.Toggle("UIToggle", label, help, root);
            Save(root, "Controls/UIToggle");
        }

        static void BuildSlider()
        {
            var root = Instance("Layout/UIColumn"); root.name = "UISlider";
            var label = TextSlot(root, "LabelContainer", "Value", UITextRole.Label);
            var value = TextSlot(root, "ValueContainer", "", UITextRole.Body);
            var help = TextSlot(root, "HelpContainer", "", UITextRole.Help, UIInkRole.Muted); help.transform.parent.gameObject.SetActive(false);
            SeyraMetaUIAuthoring.Slider(root, label, help, value);
            Save(root, "Controls/UISlider");
        }

        static void BuildFoldout()
        {
            var root = Instance("Layout/UIColumn");
            root.name = "UIFoldout";
            var header = Button(root, "HeaderContainer", "Controls/UIButtonSecondary", "Details");
            header.SetIcon(arrow);
            var body = Column(root, "BodyContainer");
            TextSlot(body, "DescriptionContainer", "Content that can be expanded or collapsed.", UITextRole.Body);
            root.gameObject.AddComponent<UIFoldout>().Configure(header, body.gameObject);
            Save(root, "Controls/UIFoldout");
        }

        static void BuildListItem()
        {
            var root = Rect("UIListItem");
            Horizontal(root, UISpacing.Small);
            Image(root, false);
            Style(root, surface: UISurfaceRole.Subtle, padding: UISpacing.Small, spacing: UISpacing.Standard);
            var icon = IconSlot(root, "IconContainer");
            var iconImage = Image(Rect("Icon", icon), false);
            icon.gameObject.SetActive(false);
            var content = FlexibleColumn(root, "TextContainer");
            var title = TextSlot(content, "TitleContainer", "List item", UITextRole.Label);
            var description = TextSlot(content, "DescriptionContainer", "Item description.", UITextRole.Help, UIInkRole.Muted);
            var action = Button(root, "ActionContainer", "Controls/UIButtonSecondary", "Open");
            action.transform.parent.gameObject.SetActive(false);
            var panel = root.gameObject.AddComponent<UIPanelView>();
            panel.Configure(content, (RectTransform)action.transform.parent, title, description);
            root.gameObject.AddComponent<UIListItemView>().Configure(panel, iconImage, icon.gameObject, action);
            Save(root, "Controls/UIListItem");
        }

        static void BuildConfirmation()
        {
            var root = Instance("Panels/UITitledPanel");
            root.name = "UIConfirmationPanel";
            var panel = root.GetComponent<UIPanelView>();
            panel.Title = "Confirm action";
            panel.Description = "Review the message before continuing.";
            var message = TextSlot(panel.Content, "MessageContainer", "This panel emits local accept or cancel events.", UITextRole.Body);
            panel.Actions.gameObject.SetActive(true);
            var cancel = Button(panel.Actions, "CancelContainer", "Controls/UIButtonSecondary", "Cancel", true);
            var accept = Button(panel.Actions, "AcceptContainer", "Controls/UIButtonPrimary", "Accept", true);
            root.gameObject.AddComponent<UIConfirmationPanel>().Configure(accept, cancel, message);
            Save(root, "Panels/UIConfirmationPanel");
        }

        static void BuildGallery()
        {
            var root = Rect("UIComponentGallery");
            Vertical(root, UISpacing.Large);
            Image(root, false);
            Style(root, surface: UISurfaceRole.Background, padding: UISpacing.Large, spacing: UISpacing.Standard);
            root.gameObject.AddComponent<UIFactory>();
            root.gameObject.AddComponent<UIMetaCatalogBrowser>();
            Save(root, "Catalog/UIComponentGallery");
        }

        static void BuildPreviewScene()
        {
            var canvasRoot = Rect("UICatalogCanvas");
            var canvas = canvasRoot.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRoot.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            canvasRoot.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Instance("Catalog/UIComponentGallery", canvasRoot);
            SeyraMetaUIAuthoring.ConfigureTheme(canvasRoot.gameObject);
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            var cameraObject = new GameObject("UICatalogCamera", typeof(Camera));
            cameraObject.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cameraObject.GetComponent<Camera>().backgroundColor = theme.background;
            if (!EditorSceneManager.SaveScene(authoringScene, GalleryScenePath)) throw new InvalidOperationException("Could not save the component gallery scene.");
        }

        static UIPanelView Panel(RectTransform parent, string title, string description)
        {
            var panel = Instance("Panels/UITitledPanel", parent).GetComponent<UIPanelView>();
            panel.Title = title; panel.Description = description;
            return panel;
        }

        static UIActionButton Button(RectTransform parent, string slotName, string prefab, string label, bool flexible = false)
        {
            var slot = flexible ? FlexibleColumn(parent, slotName) : Column(parent, slotName);
            Element(slot).flexibleWidth = flexible ? 1 : 0;
            var button = Instance(prefab, slot).GetComponent<UIActionButton>(); button.Label = label;
            return button;
        }

        static RectTransform IconSlot(RectTransform parent, string name)
        {
            var slot = Rect(name, parent);
            Element(slot);
            Style(slot, size: UISizeRole.Icon);
            return slot;
        }

        static TMP_Text TextSlot(RectTransform parent, string name, string value, UITextRole role, UIInkRole ink = UIInkRole.Text)
            => Text(Column(parent, name), "Text", value, role, ink);

        static TMP_Text Text(RectTransform parent, string name, string value, UITextRole role, UIInkRole ink = UIInkRole.Text)
        {
            var rect = Instance("Controls/UIText", parent); rect.name = name;
            var text = rect.GetComponent<TMP_Text>(); text.text = value;
            Style(rect, role, ink);
            return text;
        }

        static RectTransform FlexibleColumn(RectTransform parent, string name)
        {
            var column = Column(parent, name);
            var size = Element(column); size.minWidth = size.preferredWidth = 0; size.flexibleWidth = 1;
            return column;
        }

        static RectTransform Column(RectTransform parent, string name)
        { var rect = Rect(name, parent); Vertical(rect); return rect; }
        static RectTransform Row(RectTransform parent, string name)
        { var rect = Rect(name, parent); Horizontal(rect); return rect; }

        static UnityEngine.UI.VerticalLayoutGroup Vertical(RectTransform rect, UISpacing padding = UISpacing.None)
        {
            var group = rect.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.UpperLeft;
            Style(rect, padding: padding, spacing: UISpacing.Small);
            return group;
        }

        static UnityEngine.UI.HorizontalLayoutGroup Horizontal(RectTransform rect, UISpacing padding = UISpacing.None, UISpacing spacing = UISpacing.Small)
        {
            var group = rect.gameObject.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.UpperLeft;
            Style(rect, padding: padding, spacing: spacing);
            return group;
        }

        static UnityEngine.UI.LayoutElement Element(RectTransform rect)
        {
            var size = rect.GetComponent<UnityEngine.UI.LayoutElement>() ?? rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.layoutPriority = 2;
            return size;
        }

        static UIStyle Style(RectTransform rect, UITextRole text = UITextRole.None, UIInkRole ink = UIInkRole.Text,
            UISurfaceRole surface = UISurfaceRole.None, UISpacing padding = UISpacing.None,
            UISpacing spacing = UISpacing.None, UISizeRole size = UISizeRole.None)
        {
            var style = rect.GetComponent<UIStyle>() ?? rect.gameObject.AddComponent<UIStyle>();
            style.Configure(theme, text, ink, surface, padding, spacing, size);
            return style;
        }

        static UnityEngine.UI.Image Image(RectTransform rect, bool raycast)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.raycastTarget = raycast;
            return image;
        }

        static void Focus(RectTransform rect)
        {
            var outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>(); outline.enabled = false;
        }

        static RectTransform Rect(string name, RectTransform parent = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = Vector2.one / 2;
            return rect;
        }

        static RectTransform Instance(string relativePath, RectTransform parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + relativePath + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing " + relativePath);
            return (RectTransform)((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).transform;
        }

        static void Save(RectTransform root, string relativePath)
        {
            foreach (var style in root.GetComponentsInChildren<UIStyle>(true)) style.RefreshTheme();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Root + "/" + relativePath + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Could not save " + relativePath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
    }
}
