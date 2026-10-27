using System;
using System.Linq;
using Oculus.Interaction;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.Editor
{
    public static class SeyraPanelRuntimeSetup
    {
        [MenuItem("Seyra/UI/Configure Panel Runtime Scenes")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before configuring scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes before configuring panel runtime scenes.");
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(SeyraUIPrefabCatalogBuilder.CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Build the UI library first.");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var path in new[] { "Assets/Scenes/OpencodeTestUI.unity", "Assets/Scenes/SeyraAssistant.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    var runtimes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SeyraSceneRuntime>(true)).ToArray();
                    if (runtimes.Length != 1) throw new InvalidOperationException("Expected one scene runtime: " + path);
                    var serialized = new SerializedObject(runtimes[0]);
                    serialized.FindProperty("uiCatalog").objectReferenceValue = catalog;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    var events = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true)).ToArray();
                    if (events.Length > 1) throw new InvalidOperationException("Multiple EventSystems: " + path);
                    var input = events.SingleOrDefault() ?? new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
                    var module = input.GetComponent<PointableCanvasModule>() ?? input.gameObject.AddComponent<PointableCanvasModule>();
                    module.ExclusiveMode = false;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene: " + path);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }
    }
}
