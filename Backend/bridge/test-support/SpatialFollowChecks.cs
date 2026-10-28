using System;
using System.Threading.Tasks;
using Seyra.Spatial;
using Seyra.UI;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class SpatialFollowChecks
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var scene=SceneManager.CreateScene("__SpatialFollowChecks");
        var app=new GameObject("__FollowApp");SceneManager.MoveGameObjectToScene(app,scene);app.SetActive(false);
        var head=new GameObject("__FollowHead");SceneManager.MoveGameObjectToScene(head,scene);head.transform.position=new Vector3(0,1.6f,0);
        int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        try
        {
            var chatGo=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seyra/UI/Panels/SeyraSpatialChatWindow.prefab"),app.transform);
            var menuGo=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seyra/UI/Panels/SeyraSpatialMenu.prefab"),app.transform);
            var chat=chatGo.GetComponent<SeyraSpatialChatWindow>();chat.enabled=false;
            chatGo.transform.localPosition=new Vector3(0,1.45f,3);menuGo.transform.localPosition=new Vector3(0,1,3);
            var placement=SpatialPlacementService.ForScene(scene);var controller=app.AddComponent<SpatialFollowController>();
            controller.Configure(head.transform,chat,chatGo.GetComponent<UIXRWindow>(),menuGo.GetComponent<UIXRWindow>(),placement);controller.UseTestTracking(true);
            app.SetActive(true);controller.enabled=false;controller.enabled=true;
            var menu=menuGo.GetComponent<SeyraSpatialMenu>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(SeyraSpatialMenu).GetField("roomActionStatus",flags).SetValue(menu,"Escaneando habitación…");menu.RefreshUI();
            var summary=(TMPro.TMP_Text)typeof(SeyraSpatialMenu).GetField("statusSummary",flags).GetValue(menu);
            Check(summary.text.Contains("Escaneando habitación…"),"Scan status must take precedence over follow status.");
            typeof(SeyraSpatialMenu).GetField("roomActionStatus",flags).SetValue(menu,"");
            Check(!menuGo.GetComponent<SeyraLazyFollow>().enabled,"Legacy menu motion must be disabled.");
            controller.Tick(.1f);Check(controller.State==SpatialFollowController.FollowState.Waiting,"Out-of-range chat should wait.");
            var before=chatGo.transform.position;controller.Tick(.3f);Check(chatGo.transform.position==before,"Brief motion must not move the chat.");
            for(int i=0;i<20&&controller.State!=SpatialFollowController.FollowState.Moving;i++)controller.Tick(.1f);
            Check(controller.State==SpatialFollowController.FollowState.Moving,"Stable head should produce a captured air destination.");
            before=chatGo.transform.position;var offset=menuGo.transform.position-chatGo.transform.position;controller.Tick(.1f);
            Check(Vector3.Distance(before,chatGo.transform.position)<=.0351f,"Chat exceeded its speed limit.");
            Check(Vector3.Distance(offset,menuGo.transform.position-chatGo.transform.position)<.0001f,"The group lost its relative pose.");
            chat.BeginDictationPreview();before=app.transform.position;controller.Tick(.1f);Check(app.transform.position==before,"Dictation must block movement.");chat.CancelDictationPreview();
            controller.SendMessage("GrabChanged",true);controller.SendMessage("GrabChanged",false);controller.Tick(.1f);
            Check(controller.Mode==SpatialFollowController.FollowMode.Pinned,"A manual release must pin the group.");
            controller.ResumeFollow();controller.Tick(.1f);Check(controller.Mode==SpatialFollowController.FollowMode.Adaptive,"Follow action must restore adaptive mode.");
            Check(controller.Recenter(),"Explicit recenter should use the air planner without a map.");
            Check(controller.Status.Contains("sin validación física"),"No-map recenter must declare its limit.");
            return "Spatial follow basic checks passed: "+checks;
        }
        finally
        {
            Object.DestroyImmediate(app);Object.DestroyImmediate(head);
            var unload=SceneManager.UnloadSceneAsync(scene);if(unload!=null)while(!unload.isDone)await Task.Yield();
        }
    }
}
