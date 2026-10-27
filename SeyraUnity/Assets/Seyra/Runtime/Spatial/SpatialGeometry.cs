using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seyra.Spatial
{
    public enum RoomState { AirOnly, WaitingForPermission, Loading, Ready, PermissionDenied, Unavailable, Suspended, Error }
    public sealed class SpatialBox
    {
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public Quaternion Rotation { get; }
        public SpatialBox(Vector3 center, Vector3 size, Quaternion rotation) { Center=center; Size=size; Rotation=rotation; }
        public Vector3[] Corners => SpatialGeometry.Corners(new Bounds(Vector3.zero,Size), Matrix4x4.TRS(Center,Rotation,Vector3.one));
    }
    public sealed class PhysicalSurface
    {
        public string PhysicalId { get; }
        public string Kind { get; }
        public IReadOnlyList<string> Labels { get; }
        public Pose Pose { get; }
        public IReadOnlyList<Vector2> Polygon { get; }
        public string SupportId { get; }
        public PhysicalSurface(string id,string kind,IEnumerable<string> labels,Pose pose,IEnumerable<Vector2> polygon,string supportId=null)
        { PhysicalId=id;Kind=kind;Labels=Array.AsReadOnly(labels.ToArray());Pose=pose;Polygon=Array.AsReadOnly(polygon.ToArray());SupportId=supportId; }
        public Vector3[] Corners => Polygon.Select(p=>Pose.position+Pose.rotation*new Vector3(p.x,p.y,0)).ToArray();
    }
    public sealed class PhysicalObject
    {
        public string PhysicalId { get; }
        public IReadOnlyList<string> Labels { get; }
        public SpatialBox Box { get; }
        public Vector3? Facing { get; }
        public PhysicalObject(string id,IEnumerable<string> labels,SpatialBox box,Vector3? facing)
        { PhysicalId=id;Labels=Array.AsReadOnly(labels.ToArray());Box=box;Facing=facing; }
    }
    public sealed class RoomSnapshot
    {
        public long Revision { get; }
        public string RoomId { get; }
        public bool Simulated { get; }
        public RoomState State { get; }
        public IReadOnlyList<PhysicalSurface> Surfaces { get; }
        public IReadOnlyList<PhysicalObject> Objects { get; }
        public RoomSnapshot(long revision,string roomId,bool simulated,RoomState state,IEnumerable<PhysicalSurface> surfaces=null,IEnumerable<PhysicalObject> objects=null)
        { Revision=revision;RoomId=roomId;Simulated=simulated;State=state;
          Surfaces=Array.AsReadOnly((surfaces??Array.Empty<PhysicalSurface>()).ToArray());Objects=Array.AsReadOnly((objects??Array.Empty<PhysicalObject>()).ToArray()); }
    }
    public static class SpatialGeometry
    {
        public static Vector3[] Corners(Bounds bounds,Matrix4x4 matrix)
        {
            var result=new Vector3[8];
            for(int i=0;i<8;i++)result[i]=matrix.MultiplyPoint3x4(new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z));
            return result;
        }
        public static Bounds Project(IEnumerable<Vector3> points,Quaternion frame)
        {
            var inverse=Quaternion.Inverse(frame);bool found=false;var result=new Bounds();
            foreach(var point in points)
            {
                var p=inverse*point;
                if(!Finite(p))throw new ArgumentException("Geometry contains non-finite coordinates.");
                if(!found){result=new Bounds(p,Vector3.zero);found=true;}else result.Encapsulate(p);
            }
            if(!found||!Finite(result.size))throw new ArgumentException("Geometry is empty or exceeds the float range.");
            return result;
        }
        public static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        public static Quaternion Yaw(Vector3 forward)
        {
            forward.y=0;
            if(!Finite(forward)||forward.sqrMagnitude<0.000001f)throw new ArgumentException("Horizontal facing direction is undefined.");
            return Quaternion.LookRotation(forward.normalized,Vector3.up);
        }
    }
}
