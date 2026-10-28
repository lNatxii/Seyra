using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.Spatial;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class SpatialPlacementChecks
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var context=Object.FindFirstObjectByType<RoomContext>();var oldViewer=context.Viewer;
        var head=new GameObject("__PlacementViewer");head.transform.position=new Vector3(0,1.6f,0);Application.runInBackground=true;
        var root=new GameObject("__SpatialPlacementChecks");
        var catalog=AssetDatabase.LoadAssetAtPath<Seyra.UI.Library.UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service=new SceneToolService(root.transform,Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/spatial-placement")),catalog);
        int checks=0;
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        JObject Call(string tool,JObject args=null)=>JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args??new JObject(),["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        JObject Ok(string tool,JObject args=null){var result=Call(tool,args);Check((bool)result["success"],result.ToString());return(JObject)result["data"];}
        try
        {
            await context.LoadTestRoomAsync(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Office/Office00.prefab"),head.transform);
            Check(context.Snapshot.State==RoomState.Ready,context.Diagnostic);
            var spatialService=SpatialPlacementService.ForScene(root.scene);var initialYaw=spatialService.View("review-task").rotation;
            head.transform.rotation=Quaternion.Euler(0,60,0);
            Check(Quaternion.Angle(spatialService.View("review-task").rotation,initialYaw)<.001f,"An open task must retain its work yaw.");
            Check(Quaternion.Angle(spatialService.View().rotation,head.transform.rotation)<.001f,"Unscoped calls must use current yaw.");
            head.transform.rotation=Quaternion.identity;bool checkedPersistentGeometry=false;
            var cube=Ok("create_primitive",new JObject{["name"]="PlacedCube",["parentId"]="Generated",["primitiveType"]="cube",["position"]=V(0,1,1),["rotation"]=V(),["scale"]=V(.12f,.12f,.12f)});string id=(string)cube["entityId"];
            foreach(var destination in new[]{"tabletop","wall","air"})
            {
                var args=new JObject{["entityId"]=id,["destination"]=destination};
                var candidate=Ok("spatial_candidates",args);Check(!(bool)candidate["applied"],"Candidate query moved content.");
                var placed=Ok("spatial_place",args);Check(placed["code"]!=null,"Placement must provide an honest result.");
                if((bool)placed["applied"]&&!checkedPersistentGeometry)
                {
                    head.transform.SetPositionAndRotation(new Vector3(10,1.6f,10),Quaternion.Euler(0,120,0));
                    Check(!(bool)Ok("scene_get",new JObject{["entityId"]=id})["spatial"]["needsPlacement"],"Turning or walking away must not invalidate a geometric placement.");
                    head.transform.SetPositionAndRotation(new Vector3(0,1.6f,0),Quaternion.identity);checkedPersistentGeometry=true;
                }
                if((bool)placed["applied"])
                {Check(placed["operationId"]!=null,"Applied placement has no history.");Ok("undo",new JObject{["scope"]="operation"});Check(JToken.DeepEquals(cube["transform"],Ok("scene_get",new JObject{["entityId"]=id})["transform"]),"Undo lost the original pose.");}
                else Check(placed["operationId"]==null,"Rejected placement must not add history.");
            }
            Check((string)Call("spatial_place",new JObject{["entityId"]=id,["destination"]="air",["expectedRoomRevision"]=context.Snapshot.Revision+1})["error"]["code"]=="ROOM_REVISION_CONFLICT","Stale revision accepted.");
            var panel=Ok("create_panel",new JObject{["name"]="PlacementPanel",["parentId"]="Generated",["position"]=V(0,1,2),["rotation"]=V(),["scale"]=V(1,1,1),["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Spatial",["text"]="Basic placement",["width"] =.6,["height"] =.5,["fontSize"]=24}});
            Check(!(bool)Call("spatial_place",new JObject{["entityId"]=panel["entityId"],["destination"]="air",["minScale"]=.5})["success"],"Reading panels must not shrink.");
            Ok("spatial_place",new JObject{["entityId"]=panel["entityId"],["destination"]="wall"});
            var group=Ok("create_gameobject",new JObject{["name"]="PrimitiveGroup",["parentId"]="Generated",["position"]=V(),["rotation"]=V(0,37,0),["scale"]=V(1,1,1)});
            foreach(float offset in new[]{-.2f,.2f})Ok("create_primitive",new JObject{["name"]="GroupPart",["parentId"]=group["entityId"],["primitiveType"]="cube",["position"]=V(offset,offset,0),["rotation"]=V(),["scale"]=V(.1f,.1f,.1f)});
            var groupPlacement=Ok("spatial_place",new JObject{["entityId"]=group["entityId"],["destination"]="wall"});
            Check((bool)groupPlacement["applied"],"A managed group must support conservative envelope placement.");
            Ok("undo",new JObject{["scope"]="operation"});
            var corpus=JArray.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backend/bridge/test/presentation-corpus.json"))));
            var example=corpus.First(e=>(bool)e["valid"]);
            var presentation=Ok("ui_present",new JObject{["name"]="PlacementPresentation",["parentId"]="Generated",["position"]=V(0,1,2),["rotation"]=V(),["scale"]=V(1,1,1),["width"] =.6,["height"] =.5,["presentation"]=example["content"].DeepClone()});
            var presentationPlacement=Ok("spatial_place",new JObject{["entityId"]=presentation["entityId"],["destination"]="air"});
            Check(presentationPlacement["code"]!=null,"Presentation placement must provide a result.");
            Ok("ui_close",new JObject{["entityId"]=presentation["entityId"]});
            Ok("delete_object",new JObject{["entityId"]=panel["entityId"]});Ok("restore_object",new JObject{["entityId"]=panel["entityId"]});
            Ok("content_save",new JObject{["slot"]="spatial_basic"});Ok("content_load",new JObject{["slot"]="spatial_basic"});
            Check(Ok("scene_get",new JObject{["entityId"]=id})["spatial"]==null,"Load must not persist physical placement metadata.");
            return "Spatial placement basic checks passed: "+checks;
        }
        finally{context.Configure(Object.FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>(),oldViewer);Object.DestroyImmediate(root);Object.DestroyImmediate(head);}
    }
}
