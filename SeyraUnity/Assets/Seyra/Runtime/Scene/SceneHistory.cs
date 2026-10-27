using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        const int HistoryLimit=128;
        const int ContentLimit=2048;
        Transform trash;
        string activeRequest;
        readonly List<HistoryEntry> history=new List<HistoryEntry>();
        readonly HashSet<GameObject> externalTargets=new HashSet<GameObject>();
        readonly Dictionary<GameObject,ObjectState> deletedOrigins=new Dictionary<GameObject,ObjectState>();
        Snapshot lastCapture;
        string storageDirectory;
        static readonly HashSet<string> recordedTools=new HashSet<string>{"create_gameobject","create_primitive","set_transform","set_parent","rename_object","request_begin","delete_object","restore_object","content_load"};

        void InitializeLifecycle(string directory)
        {
            storageDirectory=directory??DefaultStorageDirectory();
            var record=generated.GetComponent<GeneratedObject>();
            if(record==null)record=AttachRecord(generated.gameObject,"root",null,null);
            // A runtime can be restarted after a script reload; retain existing tombstones and IDs.
            // Ownership is an explicit reference, never inferred from a user-controlled name.
            trash=record.TrashRoot;
            if(trash!=null && trash.parent!=generated)throw new InvalidOperationException("The configured trash container is no longer inside Generated.");
            if(trash==null){trash=new GameObject("__SeyraTrash").transform;trash.SetParent(generated,false);}
            record.TrashRoot=trash;
            trash.gameObject.SetActive(false);
            Reindex();
        }
        bool IsDeleted(GameObject go)=>trash!=null && (go.transform==trash || go.transform.IsChildOf(trash));
        void RequireCapacity(int additional)
        {
            if(generated.GetComponentsInChildren<Transform>(true).Length+additional>8192)throw new InvalidOperationException("Retained object limit reached, including trash. Save the content and restart Play Mode.");
        }
        GeneratedObject AttachRecord(GameObject go,string kind,string shape,string group,string persistentId=null)
        {
            var record=go.GetComponent<GeneratedObject>();
            if(record==null)record=go.AddComponent<GeneratedObject>();
            record.Initialize(persistentId??"go_"+Guid.NewGuid().ToString("N"),kind,shape,group);
            var filter=go.GetComponent<MeshFilter>();var renderer=go.GetComponent<Renderer>();
            record.OriginalMesh=filter!=null?filter.sharedMesh:null;
            record.OriginalMaterial=renderer!=null?renderer.sharedMaterial:null;
            record.OriginalComponents=ComponentConfiguration(go);
            Id(go);return record;
        }
        IEnumerable<Transform> LiveContent()
        {
            var stack=new Stack<Transform>();stack.Push(generated);
            while(stack.Count>0)
            {
                var t=stack.Pop();if(t==trash)continue;
                yield return t;
                for(int i=t.childCount-1;i>=0;i--)stack.Push(t.GetChild(i));
            }
        }
        void Reindex()
        {
            foreach(var key in objects.Where(p=>p.Value==null || (p.Value is GameObject g && g.GetComponent<GeneratedObject>()!=null)).Select(p=>p.Key).ToArray())objects.Remove(key);
            foreach(var t in LiveContent())Id(t.gameObject);
            var liveIds=new HashSet<string>(LiveContent().Select(t=>t.GetComponent<GeneratedObject>()).Where(r=>r!=null).Select(r=>r.EntityId));
            if(trash!=null)foreach(var record in trash.GetComponentsInChildren<GeneratedObject>(true))
                if(!liveIds.Contains(record.EntityId))objects[record.EntityId]=record.gameObject;
            if(activeRequest!=null && (!objects.TryGetValue(activeRequest,out var value)||value==null || !(value is GameObject group) || IsDeleted(group)))activeRequest=null;
        }

        sealed class ObjectState
        {
            public GameObject Object;
            public Transform Parent;
            public int Sibling,Layer;
            public string Name,Tag,EntityId,Kind,Shape,GroupId,Components;
            public Vector3 Position,Scale;
            public Quaternion Rotation;
            public bool Active;
            public static ObjectState Capture(GameObject go)
            {
                var t=go.transform;var record=go.GetComponent<GeneratedObject>();
                return new ObjectState{Object=go,Parent=t.parent,Sibling=t.GetSiblingIndex(),Name=go.name,Tag=go.tag,Layer=go.layer,
                    Position=t.localPosition,Rotation=t.localRotation,Scale=t.localScale,Active=go.activeSelf,
                    EntityId=record!=null?record.EntityId:null,Kind=record!=null?record.Kind:null,Shape=record!=null?record.PrimitiveType:null,GroupId=record!=null?record.RequestGroupId:null,
                    Components=ComponentConfiguration(go)};
            }
            public bool Same(ObjectState other)=>ReferenceEquals(Object,other.Object) && ReferenceEquals(Parent,other.Parent) && Sibling==other.Sibling && Name==other.Name && Tag==other.Tag && Layer==other.Layer && Active==other.Active &&
                Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale) && EntityId==other.EntityId && Kind==other.Kind && Shape==other.Shape && GroupId==other.GroupId && Components==other.Components;
            public void Validate()
            {
                if(Object==null || (!ReferenceEquals(Parent,null) && Parent==null))throw new InvalidOperationException("Cannot restore: an object or its parent was destroyed outside Seyra.");
            }
            public void Apply()
            {
                var t=Object.transform;
                if(t.parent!=Parent)t.SetParent(Parent,false);
                t.localPosition=Position;t.localRotation=Rotation;t.localScale=Scale;
                Object.name=Name;Object.layer=Layer;Object.tag=Tag;
                var record=Object.GetComponent<GeneratedObject>();
                if(record!=null)record.Initialize(EntityId,Kind,Shape,GroupId);
            }
        }
        sealed class Snapshot
        {
            public readonly Dictionary<GameObject,ObjectState> States=new Dictionary<GameObject,ObjectState>();
            public readonly HashSet<GameObject> Content=new HashSet<GameObject>();
        }
        sealed class HistoryEntry
        {
            public string Id,Tool,GroupId,EntityId;
            public Snapshot Before,After;
        }
        sealed class SceneRollbackException:InvalidOperationException
        {
            public SceneRollbackException():base("The operation failed and could not be fully rolled back. Outcome unknown: inspect the scene before continuing; do not retry the mutation automatically."){}
        }
        Snapshot Capture(IEnumerable<GameObject> externals=null)
        {
            var snapshot=new Snapshot();
            foreach(var t in LiveContent())snapshot.Content.Add(t.gameObject);
            if(snapshot.Content.Count>ContentLimit)throw new InvalidOperationException("Generated content exceeds the limit of 2048 objects.");
            foreach(var go in snapshot.Content.Concat(externals??externalTargets).Where(g=>g!=null).Distinct())
            {
                var state=ObjectState.Capture(go);
                if(lastCapture!=null && lastCapture.States.TryGetValue(go,out var previous) && previous.Same(state))state=previous;
                snapshot.States[go]=state;
            }
            lastCapture=snapshot;return snapshot;
        }
        void RequireCurrent(Snapshot expected)
        {
            foreach(var state in expected.States.Values)state.Validate();
            var current=Capture(expected.States.Keys.Where(g=>!expected.Content.Contains(g)));
            if(!expected.Content.SetEquals(current.Content) || expected.States.Any(p=>!current.States.TryGetValue(p.Key,out var state)||!p.Value.Same(state)))
                throw new InvalidOperationException("HISTORY_CONFLICT: content changed outside the recorded operation. Nothing was undone.");
        }
        void MoveToTrash(GameObject go)
        {
            if(go==generated.gameObject || go.transform==trash || !go.transform.IsChildOf(generated))throw new ArgumentException("Only descendants of Generated can be deleted; the root and trash container are protected.");
            deletedOrigins[go]=ObjectState.Capture(go);
            go.SetActive(false);go.transform.SetParent(trash,false);
        }
        void ApplySnapshot(Snapshot target)
        {
            foreach(var state in target.States.Values)state.Validate();
            var extras=LiveContent().Select(t=>t.gameObject).Where(g=>!target.Content.Contains(g)).ToHashSet();
            foreach(var go in extras.Where(g=>g.transform.parent==null||!extras.Contains(g.transform.parent.gameObject)).ToArray())MoveToTrash(go);
            // Topological restoration works even when an entire deleted hierarchy is in the trash.
            var pending=target.States.Values.ToList();var applied=new HashSet<GameObject>();
            while(pending.Count>0)
            {
                var ready=pending.Where(s=>s.Parent==null || !target.States.ContainsKey(s.Parent.gameObject) || applied.Contains(s.Parent.gameObject)).ToArray();
                if(ready.Length==0)throw new InvalidOperationException("History contains a cyclic hierarchy.");
                foreach(var state in ready){state.Apply();applied.Add(state.Object);pending.Remove(state);}
            }
            foreach(var group in target.States.Values.GroupBy(s=>s.Parent))foreach(var state in group.OrderBy(s=>s.Sibling))state.Object.transform.SetSiblingIndex(state.Sibling);
            foreach(var state in target.States.Values)state.Object.SetActive(state.Active);
            Reindex();lastCapture=target;
        }
        JObject ExecuteRecorded(string tool,JObject args)
        {
            if(!recordedTools.Contains(tool))return ExecuteLifecycle(tool,args);
            if(history.Count>=HistoryLimit)throw new InvalidOperationException("Limit of 128 recoverable operations reached. Save the content and restart the runtime, or undo operations.");
            externalTargets.Clear();
            if(tool=="set_transform")
            {
                var target=Find(args["entityId"]);
                if(target.transform!=generated && !target.transform.IsChildOf(generated))externalTargets.Add(target);
            }
            var before=Capture();
            try
            {
                var result=ExecuteLifecycle(tool,args);
                var after=Capture();
                var operationId="op_"+Guid.NewGuid().ToString("N");
                history.Add(new HistoryEntry{Id=operationId,Tool=tool,GroupId=activeRequest,EntityId=(string)result["entityId"],Before=before,After=after});
                result["operationId"]=operationId;result["operationRequestGroupId"]=activeRequest;
                return result;
            }
            catch {try{ApplySnapshot(before);}catch{throw new SceneRollbackException();}throw;}
        }
        JObject ExecuteLifecycle(string tool,JObject args)
        {
            switch(tool)
            {
                case "request_begin":
                    Keys(args,new[]{"name"});
                    if(activeRequest!=null)throw new InvalidOperationException("A request is already open. End it before starting another.");
                    RequireCapacity(1);
                    var groupId="go_"+Guid.NewGuid().ToString("N");
                    var container=InstantiateManaged(String(args["name"],256),generated,"request",null,groupId,Vector3.zero,Quaternion.identity,Vector3.one,groupId);
                    var record=container.GetComponent<GeneratedObject>();
                    activeRequest=record.EntityId;return Inspect(container);
                case "request_end":
                    Keys(args,new[]{"requestGroupId"});
                    if(Text(args,"requestGroupId")!=activeRequest)throw new ArgumentException("The request is not the currently open request.");
                    var ended=activeRequest;activeRequest=null;return new JObject{["requestGroupId"]=ended,["closed"]=true};
                case "request_list":
                    Keys(args,Array.Empty<string>());
                    return new JObject{["activeRequestGroupId"]=activeRequest,["requests"]=new JArray(LiveContent().Select(t=>t.GetComponent<GeneratedObject>()).Where(r=>r!=null&&r.Kind=="request").Select(r=>new JObject{["requestGroupId"]=r.EntityId,["name"]=r.name,["open"]=r.EntityId==activeRequest}))};
                case "delete_object":
                    Keys(args,new[]{"entityId"});var deleted=Find(args["entityId"]);
                    if(Id(deleted)==activeRequest)throw new InvalidOperationException("End the request before deleting its container.");
                    MoveToTrash(deleted);return Inspect(deleted);
                case "restore_object":
                    Keys(args,new[]{"entityId"});var restored=Find(args["entityId"],true);
                    if(!IsDeleted(restored)||!deletedOrigins.TryGetValue(restored,out var origin))throw new ArgumentException("This object has no recoverable deletion in this runtime.");
                    origin.Validate();
                    if(origin.Parent==null||IsDeleted(origin.Parent.gameObject))throw new InvalidOperationException("Restore the original parent first.");
                    origin.Apply();restored.transform.SetSiblingIndex(origin.Sibling);restored.SetActive(origin.Active);Reindex();return Inspect(restored);
                case "history_list":
                    Keys(args,Array.Empty<string>(),new[]{"offset","limit"});
                    int offset=Int(args,"offset",0,0,1000000),limit=Int(args,"limit",20,1,100);
                    return new JObject{["total"]=history.Count,["nextOffset"]=offset+limit<history.Count?new JValue(offset+limit):JValue.CreateNull(),["operations"]=new JArray(history.AsEnumerable().Reverse().Skip(offset).Take(limit).Select(h=>new JObject{["operationId"]=h.Id,["tool"]=h.Tool,["entityId"]=h.EntityId,["requestGroupId"]=h.GroupId}))};
                case "undo":return Undo(args);
                case "content_save":return SaveContent(args);
                case "content_load":return LoadContent(args);
                case "content_list":return ListContent(args);
                default:return Execute(tool,args);
            }
        }
        JObject Undo(JObject args)
        {
            Keys(args,new[]{"scope"},new[]{"expectedOperationId"});
            if(history.Count==0)throw new InvalidOperationException("There are no operations to undo.");
            var latest=history[history.Count-1];
            if(args["expectedOperationId"]!=null && Text(args,"expectedOperationId")!=latest.Id)throw new InvalidOperationException("HISTORY_CONFLICT: the latest operation does not match.");
            var scope=Text(args,"scope");int start=history.Count-1;
            if(scope=="request")
            {
                if(latest.GroupId==null)throw new InvalidOperationException("The latest operation does not belong to a request.");
                while(start>0 && history[start-1].GroupId==latest.GroupId)start--;
            }
            else if(scope!="operation")throw new ArgumentException("scope must be operation or request.");
            var target=new Snapshot();var expected=new Snapshot();
            target.Content.UnionWith(history[start].Before.Content);expected.Content.UnionWith(latest.After.Content);
            foreach(var pair in history[start].Before.States)target.States[pair.Key]=pair.Value;
            foreach(var pair in latest.After.States)expected.States[pair.Key]=pair.Value;
            // External Transform targets may first appear midway through a request.
            // Restore their first before-state and validate their last after-state, without tracking unrelated objects.
            foreach(var entry in history.Skip(start))
            {
                foreach(var pair in entry.Before.States.Where(p=>!entry.Before.Content.Contains(p.Key)))if(!target.States.ContainsKey(pair.Key))target.States[pair.Key]=pair.Value;
                foreach(var pair in entry.After.States.Where(p=>!entry.After.Content.Contains(p.Key)))expected.States[pair.Key]=pair.Value;
            }
            RequireCurrent(expected);
            var current=Capture(expected.States.Keys.Where(g=>!expected.Content.Contains(g)));
            try{ApplySnapshot(target);}catch{try{ApplySnapshot(current);}catch{throw new SceneRollbackException();}throw;}
            var undone=history.Skip(start).Select(h=>h.Id).ToArray();history.RemoveRange(start,history.Count-start);
            return new JObject{["undoneOperationIds"]=new JArray(undone),["requestGroupId"]=latest.GroupId,["remainingOperations"]=history.Count};
        }
    }
}
