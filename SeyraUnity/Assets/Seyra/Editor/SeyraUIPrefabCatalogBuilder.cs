using System;
using System.Collections.Generic;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;

namespace Seyra.Editor
{
    public static class SeyraUIPrefabCatalogBuilder
    {
        public const string CatalogPath = SeyraUILibraryBuilder.Root + "/Catalog/SeyraUIPrefabCatalog.asset";

        [MenuItem("Seyra/UI/Rebuild Prefab Catalog")]
        public static void BuildCatalog()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before registering prefabs.");
            UpdateBindings();
            var entries = new List<UITemplateEntry>();
            Add(entries, UITemplateIds.Container, UITemplateKind.Container, "Layout/UIContainer");
            Add(entries, UITemplateIds.Row, UITemplateKind.Row, "Layout/UIRow");
            Add(entries, UITemplateIds.Column, UITemplateKind.Column, "Layout/UIColumn");
            Add(entries, UITemplateIds.Text, UITemplateKind.Text, "Controls/UIText");
            Add(entries, UITemplateIds.Button, UITemplateKind.Button, "Controls/UIButton");
            Add(entries, UITemplateIds.PrimaryButton, UITemplateKind.Button, "Controls/UIButtonPrimary");
            Add(entries, UITemplateIds.SecondaryButton, UITemplateKind.Button, "Controls/UIButtonSecondary");
            Add(entries, UITemplateIds.DangerButton, UITemplateKind.Button, "Controls/UIButtonDanger");
            Add(entries, UITemplateIds.Panel, UITemplateKind.Panel, "Panels/UIPanel");
            Add(entries, UITemplateIds.TitledPanel, UITemplateKind.Panel, "Panels/UITitledPanel");
            Add(entries, UITemplateIds.Confirmation, UITemplateKind.Confirmation, "Panels/UIConfirmationPanel");
            Add(entries, UITemplateIds.Input, UITemplateKind.Input, "Controls/UIInputField");
            Add(entries, UITemplateIds.GrowingInput, UITemplateKind.Input, "Controls/UIInputFieldGrowing");
            Add(entries, UITemplateIds.Dropdown, UITemplateKind.Dropdown, "Controls/UIDropdown");
            Add(entries, UITemplateIds.Toggle, UITemplateKind.Toggle, "Controls/UIToggle");
            Add(entries, UITemplateIds.Slider, UITemplateKind.Slider, "Controls/UISlider");
            Add(entries, UITemplateIds.Foldout, UITemplateKind.Foldout, "Controls/UIFoldout");
            Add(entries, UITemplateIds.ScrollView, UITemplateKind.ScrollView, "Controls/UIScrollView");
            Add(entries, UITemplateIds.ListItem, UITemplateKind.ListItem, "Controls/UIListItem");
            Add(entries, UITemplateIds.Gallery, UITemplateKind.Example, "Catalog/UIComponentGallery");
            SeyraMetaUIAuthoring.AddNativeCatalog(entries);
            var theme = AssetDatabase.LoadAssetAtPath<SeyraUITheme>(SeyraUILibraryBuilder.ThemePath);
            if (theme == null) throw new InvalidOperationException("The library theme is missing.");
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<UIPrefabCatalog>();
                catalog.Configure(theme, entries); AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else { catalog.Configure(theme, entries); EditorUtility.SetDirty(catalog); }
            catalog.ConfigureMeta(AssetDatabase.LoadAssetAtPath<UIXRWindow>(SeyraMetaUIAuthoring.WindowPath),
                new[] { SeyraMetaUIAuthoring.Theme("Dark"), SeyraMetaUIAuthoring.Theme("Light") });
            EditorUtility.SetDirty(catalog);
            catalog.ConfigureCloseIcon(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Icons/cancel_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48.png"));
            var gallery = PrefabUtility.LoadPrefabContents(SeyraUILibraryBuilder.GalleryPath);
            try
            {
                var browser = gallery.GetComponent<UIMetaCatalogBrowser>();
                if (browser != null) { browser.Configure(catalog); PrefabUtility.SaveAsPrefabAsset(gallery, SeyraUILibraryBuilder.GalleryPath); }
            }
            finally { PrefabUtility.UnloadPrefabContents(gallery); }
            AssetDatabase.SaveAssets();
        }
        static void Add(List<UITemplateEntry> entries, string id, UITemplateKind kind, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SeyraUILibraryBuilder.Root + "/" + path + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing prefab: " + path);
            entries.Add(new UITemplateEntry(id, prefab.name, kind, prefab));
        }
        // One-time binding upgrade for libraries generated before the composition API existed.
        // Paths belong to the authoring template; runtime composition uses serialized references only.
        static void UpdateBindings()
        {
            UpdatePrefab("Controls/UIListItem", go =>
            {
                if (go.GetComponent<UIListItemView>() != null) return false;
                var panel = go.GetComponent<UIPanelView>();
                var container = go.transform.Find("IconContainer");
                var image = container != null ? container.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
                var action = panel != null && panel.Actions != null ? panel.Actions.GetComponentInChildren<UIActionButton>(true) : null;
                if (image == null || action == null) throw new InvalidOperationException("UIListItem does not match the authoring template.");
                go.AddComponent<UIListItemView>().Configure(panel, image, container.gameObject, action); return true;
            });
            UpdatePrefab("Panels/UIConfirmationPanel", go =>
            {
                var confirmation = go.GetComponent<UIConfirmationPanel>();
                if (confirmation == null) throw new InvalidOperationException("UIConfirmationPanel is missing.");
                var serialized = new SerializedObject(confirmation);
                if (serialized.FindProperty("message").objectReferenceValue != null) return false;
                var panel = go.GetComponent<UIPanelView>();
                var messageSlot = panel.Content.Find("MessageContainer");
                var message = messageSlot != null ? messageSlot.GetComponentInChildren<TMPro.TMP_Text>(true) : null;
                if (message == null) throw new InvalidOperationException("The confirmation message is missing.");
                confirmation.Configure(confirmation.AcceptButton, confirmation.CancelButton, message); return true;
            });
        }
        static void UpdatePrefab(string relativePath, Func<GameObject, bool> update)
        {
            var path = SeyraUILibraryBuilder.Root + "/" + relativePath + ".prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try { if (update(contents)) PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
