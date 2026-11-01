using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Seyra.SceneTools
{
    // Generated content has persistent logical IDs; external inspected objects keep runtime-only identities.
    public sealed partial class SceneToolService : IDisposable
    {
        public string RuntimeId { get; } = Guid.NewGuid().ToString("N");
        readonly Transform generated;
        readonly Dictionary<int, (Object value, string id)> identities = new Dictionary<int, (Object, string)>();
        readonly Dictionary<string, Object> objects = new Dictionary<string, Object>(StringComparer.Ordinal);
        readonly Dictionary<string, (string signature, string response)> completed = new Dictionary<string, (string, string)>();
        static readonly HashSet<string> queries = new HashSet<string> { "scene_hierarchy", "scene_find", "scene_get", "scene_bounds", "room_get", "room_objects", "spatial_candidates", "layout_plan", "request_list", "history_list", "content_list", "ui_catalog", "ui_media_list", "ui_list", "ui_get", "card_checks", "media_status", "media_deliver" };

        public SceneToolService(Transform generatedRoot,string persistenceDirectory=null,Seyra.UI.Library.UIPrefabCatalog panelCatalog=null,Seyra.UI.Presentations.UIPresentationCatalog presentationCatalog=null)
        {
            if (generatedRoot == null) throw new ArgumentException("Generated must be assigned.");
            generated = generatedRoot;
            uiCatalog = panelCatalog;
            presentations = presentationCatalog ?? Resources.Load<Seyra.UI.Presentations.UIPresentationCatalog>("SeyraUIPresentations");
            InitializeLifecycle(persistenceDirectory);
            remoteMedia = RemoteMediaStore.Acquire(generated, storageDirectory);
        }

        bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (remoteMedia != null) remoteMedia.ReleaseOwner();
        }

        public string Dispatch(string json)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SceneToolService));
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 };
            var request = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            while (reader.Read()) throw new ArgumentException("Unexpected trailing JSON content.");
            Keys(request, new[] { "version", "type", "requestId", "tool", "arguments", "deadlineUtc" });
            if (request["version"].Type != JTokenType.Integer || (int)request["version"] != 1 || Text(request,"type") != "tool_request" || !Guid.TryParse(Text(request,"requestId"), out _))
                throw new ArgumentException("Invalid bridge command.");
            var requestId = Text(request,"requestId");
            var tool = Text(request,"tool");
            var args = request["arguments"] as JObject ?? throw new ArgumentException("arguments must be an object.");
            var signature = tool + "\n" + args.ToString(Formatting.None);
            var response = new JObject { ["version"] = 1, ["type"] = "tool_result", ["requestId"] = requestId };
            try
            {
                if (completed.TryGetValue(requestId, out var old))
                {
                    if (old.signature != signature) throw new ArgumentException("requestId was reused with different arguments.");
                    return old.response;
                }
                if (!DateTimeOffset.TryParse(Text(request,"deadlineUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var deadline) || deadline <= DateTimeOffset.UtcNow)
                    throw new InvalidOperationException("Expired command: it was not executed. Inspect the state before continuing.");
                if (!Application.isPlaying) throw new InvalidOperationException("Only available in Play Mode.");
                if (!queries.Contains(tool) && completed.Count >= 4096) throw new InvalidOperationException("Mutation limit reached; restart Play Mode.");
                ProtectRoomMutation(tool,args);
                ProtectSpatialApplication(tool,args);
                response["data"] = ExecuteRecorded(tool,args);
                response["success"] = true;
            }
            catch (Exception error)
            {
                response["success"] = false;
                response["error"] = new JObject { ["code"] = error is LayoutToolException layoutError ? layoutError.Code : error.Message.StartsWith("LAYOUT_BEHAVIOR_INVALID:",StringComparison.Ordinal) ? "LAYOUT_BEHAVIOR_INVALID" : error.Message.StartsWith("LAYOUT_SIZE_INVALID:",StringComparison.Ordinal) ? "LAYOUT_SIZE_INVALID" : error is RoomToolException roomError ? roomError.Code : error is SceneRollbackException ? "ROLLBACK_FAILED" : error is ArgumentException ? "INVALID_ARGUMENT" : "TOOL_FAILED", ["message"] = error.Message.Substring(0,Math.Min(4096,error.Message.Length)) };
                if(error.Data.Contains("sceneBatchIndex"))response["error"]["index"]=(int)error.Data["sceneBatchIndex"];
                if(error is LayoutToolException layoutFailure && layoutFailure.Details!=null)foreach(var detail in layoutFailure.Details.Properties())response["error"][detail.Name]=detail.Value.DeepClone();
            }
            var result = response.ToString(Formatting.None);
            if (!queries.Contains(tool) && !completed.ContainsKey(requestId) && completed.Count < 4096) completed[requestId] = (signature,result);
            return result;
        }

        string Id(Object value)
        {
            if(value is GameObject owned)
            {
                var record=owned.GetComponent<GeneratedObject>();
                if(record!=null && !string.IsNullOrEmpty(record.EntityId)) {objects[record.EntityId]=owned;return record.EntityId;}
            }
            var key = value.GetInstanceID();
            if (identities.TryGetValue(key,out var known) && ReferenceEquals(known.value,value)) return known.id;
            var id = (value is GameObject ? "go_" : "cmp_") + Guid.NewGuid().ToString("N");
            identities[key] = (value,id); objects[id] = value;
            return id;
        }
        string SceneId(UnityEngine.SceneManagement.Scene scene) => "scene_" + RuntimeId + "_" + scene.handle;
        GameObject Find(JToken token,bool includeDeleted=false)
        {
            var id = String(token,128);
            if(id.Equals("null",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use an actual ID or Generated; never null.");
            if (id == "Generated") { if (generated == null) throw new ArgumentException("Generated has been deleted."); return generated.gameObject; }
            if (!objects.TryGetValue(id,out var target) || !(target is GameObject go) || go == null || !go.scene.IsValid() || !go.scene.isLoaded)
                throw new ArgumentException("Unknown or stale GameObject ID. Query this runtime's hierarchy.");
            if(!includeDeleted && IsDeleted(go))throw new ArgumentException("The object is in the trash. Restore it before modifying it.");
            return go;
        }
        GameObject Parent(JToken token)
        {
            var parent = Find(token);
            if(parent.GetComponentInParent<ManagedPanel>(true)!=null)throw new ArgumentException("A panel and its internals cannot be used as a content parent.");
            if (generated == null || (parent.transform != generated && !parent.transform.IsChildOf(generated)))
                throw new ArgumentException("The parent must be Generated or one of its descendants.");
            return parent;
        }
        static string Path(Transform transform)
        {
            var names = new Stack<string>();
            for (var t=transform; t!=null; t=t.parent) names.Push(t.name);
            return string.Join("/",names);
        }
        JObject Node(GameObject go, int depth)
        {
            var t=go.transform;
            var record=go.GetComponent<GeneratedObject>();
            return new JObject { ["entityId"]=Id(go), ["name"]=go.name, ["path"]=Path(t), ["sceneId"]=SceneId(go.scene),
                ["parentId"]=t.parent!=null ? Id(t.parent.gameObject) : SceneId(go.scene), ["depth"]=depth,
                ["siblingIndex"]=t.GetSiblingIndex(), ["childCount"]=t.Cast<Transform>().Count(c=>c!=trash), ["activeSelf"]=go.activeSelf,
                ["physical"]=PhysicalNode(go),["readOnly"]=PhysicalNode(go),["physicalId"]=go.GetComponent<Meta.XR.MRUtilityKit.MRUKAnchor>() is Meta.XR.MRUtilityKit.MRUKAnchor anchor ? "physical:"+anchor.Anchor.Uuid : null,
                ["managed"]=record!=null,["requestGroupId"]=record==null?null:record.RequestGroupId,["deleted"]=IsDeleted(go),
                ["activeInHierarchy"]=go.activeInHierarchy, ["tag"]=go.tag, ["layer"]=go.layer };
        }
        List<GameObject> Roots()
        {
            var result=new List<GameObject>();
            for(var i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.isLoaded) result.AddRange(scene.GetRootGameObjects());
            }
            // Unity excludes DontDestroyOnLoad from sceneCount. Include those scene objects, not prefab assets.
            result.AddRange(Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g!=null && g.scene.IsValid() && g.scene.isLoaded && g.scene.name=="DontDestroyOnLoad" && g.transform.parent==null));
            return result.Distinct().OrderBy(g=>SceneId(g.scene),StringComparer.Ordinal).ThenBy(g=>g.transform.GetSiblingIndex()).ToList();
        }
        List<(GameObject go, JObject node)> Scan(JObject args)
        {
            var roots=args["rootId"]==null ? Roots() : new List<GameObject>{Find(args["rootId"])};
            bool inactive=Bool(args,"includeInactive",true);
            var stack=new Stack<(Transform t,int depth)>();
            for(int i=roots.Count-1;i>=0;i--) stack.Push((roots[i].transform,0));
            var result=new List<(GameObject,JObject)>();
            while(stack.Count>0)
            {
                var (t,depth)=stack.Pop();
                if(t==trash || IsDeleted(t.gameObject))continue;
                if(!inactive && !t.gameObject.activeInHierarchy) continue;
                result.Add((t.gameObject,Node(t.gameObject,depth)));
                if(result.Count>50000) throw new InvalidOperationException("More than 50000 nodes; narrow the query with rootId. A truncated hierarchy will not be returned.");
                for(int i=t.childCount-1;i>=0;i--) stack.Push((t.GetChild(i),depth+1));
            }
            return result;
        }
        static bool HasType(Component component,string type)
        {
            if(component==null)return false;
            for(var t=component.GetType();t!=null;t=t.BaseType) if(t.Name==type || t.FullName==type)return true;
            return component.GetType().GetInterfaces().Any(t=>t.Name==type || t.FullName==type);
        }
        JObject Hierarchy(JObject args,bool search)
        {
            Keys(args,Array.Empty<string>(),search ? new[]{"rootId","includeInactive","offset","limit","expectedRevision","name","path","componentType","match"} : new[]{"rootId","includeInactive","offset","limit","expectedRevision"});
            var all=Scan(args);
            var filtered=all;
            if(search)
            {
                if(args["name"]==null && args["path"]==null && args["componentType"]==null) throw new ArgumentException("Provide name, path or componentType.");
                var match=args["match"]==null ? "contains" : Text(args,"match");
                if(match!="exact" && match!="contains")throw new ArgumentException("match must be exact or contains.");
                string name=args["name"]==null?null:String(args["name"],256), path=args["path"]==null?null:String(args["path"],4096), type=args["componentType"]==null?null:String(args["componentType"],512);
                bool Matches(string value,string filter)=>filter==null || (match=="exact" ? string.Equals(value,filter,StringComparison.OrdinalIgnoreCase) : value.IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0);
                filtered=all.Where(p=>Matches(p.go.name,name) && Matches((string)p.node["path"],path) && (type==null || p.go.GetComponents<Component>().Any(c=>HasType(c,type)))).ToList();
            }
            var revisionState=new JObject{["runtimeId"]=RuntimeId,["rootId"]=args["rootId"]?.DeepClone(),["includeInactive"]=Bool(args,"includeInactive",true),
                ["name"]=args["name"]?.DeepClone(),["path"]=args["path"]?.DeepClone(),["componentType"]=args["componentType"]?.DeepClone(),["match"]=args["match"]?.DeepClone(),
                ["nodes"]=new JArray(filtered.Select(p=>p.node))};
            string revision;
            using(var hash=SHA256.Create()) revision=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(revisionState.ToString(Formatting.None)))).Replace("-","").ToLowerInvariant();
            if(args["expectedRevision"]!=null && String(args["expectedRevision"],128)!=revision) throw new InvalidOperationException("HIERARCHY_CHANGED: restart the query from offset=0.");
            int offset=Int(args,"offset",0,0,1000000),limit=Int(args,"limit",200,1,500);
            var loadedScenes=new List<UnityEngine.SceneManagement.Scene>();
            if(args["rootId"]==null) for(int i=0;i<SceneManager.sceneCount;i++) {var s=SceneManager.GetSceneAt(i);if(s.isLoaded)loadedScenes.Add(s);}
            var scenes=loadedScenes.Concat(all.Select(p=>p.go.scene)).GroupBy(s=>s.handle).Select(g=>g.First()).Select(s=>new JObject{["sceneId"]=SceneId(s),["name"]=s.name,["path"]=s.path});
            return new JObject { ["runtimeId"]=RuntimeId,["generatedRootId"]=generated==null?null:Id(generated.gameObject),["revision"]=revision,
                ["scenes"]=new JArray(scenes),["nodes"]=new JArray(filtered.Skip(offset).Take(limit).Select(p=>p.node)),["total"]=filtered.Count,
                ["nextOffset"]=offset+limit<filtered.Count ? new JValue(offset+limit) : JValue.CreateNull() };
        }

        JObject Inspect(GameObject go,int offset=0,int limit=50)
        {
            var node=Node(go,0); var t=go.transform;
            int depth=0; for(var p=t.parent;p!=null;p=p.parent)depth++;
            node["depth"]=depth; node["runtimeId"]=RuntimeId;
            var layout=go.GetComponent<Seyra.Spatial.SeyraLayoutGroup>()?.Specification;if(layout!=null)node["layout"]=layout;
            var motions=MotionsJson(go);if(motions!=null)node["motions"]=motions;
            var spatial=SpatialState(go);if(spatial!=null)node["spatial"]=spatial;
            node["transform"]=new JObject{["position"]=Vector(t.position),["rotation"]=Vector(t.eulerAngles),["localPosition"]=Vector(t.localPosition),["localRotation"]=Vector(t.localEulerAngles),["localScale"]=Vector(t.localScale),["lossyScale"]=Vector(t.lossyScale)};
            var appearance=go.GetComponent<ManagedAppearance>();
            if(appearance!=null){node["appearance"]=AppearanceJson(appearance);node["appearanceIntact"]=appearance.IsIntact();}
            var panel=go.GetComponent<ManagedPanel>();
            if(panel!=null)
            {
                if(panel.IsPresentation){node["presentation"]=panel.Presentation.ToJson();node["interaction"]=panel.InteractionState;
                    node["missingMedia"]=panel.PresentationView!=null?panel.PresentationView.Snapshot()["missingMedia"]:new JArray();}
                else node["panel"]=panel.Content.ToJson();
                node["panelIntact"]=panel.IsIntact();
            }
            var line=go.GetComponent<ManagedLine>();
            if(line!=null){line.Refresh();node["line"]=line.Content.ToJson();node["status"]=line.Status;node["missingTargets"]=line.MissingTargets;}
            var components=go.GetComponents<Component>();
            node["components"]=new JArray(components.Skip(offset).Take(limit).Select((c,i)=>c==null
                ? new JObject{["index"]=offset+i,["componentId"]=null,["type"]=null,["missingScript"]=true}
                : new JObject{["index"]=offset+i,["componentId"]=Id(c),["type"]=c.GetType().FullName,["missingScript"]=false,["enabled"]=Enabled(c)}));
            node["componentCount"]=components.Length;
            node["nextComponentOffset"]=offset+limit<components.Length ? new JValue(offset+limit) : JValue.CreateNull();
            var meshFilter=go.GetComponent<MeshFilter>();
            var mesh=meshFilter!=null ? meshFilter.sharedMesh : null;
            if(mesh!=null) node["geometry"]=new JObject{["meshName"]=mesh.name,["vertexCount"]=mesh.vertexCount,
                ["meshBoundsLocal"]=new JObject{["center"]=Vector(mesh.bounds.center),["size"]=Vector(mesh.bounds.size)}};
            return node;
        }
        static PrimitiveType Primitive(string name)
        {
            switch(name)
            {
                case "cube":return PrimitiveType.Cube;
                case "sphere":return PrimitiveType.Sphere;
                case "capsule":return PrimitiveType.Capsule;
                case "cylinder":return PrimitiveType.Cylinder;
                case "plane":return PrimitiveType.Plane;
                case "quad":return PrimitiveType.Quad;
                default:throw new ArgumentException("primitiveType must be cube, sphere, capsule, cylinder, plane or quad.");
            }
        }
        // Both creation tools use this path: validate everything before allocating, then commit or roll back.
        JObject CreateObject(JObject args,PrimitiveType? primitive)
        {
            RequireCapacity(1);
            var parent=Parent(args["parentId"]);var name=String(args["name"],256);
            if((string)args["parentId"]=="Generated" && !string.IsNullOrEmpty(activeRequest))parent=Find(new JValue(activeRequest));
            var position=ReadVector(args["position"]);var rotation=ReadVector(args["rotation"]);var scale=ReadVector(args["scale"]);
            var go=InstantiateManaged(name,parent.transform,primitive.HasValue?"primitive":"empty",primitive.HasValue?Text(args,"primitiveType"):null,activeRequest,position,Quaternion.Euler(rotation),scale);
            var result=Inspect(go);
            if(primitive.HasValue)result["primitiveType"]=Text(args,"primitiveType");
            return result;
        }
        GameObject InstantiateManaged(string name,Transform parent,string kind,string shape,string group,Vector3 position,Quaternion rotation,Vector3 scale,string persistentId=null)
        {
            if(parent==null)throw new ArgumentException("Creation requires an existing parent.");
            var go=shape!=null ? GameObject.CreatePrimitive(Primitive(shape)) : new GameObject(name);
            try
            {
                go.name=name;
                SceneManager.MoveGameObjectToScene(go,parent.gameObject.scene);
                go.transform.SetParent(parent,false);
                go.transform.localPosition=position;go.transform.localRotation=rotation;go.transform.localScale=scale;
                if(shape!=null && GraphicsSettings.currentRenderPipeline!=null && GraphicsSettings.currentRenderPipeline.defaultMaterial!=null)
                    go.GetComponent<Renderer>().sharedMaterial=GraphicsSettings.currentRenderPipeline.defaultMaterial;
                Check(go.transform);
                if(shape!=null)go.AddComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.MatteOpaque,Color.white);
                AttachRecord(go,kind,shape,group,persistentId);
                return go;
            }
            catch { Object.DestroyImmediate(go);throw; }
        }
        static JToken Enabled(Component c)
        {
            if(c is Behaviour b)return new JValue(b.enabled);
            if(c is Renderer r)return new JValue(r.enabled);
            if(c is Collider co)return new JValue(co.enabled);
            if(c is Collider2D co2)return new JValue(co2.enabled);
            return JValue.CreateNull();
        }
        JObject Execute(string tool,JObject args)
        {
            if(tool=="motion_set")return MotionSet(args);
            if(tool=="motion_remove")return MotionRemove(args);
            if(tool=="motion_control")return MotionControl(args);
            if(tool=="scene_build"||tool=="scene_edit")return ExecuteBatch(tool,args);
            if(tool.StartsWith("layout_",StringComparison.Ordinal))return ExecuteLayout(tool,args);
            if(tool.StartsWith("ui_",StringComparison.Ordinal)||tool=="media_status"||tool=="media_deliver")return ExecutePresentation(tool,args);
            if(tool=="create_point")return CreatePoint(args);
            if(tool=="create_line")return CreateLine(args);
            if(tool=="update_line")return UpdateLine(args);
            if(tool=="create_panel")return CreatePanel(args);
            if(tool=="update_panel")return UpdatePanel(args);
            if(tool=="card_checks")return CardChecks(args);
            if(tool=="spatial_candidates"||tool=="spatial_place")return SpatialPlace(args,tool=="spatial_place");
            if(tool=="room_get")return RoomInfo(args);
            if(tool=="room_objects")return RoomObjects(args);
            if(tool=="scene_bounds")return GetBounds(args);
            if(tool=="place_relative")return PlaceRelative(args);
            if(tool=="scene_hierarchy" || tool=="scene_find") return Hierarchy(args,tool=="scene_find");
            if(tool=="set_appearance")return SetAppearance(args);
            if(tool=="scene_get")
            {
                Keys(args,new[]{"entityId"},new[]{"componentOffset","componentLimit"});
                return Inspect(Find(args["entityId"],true),Int(args,"componentOffset",0,0,int.MaxValue),Int(args,"componentLimit",50,1,100));
            }
            if(tool=="create_gameobject" || tool=="create_primitive")
            {
                var primitive=tool=="create_primitive";
                Keys(args,primitive ? new[]{"name","parentId","position","rotation","scale","primitiveType"} : new[]{"name","parentId","position","rotation","scale"});
                return CreateObject(args,primitive ? Primitive(Text(args,"primitiveType")) : (PrimitiveType?)null);
            }
            if(tool=="set_transform")
            {
                Keys(args,new[]{"entityId","space"},new[]{"position","rotation","scale"});
                if(args["position"]==null && args["rotation"]==null && args["scale"]==null)throw new ArgumentException("Provide at least one transform field.");
                var go=Find(args["entityId"]);var t=go.transform;var space=Text(args,"space");
                RequirePanelRoot(go);
                if(space!="local" && space!="world")throw new ArgumentException("space must be local or world.");
                Vector3? pos=args["position"]==null?null:ReadVector(args["position"]), rot=args["rotation"]==null?null:ReadVector(args["rotation"]), scale=args["scale"]==null?null:ReadVector(args["scale"]);
                DrivenTransform.Require(t,(pos.HasValue?DrivenChannels.Position:DrivenChannels.None)|(rot.HasValue?DrivenChannels.Rotation:DrivenChannels.None));
                if(space=="world" && (pos.HasValue || rot.HasValue)) Invertible(t.parent);
                var oldPos=t.localPosition;var oldRot=t.localRotation;var oldScale=t.localScale;
                try
                {
                    if(pos.HasValue) { if(space=="local")t.localPosition=pos.Value;else t.position=pos.Value; }
                    if(rot.HasValue) { if(space=="local")t.localRotation=Quaternion.Euler(rot.Value);else t.rotation=Quaternion.Euler(rot.Value); }
                    if(scale.HasValue)t.localScale=scale.Value;
                    Check(t);return Inspect(go);
                }
                catch {t.localPosition=oldPos;t.localRotation=oldRot;t.localScale=oldScale;throw;}
            }
            if(tool=="rename_object")
            {
                Keys(args,new[]{"entityId","name"});
                var go=Find(args["entityId"]);
                RequirePanelRoot(go);
                if(go.transform==generated || !go.transform.IsChildOf(generated))throw new ArgumentException("Only descendants of Generated can be renamed.");
                go.name=String(args["name"],256);
                return Inspect(go);
            }
            if(tool=="set_parent")
            {
                Keys(args,new[]{"entityId","parentId","worldPositionStays"});
                var go=Find(args["entityId"]);var t=go.transform;var parent=Parent(args["parentId"]);
                RequirePanelRoot(go);
                if(go.GetComponent<SeyraMotion>()!=null)throw new RoomToolException("MOTION_DRIVEN",MotionDrivenMessage);
                if(t==generated || !t.IsChildOf(generated))throw new ArgumentException("Only descendants of Generated can be reparented.");
                if(parent==go || parent.transform.IsChildOf(t))throw new ArgumentException("The hierarchy cannot contain cycles.");
                if(parent.scene!=go.scene)throw new ArgumentException("Moving between scenes is not allowed.");
                var stays=Bool(args,"worldPositionStays");if(stays)Invertible(parent.transform);
                var oldParent=t.parent;var oldIndex=t.GetSiblingIndex();var oldPos=t.localPosition;var oldRot=t.localRotation;var oldScale=t.localScale;
                try { t.SetParent(parent.transform,stays);Check(t);return Inspect(go); }
                catch {t.SetParent(oldParent,false);t.SetSiblingIndex(oldIndex);t.localPosition=oldPos;t.localRotation=oldRot;t.localScale=oldScale;throw;}
            }
            throw new ArgumentException("Unknown tool.");
        }
        static void Invertible(Transform parent)
        {
            if(parent==null)return;
            var determinant=parent.localToWorldMatrix.determinant;
            if(!Finite(determinant) || Math.Abs(determinant)<1e-12f)throw new ArgumentException("The parent matrix is not invertible for world-space operations.");
        }
        static void Check(Transform t)
        {
            foreach(var value in new[]{t.position,t.localPosition,t.eulerAngles,t.localEulerAngles,t.localScale,t.lossyScale})
                if(!Finite(value.x)||!Finite(value.y)||!Finite(value.z))throw new ArgumentException("Transform cannot be represented; operation rolled back.");
            var matrix=t.localToWorldMatrix;
            for(int i=0;i<16;i++)if(!Finite(matrix[i]))throw new ArgumentException("Non-finite matrix; operation rolled back.");
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static JObject Vector(Vector3 v)=>new JObject{["x"]=v.x,["y"]=v.y,["z"]=v.z};
        static Vector3 ReadVector(JToken token)
        {
            var a=token as JObject??throw new ArgumentException("Vector must contain x,y,z.");Keys(a,new[]{"x","y","z"});
            float Number(string key)
            {
                if(a[key].Type!=JTokenType.Integer && a[key].Type!=JTokenType.Float)throw new ArgumentException("Coordinate must be numeric.");
                var value=(double)a[key];if(double.IsNaN(value)||double.IsInfinity(value)||Math.Abs(value)>float.MaxValue)throw new ArgumentException("Coordinate is outside the float range.");
                return (float)value;
            }
            return new Vector3(Number("x"),Number("y"),Number("z"));
        }
        static string String(JToken value,int maximum=4096)
        {
            if(value==null || value.Type!=JTokenType.String)throw new ArgumentException("Expected a non-empty string.");
            var text=((string)value).Trim();if(text.Length==0||text.Length>maximum)throw new ArgumentException("String is empty or too long.");return text;
        }
        static string Text(JObject a,string key)=>String(a[key]);
        static int Int(JObject a,string key,int fallback,int minimum,int maximum)
        {
            if(a[key]==null)return fallback;
            if(a[key].Type!=JTokenType.Integer)throw new ArgumentException(key+" must be an integer.");
            var value=(long)a[key];if(value<minimum||value>maximum)throw new ArgumentException(key+" is out of range.");return(int)value;
        }
        static bool Bool(JObject a,string key,bool fallback=false)
        {
            if(a[key]==null)return fallback;
            if(a[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be a boolean.");return(bool)a[key];
        }
        static void Keys(JObject a,string[] required,string[] optional=null)
        {
            var allowed=new HashSet<string>(required.Concat(optional??Array.Empty<string>()));
            if(required.Any(k=>a[k]==null)||a.Properties().Any(p=>!allowed.Contains(p.Name)))throw new ArgumentException("Required fields are missing or unrecognized fields are present.");
        }
    }
}
