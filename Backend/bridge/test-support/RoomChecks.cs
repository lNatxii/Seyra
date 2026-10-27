using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.Spatial;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class RoomChecks
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        Application.runInBackground=true;
        var context=Object.FindFirstObjectByType<RoomContext>();var provider=Object.FindFirstObjectByType<MRUK>();
        if(context==null||provider==null)throw new Exception("Configure room support first.");
        var root=new GameObject("__RoomChecks");var head=new GameObject("__RoomTestViewer");head.transform.position=new Vector3(0,1.5f,0);
        int checks=0;var evidence=new JArray();
        var catalog=AssetDatabase.LoadAssetAtPath<Seyra.UI.Library.UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service=new SceneToolService(root.transform,Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/room-checks")),catalog);
        void Require(bool value,string message){checks++;if(!value)throw new Exception(message);}
        JObject Call(string tool,JObject args=null)=>JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args??new JObject(),["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        JObject Ok(string tool,JObject args=null){var response=Call(tool,args);Require((bool)response["success"],response.ToString());return(JObject)response["data"];}
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        Vector3 Read(JToken value)=>new Vector3((float)value["x"],(float)value["y"],(float)value["z"]);
        try
        {
            Require(context.Snapshot.State==RoomState.AirOnly,"Editor must not load a simulated room automatically.");
            context.SendMessage("OnApplicationPause",true);context.SendMessage("OnApplicationPause",false);
            Require(context.Snapshot.State==RoomState.AirOnly,"Pause/resume without captured geometry must preserve the no-map state.");
            Require((string)Ok("room_get")["state"]=="AirOnly","No-map state must be honest.");
            Require((string)Call("scene_bounds",new JObject{["entityId"]="physical:unknown"})["error"]["code"]=="ROOM_UNAVAILABLE","No-map bounds must fail.");
            string previousId=null;long previousRevision=context.Snapshot.Revision;
            foreach(var path in new[]{"Bedroom/Bedroom00","Office/Office00","Other/OtherRoom07_Cluttered"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/"+path+".prefab");
                Require(prefab!=null,"Missing room prefab: "+path);
                var loadTimer=System.Diagnostics.Stopwatch.StartNew();
                await context.LoadTestRoomAsync(prefab,head.transform);
                var room=provider.Rooms[0];var floor=room.FloorAnchors[0];
                var rect=floor.PlaneRect.Value;head.transform.position=floor.transform.TransformPoint(new Vector3(rect.center.x,rect.center.y,0))+Vector3.up*1.5f;
                for(int attempt=0;attempt<40&&context.Snapshot.State!=RoomState.Ready;attempt++)await Task.Delay(50);
                Require(context.Snapshot.State==RoomState.Ready,context.Diagnostic);
                loadTimer.Stop();
                var snapshot=context.Snapshot;
                await Task.Delay(600);Require(context.Snapshot.Revision==snapshot.Revision,"Unchanged geometry must reuse the snapshot between polls.");
                Require(snapshot.Simulated&&snapshot.Revision>previousRevision,"Simulation and revision must be explicit.");previousRevision=snapshot.Revision;
                Require(snapshot.Surfaces.Any(s=>s.Kind=="wall")&&snapshot.Surfaces.Any(s=>s.Kind=="floor"),"Room surfaces missing.");
                Require(!snapshot.Objects.Any(o=>o.Labels.Contains("GLOBAL_MESH")||o.Labels.Contains("INVISIBLE_WALL_FACE")),"Hidden geometry exposed.");
                foreach(var top in snapshot.Surfaces.Where(s=>s.Kind=="tabletop"))
                {
                    Require(Vector3.Angle(top.Pose.rotation*Vector3.forward,Vector3.up)<=20.001f,"Tabletop normal must point up.");
                    Require(!top.Labels.Contains("BED")&&!top.Labels.Contains("COUCH"),"Soft furniture is not a support.");
                    Require(top.Polygon.Count==4&&top.SupportId==top.PhysicalId,"Tabletop corners/support missing.");
                }
                var all=Ok("room_objects",new JObject{["limit"]=1});int total=(int)all["total"],count=all["objects"].Count();var next=all["nextOffset"];
                while(next.Type!=JTokenType.Null){var page=Ok("room_objects",new JObject{["offset"]=(int)next,["limit"]=1});count+=page["objects"].Count();next=page["nextOffset"];}
                Require(count==total&&total>5,"Inventory pagination lost references.");
                Require(Ok("room_objects",new JObject{["kind"]="wall"})["objects"].All(i=>(string)i["kind"]=="wall"),"Kind filtering failed.");
                foreach(var item in snapshot.Objects)
                {
                    var bounds=Ok("scene_bounds",new JObject{["entityId"]=item.PhysicalId})["bounds"];
                    var anchor=room.Anchors.Single(a=>"physical:"+a.Anchor.Uuid==item.PhysicalId);var local=anchor.VolumeBounds.Value;
                    var points=SpatialGeometry.Corners(local,anchor.transform.localToWorldMatrix);
                    var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);
                    Require((Read(bounds["min"])-min).magnitude<.0001f&&(Read(bounds["max"])-max).magnitude<.0001f,"Physical box lost transformed corners.");
                    foreach(var tool in new[]{"set_transform","rename_object","delete_object","restore_object","set_parent","set_appearance","update_panel"})
                        Require((string)Call(tool,new JObject{["entityId"]=item.PhysicalId})["error"]["code"]=="ROOM_READ_ONLY","Physical mutation accepted: "+tool);
                }
                var physical=snapshot.Objects.FirstOrDefault()?.PhysicalId??snapshot.Surfaces[0].PhysicalId;
                var nodes=Ok("scene_find",new JObject{["componentType"]="MRUKAnchor"})["nodes"];
                Require(nodes.Any(n=>(bool)n["physical"]&&(bool)n["readOnly"]),"Hierarchy physical annotation missing.");
                var node=nodes.First();Require((string)Call("set_transform",new JObject{["entityId"]=node["entityId"],["space"]="world",["position"]=V()})["error"]["code"]=="ROOM_READ_ONLY","Generic anchor ID bypassed protection.");
                var created=Ok("create_primitive",new JObject{["name"]="RoomPlacement",["primitiveType"]="cube",["parentId"]="Generated",["position"]=V(),["rotation"]=V(),["scale"]=V(.1f,.1f,.1f)});var id=(string)created["entityId"];
                Require((string)Call("set_parent",new JObject{["entityId"]=id,["parentId"]=physical,["worldPositionStays"]=true})["error"]["code"]=="ROOM_READ_ONLY","Physical parent accepted.");
                var table=snapshot.Surfaces.FirstOrDefault(s=>s.Kind=="tabletop");
                if(table!=null)
                {
                    var result=Ok("place_relative",new JObject{["entityId"]=id,["referenceId"]=table.PhysicalId,["direction"]="above",["gap"]=0});
                    Require(Math.Abs((float)result["bounds"]["min"]["y"]-(float)result["referenceBounds"]["max"]["y"])<.0001f,"Table placement gap failed.");
                    Ok("undo",new JObject{["scope"]="operation"});
                    Require(JToken.DeepEquals(created["transform"],Ok("scene_get",new JObject{["entityId"]=id})["transform"]),"Physical placement undo failed.");
                    Require((string)Call("scene_bounds",new JObject{["entityId"]=table.PhysicalId,["frame"]="reference"})["error"]["code"]=="FRAME_UNDEFINED","Unknown table front must fail.");
                }
                head.transform.rotation=Quaternion.Euler(0,63,0);
                var viewerResult=Ok("place_relative",new JObject{["entityId"]=id,["referenceId"]=physical,["direction"]="right",["gap"]=.1f,["frame"]="viewer"});
                Require((string)viewerResult["bounds"]["kind"]=="frame_aligned_box","Viewer frame missing.");
                Require(Math.Abs((float)viewerResult["bounds"]["min"]["x"]-(float)viewerResult["referenceBounds"]["max"]["x"]-.1f)<.0001f,"Viewer frame gap failed.");
                Ok("undo",new JObject{["scope"]="operation"});
                var wall=snapshot.Surfaces.First(s=>s.Kind=="wall");
                var placed=Ok("place_relative",new JObject{["entityId"]=id,["referenceId"]=wall.PhysicalId,["direction"]="front",["gap"]=.03f,["frame"]="reference",["orient"]="match_reference"});
                Require(Math.Abs((float)placed["bounds"]["min"]["z"]-(float)placed["referenceBounds"]["max"]["z"]-.03f)<.0001f,"Wall frame gap failed.");
                Ok("undo",new JObject{["scope"]="operation"});
                Require(JToken.DeepEquals(created["transform"],Ok("scene_get",new JObject{["entityId"]=id})["transform"]),"Orientation undo failed.");
                var panel=Ok("create_panel",new JObject{["name"]="OrientationPanel",["parentId"]="Generated",["position"]=V(0,1,2),["rotation"]=V(),["scale"]=V(1,1,1),
                    ["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Orientation",["text"]="Readable front",["width"]=.8,["height"]=.6,["fontSize"]=24}});
                var panelId=(string)panel["entityId"];var panelView=root.GetComponentsInChildren<ManagedPanel>().Single();
                var panelMounted=Ok("place_relative",new JObject{["entityId"]=panelId,["referenceId"]=wall.PhysicalId,["direction"]="front",["gap"]=.03,["frame"]="reference",["orient"]="match_reference"});
                var normal=Quaternion.Euler(Read(panelMounted["bounds"]["frameRotation"]))*Vector3.forward;
                Require(Vector3.Dot(-panelView.Surface.forward,normal)>.999f,"UIXRWindow readable -Z side must face into the room.");
                Ok("undo",new JObject{["scope"]="operation"});
                Require(JToken.DeepEquals(panel["transform"],Ok("scene_get",new JObject{["entityId"]=panelId})["transform"]),"Panel orientation undo failed.");
                var facingViewer=Ok("place_relative",new JObject{["entityId"]=panelId,["referenceId"]=wall.PhysicalId,["direction"]="front",["gap"]=.03,["orient"]="face_viewer"});
                var expectedForward=SpatialGeometry.Yaw(new Vector3(0,1,2)-head.transform.position)*Vector3.forward;
                Require(Vector3.Dot(panelView.transform.forward,expectedForward)>.999f,"Panel face_viewer convention changed.");
                Ok("undo",new JObject{["scope"]="operation"});
                var oldPose=panelView.transform.localRotation;var oldPosition=panelView.transform.localPosition;
                root.transform.localScale=new Vector3(1,2,1);
                Require(!(bool)Call("place_relative",new JObject{["entityId"]=panelId,["referenceId"]=wall.PhysicalId,["direction"]="front",["gap"]=0,["orient"]="match_reference"})["success"],"Orientation under nonuniform ancestors must fail.");
                Require(panelView.transform.localRotation==oldPose&&panelView.transform.localPosition==oldPosition,"Rejected placement changed panel pose.");
                root.transform.localScale=Vector3.one;
                Ok("delete_object",new JObject{["entityId"]=panelId});
                var oldSnapshot=context.Snapshot;room.AnchorUpdatedEvent.Invoke(room.Anchors[0]);
                Require(context.Snapshot.Revision>oldSnapshot.Revision&&context.Snapshot.Surfaces.Count==0,"An anchor event must invalidate geometry immediately.");
                for(int attempt=0;attempt<40&&context.Snapshot.State!=RoomState.Ready;attempt++)await Task.Delay(50);
                Require(context.Snapshot.State==RoomState.Ready&&context.Snapshot.Revision>oldSnapshot.Revision,"Invalidated room did not rebuild.");
                Require(oldSnapshot.Surfaces.Count>0,"Published snapshots must remain immutable.");
                if(previousId!=null)Require((string)Call("scene_bounds",new JObject{["entityId"]=previousId})["error"]["code"]=="ROOM_REFERENCE_STALE","Old room reference accepted.");
                previousId=physical;
                evidence.Add(new JObject{["prefab"]=path,["surfaces"]=snapshot.Surfaces.Count,["volumes"]=snapshot.Objects.Count,["tabletops"]=snapshot.Surfaces.Count(s=>s.Kind=="tabletop"),["revision"]=snapshot.Revision,["loadMilliseconds"]=loadTimer.ElapsedMilliseconds,["inventoryBytes"]=System.Text.Encoding.UTF8.GetByteCount(Ok("room_objects",new JObject{["limit"]=64}).ToString(Newtonsoft.Json.Formatting.None)),
                    ["wallNormal"]=V((wall.Pose.rotation*Vector3.forward).x,(wall.Pose.rotation*Vector3.forward).y,(wall.Pose.rotation*Vector3.forward).z)});
                Ok("delete_object",new JObject{["entityId"]=id});
            }
            return new JObject{["checks"]=checks,["rooms"]=evidence,["simulated"]=true}.ToString();
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(head);}
    }
}
