using System;
using System.Linq;
using Meta.XR.MRUtilityKit;
using Seyra.Spatial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.Editor
{
    public static class SeyraRoomSetup
    {
        [MenuItem("Seyra/Spatial/Configure Room Support")]
        public static void Configure()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before configuring room support.");
            var previous=SceneManager.GetActiveScene();
            foreach(var path in new[]{"Assets/Scenes/SeyraAssistant.unity","Assets/Scenes/OpencodeTestUI.unity"})
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                try
                {
                    var roots=scene.GetRootGameObjects();
                    var providers=roots.SelectMany(r=>r.GetComponentsInChildren<MRUK>(true)).ToArray();
                    if(providers.Length>1)throw new InvalidOperationException("Only one MRUK provider is supported per scene.");
                    var provider=providers.FirstOrDefault();
                    if(provider==null)provider=new GameObject("MRUK").AddComponent<MRUK>();
                    provider.SceneSettings??=new MRUK.MRUKSettings();
                    provider.SceneSettings.LoadSceneOnStartup=false;provider.SceneSettings.DataSource=MRUK.SceneDataSource.Device;provider.EnableWorldLock=true;
                    var context=roots.SelectMany(r=>r.GetComponentsInChildren<RoomContext>(true)).FirstOrDefault();
                    if(context==null)context=new GameObject("SeyraRoom").AddComponent<RoomContext>();
                    var rig=roots.SelectMany(r=>r.GetComponentsInChildren<OVRCameraRig>(true)).FirstOrDefault();
                    context.Configure(provider,rig?.centerEyeAnchor);
                    foreach(var manager in roots.SelectMany(r=>r.GetComponentsInChildren<OVRManager>(true)))
                    {
                        var serialized=new SerializedObject(manager);var request=serialized.FindProperty("requestScenePermissionOnStartup");
                        if(request==null)throw new InvalidOperationException("Cannot verify OVRManager scene permission ownership.");
                        request.boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    EditorUtility.SetDirty(provider);EditorUtility.SetDirty(context);
                    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                }
                finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            }
            if(previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
    }
}
