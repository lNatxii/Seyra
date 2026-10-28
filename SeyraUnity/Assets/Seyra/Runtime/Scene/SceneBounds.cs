using System;
using System.Collections.Generic;
using Seyra.Spatial;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        // Structural visual envelope: includes inactive/disabled geometry, excludes trash.
        // Transform all corners with the full matrix; lossyScale cannot encode hierarchy shear.
        Bounds WorldBounds(GameObject root)=>SpatialGeometry.Project(GeometryPoints(root),Quaternion.identity);
        List<Vector3> GeometryPoints(GameObject root)
        {
            var points=new List<Vector3>();
            bool found=false;
            var combined=new Bounds();
            void Include(Vector3 point)
            {
                if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z))
                    throw new ArgumentException("Bounds contain non-finite world coordinates.");
                points.Add(point);
                if(!found){combined=new Bounds(point,Vector3.zero);found=true;}
                else combined.Encapsulate(point);
            }
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(IsDeleted(t.gameObject))continue;
                var panel=t.GetComponentInParent<ManagedPanel>(true);
                if(panel!=null)
                {
                    if(t!=panel.transform)continue;
                    foreach(var corner in panel.EnvelopeCorners())Include(corner);
                    continue;
                }
                var canvas=t.GetComponentInParent<Canvas>(true);
                if(t is RectTransform rect)
                {
                    if(canvas==null || canvas.rootCanvas.renderMode!=RenderMode.WorldSpace)
                        throw new ArgumentException("RectTransform bounds require a world-space root Canvas.");
                    var corners=new Vector3[4];rect.GetWorldCorners(corners);
                    foreach(var corner in corners)Include(corner);
                }
                foreach(var renderer in t.GetComponents<Renderer>())
                {
                    if(!(renderer is MeshRenderer))throw new ArgumentException("Only static MeshRenderer geometry and world-space RectTransforms are supported.");
                    var filter=t.GetComponent<MeshFilter>();
                    if(filter==null || filter.sharedMesh==null)throw new ArgumentException("MeshRenderer has no mesh.");
                    var local=filter.sharedMesh.bounds;
                    for(int i=0;i<8;i++)Include(t.localToWorldMatrix.MultiplyPoint3x4(new Vector3(
                        (i&1)==0?local.min.x:local.max.x,
                        (i&2)==0?local.min.y:local.max.y,
                        (i&4)==0?local.min.z:local.max.z)));
                }
            }
            if(!found)throw new ArgumentException("The subtree has no supported visual geometry; bounds are unavailable.");
            if(!Finite(combined.size.x)||!Finite(combined.size.y)||!Finite(combined.size.z))throw new ArgumentException("Bounds size exceeds the float range.");
            return points;
        }
        static JObject BoundsJson(Bounds bounds)=>new JObject{
            ["space"]="world",["kind"]="aabb",["center"]=Vector(bounds.center),
            ["size"]=Vector(bounds.size),["min"]=Vector(bounds.min),["max"]=Vector(bounds.max)};

        static JObject FrameBoundsJson(Bounds bounds,string frame,Quaternion rotation)
        {
            var result=BoundsJson(bounds);
            if(frame!="world"){result["space"]=frame;result["kind"]="frame_aligned_box";result["frameRotation"]=Vector(rotation.eulerAngles);}
            return result;
        }
        JObject GetBounds(JObject args)
        {
            Keys(args,new[]{"entityId"},new[]{"frame"});
            var id=Text(args,"entityId");
            Vector3[] points;Vector3? facing;
            if(PhysicalId(id)){var physical=ResolvePhysical(id);points=physical.points;facing=physical.facing;}
            else {var go=Find(args["entityId"]);id=Id(go);points=GeometryPoints(go).ToArray();facing=go.transform.forward;}
            string frame=args["frame"]==null?"world":Text(args,"frame");var rotation=ResolveFrame(frame,facing);
            var result=new JObject{["entityId"]=id,["runtimeId"]=RuntimeId,["bounds"]=FrameBoundsJson(SpatialGeometry.Project(points,rotation),frame,rotation)};
            if(PhysicalId(id)){result["roomRevision"]=RoomData.Revision;result["simulated"]=RoomData.Simulated;result["geometryKnownOnly"]=true;}
            return result;
        }

        JObject PlaceRelative(JObject args)
        {
            Keys(args,new[]{"entityId","referenceId","direction","gap"},new[]{"frame","orient"});
            var moving=Find(args["entityId"]);var referenceId=Text(args,"referenceId");
            var reference=PhysicalId(referenceId)?null:Find(args["referenceId"]);
            var t=moving.transform;var r=reference!=null?reference.transform:null;
            if(t==generated || !t.IsChildOf(generated) || moving.GetComponent<GeneratedObject>()==null)
                throw new ArgumentException("Only managed descendants of Generated can be placed.");
            if(r!=null && (t==r || t.IsChildOf(r) || r.IsChildOf(t)))
                throw new ArgumentException("Moving and reference subtrees must be disjoint and unrelated.");
            if(args["gap"].Type!=JTokenType.Integer && args["gap"].Type!=JTokenType.Float)throw new ArgumentException("gap must be numeric.");
            var gapValue=(double)args["gap"];
            if(double.IsNaN(gapValue)||double.IsInfinity(gapValue)||gapValue<0 || gapValue>float.MaxValue)throw new ArgumentException("gap must be finite, non-negative and representable as float.");
            int axis;bool positive;
            switch(Text(args,"direction"))
            {
                case "above":axis=1;positive=true;break;
                case "below":axis=1;positive=false;break;
                case "right":axis=0;positive=true;break;
                case "left":axis=0;positive=false;break;
                case "front":axis=2;positive=true;break;
                case "behind":axis=2;positive=false;break;
                default:throw new ArgumentException("Unknown placement direction.");
            }
            Invertible(t.parent);
            Vector3[] referencePoints;Vector3? referenceFacing;
            if(reference==null){var physical=ResolvePhysical(referenceId);referencePoints=physical.points;referenceFacing=physical.facing;}
            else{referencePoints=GeometryPoints(reference).ToArray();referenceFacing=r.forward;referenceId=Id(reference);}
            string frame=args["frame"]==null?"world":Text(args,"frame"),orient=args["orient"]==null?"keep":Text(args,"orient");
            if(orient!="keep"&&orient!="face_viewer"&&orient!="match_reference")throw new ArgumentException("Unknown orientation.");
            DrivenTransform.Require(t,DrivenChannels.Position|(orient!="keep"?DrivenChannels.Rotation:DrivenChannels.None));
            var frameRotation=ResolveFrame(frame,referenceFacing);
            var oldPosition=t.localPosition;var oldRotation=t.localRotation;
            try
            {
                if(orient!="keep")
                {
                    for(var parent=t.parent;parent!=null;parent=parent.parent)
                    {
                        var scale=parent.localScale;
                        if(scale.x<=0||Math.Abs(scale.x-scale.y)>.00001f||Math.Abs(scale.x-scale.z)>.00001f)
                            throw new ArgumentException("Orientation requires positive uniform ancestor scales.");
                    }
                    if(orient=="face_viewer")
                    {
                        if(Room?.ViewerReady!=true)throw new RoomToolException("FRAME_UNDEFINED","A tracked viewer is required to orient content.");
                        t.rotation=SpatialGeometry.Yaw(t.position-Room.Viewer.position);
                    }
                    else
                    {
                        // UIXRWindow's readable front faces local -Z.
                        var facing=ResolveFrame("reference",referenceFacing)*Vector3.forward;
                        t.rotation=SpatialGeometry.Yaw(moving.GetComponent<ManagedPanel>()!=null?-facing:facing);
                    }
                }
                var before=SpatialGeometry.Project(GeometryPoints(moving),frameRotation);
                var target=SpatialGeometry.Project(referencePoints,frameRotation);
                t.position+=RelativeOffset(before,target,axis,positive,(float)gapValue,frameRotation);Check(t);
                var after=SpatialGeometry.Project(GeometryPoints(moving),frameRotation);
                // Reject precision loss or driven UI that cannot represent the requested placement.
                var actualGap=positive?after.min[axis]-target.max[axis]:target.min[axis]-after.max[axis];
                if(Math.Abs(actualGap-gapValue)>0.0001 ||
                    Math.Abs(after.center[(axis+1)%3]-target.center[(axis+1)%3])>0.0001 ||
                    Math.Abs(after.center[(axis+2)%3]-target.center[(axis+2)%3])>0.0001)
                    throw new ArgumentException("Placement cannot be represented within 0.0001 world units.");
                var result=Inspect(moving);result["bounds"]=FrameBoundsJson(after,frame,frameRotation);
                result["referenceId"]=referenceId;result["referenceBounds"]=FrameBoundsJson(target,frame,frameRotation);
                if(reference==null){result["roomRevision"]=RoomData.Revision;result["simulated"]=RoomData.Simulated;result["geometryKnownOnly"]=true;}
                result["direction"]=Text(args,"direction");result["gap"]=gapValue;
                return result;
            }
            catch{t.localPosition=oldPosition;t.localRotation=oldRotation;throw;}
        }
        internal static Vector3 RelativeOffset(Bounds moving,Bounds target,int axis,bool positive,float gap,Quaternion frame)
        {
            var offset=target.center-moving.center;
            offset[axis]=positive?target.max[axis]+gap-moving.min[axis]:target.min[axis]-gap-moving.max[axis];
            return frame*offset;
        }
    }
}
