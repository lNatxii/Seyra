using System;
using System.Linq;
using Seyra.Spatial;
using Seyra.UI;
using Seyra.UI.Library;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Seyra.Editor
{
    public static class SeyraSpatialFollowSetup
    {
        const string MenuPath="Assets/Seyra/UI/Panels/SeyraSpatialMenu.prefab";
        static void Reference(UnityEngine.Object owner,string name,UnityEngine.Object value)
        {var serialized=new SerializedObject(owner);serialized.FindProperty(name).objectReferenceValue=value;serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(owner);}
        public static void ConfigureMenu(GameObject root)
        {
            var menu=root.GetComponent<SeyraSpatialMenu>();var window=root.GetComponent<UIXRWindow>();
            var source=root.GetComponentsInChildren<Button>(true).First(b=>b.name=="RecenterButton");
            var row=window.Content.Find("RoomControls");
            if(row==null)
            {
                var go=new GameObject("RoomControls",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(LayoutElement));row=go.transform;row.SetParent(window.Content,false);
                var layout=go.GetComponent<HorizontalLayoutGroup>();layout.spacing=12;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
                var size=go.GetComponent<LayoutElement>();size.minHeight=size.preferredHeight=52;
            }
            Button Button(string name,string label)
            {
                var existing=row.Find(name);var button=existing!=null?existing.GetComponent<Button>():UnityEngine.Object.Instantiate(source,row,false);
                button.name=name;button.onClick=new Button.ButtonClickedEvent();button.GetComponentInChildren<TMP_Text>(true).text=label;
                var size=button.GetComponent<LayoutElement>();size.minWidth=0;size.preferredWidth=260;size.flexibleWidth=1;
                return button;
            }
            Reference(menu,"followButton",Button("FollowButton","Seguirme"));Reference(menu,"scanRoomButton",Button("ScanRoomButton","Escanear habitación"));
            window.GrabFrame?.Grabbable?.InjectOptionalTargetTransform(root.transform);
            var legacy=root.GetComponent<SeyraLazyFollow>();if(legacy!=null)legacy.enabled=true;
            var visibility=root.GetComponent<SeyraMenuVisibility>();
            if(visibility!=null)
            {
                var serialized=new SerializedObject(visibility);serialized.FindProperty("maxChatDistance").floatValue=2.1f;
                serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(visibility);
            }
            var summary=root.GetComponentsInChildren<TMP_Text>(true).First(t=>t.name=="StatusSummary");
            var summarySize=summary.GetComponent<LayoutElement>()??summary.gameObject.AddComponent<LayoutElement>();summarySize.minWidth=0;summarySize.preferredWidth=360;summarySize.flexibleWidth=1;
            var header=summary.transform.parent.GetComponent<LayoutElement>();if(header!=null)header.minHeight=header.preferredHeight=80;
            summary.textWrappingMode=TextWrappingModes.Normal;
            window.Resize(new Vector2(640,240));
        }
        public static void ConfigureScene(Scene scene)
        {
            var roots=scene.GetRootGameObjects();
            var placement=roots.SelectMany(r=>r.GetComponentsInChildren<SpatialPlacementService>(true)).FirstOrDefault();
            if(placement==null){var go=new GameObject("SeyraSpatialPlacement");SceneManager.MoveGameObjectToScene(go,scene);placement=go.AddComponent<SpatialPlacementService>();}
            if(placement.GetComponent<SpatialWindowSnapping>()==null)placement.gameObject.AddComponent<SpatialWindowSnapping>();
            var room=roots.SelectMany(r=>r.GetComponentsInChildren<RoomContext>(true)).FirstOrDefault();Reference(placement,"room",room);
            var app=roots.FirstOrDefault(r=>r.name=="SeyraAssistantApp");if(app==null)return;
            var chat=app.GetComponentInChildren<SeyraSpatialChatWindow>(true);var menu=app.GetComponentInChildren<SeyraSpatialMenu>(true);if(chat==null||menu==null)return;
            var controller=app.GetComponent<SpatialFollowController>()??app.AddComponent<SpatialFollowController>();
            var rig=roots.SelectMany(r=>r.GetComponentsInChildren<OVRCameraRig>(true)).FirstOrDefault();
            Reference(controller,"head",rig?.centerEyeAnchor);Reference(controller,"chat",chat);Reference(controller,"chatWindow",chat.GetComponent<UIXRWindow>());
            Reference(controller,"menuWindow",menu.GetComponent<UIXRWindow>());Reference(controller,"placement",placement);
            Reference(menu,"spatialFollow",controller);Reference(menu,"roomContext",room);
            foreach(var window in new[]{chat.GetComponent<UIXRWindow>(),menu.GetComponent<UIXRWindow>()})
            {
                var grab=window.GrabFrame?.Grabbable;if(grab==null)continue;
                grab.InjectOptionalTargetTransform(window.transform);EditorUtility.SetDirty(grab);PrefabUtility.RecordPrefabInstancePropertyModifications(grab);
            }
            var legacy=menu.GetComponent<SeyraLazyFollow>();if(legacy!=null){legacy.enabled=true;EditorUtility.SetDirty(legacy);PrefabUtility.RecordPrefabInstancePropertyModifications(legacy);}
            var visibility=menu.GetComponent<SeyraMenuVisibility>();
            if(visibility!=null)
            {
                var serialized=new SerializedObject(visibility);serialized.FindProperty("maxChatDistance").floatValue=2.1f;
                serialized.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(visibility);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(menu);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        [MenuItem("Seyra/Spatial/Configure Chat Following")]
        public static void Configure()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before configuring spatial following.");
            var prefab=PrefabUtility.LoadPrefabContents(MenuPath);
            try{ConfigureMenu(prefab);PrefabUtility.SaveAsPrefabAsset(prefab,MenuPath);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var previous=SceneManager.GetActiveScene();
            foreach(var path in new[]{"Assets/Scenes/SeyraAssistant.unity","Assets/Scenes/OpencodeTestUI.unity"})
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try{ConfigureScene(scene);EditorSceneManager.SaveScene(scene);}finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            }
            if(previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
    }
}
