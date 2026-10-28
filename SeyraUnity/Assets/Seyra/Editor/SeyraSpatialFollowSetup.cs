using System;
using System.Linq;
using Seyra.Spatial;
using Seyra.UI;
using Seyra.UI.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.Editor
{
    public static class SeyraSpatialFollowSetup
    {
        const string MenuPath = "Assets/Seyra/UI/Panels/SeyraSpatialMenu.prefab";
        static void Reference(UnityEngine.Object owner, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
            if (PrefabUtility.IsPartOfPrefabInstance(owner)) PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
        }
        public static void ConfigureMenu(GameObject root)
        {
            if (root.GetComponent<SeyraHandMenu>() == null || root.GetComponentInChildren<SeyraSpatialMenu>(true) == null)
                throw new InvalidOperationException("Rebuild the hand menu prefab with Seyra/UI/Build Hand Menu Prefab first.");
        }
        public static void ConfigureScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var app = roots.FirstOrDefault(r => r.name == "SeyraAssistantApp");
            // The OpenCode testbed does not contain a spatial chat/menu app.
            if (app == null) return;
            var chat = app.GetComponentInChildren<SeyraSpatialChatWindow>(true);
            var menu = app.GetComponentInChildren<SeyraSpatialMenu>(true);
            if (chat == null || menu == null) return;
            var placement = roots.SelectMany(r => r.GetComponentsInChildren<SpatialPlacementService>(true)).FirstOrDefault();
            var room = roots.SelectMany(r => r.GetComponentsInChildren<RoomContext>(true)).FirstOrDefault();
            var controller = app.GetComponent<SpatialFollowController>() ?? app.AddComponent<SpatialFollowController>();
            var rig = roots.SelectMany(r => r.GetComponentsInChildren<OVRCameraRig>(true)).FirstOrDefault();
            Reference(controller, "head", rig?.centerEyeAnchor); Reference(controller, "chat", chat);
            Reference(controller, "chatWindow", chat.GetComponent<UIXRWindow>()); Reference(controller, "placement", placement);
            Reference(menu, "chatWindow", chat); Reference(menu, "chatXrWindow", chat.GetComponent<UIXRWindow>());
            Reference(menu, "spatialFollow", controller); Reference(menu, "roomContext", room);
            var hand = menu.GetComponentInParent<SeyraHandMenu>();
            Reference(hand, "head", rig?.centerEyeAnchor); Reference(hand, "leftControllerAnchor", rig?.leftControllerAnchor);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        [MenuItem("Seyra/UI/Configure Hand Menu Scenes")]
        [MenuItem("Seyra/Spatial/Configure Chat Following")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before configuring spatial following.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
            ConfigureMenu(prefab);
            var previous = SceneManager.GetActiveScene();
            foreach (var path in new[] { "Assets/Scenes/SeyraAssistant.unity", "Assets/Scenes/OpencodeTestUI.unity" })
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var app = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "SeyraAssistantApp");
                    if (app != null)
                    {
                        // Replace only the menu instance; preserve chat, room, rig and their file IDs.
                        var oldHand = app.GetComponentInChildren<SeyraHandMenu>(true);
                        var oldMenu = app.GetComponentInChildren<SeyraSpatialMenu>(true);
                        if (oldHand != null) UnityEngine.Object.DestroyImmediate(oldHand.gameObject);
                        else if (oldMenu != null) UnityEngine.Object.DestroyImmediate(oldMenu.gameObject);
                        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, app.transform);
                        root.name = "HandMenu";
                        ConfigureScene(scene);
                    }
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            if (previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
}
