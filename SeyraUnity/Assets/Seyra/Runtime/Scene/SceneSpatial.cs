using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        void ProtectSpatialApplication(string tool,JObject args)
        {
            if(queries.Contains(tool))return;
            foreach(var key in new[]{"entityId","parentId"})
            {
                if(args[key]?.Type!=JTokenType.String)continue;
                if(objects.TryGetValue((string)args[key],out var value)&&value is GameObject go&&go!=null&&
                    (go.GetComponentInParent<SpatialFollowController>(true)!=null||go.GetComponentInChildren<SpatialFollowController>(true)!=null))
                    throw new ArgumentException("Application windows are controlled by native interaction and spatial following, not scene mutations.");
            }
        }
        SpatialPlacementService PlacementService=>SpatialPlacementService.ForScene(generated.gameObject.scene);
        JObject SpatialState(GameObject go)
        {
            var service=UnityEngine.Object.FindObjectsByType<SpatialPlacementService>(FindObjectsSortMode.None).FirstOrDefault(s=>s.gameObject.scene==go.scene);
            var placement=service?.Placement(go.transform);if(placement==null)return null;
            bool needs=service.NeedsPlacement(go.transform);
            return new JObject{["surface"]=placement.Destination,["physicalId"]=placement.PhysicalId,["needsPlacement"]=needs,
                ["lastValidPose"]=new JObject{["position"]=Vector(placement.Pose.position),["rotation"]=Vector(placement.Pose.rotation.eulerAngles)}};
        }
        JObject SpatialPlace(JObject args,bool apply)
        {
            Keys(args,new[]{"entityId","destination"},apply?new[]{"physicalId","allowAirFallback","minScale","expectedRoomRevision"}:new[]{"physicalId","allowAirFallback","minScale"});
            var go=Find(args["entityId"]);var root=go.transform;RequirePanelRoot(go);
            if(root==generated||!root.IsChildOf(generated)||go.GetComponent<GeneratedObject>()==null)throw new ArgumentException("Spatial placement requires a managed descendant of Generated.");
            if(root.GetComponentsInChildren<Seyra.UI.Library.UIMetaGrabFrame>(true).Any(f=>f.IsGrabbed))throw new RoomToolException("OBJECT_MANIPULATED","Release the content before placing it.");
            for(var parent=root.parent;parent!=null;parent=parent.parent)
            {var scale=parent.localScale;if(scale.x<=0||Mathf.Abs(scale.x-scale.y)>.00001f||Mathf.Abs(scale.x-scale.z)>.00001f)throw new ArgumentException("Spatial placement requires positive uniform ancestor scales.");}
            if(args["minScale"]!=null&&args["minScale"].Type!=JTokenType.Integer&&args["minScale"].Type!=JTokenType.Float)throw new ArgumentException("minScale must be numeric.");
            var min=args["minScale"]==null?1:(float)(double)args["minScale"];
            var bounds=SpatialGeometry.Project(GeometryPoints(go).Select(p=>Quaternion.Inverse(root.rotation)*(p-root.position)),Quaternion.identity);
            var request=new SpatialPlacementRequest{Content=bounds,Destination=Text(args,"destination"),PhysicalId=args["physicalId"]==null?null:String(args["physicalId"],128),
                AllowAirFallback=Bool(args,"allowAirFallback",true),MinScale=min,Readable=go.GetComponentInChildren<ManagedPanel>(true)!=null};
            var service=PlacementService;var room=service.Snapshot;
            if(args["expectedRoomRevision"]!=null&&(args["expectedRoomRevision"].Type!=JTokenType.Integer||(long)args["expectedRoomRevision"]<0))throw new ArgumentException("expectedRoomRevision must be a non-negative integer.");
            if(args["expectedRoomRevision"]!=null&&(long)args["expectedRoomRevision"]!=room.Revision)throw new RoomToolException("ROOM_REVISION_CONFLICT","The room changed. Query candidates again.");
            if(room.State==RoomState.Loading||room.State==RoomState.Suspended||room.State==RoomState.WaitingForPermission)throw new RoomToolException("ROOM_UNAVAILABLE","Room loading or tracking is suspended.");
            request.PreviousSupport=service.Placement(root)?.PhysicalId;
            Pose view;
            try{view=service.View(activeRequest,currentYaw:activeRequest==null);}catch(InvalidOperationException error){throw new RoomToolException("FRAME_UNDEFINED",error.Message);}
            var plan=service.Plan(request,view,root);bool applied=false;
            if(apply&&plan.Best!=null)
            {
                var currentView=service.View(activeRequest,currentYaw:activeRequest==null);
                if(service.Snapshot.Revision!=room.Revision||Vector3.Distance(currentView.position,view.position)>.0001f||Quaternion.Angle(currentView.rotation,view.rotation)>.001f)
                    throw new RoomToolException("ROOM_REVISION_CONFLICT","Room or viewer changed before commit.");
                var reason=SpatialPlacementPlanner.ValidatePose(service.Snapshot,request,plan.Best,currentView,service.Reservations(root));
                if(reason!=null){plan.Reason=reason;plan.Best=null;}
                else
                {
                    root.SetPositionAndRotation(plan.Best.Pose.position,plan.Best.Pose.rotation);root.localScale*=plan.Best.Scale;Check(root);
                    var inverse=Quaternion.Inverse(plan.Best.Box.Rotation);var half=plan.Best.Box.Size*.5f;
                    // A rotated group's root envelope can be wider than its actual world AABB.
                    // Verify containment of real geometry, rather than requiring those two boxes to match.
                    if(Vector3.Distance(root.position,plan.Best.Pose.position)>.0001f||Quaternion.Angle(root.rotation,plan.Best.Pose.rotation)>.001f||
                        GeometryPoints(go).Any(p=>{var point=inverse*(p-plan.Best.Box.Center);return Mathf.Abs(point.x)>half.x+.0001f||Mathf.Abs(point.y)>half.y+.0001f||Mathf.Abs(point.z)>half.z+.0001f;}))
                        throw new ArgumentException("Placement cannot be represented by the current hierarchy.");
                    service.Register(root,plan.Best);applied=true;
                }
            }
            var best=plan.Best;
            return new JObject{["entityId"]=Id(go),["applied"]=applied,["code"]=best==null?"PLACEMENT_REJECTED":apply?"PLACED":"CANDIDATE",["reason"]=plan.Reason,
                ["destination"]=best?.Destination,["physicalId"]=best?.PhysicalId,["roomRevision"]=plan.RoomRevision,["simulated"]=plan.Simulated,["geometryKnownOnly"]=true,["physicalValidation"]=plan.PhysicalValidation,
                ["pose"]=best==null?null:new JObject{["position"]=Vector(best.Pose.position),["rotation"]=Vector(best.Pose.rotation.eulerAngles)},["scale"]=best?.Scale,
                ["score"]=best?.Score,["distance"]=best?.Distance,["yaw"]=best?.Yaw,["elevation"]=best?.Elevation,["angularHeight"]=best?.AngularHeight,
                ["rejected"]=new JArray(plan.Rejected.Select(r=>new JObject{["physicalId"]=r.support,["reason"]=r.reason}))};
        }
    }
}
