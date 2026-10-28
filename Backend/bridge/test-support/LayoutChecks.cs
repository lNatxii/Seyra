using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.Spatial;
using Seyra.UI.Library;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LayoutChecks
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var scene=UnityEngine.SceneManagement.SceneManager.CreateScene("__LayoutChecks");
        var root=new GameObject("__LayoutChecks");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var storage=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/layouts-"+Guid.NewGuid().ToString("N")));
        var service=new SceneToolService(root.transform,storage,AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset"));
        int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        JObject Call(string tool,JObject args)=>JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args,["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        JObject Ok(string tool,JObject args){var response=Call(tool,args);Check((bool)response["success"],response.ToString());return(JObject)response["data"];}
        GameObject Owned(string id)=>root.GetComponentsInChildren<GeneratedObject>().First(r=>r.EntityId==id).gameObject;
        var context=Object.FindFirstObjectByType<RoomContext>();var oldViewer=context.Viewer;GameObject testHead=null;
        try
        {
            var items=new JArray(Enumerable.Range(0,6).Select(i=>new JObject{["name"]="Book "+i,["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Book "+i,["text"]="Summary",["fontSize"]=24}}));
            var args=new JObject{["name"]="Books",["layout"]="arc",["behavior"]="billboard",["itemSize"]=new JObject{["width"]=.5,["height"]=.3},["items"]=items};
            var created=Ok("layout_create",args);string id=(string)created["rootId"];
            Check(((JArray)created["items"]).Count==6,"Expected six cards.");
            await Task.Delay(100);
            var group=Owned(id);var card=group.transform.GetChild(0);
            card.rotation=Quaternion.Euler(0,60,0);
            Check(DrivenTransform.Channels(card)==DrivenChannels.Rotation,"Billboard rotation is not registered.");
            Check((string)Call("set_transform",new JObject{["entityId"]=card.GetComponent<GeneratedObject>().EntityId,["space"]="world",["rotation"]=new JObject{["x"]=0,["y"]=90,["z"]=0}})["error"]?["code"]=="MOTION_DRIVEN","Billboard rotation must reject set_transform with MOTION_DRIVEN.");
            var applied=Ok("layout_apply",new JObject{["rootId"]=id,["layout"]="plane"});
            Check((string)applied["layout"]=="plane","Expected plane.");
            Ok("undo",new JObject{["scope"]="operation"});
            Check((string)group.GetComponent<SeyraLayoutGroup>().Specification["layout"]=="arc","Undo did not restore the layout specification.");
            await Task.Delay(50);
            var saved=Ok("content_save",new JObject{["slot"]="books"});Check((int)saved["version"]==5,"Expected persistence version 5.");
            Ok("content_load",new JObject{["slot"]="books"});
            var loaded=Owned(id);Check((string)loaded.GetComponent<SeyraLayoutGroup>().Specification["behavior"]=="billboard","Behavior was not reconstructed.");
            Check(loaded.transform.GetChild(0).GetComponent<SeyraBillboard>().enabled,"Billboard missing after load.");
            Ok("undo",new JObject{["scope"]="operation"});
            Ok("undo",new JObject{["scope"]="request"});
            Check(!root.GetComponentsInChildren<SeyraLayoutGroup>().Any(),"Request undo retained a live layout.");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            testHead=new GameObject("__LayoutRoomViewer");testHead.transform.position=new Vector3(0,1.6f,0);
            await context.LoadTestRoomAsync(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Bedroom/Bedroom00.prefab"),testHead.transform);
            Check(context.Snapshot.State==RoomState.Ready,context.Diagnostic);
            var room=context.Snapshot;var floor=room.Surfaces.First(s=>s.Kind=="floor");
            testHead.transform.position=floor.Corners.Aggregate(Vector3.zero,(sum,p)=>sum+p)/floor.Corners.Length+Vector3.up*1.6f;
            var wallSpec=new JObject{["layout"]="wall",["count"]=8,["itemSize"]=new JObject{["width"]=.24,["height"]=.65}};
            // Face the closest wall's center from the floor center for a reproducible working view.
            var wall=room.Surfaces.Where(s=>s.Kind=="wall").OrderBy(s=>Vector3.Distance(s.Pose.position,testHead.transform.position)).First();
            testHead.transform.rotation=SpatialGeometry.Yaw(wall.Pose.position-testHead.transform.position);
            var wallItems=new JArray(Enumerable.Range(0,8).Select(i=>new JObject{["name"]="Wall book "+i,["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Book",["text"]="Summary",["fontSize"]=24}}));
            var wallCreated=Ok("layout_create",new JObject{["name"]="Wall books",["layout"]="wall",["itemSize"]=wallSpec["itemSize"].DeepClone(),["items"]=wallItems});
            foreach(var entry in wallCreated["items"])
            {
                string cardId=(string)entry["entityId"];var box=SpatialPlacementService.CurrentBox(Owned(cardId).transform);
                Check(!room.Surfaces.Where(s=>s.Kind=="opening").Any(s=>SpatialCollision.Intersects(box,s))&&!room.Objects.Any(o=>SpatialCollision.Intersects(box,o.Box)),"Wall card intersects opening or furniture.");
                Check((string)Ok("scene_get",new JObject{["entityId"]=cardId})["spatial"]["surface"]=="wall","Wall reservation missing.");
            }
            Ok("undo",new JObject{["scope"]="request"});
            Check(!root.GetComponentsInChildren<SeyraLayoutGroup>().Any(),"Wall request undo retained layout.");
            return "Layout basic checks passed: "+checks;
        }
        finally{if(testHead!=null){context.Configure(Object.FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>(),oldViewer);Object.DestroyImmediate(testHead);}Object.DestroyImmediate(root);if(Directory.Exists(storage))Directory.Delete(storage,true);var unload=UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);while(!unload.isDone)await Task.Delay(10);}
    }
}
