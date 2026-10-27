using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using UnityEngine;
public static class SceneContractChecks
{
    public static string Run()
    {
        var service=UnityEngine.Object.FindFirstObjectByType<SeyraSceneRuntime>().Tools;
        JObject Call(string tool,JObject args,string id=null,int deadline=30)
        {
            return JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=id??Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args,["deadlineUtc"]=DateTimeOffset.UtcNow.AddSeconds(deadline).ToString("O")}.ToString()));
        }
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        var id=Guid.NewGuid().ToString();
        var args=new JObject{["name"]="RuntimeContractTest",["parentId"]="Generated",["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)};
        var first=Call("create_gameobject",args,id);Require((bool)first["success"],first.ToString());
        var repeat=Call("create_gameobject",args,id);Require(JToken.DeepEquals(first,repeat),"Duplicate UUID did not return cached result");
        var count=Call("scene_find",new JObject{["name"]="RuntimeContractTest",["match"]="exact"});Require((int)count["data"]["total"]==1,"Duplicate creation occurred");
        args["name"]="DifferentArguments";Require(!(bool)Call("create_gameobject",args,id)["success"],"UUID with different args accepted");
        args["name"]="ExpiredCreation";Require(!(bool)Call("create_gameobject",args,deadline:-1)["success"],"Expired mutation executed");
        Require((int)Call("scene_find",new JObject{["name"]="ExpiredCreation",["match"]="exact"})["data"]["total"]==0,"Expired object exists");
        var entityId=(string)first["data"]["entityId"];
        var go=GameObject.Find("Generated/RuntimeContractTest");
        go.name="RenamedContractTest";go.SetActive(false);
        var unchanged=Call("scene_get",new JObject{["entityId"]=entityId});Require((string)unchanged["data"]["name"]=="RenamedContractTest","ID changed on rename");
        Require((string)unchanged["data"]["components"][0]["componentId"]==(string)first["data"]["components"][0]["componentId"],"Component ID changed");
        Require((int)Call("scene_find",new JObject{["name"]="RenamedContractTest",["includeInactive"]=true})["data"]["total"]==1,"Inactive object omitted");
        Require((int)Call("scene_find",new JObject{["name"]="RenamedContractTest",["includeInactive"]=false})["data"]["total"]==0,"Inactive filter ignored");
        var zero=Call("set_transform",new JObject{["entityId"]=entityId,["space"]="local",["scale"]=V(0,1,1)});Require((bool)zero["success"],"Zero scale rejected");
        var child=Call("create_gameobject",new JObject{["name"]="Child",["parentId"]=entityId,["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)});
        Require((bool)child["success"],"Local creation under zero scale rejected");
        Require(!(bool)Call("set_transform",new JObject{["entityId"]=child["data"]["entityId"],["space"]="world",["position"]=V(1,2,3)})["success"],"World transform with singular parent accepted");
        Require(!(bool)Call("create_gameobject",new JObject{["name"]="Invalid",["parentId"]=null,["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)})["success"],"Runtime accepted parent null");
        Require(!(bool)Call("set_transform",new JObject{["entityId"]=entityId,["space"]="local",["position"]=new JObject{["x"]=double.MaxValue,["y"]=0,["z"]=0}})["success"],"Out-of-float vector accepted");
        UnityEngine.Object.DestroyImmediate(go);
        Require(!(bool)Call("scene_get",new JObject{["entityId"]=entityId})["success"],"Destroyed ID accepted");
        var primitiveRequest=Guid.NewGuid().ToString();
        var primitiveArgs=new JObject{["primitiveType"]="cube",["name"]="PrimitiveContractTest",["parentId"]="Generated",["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)};
        var primitive=Call("create_primitive",primitiveArgs,primitiveRequest);Require((bool)primitive["success"],primitive.ToString());
        Require(JToken.DeepEquals(primitive,Call("create_primitive",primitiveArgs,primitiveRequest)),"Primitive duplicated request was not cached");
        Require((int)Call("scene_find",new JObject{["name"]="PrimitiveContractTest",["match"]="exact"})["data"]["total"]==1,"Primitive created twice");
        var primitiveObject=GameObject.Find("Generated/PrimitiveContractTest");
        var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        Require(primitiveObject.GetComponent<Renderer>().sharedMaterial.shader.name=="Seyra/MatteOpaque","Primitive does not use the managed matte style");
        Require(primitiveObject.GetComponent<Renderer>().sharedMaterial.shader.isSupported,"Primitive shader unsupported");
        UnityEngine.Object.DestroyImmediate(primitiveObject);
        primitiveArgs["name"]="InvalidPrimitiveType";primitiveArgs["primitiveType"]="torus";
        Require(!(bool)Call("create_primitive",primitiveArgs)["success"],"Unknown primitive accepted by runtime");
        Require(GameObject.Find("Generated/InvalidPrimitiveType")==null,"Invalid primitive leaked an object");
        primitiveArgs["primitiveType"]="cube";primitiveArgs["parentId"]=null;
        Require(!(bool)Call("create_primitive",primitiveArgs)["success"],"Primitive accepted null parent at runtime");
        return "PASS: duplicate/conflicting UUIDs, expired mutation, stable object/component IDs, inactive filtering, zero scale, singular parent, null-parent rejection, float range, destroyed IDs, primitive deduplication, invalid primitive rollback, managed matte material and supported shader.";
    }
}
