using System;
using Seyra.UI.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace Seyra.Editor
{
    public static class SeyraUIWindowPreviewBuilder
    {
        public const string ScenePath = "Assets/Seyra/UI/Catalog/UIWindowPreview.unity";
        public const string PrefabPath = "Assets/Seyra/UI/Catalog/UILibraryPreview.prefab";

        [MenuItem("Seyra/UI/Build Window Preview")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building the preview.");
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new InvalidOperationException("Close the window preview scene before rebuilding it.");
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(SeyraUIPrefabCatalogBuilder.CatalogPath);
            if (catalog == null || catalog.WindowPrefab == null) throw new InvalidOperationException("Build the UI library first.");
            catalog.Validate();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("UILibraryPreview");
                var controls = new GameObject("ControlsAnchor").transform;
                controls.SetParent(root.transform, false); controls.localPosition = new Vector3(-.5f, 1.4f, 1.5f);
                var gallery = new GameObject("GalleryAnchor").transform;
                gallery.SetParent(root.transform, false); gallery.localPosition = new Vector3(.5f, 1.4f, 1.5f);
                root.AddComponent<UILibraryPreview>().Configure(catalog, controls, gallery);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
                var camera = new GameObject("PreviewCamera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 1.4f, 0);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.07f, .09f, .11f);
                camera.fieldOfView = 60;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                var input = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule), typeof(Oculus.Interaction.PointableCanvasModule));
                input.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save the window preview scene.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Seyra/UI/Open Window Preview")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before opening the preview.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
