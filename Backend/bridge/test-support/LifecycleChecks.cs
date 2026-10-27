using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LifecycleChecks
{
    public static string Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/lifecycle-tests-"+Guid.NewGuid().ToString("N")));
        var host=Object.FindFirstObjectByType<SeyraSceneRuntime>().transform;
        var root=new GameObject("LifecycleFixture");root.transform.SetParent(host,false);
        var collision=new GameObject("__SeyraTrash");collision.transform.SetParent(root.transform,false);
        var service=new SceneToolService(root.transform,directory);
        if(!collision.activeSelf)throw new Exception("A user object was mistaken for the trash by name");
        Object.DestroyImmediate(collision);
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        JObject Raw(string tool,JObject args,string id=null)=>JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=id??Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args,["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(1).ToString("O")}.ToString()));
        JObject Call(string tool,JObject args){var r=Raw(tool,args);Check((bool)r["success"],tool+": "+r);return(JObject)r["data"];}
        JObject Create(string name,string parent="Generated")=>new JObject{["name"]=name,["parentId"]=parent,["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)};
        string groupId=null,cubeId=null;
        try
        {
            var group=Call("request_begin",new JObject{["name"]="Build table"});groupId=(string)group["entityId"];
            Check(!(bool)Raw("request_begin",new JObject{["name"]="Overlap"})["success"],"Overlapping requests accepted");
            var cubeArgs=Create("Tabletop");cubeArgs["primitiveType"]="cube";
            var cube=Call("create_primitive",cubeArgs);cubeId=(string)cube["entityId"];
            Check((string)cube["parentId"]==groupId,"Generated did not route to request container");
            Check((string)cube["requestGroupId"]==groupId,"Provenance missing");
            var componentIds=cube["components"].DeepClone();
            var renamed=Call("rename_object",new JObject{["entityId"]=cubeId,["name"]=" Tabletop renamed "});
            Check((string)renamed["name"]=="Tabletop renamed" && (string)renamed["entityId"]==cubeId && JToken.DeepEquals(componentIds,renamed["components"]),"Rename lost identity or components");
            Call("undo",new JObject{["scope"]="operation",["expectedOperationId"]=renamed["operationId"]});
            Check((string)Call("scene_get",new JObject{["entityId"]=cubeId})["name"]=="Tabletop","Rename undo failed");
            foreach(var invalidName in new[]{"", "   ", new string('x',257)})Check(!(bool)Raw("rename_object",new JObject{["entityId"]=cubeId,["name"]=invalidName})["success"],"Invalid rename accepted");
            Check(!(bool)Raw("rename_object",new JObject{["entityId"]="Generated",["name"]="Forbidden"})["success"],"Renamed protected root");
            Call("rename_object",new JObject{["entityId"]=cubeId,["name"]="Tabletop renamed"});
            var changed=Call("set_transform",new JObject{["entityId"]=cubeId,["space"]="local",["position"]=V(1,2,3)});
            Check(!(bool)Raw("content_save",new JObject{["slot"]="table"})["success"],"Saved an open request");
            Call("request_end",new JObject{["requestGroupId"]=groupId});
            Call("content_save",new JObject{["slot"]="table"});Call("content_save",new JObject{["slot"]="table"});
            Check(((JArray)Call("content_list",new JObject())["slots"]).Count==1,"Atomic overwrite created extra slots");
            var before=File.ReadAllText(Path.Combine(directory,"table.json"));
            var operation=(string)changed["operationId"];
            Call("undo",new JObject{["scope"]="operation",["expectedOperationId"]=operation});
            Check(JToken.DeepEquals(Call("scene_get",new JObject{["entityId"]=cubeId})["transform"]["localPosition"],V()),"Transform undo failed");
            Call("undo",new JObject{["scope"]="request"});
            Check((int)Call("scene_hierarchy",new JObject{["rootId"]="Generated"})["total"]==1,"Request undo left visible objects");
            Check((bool)Call("scene_get",new JObject{["entityId"]=cubeId})["deleted"],"Undo creation destroyed the object instead of retaining it");
            Call("restore_object",new JObject{["entityId"]=groupId});
            Check(JToken.DeepEquals(componentIds,Call("scene_get",new JObject{["entityId"]=cubeId})["components"]),"Restoration changed component IDs");
            var deletion=Call("delete_object",new JObject{["entityId"]=groupId});
            Check((bool)deletion["deleted"],"Delete did not mark tombstone");
            Check((int)Call("scene_find",new JObject{["name"]="Tabletop",["includeInactive"]=true})["total"]==0,"Deleted object leaked into queries");
            Check(!(bool)Raw("set_transform",new JObject{["entityId"]=cubeId,["space"]="local",["position"]=V(9)})["success"],"Deleted object could be edited");
            Call("undo",new JObject{["scope"]="operation",["expectedOperationId"]=deletion["operationId"]});
            Check(!(bool)Call("scene_get",new JObject{["entityId"]=cubeId})["deleted"],"Undo delete failed");
            var live=host.GetComponentsInChildren<GeneratedObject>(true).First(r=>r.EntityId==cubeId&&!r.transform.IsChildOf(root.transform.Find("__SeyraTrash"))).gameObject;
            live.transform.localPosition=new Vector3(7,8,9);
            Check(!(bool)Raw("undo",new JObject{["scope"]="operation"})["success"],"Undo overwrote an external edit");
            Check(live.transform.localPosition==new Vector3(7,8,9),"Conflict changed the scene");
            Call("content_load",new JObject{["slot"]="table"});
            var loaded=Call("scene_get",new JObject{["entityId"]=cubeId});
            Check((string)loaded["name"]=="Tabletop renamed","Persistence lost renamed object name");
            Check(JToken.DeepEquals(loaded["transform"]["localPosition"],V(1,2,3)),"Load lost saved Transform");
            Check(!JToken.DeepEquals(loaded["components"],componentIds),"Load should create new component IDs");
            Call("undo",new JObject{["scope"]="operation"});
            Check(JToken.DeepEquals(Call("scene_get",new JObject{["entityId"]=cubeId})["transform"]["localPosition"],V(7,8,9)),"Load undo did not restore replaced content");
            live.AddComponent<Rigidbody>();
            Check(!(bool)Raw("content_save",new JObject{["slot"]="table"})["success"],"Unsupported components silently lost on save");
            Check(File.ReadAllText(Path.Combine(directory,"table.json"))==before,"Failed save overwrote valid slot");
            foreach(var slot in new[]{"../outside","C:/bad","a/b",""})Check(!(bool)Raw("content_load",new JObject{["slot"]=slot})["success"],"Path traversal accepted");
            var invalid=JObject.Parse(before);((JArray)invalid["nodes"])[0]["parentId"]="go_"+Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(directory,"invalid.json"),invalid.ToString());
            Check(!(bool)Raw("content_load",new JObject{["slot"]="invalid"})["success"],"Invalid parent accepted");
            Check(JToken.DeepEquals(Call("scene_get",new JObject{["entityId"]=cubeId})["transform"]["localPosition"],V(7,8,9)),"Failed load replaced content");
            var external=new GameObject("ExternalTransformFixture");external.transform.SetParent(host,false);
            try
            {
                var externalId=(string)Call("scene_find",new JObject{["name"]="ExternalTransformFixture",["match"]="exact"})["nodes"][0]["entityId"];
                var edit=Call("request_begin",new JObject{["name"]="Edit existing objects"});
                Call("set_transform",new JObject{["entityId"]=externalId,["space"]="local",["position"]=V(4,5,6)});
                Call("set_transform",new JObject{["entityId"]=cubeId,["space"]="local",["position"]=V(11,12,13)});
                Call("request_end",new JObject{["requestGroupId"]=edit["entityId"]});
                Call("undo",new JObject{["scope"]="request"});
                Check(external.transform.localPosition==Vector3.zero,"Request undo missed an external Transform target");
                Check(JToken.DeepEquals(Call("scene_get",new JObject{["entityId"]=cubeId})["transform"]["localPosition"],V(7,8,9)),"Request undo missed existing generated content");
            }
            finally{Object.DestroyImmediate(external);}
            Object.DestroyImmediate(root);
            root=new GameObject("NewRuntimeFixture");root.transform.SetParent(host,false);service=new SceneToolService(root.transform,directory);
            Call("content_load",new JObject{["slot"]="table"});
            Check((string)Call("scene_get",new JObject{["entityId"]=cubeId})["parentId"]==groupId,"IDs or hierarchy did not survive a new runtime");
            Check((string)Call("request_list",new JObject())["activeRequestGroupId"]==null,"Loaded a phantom open request");
            return "PASS: request grouping, operation/request undo, soft delete, subtree recovery with component IDs, conflict detection, atomic save overwrite, persistent IDs across runtimes, undo load, unsupported-component rejection, invalid-file rollback and slot traversal rejection.";
        }
        finally
        {
            if(root!=null)Object.DestroyImmediate(root);
            // Only files created in this isolated fixture are removed; no recursive directory deletion.
            if(Directory.Exists(directory)){foreach(var file in Directory.EnumerateFiles(directory))File.Delete(file);Directory.Delete(directory);}
        }
    }
}
