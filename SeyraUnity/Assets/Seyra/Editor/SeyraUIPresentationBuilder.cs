using System;
using System.Collections.Generic;
using System.IO;
using Seyra.UI.Library;
using Seyra.UI.Presentations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Seyra.Editor
{
    public static class SeyraUIPresentationBuilder
    {
        public const string Root = "Assets/Seyra/UI/Presentations";
        public const string CatalogPath = "Assets/Seyra/UI/Resources/SeyraUIPresentations.asset";
        [MenuItem("Seyra/UI/Build Content Presentations")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode is required.");
            var components = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(SeyraUIPrefabCatalogBuilder.CatalogPath);
            if (components == null) throw new InvalidOperationException("The basic UI catalog is missing.");
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var entries = new List<UIPresentationTemplate>();
                foreach (var type in UIPresentationCodec.Types.Keys)
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(components.Get(UITemplateIds.TitledPanel).Prefab, scene);
                    try
                    {
                        root.name = "UI" + char.ToUpperInvariant(type[0]) + type.Substring(1);
                        var panel = root.GetComponent<UIPanelView>(); panel.Title = type; panel.Description = "";
                        var heading = (TMPro.TMP_Text)new SerializedObject(panel).FindProperty("title").objectReferenceValue;
                        heading.maxVisibleLines = 2; heading.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                        var headingSize = heading.transform.parent.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                        headingSize.minHeight = 0; headingSize.preferredHeight = 56; headingSize.flexibleHeight = 0;
                        UnityEngine.Object.DestroyImmediate(panel.Actions.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>());
                        panel.Actions.gameObject.AddComponent<UIAdaptiveRow>().Configure(components.DefaultTheme);
                        var shield = new GameObject("InputShield", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement));
                        shield.transform.SetParent(root.transform, false); shield.transform.SetAsFirstSibling();
                        var shieldRect = (RectTransform)shield.transform;
                        shieldRect.anchorMin = Vector2.zero; shieldRect.anchorMax = Vector2.one; shieldRect.offsetMin = shieldRect.offsetMax = Vector2.zero;
                        shield.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                        shield.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                        var body = panel.Content;
                        var bodySize = body.GetComponent<UnityEngine.UI.LayoutElement>();
                        if (bodySize == null) bodySize = body.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                        bodySize.minHeight = 0; bodySize.flexibleHeight = 1;
                        var scrollObject = (GameObject)PrefabUtility.InstantiatePrefab(components.Get(UITemplateIds.ScrollView).Prefab, scene);
                        scrollObject.transform.SetParent(body, false);
                        var scroll = scrollObject.GetComponent<UnityEngine.UI.ScrollRect>();
                        scrollObject.GetComponent<UIStyle>().Configure(components.DefaultTheme, surface: UISurfaceRole.Surface);
                        var scrollSize = scrollObject.GetComponent<UnityEngine.UI.LayoutElement>();
                        scrollSize.minHeight = 0; scrollSize.preferredHeight = components.DefaultTheme.scrollRegionHeight; scrollSize.flexibleHeight = 1;
                        var rootSize = root.GetComponent<UnityEngine.UI.LayoutElement>();
                        rootSize.minHeight = 0; rootSize.flexibleHeight = 1;
                        var factory = root.AddComponent<UIFactory>(); factory.Configure(components, (RectTransform)root.transform);
                        var view = root.AddComponent<UIPresentationView>(); view.Configure(type, panel, scroll, factory);
                        UIAssetResources.EnsureTextMaterials(root);
                        var saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + root.name + ".prefab");
                        if (saved == null) throw new InvalidOperationException("Could not save " + type);
                        entries.Add(new UIPresentationTemplate { type = type, prefab = saved.GetComponent<UIPresentationView>() });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                var catalog = AssetDatabase.LoadAssetAtPath<UIPresentationCatalog>(CatalogPath);
                if (catalog == null) { catalog = ScriptableObject.CreateInstance<UIPresentationCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
                catalog.Configure(components, entries); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                Debug.Log("Seyra: 12 uGUI presentations registered.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
