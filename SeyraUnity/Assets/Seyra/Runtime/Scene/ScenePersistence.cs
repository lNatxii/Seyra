using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        const int FileLimit=5*1024*1024;
        static string DefaultStorageDirectory()
        {
#if UNITY_EDITOR
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.seyra/generated"));
#else
            return System.IO.Path.Combine(Application.persistentDataPath,"Seyra","Generated");
#endif
        }
        static JObject QuaternionJson(Quaternion q)=>new JObject{["x"]=q.x,["y"]=q.y,["z"]=q.z,["w"]=q.w};
        static Quaternion ReadQuaternion(JToken token)
        {
            var value=token as JObject??throw new ArgumentException("Invalid saved rotation.");
            Keys(value,new[]{"x","y","z","w"});
            var xyz=ReadVector(new JObject{["x"]=value["x"].DeepClone(),["y"]=value["y"].DeepClone(),["z"]=value["z"].DeepClone()});
            if(value["w"].Type!=JTokenType.Float && value["w"].Type!=JTokenType.Integer)throw new ArgumentException("Invalid quaternion.");
            var w=(double)value["w"];var norm=(double)xyz.x*xyz.x+(double)xyz.y*xyz.y+(double)xyz.z*xyz.z+w*w;
            if(double.IsNaN(norm)||double.IsInfinity(norm)||Math.Abs(norm-1)>0.001)throw new ArgumentException("Quaternion is not normalized.");
            return new Quaternion(xyz.x,xyz.y,xyz.z,(float)w).normalized;
        }
        static string ComponentConfiguration(GameObject go)
        {
            var result=new JArray();
            foreach(var c in go.GetComponents<Component>())
            {
                if(c==null){result.Add("missing");continue;}
                var value=new JObject{["type"]=c.GetType().FullName,["instance"]=c.GetInstanceID(),["enabled"]=Enabled(c)};
                if(c is MeshFilter filter)value["mesh"]=filter.sharedMesh!=null?filter.sharedMesh.GetInstanceID():0;
                if(c is Renderer renderer)
                {
                    value["materials"]=new JArray(renderer.sharedMaterials.Select(m=>m!=null?m.GetInstanceID():0));
                    value["shadows"]=(int)renderer.shadowCastingMode;value["receiveShadows"]=renderer.receiveShadows;
                    value["propertyBlock"]=renderer.HasPropertyBlock();
                    var material=renderer.sharedMaterial;
                    if(material!=null)
                    {
                        var property=material.HasProperty("_BaseColor")?"_BaseColor":material.HasProperty("_Color")?"_Color":null;
                        if(property!=null){var color=material.GetColor(property);value["color"]=new JArray(color.r,color.g,color.b,color.a);}
                    }
                }
                if(c is Collider collider)
                {
                    value["trigger"]=collider.isTrigger;value["contactOffset"]=collider.contactOffset;
                    value["physicsMaterial"]=collider.sharedMaterial!=null?collider.sharedMaterial.GetInstanceID():0;
                    if(c is BoxCollider box){value["center"]=Vector(box.center);value["size"]=Vector(box.size);}
                    if(c is SphereCollider sphere){value["center"]=Vector(sphere.center);value["radius"]=sphere.radius;}
                    if(c is CapsuleCollider capsule){value["center"]=Vector(capsule.center);value["radius"]=capsule.radius;value["height"]=capsule.height;value["direction"]=capsule.direction;}
                    if(c is MeshCollider mesh){value["mesh"]=mesh.sharedMesh!=null?mesh.sharedMesh.GetInstanceID():0;value["convex"]=mesh.convex;value["cooking"]= (int)mesh.cookingOptions;}
                }
                result.Add(value);
            }
            return result.ToString(Formatting.None);
        }
        static string PersistentId(JToken token)
        {
            var id=String(token,128);
            if(!id.StartsWith("go_",StringComparison.Ordinal)||!Guid.TryParseExact(id.Substring(3),"N",out _))throw new ArgumentException("Invalid persistent ID.");
            return id;
        }
        string SlotPath(JObject args)
        {
            Keys(args,new[]{"slot"});var slot=Text(args,"slot");
            if(!Regex.IsMatch(slot,"^[A-Za-z0-9_-]{1,64}$"))throw new ArgumentException("slot only accepts ASCII letters, digits, hyphens and underscores (1..64).");
            return System.IO.Path.Combine(storageDirectory,slot+".json");
        }
        JObject PersistNode(Transform t)
        {
            var record=t.GetComponent<GeneratedObject>();
            if(record==null)throw new InvalidOperationException("Cannot save "+t.name+": the object is not managed by Seyra.");
            var allowed=record.Kind=="primitive"?new[]{typeof(Transform),typeof(GeneratedObject),typeof(MeshFilter),typeof(MeshRenderer),typeof(BoxCollider),typeof(SphereCollider),typeof(CapsuleCollider),typeof(MeshCollider)}:new[]{typeof(Transform),typeof(GeneratedObject)};
            if(t.GetComponents<Component>().Any(c=>c==null||!allowed.Contains(c.GetType())) || ComponentConfiguration(t.gameObject)!=record.OriginalComponents)
                throw new InvalidOperationException("Cannot save "+t.name+": modified components, mesh, material or native parameters are not supported by format v1. The file was not overwritten.");
            Check(t);
            var parentId=t==generated?"Generated":t.parent==generated?"Generated":Id(t.parent.gameObject);
            return new JObject{["entityId"]=record.EntityId,["kind"]=record.Kind,["primitiveType"]=record.Kind=="primitive"?new JValue(record.PrimitiveType):JValue.CreateNull(),["requestGroupId"]=string.IsNullOrEmpty(record.RequestGroupId)?JValue.CreateNull():new JValue(record.RequestGroupId),
                ["name"]=t.name,["parentId"]=parentId,["siblingIndex"]=t.GetSiblingIndex(),["position"]=Vector(t.localPosition),["rotation"]=QuaternionJson(t.localRotation),["scale"]=Vector(t.localScale),
                ["activeSelf"]=t.gameObject.activeSelf,["layer"]=t.gameObject.layer,["tag"]=t.gameObject.tag};
        }
        JObject SaveContent(JObject args)
        {
            var path=SlotPath(args);
            if(activeRequest!=null)throw new InvalidOperationException("End the open request before saving.");
            var content=LiveContent().ToArray();if(content.Length>ContentLimit)throw new InvalidOperationException("Too many objects to save.");
            var root=PersistNode(generated);
            var nodes=new JArray(content.Where(t=>t!=generated).Select(PersistNode));
            var document=new JObject{["version"]=1,["savedAtUtc"]=DateTimeOffset.UtcNow.ToString("O"),["root"]=root,["nodes"]=nodes};
            var bytes=new UTF8Encoding(false,true).GetBytes(document.ToString(Formatting.Indented));
            if(bytes.Length>FileLimit)throw new InvalidOperationException("Saved content exceeds 5 MiB.");
            Directory.CreateDirectory(storageDirectory);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                ReplaceAtomically(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return new JObject{["slot"]=Text(args,"slot"),["saved"]=true,["nodeCount"]=nodes.Count,["version"]=1};
        }
        static void ReplaceAtomically(string temporary,string destination)
        {
            for(int attempt=0;;attempt++)
            {
                try
                {
                    if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
                    return;
                }
                catch(IOException) when(attempt<3 && File.Exists(temporary) && File.Exists(destination))
                {
                    // Windows indexers/antivirus can briefly lock the destination just after its creation.
                    // Retry only this atomic replacement while both files still exist, never the tool or scene mutation.
                    System.Threading.Thread.Sleep(20*(attempt+1));
                }
            }
        }
        JObject ListContent(JObject args)
        {
            Keys(args,Array.Empty<string>());
            var slots=Directory.Exists(storageDirectory)?Directory.EnumerateFiles(storageDirectory,"*.json").Where(p=>Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(p),"^[A-Za-z0-9_-]{1,64}$")).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new FileInfo(p)).Take(1000).ToArray():Array.Empty<FileInfo>();
            return new JObject{["slots"]=new JArray(slots.Select(f=>new JObject{["slot"]=System.IO.Path.GetFileNameWithoutExtension(f.Name),["bytes"]=f.Length,["modifiedUtc"]=f.LastWriteTimeUtc.ToString("O")}))};
        }
        sealed class SavedNode
        {
            public string Id,Parent,Name,Kind,Shape,Group,Tag;
            public int Sibling,Layer;
            public Vector3 Position,Scale;
            public Quaternion Rotation;
            public bool Active;
        }
        SavedNode ParseSaved(JToken token,bool root)
        {
            var a=token as JObject??throw new ArgumentException("Invalid saved node.");
            Keys(a,new[]{"entityId","kind","primitiveType","requestGroupId","name","parentId","siblingIndex","position","rotation","scale","activeSelf","layer","tag"});
            var node=new SavedNode{Id=PersistentId(a["entityId"]),Kind=Text(a,"kind"),Name=String(a["name"],256),Parent=String(a["parentId"],128),
                Sibling=Int(a,"siblingIndex",0,0,1000000),Layer=Int(a,"layer",0,0,31),Tag=String(a["tag"],256),Position=ReadVector(a["position"]),Scale=ReadVector(a["scale"]),Rotation=ReadQuaternion(a["rotation"]),Active=Bool(a,"activeSelf")};
            if(a["primitiveType"].Type!=JTokenType.Null){node.Shape=Text(a,"primitiveType");Primitive(node.Shape);}
            if(a["requestGroupId"].Type!=JTokenType.Null)node.Group=PersistentId(a["requestGroupId"]);
            if(root ? node.Kind!="root"||node.Parent!="Generated"||node.Group!=null : node.Kind!="empty"&&node.Kind!="request"&&node.Kind!="primitive")throw new ArgumentException("Invalid saved node type.");
            if((node.Kind=="primitive")!=(node.Shape!=null))throw new ArgumentException("Saved geometry does not match the node type.");
            return node;
        }
        JObject LoadContent(JObject args)
        {
            var path=SlotPath(args);
            if(activeRequest!=null)throw new InvalidOperationException("End the open request before loading.");
            if(!File.Exists(path))throw new ArgumentException("The slot does not exist.");
            if(new FileInfo(path).Length>FileLimit)throw new ArgumentException("File is too large.");
            JObject document;
            using(var stream=File.OpenRead(path))using(var text=new StreamReader(stream,new UTF8Encoding(false,true)))using(var reader=new JsonTextReader(text){DateParseHandling=DateParseHandling.None,MaxDepth=32})
            {
                document=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                while(reader.Read())throw new ArgumentException("Unexpected trailing content in the file.");
            }
            Keys(document,new[]{"version","savedAtUtc","root","nodes"});
            if(document["version"].Type!=JTokenType.Integer||(int)document["version"]!=1)throw new ArgumentException("Unsupported persistence version.");
            var root=ParseSaved(document["root"],true);
            var array=document["nodes"] as JArray??throw new ArgumentException("Invalid node list.");
            if(array.Count>=ContentLimit)throw new ArgumentException("Too many saved nodes.");
            var nodes=array.Select(t=>ParseSaved(t,false)).ToArray();var byId=new Dictionary<string,SavedNode>();
            RequireCapacity(nodes.Length+1);
            byId.Add(root.Id,root);
            foreach(var node in nodes)if(byId.ContainsKey(node.Id))throw new ArgumentException("Duplicate IDs in the file.");else byId.Add(node.Id,node);
            foreach(var node in nodes)
            {
                if(node.Parent!="Generated" && (!byId.ContainsKey(node.Parent)||node.Parent==root.Id))throw new ArgumentException("Invalid saved parent: use Generated for the root.");
                if(node.Group!=null && (!byId.TryGetValue(node.Group,out var group)||group.Kind!="request"))throw new ArgumentException("Saved request does not exist.");
                if(node.Kind=="request" && node.Group!=node.Id)throw new ArgumentException("Invalid request identity.");
                var visited=new HashSet<string>{node.Id};var parent=node.Parent;
                while(parent!="Generated"){if(!visited.Add(parent))throw new ArgumentException("Cycle in the saved hierarchy.");parent=byId[parent].Parent;}
            }
            foreach(var id in byId.Keys)
                if(objects.TryGetValue(id,out var existing)&&existing is GameObject go&&go!=null&&go.transform!=generated&&!go.transform.IsChildOf(generated))throw new ArgumentException("Saved ID conflicts with an external object.");
            var stage=new GameObject("__LoadStaging");stage.transform.SetParent(trash,false);stage.SetActive(false);
            var created=new Dictionary<string,GameObject>();
            try
            {
                foreach(var node in nodes)
                {
                    var go=InstantiateManaged(node.Name,stage.transform,node.Kind,node.Shape,node.Group,node.Position,node.Rotation,node.Scale,node.Id);
                    go.layer=node.Layer;go.tag=node.Tag;created.Add(node.Id,go);
                }
                // Validate the complete staged hierarchy, including transformed parents, before replacing live content.
                var remaining=nodes.ToList();var ordered=new List<SavedNode>();
                while(remaining.Count>0)
                {
                    var ready=remaining.Where(n=>n.Parent=="Generated"||ordered.Any(o=>o.Id==n.Parent)).ToArray();
                    foreach(var node in ready){var t=created[node.Id].transform;t.SetParent(node.Parent=="Generated"?stage.transform:created[node.Parent].transform,false);Check(t);ordered.Add(node);remaining.Remove(node);}
                    if(ready.Length==0)throw new ArgumentException("The hierarchy cannot be reconstructed.");
                }
                foreach(var child in generated.Cast<Transform>().Where(t=>t!=trash).ToArray())MoveToTrash(child.gameObject);
                generated.GetComponent<GeneratedObject>().Initialize(root.Id,"root",null,null);
                generated.localPosition=root.Position;generated.localRotation=root.Rotation;generated.localScale=root.Scale;
                generated.gameObject.layer=root.Layer;generated.gameObject.tag=root.Tag;generated.gameObject.name=root.Name;
                foreach(var node in ordered.Where(n=>n.Parent=="Generated"))created[node.Id].transform.SetParent(generated,false);
                foreach(var group in nodes.GroupBy(n=>n.Parent))foreach(var node in group.OrderBy(n=>n.Sibling))created[node.Id].transform.SetSiblingIndex(node.Sibling);
                foreach(var node in nodes){Check(created[node.Id].transform);created[node.Id].SetActive(node.Active);}
                Check(generated);generated.gameObject.SetActive(root.Active);Reindex();
                return new JObject{["slot"]=Text(args,"slot"),["loaded"]=true,["nodeCount"]=nodes.Length,["entityId"]=Id(generated.gameObject),["historyReset"]=false};
            }
            finally{Object.DestroyImmediate(stage);}
        }
    }
}
