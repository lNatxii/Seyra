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

public static class WindowSnappingChecks
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var context=Object.FindFirstObjectByType<RoomContext>();var oldViewer=context.Viewer;
        var head=new GameObject("__SnappingViewer");head.transform.position=new Vector3(0,1.6f,0);Application.runInBackground=true;
        var root=new GameObject("__WindowSnappingChecks");
        var catalog=AssetDatabase.LoadAssetAtPath<Seyra.UI.Library.UIPrefabCatalog>("Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset");
        var service=new SceneToolService(root.transform,Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/window-snapping")),catalog);
        int checks=0;
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        JObject Call(string tool,JObject args=null)=>JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args??new JObject(),["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        JObject Ok(string tool,JObject args=null){var result=Call(tool,args);Check((bool)result["success"],result.ToString());return(JObject)result["data"];}
        Transform Owned(string id)=>root.GetComponentsInChildren<GeneratedObject>(true).First(r=>r.EntityId==id).transform;
        bool Same(Transform t,Pose pose)=>Vector3.Distance(t.position,pose.position)<.001f&&Quaternion.Angle(t.rotation,pose.rotation)<.1f;
        try
        {
            await context.LoadTestRoomAsync(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/Office/Office00.prefab"),head.transform);
            Check(context.Snapshot.State==RoomState.Ready,context.Diagnostic);
            var placement=SpatialPlacementService.ForScene(root.scene);var snapping=placement.GetComponent<SpatialWindowSnapping>();
            Check(snapping!=null,"The scene placement service has no window snapping manager.");
            int baseline=snapping.WindowCount;

            // 1. A panel released 10 cm from a wall and turned 20 degrees snaps to the pose given by Snap.
            var panel=Ok("create_panel",new JObject{["name"]="SnapPanel",["parentId"]="Generated",["position"]=V(0,1.5f,1.3f),["rotation"]=V(),["scale"]=V(1,1,1),["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Snap",["text"]="Window snapping",["width"]=.6,["height"]=.5,["fontSize"]=24}});
            string id=(string)panel["entityId"];var panelRoot=Owned(id);
            Check(snapping.IsManaged(panelRoot),"A managed panel must be covered by snapping.");
            var placed=Ok("spatial_place",new JObject{["entityId"]=id,["destination"]="wall"});
            Check((bool)placed["applied"],"The panel needs a valid wall placement first: "+placed);
            var wall=context.Snapshot.Surfaces.First(s=>s.PhysicalId==(string)placed["physicalId"]);
            var normal=wall.Pose.rotation*Vector3.forward;if(Vector3.Dot(normal,head.transform.position-wall.Pose.position)<0)normal=-normal;
            // Wall placement leaves a 3 cm gap; 7 cm more puts the panel 10 cm from the wall.
            panelRoot.SetPositionAndRotation(panelRoot.position+normal*.07f,Quaternion.Euler(0,20,0)*panelRoot.rotation);
            var request=new SpatialPlacementRequest{Content=SpatialPlacementService.Measure(panelRoot),Readable=true};
            var expected=SpatialPlacementPlanner.Snap(context.Snapshot,request,new Pose(panelRoot.position,panelRoot.rotation),placement.View(currentYaw:true),placement.Reservations(panelRoot));
            Check(expected!=null&&expected.Destination=="wall","Snap must find the nearby wall.");
            Check(snapping.SimulateRelease(panelRoot)=="snapped","Release near a wall must snap.");
            Check((string)Ok("scene_get",new JObject{["entityId"]=id})["spatial"]["surface"]=="wall","scene_get must report the wall.");
            Check(Same(panelRoot,expected.Pose),"Snapped pose differs from Snap.");

            // 2. A panel released inside furniture returns to its last valid pose.
            var last=placement.Placement(panelRoot).Pose;bool inside=false;
            foreach(var item in context.Snapshot.Objects)
            {
                var pose=new Pose(item.Box.Center-panelRoot.rotation*request.Content.center,panelRoot.rotation);
                var air=new SpatialCandidate{Destination="air",Pose=pose,Box=SpatialPlacementPlanner.BoxAt(request,pose)};
                if(SpatialPlacementPlanner.Snap(context.Snapshot,request,pose,placement.View(currentYaw:true),placement.Reservations(panelRoot))!=null||
                    SpatialPlacementPlanner.ValidateGeometry(context.Snapshot,air,placement.Reservations(panelRoot))==null)continue;
                panelRoot.SetPositionAndRotation(pose.position,pose.rotation);inside=true;break;
            }
            Check(inside,"Office00 must contain furniture away from snapping surfaces.");
            Check(snapping.SimulateRelease(panelRoot)=="restored","Release inside furniture must restore.");
            Check(Same(panelRoot,last),"Restored pose differs from the last valid pose.");

            // 3. A billboard layout card is not managed.
            var items=new JArray(Enumerable.Range(0,2).Select(i=>new JObject{["name"]="Card "+i,["panel"]=new JObject{["version"]=1,["kind"]="panel",["title"]="Card "+i,["text"]="Summary",["fontSize"]=24}}));
            var layout=Ok("layout_create",new JObject{["name"]="SnapCards",["layout"]="arc",["behavior"]="billboard",["itemSize"]=new JObject{["width"]=.5,["height"]=.3},["items"]=items});
            await Task.Delay(100);
            var card=Owned((string)layout["rootId"]).GetChild(0);var cardPose=new Pose(card.position,card.rotation);
            Check(!snapping.IsManaged(card),"Billboard cards must not be managed.");
            Check(snapping.SimulateRelease(card)=="unmanaged"&&Same(card,cardPose)&&placement.Placement(card)==null,"Releasing a billboard card changed its pose or registration.");

            // 4. The chat is not managed.
            var chat=Object.FindFirstObjectByType<Seyra.UI.SeyraSpatialChatWindow>(FindObjectsInactive.Include);
            Check(chat!=null,"The scene has no chat window.");
            var chatPose=new Pose(chat.transform.position,chat.transform.rotation);
            Check(!snapping.IsManaged(chat.transform)&&snapping.SimulateRelease(chat.transform)=="unmanaged"&&Same(chat.transform,chatPose)&&placement.Placement(chat.transform)==null,"The chat must not be managed by snapping.");

            // 5. No active preview line and no residual content.
            Object.DestroyImmediate(root);
            Check(snapping.WindowCount==baseline,"Destroyed windows are still tracked.");
            Check(!snapping.Preview.gameObject.activeSelf&&!placement.GetComponentsInChildren<LineRenderer>().Any(),"A preview LineRenderer remains active.");
            Check(GameObject.Find("__WindowSnappingChecks")==null,"Residual test content.");
            return "Window snapping basic checks passed: "+checks;
        }
        finally{context.Configure(Object.FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>(),oldViewer);if(root!=null)Object.DestroyImmediate(root);Object.DestroyImmediate(head);}
    }
}
