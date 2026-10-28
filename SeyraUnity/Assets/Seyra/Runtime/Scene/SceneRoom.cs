using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed class RoomToolException : InvalidOperationException
    {
        public string Code { get; }
        public RoomToolException(string code,string message):base(message){Code=code;}
    }
    public sealed partial class SceneToolService
    {
        RoomContext roomContext;
        RoomContext Room
        {
            get
            {
                if(roomContext==null)roomContext=UnityEngine.Object.FindObjectsByType<RoomContext>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.scene==generated.gameObject.scene);
                return roomContext;
            }
        }
        RoomSnapshot RoomData=>Room?.Snapshot??new RoomSnapshot(0,null,false,RoomState.AirOnly);
        static bool PhysicalId(string id)=>id!=null&&id.StartsWith("physical:",StringComparison.Ordinal);
        static bool PhysicalNode(GameObject go)=>go.GetComponentInParent<MRUKAnchor>(true)!=null||go.GetComponentInParent<MRUKRoom>(true)!=null||go.GetComponentInParent<MRUK>(true)!=null;
        void ProtectRoomMutation(string tool,JObject args)
        {
            if(queries.Contains(tool))return;
            foreach(var key in new[]{"entityId","parentId"})
            {
                if(args[key]?.Type!=JTokenType.String)continue;
                var id=(string)args[key];
                if(PhysicalId(id))throw new RoomToolException("ROOM_READ_ONLY","Physical references cannot be mutated or used as parents.");
                if(objects.TryGetValue(id,out var value)&&value is GameObject go&&go!=null &&
                    (PhysicalNode(go)||go.GetComponentInChildren<MRUKRoom>(true)!=null||go.GetComponentInChildren<MRUK>(true)!=null))
                    throw new RoomToolException("ROOM_READ_ONLY","Captured room nodes and their ancestors are read-only.");
            }
        }
        (Vector3[] points, Vector3? facing) ResolvePhysical(string id)
        {
            var snapshot=RoomData;
            if(snapshot.State!=RoomState.Ready)throw new RoomToolException("ROOM_UNAVAILABLE","The captured room is not ready. Query room_get.");
            var item=snapshot.Objects.FirstOrDefault(o=>o.PhysicalId==id);
            if(item!=null)return(item.Box.Corners,item.Facing);
            var surface=snapshot.Surfaces.FirstOrDefault(s=>s.PhysicalId==id);
            if(surface==null)throw new RoomToolException("ROOM_REFERENCE_STALE","Unknown physical reference in the current room. Query room_objects again.");
            Vector3? forward=null;
            if(surface.Kind=="wall")
            {
                forward=WallFacing(surface);
            }
            return(surface.Corners,forward);
        }
        Vector3 WallFacing(PhysicalSurface surface)
        {
            var normal=surface.Pose.rotation*Vector3.forward;
            if(Room?.ViewerReady==true&&Vector3.Dot(normal,Room.Viewer.position-surface.Pose.position)<0)normal=-normal;
            return normal;
        }
        Quaternion ResolveFrame(string name,Vector3? referenceFacing)
        {
            if(name=="world")return Quaternion.identity;
            if(name=="viewer")
            {
                if(Room?.ViewerReady!=true)throw new RoomToolException("FRAME_UNDEFINED","A tracked viewer is required for the viewer frame.");
                referenceFacing=Room.Viewer.forward;
            }
            else if(name!="reference")throw new ArgumentException("frame must be world, viewer or reference.");
            if(!referenceFacing.HasValue)throw new RoomToolException("FRAME_UNDEFINED","This reference has no defined front. Use frame=viewer.");
            try{return SpatialGeometry.Yaw(referenceFacing.Value);}
            catch(ArgumentException){throw new RoomToolException("FRAME_UNDEFINED","Horizontal facing is undefined. Use a reference with a known front.");}
        }
        JObject RoomInfo(JObject args)
        {
            Keys(args,Array.Empty<string>());var snapshot=RoomData;
            return new JObject{["runtimeId"]=RuntimeId,["roomRevision"]=snapshot.Revision,["roomId"]=snapshot.RoomId,["state"]=snapshot.State.ToString(),
                ["simulated"]=snapshot.Simulated,["viewerReady"]=Room?.ViewerReady==true,["geometryKnownOnly"]=true,
                ["surfaceCount"]=snapshot.Surfaces.Count,["objectCount"]=snapshot.Objects.Count,["reason"]=Room?.Diagnostic??"Room support is not configured."};
        }
        JObject RoomObjects(JObject args)
        {
            Keys(args,Array.Empty<string>(),new[]{"offset","limit","kind","label"});
            int offset=Int(args,"offset",0,0,1000000),limit=Int(args,"limit",32,1,64);
            string kind=args["kind"]==null?null:Text(args,"kind"),label=args["label"]==null?null:String(args["label"],128);
            if(kind!=null&&!new[]{"wall","floor","ceiling","opening","tabletop","object"}.Contains(kind))throw new ArgumentException("Unknown physical reference kind.");
            var snapshot=RoomData;var items=new List<JObject>();
            void Add(string id,string entryKind,IReadOnlyList<string> labels,Vector3[] points,Vector3? facing,PhysicalSurface surface=null)
            {
                if(kind!=null&&kind!=entryKind||label!=null&&!labels.Any(l=>l.Equals(label,StringComparison.OrdinalIgnoreCase)))return;
                var bounds=SpatialGeometry.Project(points,Quaternion.identity);
                var node=new JObject{["physicalId"]=id,["kind"]=entryKind,["labels"]=new JArray(labels),["readOnly"]=true,["bounds"]=BoundsJson(bounds)};
                if(facing.HasValue)node["facing"]=Vector(facing.Value);
                if(surface!=null){node["supportId"]=surface.SupportId;node["pose"]=new JObject{["position"]=Vector(surface.Pose.position),["rotation"]=Vector(surface.Pose.rotation.eulerAngles)};node["polygon"]=new JArray(surface.Polygon.Select(p=>new JObject{["x"]=p.x,["y"]=p.y}));}
                else
                {
                    var item=snapshot.Objects.First(o=>o.PhysicalId==id);
                    node["box"]=new JObject{["center"]=Vector(item.Box.Center),["size"]=Vector(item.Box.Size),["rotation"]=Vector(item.Box.Rotation.eulerAngles)};
                }
                if(surface!=null&&(entryKind=="wall"||entryKind=="opening"))
                {
                    node["width"]=surface.Polygon.Max(p=>p.x)-surface.Polygon.Min(p=>p.x);
                    node["height"]=surface.Polygon.Max(p=>p.y)-surface.Polygon.Min(p=>p.y);
                }
                if(Room?.ViewerReady==true)
                {
                    try{var delta=Quaternion.Inverse(SpatialGeometry.Yaw(Room.Viewer.forward))*(bounds.center-Room.Viewer.position);
                        node["viewerRelative"]=new JObject{["position"]=Vector(delta),["distance"]=delta.magnitude,["bearing"]=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,["side"]=Mathf.Abs(delta.x)<.1f?"center":delta.x>0?"right":"left",["depth"]=delta.z>=0?"front":"behind"};}
                    catch(ArgumentException){node["viewerRelative"]=null;}
                }
                items.Add(node);
            }
            foreach(var surface in snapshot.Surfaces)Add(surface.PhysicalId,surface.Kind,surface.Labels,surface.Corners,surface.Kind=="wall"?WallFacing(surface):(Vector3?)null,surface);
            foreach(var item in snapshot.Objects)Add(item.PhysicalId,"object",item.Labels,item.Box.Corners,item.Facing);
            items=items.OrderBy(i=>Room?.ViewerReady==true?Vector3.Distance(ReadVector(i["bounds"]["center"]),Room.Viewer.position):0f).ThenBy(i=>(string)i["physicalId"],StringComparer.Ordinal).ThenBy(i=>(string)i["kind"],StringComparer.Ordinal).ToList();
            return new JObject{["runtimeId"]=RuntimeId,["roomRevision"]=snapshot.Revision,["state"]=snapshot.State.ToString(),["simulated"]=snapshot.Simulated,["geometryKnownOnly"]=true,
                ["objects"]=new JArray(items.Skip(offset).Take(limit)),["total"]=items.Count,["nextOffset"]=offset+limit<items.Count?new JValue(offset+limit):JValue.CreateNull()};
        }
    }
}
