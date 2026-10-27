using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        // Structural visual envelope: includes inactive/disabled geometry, excludes trash.
        // Transform all corners with the full matrix; lossyScale cannot encode hierarchy shear.
        Bounds WorldBounds(GameObject root)
        {
            bool found=false;
            var combined=new Bounds();
            void Include(Vector3 point)
            {
                if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z))
                    throw new ArgumentException("Bounds contain non-finite world coordinates.");
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
            return combined;
        }
        static JObject BoundsJson(Bounds bounds)=>new JObject{
            ["space"]="world",["kind"]="aabb",["center"]=Vector(bounds.center),
            ["size"]=Vector(bounds.size),["min"]=Vector(bounds.min),["max"]=Vector(bounds.max)};

        JObject GetBounds(JObject args)
        {
            Keys(args,new[]{"entityId"});
            var go=Find(args["entityId"]);
            return new JObject{["entityId"]=Id(go),["runtimeId"]=RuntimeId,["bounds"]=BoundsJson(WorldBounds(go))};
        }

        JObject PlaceRelative(JObject args)
        {
            Keys(args,new[]{"entityId","referenceId","direction","gap"});
            var moving=Find(args["entityId"]);var reference=Find(args["referenceId"]);
            var t=moving.transform;var r=reference.transform;
            if(t==generated || !t.IsChildOf(generated) || moving.GetComponent<GeneratedObject>()==null)
                throw new ArgumentException("Only managed descendants of Generated can be placed.");
            if(t==r || t.IsChildOf(r) || r.IsChildOf(t))
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
            var before=WorldBounds(moving);var target=WorldBounds(reference);
            var offset=target.center-before.center;
            offset[axis]=positive?target.max[axis]+(float)gapValue-before.min[axis]:target.min[axis]-(float)gapValue-before.max[axis];
            var oldPosition=t.localPosition;
            try
            {
                t.position+=offset;Check(t);
                var after=WorldBounds(moving);
                // Reject precision loss or driven UI that cannot represent the requested placement.
                var actualGap=positive?after.min[axis]-target.max[axis]:target.min[axis]-after.max[axis];
                if(Math.Abs(actualGap-gapValue)>0.0001 ||
                    Math.Abs(after.center[(axis+1)%3]-target.center[(axis+1)%3])>0.0001 ||
                    Math.Abs(after.center[(axis+2)%3]-target.center[(axis+2)%3])>0.0001)
                    throw new ArgumentException("Placement cannot be represented within 0.0001 world units.");
                var result=Inspect(moving);result["bounds"]=BoundsJson(after);
                result["referenceId"]=Id(reference);result["referenceBounds"]=BoundsJson(target);
                result["direction"]=Text(args,"direction");result["gap"]=gapValue;
                return result;
            }
            catch{t.localPosition=oldPosition;throw;}
        }
    }
}
