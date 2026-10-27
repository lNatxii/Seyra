using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class AppearanceChecks
{
    public static async System.Threading.Tasks.Task<string> Run()
    {
        if(!Application.isPlaying)throw new Exception("Run in Play Mode.");
        var previousBackground=Application.runInBackground;Application.runInBackground=true;
        var root=new GameObject("__AppearanceChecks");
        var storage=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/appearance-"+Guid.NewGuid().ToString("N")));
        var service=new SceneToolService(root.transform,storage);
        JObject Call(string tool,JObject args=null,string requestId=null)
        {
            return JObject.Parse(service.Dispatch(new JObject{["version"]=1,["type"]="tool_request",["requestId"]=requestId??Guid.NewGuid().ToString(),["tool"]=tool,["arguments"]=args??new JObject(),["deadlineUtc"]=DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")}.ToString()));
        }
        JObject Ok(string tool,JObject args=null,string requestId=null)
        {
            var result=Call(tool,args,requestId);Require((bool)result["success"],result.ToString());return (JObject)result["data"];
        }
        JObject V(float x=0,float y=0,float z=0)=>new JObject{["x"]=x,["y"]=y,["z"]=z};
        JObject Color(float alpha=1)=>new JObject{["r"]=0.2,["g"]=0.4,["b"]=0.8,["a"]=alpha};
        JObject Create(string name)=>Ok("create_primitive",new JObject{["name"]=name,["parentId"]="Generated",["primitiveType"]="sphere",["position"]=V(),["rotation"]=V(),["scale"]=V(1,1,1)});
        JObject Set(string id,string style,float alpha=1)=>new JObject{["entityId"]=id,["style"]=style,["color"]=Color(alpha)};
        try
        {
            var first=Create("First");var second=Create("Second");var firstId=(string)first["entityId"];
            var firstObject=root.transform.Find("First").gameObject;
            var secondObject=root.transform.Find("Second").gameObject;
            var material=firstObject.GetComponent<Renderer>().sharedMaterial;
            Require(material!=secondObject.GetComponent<Renderer>().sharedMaterial,"Objects share their mutable material.");
            Require((string)first["appearance"]["style"]=="matte_opaque","Default appearance missing.");
            foreach(var style in new[]{"matte_opaque","colored_translucent","unlit","ui_unlit","plastic","transparent"})
            {
                var alpha=style=="colored_translucent"||style=="ui_unlit"||style=="transparent"?0.35f:1;
                var before=Ok("scene_get",new JObject{["entityId"]=firstId})["appearance"].DeepClone();
                var requestId=Guid.NewGuid().ToString();
                var applied=Ok("set_appearance",Set(firstId,style,alpha),requestId);
                Require((string)applied["appearance"]["style"]==style,"Wrong style.");
                Require(JToken.DeepEquals(applied,Ok("set_appearance",Set(firstId,style,alpha),requestId)),"Duplicate mutation was executed twice.");
                Require(firstObject.GetComponent<Renderer>().sharedMaterial==material,"Changing style allocated another material.");
                Require(secondObject.GetComponent<ManagedAppearance>().Color==UnityEngine.Color.white,"Other object's color changed.");
                ShaderUtil.CompilePass(material,0,true);
                Require(!ShaderUtil.ShaderHasError(material.shader),"Shader compilation failed: "+style);
                Ok("content_save",new JObject{["slot"]="styles"});
                Require((int)JObject.Parse(File.ReadAllText(Path.Combine(storage,"styles.json")))["version"]==2,"Wrong persistence version.");
                var loaded=Ok("content_load",new JObject{["slot"]="styles"});
                Require(JToken.DeepEquals(applied["appearance"],Ok("scene_get",new JObject{["entityId"]=firstId})["appearance"]),"Load lost appearance.");
                Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=loaded["operationId"]});
                Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=applied["operationId"]});
                Require(JToken.DeepEquals(before,Ok("scene_get",new JObject{["entityId"]=firstId})["appearance"]),"Undo lost appearance.");
            }
            foreach(var style in new[]{"matte_opaque","unlit","plastic"})Require(!(bool)Call("set_appearance",Set(firstId,style,0.5f))["success"],"Opaque alpha accepted.");
            foreach(var alpha in new[]{-0.1f,1.1f})Require(!(bool)Call("set_appearance",Set(firstId,"transparent",alpha))["success"],"Out-of-range alpha accepted.");
            Require(!(bool)Call("set_appearance",Set("Generated","unlit"))["success"],"Root appearance accepted.");
            Require(!(bool)Call("set_appearance",Set(firstId,"unknown"))["success"],"Unknown style accepted.");
            var invalid=Set(firstId,"unlit");invalid["color"]["r"]="red";
            Require(!(bool)Call("set_appearance",invalid)["success"],"Non-numeric color accepted.");
            var zero=Ok("set_appearance",Set(firstId,"transparent",0));
            Require((float)zero["appearance"]["color"]["a"]==0,"Fully transparent alpha rejected.");
            Require(firstObject.GetComponent<Collider>().enabled,"Transparency changed collider.");
            var deleted=Ok("delete_object",new JObject{["entityId"]=firstId});
            Require(!(bool)Call("set_appearance",Set(firstId,"unlit"))["success"],"Deleted mutation accepted.");
            Ok("restore_object",new JObject{["entityId"]=firstId});
            Require((string)Ok("scene_get",new JObject{["entityId"]=firstId})["appearance"]["style"]=="transparent","Restore lost appearance.");
            Ok("content_save",new JObject{["slot"]="legacy"});
            var path=Path.Combine(storage,"legacy.json");var legacy=JObject.Parse(File.ReadAllText(path));legacy["version"]=1;
            ((JObject)legacy["root"]).Remove("appearance");foreach(JObject node in legacy["nodes"])node.Remove("appearance");
            File.WriteAllText(path,legacy.ToString());
            var migration=Ok("content_load",new JObject{["slot"]="legacy"});
            Require((string)Ok("scene_get",new JObject{["entityId"]=firstId})["appearance"]["style"]=="matte_opaque","v1 migration failed.");
            Ok("undo",new JObject{["scope"]="operation",["expectedOperationId"]=migration["operationId"]});
            var malformed=JObject.Parse(File.ReadAllText(Path.Combine(storage,"styles.json")));
            var malformedNode=(JObject)malformed["nodes"][0];
            malformedNode["appearance"]["style"]="unsupported";
            File.WriteAllText(Path.Combine(storage,"invalid.json"),malformed.ToString());
            Require(!(bool)Call("content_load",new JObject{["slot"]="invalid"})["success"],"Invalid v2 appearance accepted.");
            Require(root.transform.Find("First").gameObject==firstObject,"Invalid load replaced live content.");
            var originalFile=File.ReadAllText(Path.Combine(storage,"styles.json"));
            material.SetColor("_BaseColor",UnityEngine.Color.magenta);
            Require(!(bool)Call("content_save",new JObject{["slot"]="styles"})["success"],"External material edits were saved.");
            Require(File.ReadAllText(Path.Combine(storage,"styles.json"))==originalFile,"Rejected save overwrote file.");
            var beforeExternal=material.GetColor("_BaseColor");
            Require(!(bool)Call("set_appearance",Set(firstId,"unlit"))["success"],"External material edit was overwritten.");
            Require(material.GetColor("_BaseColor")==beforeExternal,"Failed mutation changed external color.");
            Require(!(bool)Call("undo",new JObject{["scope"]="operation"})["success"],"External appearance edit did not cause history conflict.");
            firstObject.GetComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.Transparent,new UnityEngine.Color(.2f,.4f,.8f,0));
            var ui=new GameObject("Ui",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(ManagedAppearance));ui.transform.SetParent(root.transform,false);
            ui.GetComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.UiUnlit,new UnityEngine.Color(1,0,0,.5f));
            var uiMaterial=ui.GetComponent<Image>().material;
            Require(uiMaterial.shader.name=="Seyra/UiUnlit","uGUI shader was not assigned.");
            Require(uiMaterial.HasProperty("_Stencil")&&uiMaterial.HasProperty("_StencilComp"),"UI stencil properties missing.");
            Object.DestroyImmediate(ui);await System.Threading.Tasks.Task.Delay(100);Require(uiMaterial==null,"Owned UI material leaked.");
            var destructionMaterial=secondObject.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(secondObject);await System.Threading.Tasks.Task.Delay(100);Require(destructionMaterial==null,"Owned primitive material leaked.");
            return "PASS: six styles compiled; per-object material isolation/reuse; RGBA validation; alpha zero; mutation deduplication; undo; six save/load round trips; v1 migration; delete/restore; external edits rejected without overwrite; file protection; uGUI/stencil support; owned material cleanup.";
        }
        finally
        {
            Object.DestroyImmediate(root);
            Application.runInBackground=previousBackground;
            if(Directory.Exists(storage))Directory.Delete(storage,true);
        }
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}

