using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using UnityEngine;
using Object=UnityEngine.Object;

public static class BoundsChecks
{
    public static string Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var root=new GameObject("__BoundsChecks");
        var storage=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/bounds-"+Guid.NewGuid().ToString("N")));
        var service=new SceneToolService(root.transform,storage);
        int checks=0,placements=0;
        void Require(bool value,string message){checks++;if(!value)throw new Exception(message);}
        JObject Call(string tool,JObject args=null,string requestId=null)=>JObject.Parse(service.Dispatch(new JObject{
            ["version"]=1,["type"]="tool_request",["requestId"]=requestId??Guid.NewGuid().ToString(),["tool"]=tool,
            ["arguments"]=args??new JObject(),["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        JObject Ok(string tool,JObject args=null,string requestId=null){var result=Call(tool,args,requestId);Require((bool)result["success"],result.ToString());return (JObject)result["data"];}
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        Vector3 Read(JToken token)=>new Vector3((float)token["x"],(float)token["y"],(float)token["z"]);
        string Create(string name,string shape=null,string parent="Generated")=>(string)Ok(shape==null?"create_gameobject":"create_primitive",new JObject{
            ["name"]=name,["parentId"]=parent,["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1),
            // Empty creation has a strict schema too.
        }.MergeShape(shape))["entityId"];
        JObject Bounds(string id)=>(JObject)Ok("scene_bounds",new JObject{["entityId"]=id})["bounds"];
        void Near(Vector3 actual,Vector3 expected,string label)=>Require((actual-expected).magnitude<0.00015f,label+": "+actual+" != "+expected);
        JObject PlaceArgs(string id,string reference,string direction,float gap=.125f)=>new JObject{["entityId"]=id,["referenceId"]=reference,["direction"]=direction,["gap"]=gap};
        void Place(string id,string reference,string direction)
        {
            var before=Ok("scene_get",new JObject{["entityId"]=id});
            var requestId=Guid.NewGuid().ToString();var args=PlaceArgs(id,reference,direction);
            var result=Ok("place_relative",args,requestId);placements++;
            Require(JToken.DeepEquals(result,Ok("place_relative",args,requestId)),"Duplicate placement executed twice.");
            var a=result["bounds"];var b=result["referenceBounds"];
            int axis=direction=="above"||direction=="below"?1:direction=="left"||direction=="right"?0:2;
            bool positive=direction=="above"||direction=="right"||direction=="front";
            float distance=positive?Read(a["min"])[axis]-Read(b["max"])[axis]:Read(b["min"])[axis]-Read(a["max"])[axis];
            Require(Math.Abs(distance-.125f)<.0001,"Wrong face gap: "+direction);
            for(int j=0;j<3;j++)if(j!=axis)Require(Math.Abs(Read(a["center"])[j]-Read(b["center"])[j])<.0001,"Wrong center alignment.");
            Require(JToken.DeepEquals(before["transform"]["localScale"],result["transform"]["localScale"]),"Scale changed.");
            Require(JToken.DeepEquals(before["transform"]["localRotation"],result["transform"]["localRotation"]),"Rotation changed.");
            Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=result["operationId"]});
            Require(JToken.DeepEquals(before["transform"],Ok("scene_get",new JObject{["entityId"]=id})["transform"]),"Undo lost pose.");
        }
        try
        {
            var reference=Create("Reference","cube");
            var parent=Create("ScaledParent");
            Ok("set_transform",new JObject{["entityId"]=parent,["space"]="local",["position"]=V(3,2,-4),["rotation"]=V(15,25,35),["scale"]=V(-12,.02f,4)});
            var shapes=new[]{"cube","sphere","capsule","cylinder","plane","quad"};
            var directions=new[]{"above","below","right","left","front","behind"};
            foreach(var shape in shapes)
            {
                var id=Create(shape,shape,parent);var go=root.transform.Find("ScaledParent/"+shape).gameObject;
                Ok("set_transform",new JObject{["entityId"]=id,["space"]="local",["rotation"]=V(23,47,19)});
                var bounds=Bounds(id);var mesh=go.GetComponent<MeshFilter>().sharedMesh;
                // Independent oracle: every transformed mesh vertex must be contained.
                var vertices=mesh.vertices.Select(v=>go.transform.TransformPoint(v)).ToArray();
                var min=Read(bounds["min"]);var max=Read(bounds["max"]);
                foreach(var v in vertices)for(int axis=0;axis<3;axis++)Require(v[axis]>=min[axis]-.0001f && v[axis]<=max[axis]+.0001f,"Mesh escaped world bounds: "+shape);
                foreach(var direction in directions){Place(id,reference,direction);Place(reference,id,direction);}
            }
            // Identity-scaled local cube under a very flat, wide parent.
            var flat=Create("FlatParent");Ok("set_transform",new JObject{["entityId"]=flat,["space"]="local",["scale"]=V(100,.001f,25)});
            var flatCube=Create("FlatCube","cube",flat);Near(Read(Bounds(flatCube)["size"]),new Vector3(100,.001f,25),"World dimensions ignored parent scale");
            // The container pivot is far from its geometry; root AND nested meshes contribute.
            var group=Create("OffsetContainer","cube");var child=Create("OffsetChild","sphere",group);
            Ok("set_transform",new JObject{["entityId"]=child,["space"]="local",["position"]=V(6,3,-2)});
            Near(Read(Bounds(group)["min"]),new Vector3(-.5f,-.5f,-2.5f),"Aggregate minimum");
            Near(Read(Bounds(group)["max"]),new Vector3(6.5f,3.5f,.5f),"Aggregate maximum");
            foreach(var direction in directions){Place(group,reference,direction);Place(reference,group,direction);}
            var request=Ok("request_begin",new JObject{["name"]="PlacementRequest"});
            var requestBefore=Bounds(group);
            Ok("place_relative",PlaceArgs(group,reference,"above"));
            Ok("place_relative",PlaceArgs(group,reference,"right"));
            Ok("request_end",new JObject{["requestGroupId"]=request["entityId"]});
            Ok("undo",new JObject{["scope"]="request"});
            Near(Read(Bounds(group)["center"]),Read(requestBefore["center"]),"Request undo lost placement");
            var empty=Create("Empty");Require(!(bool)Call("scene_bounds",new JObject{["entityId"]=empty})["success"],"Empty bounds accepted.");
            foreach(var args in new[]{PlaceArgs(group,child,"above"),PlaceArgs(child,group,"above"),PlaceArgs(group,group,"above"),PlaceArgs("Generated",reference,"above"),PlaceArgs(empty,reference,"above"),PlaceArgs(reference,group,"above",-1)})
                Require(!(bool)Call("place_relative",args)["success"],"Invalid placement accepted.");
            // Query and placement preserve inactive structural geometry; trash is excluded.
            root.transform.Find("OffsetContainer/OffsetChild").gameObject.SetActive(false);
            Near(Read(Bounds(group)["max"]),new Vector3(6.5f,3.5f,.5f),"Inactive child omitted");
            Ok("delete_object",new JObject{["entityId"]=child});Near(Read(Bounds(group)["size"]),Vector3.one,"Deleted child included");
            Ok("restore_object",new JObject{["entityId"]=child});
            var beforeFailure=Ok("scene_get",new JObject{["entityId"]=group})["transform"].DeepClone();
            var historyBefore=Ok("history_list")["total"].DeepClone();
            var tooFar=PlaceArgs(group,reference,"right",float.MaxValue);
            Require(!(bool)Call("place_relative",tooFar)["success"],"Unrepresentable placement accepted.");
            Require(JToken.DeepEquals(beforeFailure,Ok("scene_get",new JObject{["entityId"]=group})["transform"]),"Precision failure changed target pose.");
            Require(JToken.DeepEquals(historyBefore,Ok("history_list")["total"]),"Failed placement recorded history.");
            var saved=Bounds(group);Ok("content_save",new JObject{["slot"]="bounds"});
            var placement=Ok("place_relative",PlaceArgs(group,reference,"above"));
            var placedBounds=Bounds(group);Ok("content_save",new JObject{["slot"]="placed"});
            var loaded=Ok("content_load",new JObject{["slot"]="placed"});Near(Read(Bounds(group)["center"]),Read(placedBounds["center"]),"Persistence lost placement");
            Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=loaded["operationId"]});
            Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=placement["operationId"]});Near(Read(Bounds(group)["center"]),Read(saved["center"]),"Placement undo failed");
            Ok("set_transform",new JObject{["entityId"]=parent,["space"]="local",["scale"]=V(0,1,1)});
            var singularId=(string)Ok("scene_find",new JObject{["name"]="cube",["match"]="exact",["rootId"]=parent})["nodes"][0]["entityId"];
            var singularPose=Ok("scene_get",new JObject{["entityId"]=singularId})["transform"].DeepClone();
            Require(!(bool)Call("place_relative",PlaceArgs(singularId,reference,"above"))["success"],"Singular parent accepted.");
            Require(JToken.DeepEquals(singularPose,Ok("scene_get",new JObject{["entityId"]=singularId})["transform"]),"Rejected mutation changed scene.");
            // UI is prepared structurally here; panel creation/persistence belongs to roadmap 05.
            var uiId=Create("UiContainer");var uiRoot=root.transform.Find("UiContainer");
            var canvasObject=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));canvasObject.transform.SetParent(uiRoot,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            var rect=(RectTransform)canvasObject.transform;rect.sizeDelta=new Vector2(200,100);rect.pivot=new Vector2(0,1);rect.localScale=new Vector3(.01f,.02f,.01f);rect.localRotation=Quaternion.Euler(15,40,20);
            var panel=new GameObject("Panel",typeof(RectTransform),typeof(UnityEngine.UI.Image));panel.transform.SetParent(rect,false);
            var panelRect=(RectTransform)panel.transform;panelRect.anchorMin=panelRect.anchorMax=new Vector2(.5f,.5f);panelRect.sizeDelta=new Vector2(400,50);panelRect.pivot=new Vector2(1,0);panelRect.anchoredPosition=new Vector2(150,-80);
            Canvas.ForceUpdateCanvases();
            var allCorners=new Vector3[4];var uiBounds=Bounds(uiId);var uiMin=Read(uiBounds["min"]);var uiMax=Read(uiBounds["max"]);
            foreach(var uiRect in new[]{rect,panelRect}){uiRect.GetWorldCorners(allCorners);foreach(var corner in allCorners)for(int axis=0;axis<3;axis++)Require(corner[axis]>=uiMin[axis]-.0001f&&corner[axis]<=uiMax[axis]+.0001f,"UI corner escaped bounds.");}
            foreach(var direction in directions){Place(uiId,reference,direction);Place(reference,uiId,direction);}
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;Require(!(bool)Call("scene_bounds",new JObject{["entityId"]=uiId})["success"],"Screen UI accepted.");
            return new JObject{["success"]=true,["assertions"]=checks,["placements"]=placements,["primitives"]=new JArray(shapes),["coverage"]=new JArray("six directions both target/reference","negative nonuniform parent scales and rotation/shear","mesh vertex containment","flat parent with identity local scale","aggregate root and nested geometry","offset pivot","inactive and trash","invalid relations and singular parents","duplicate mutation","operation undo","save/load","world-space UI corners and pivots","screen-space rejection")}.ToString();
        }
        finally{Object.DestroyImmediate(root);if(Directory.Exists(storage)){foreach(var file in Directory.EnumerateFiles(storage))File.Delete(file);Directory.Delete(storage);}}
    }
    static JObject MergeShape(this JObject args,string shape){if(shape!=null)args["primitiveType"]=shape;return args;}
}
