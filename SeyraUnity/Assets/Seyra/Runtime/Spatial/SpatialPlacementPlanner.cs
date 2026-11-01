using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seyra.Spatial
{
    public readonly struct ReadingTarget
    {
        public readonly float Height,MinAngle;
        public ReadingTarget(float height,float minAngle){Height=height;MinAngle=minAngle;}
    }
    public sealed class SpatialPlacementRequest
    {
        public Bounds Content;
        public string Destination="air",PhysicalId,PreviousSupport;
        public bool AllowAirFallback=true,Readable;
        public bool RelaxViewerComfort;
        public float MinScale=1;
        public IReadOnlyList<ReadingTarget> ReadingTargets;
    }
    public sealed class SpatialCandidate
    {
        public string Destination,PhysicalId;
        public bool Relaxed;
        public string WaivedReason;
        public Pose Pose;
        public SpatialBox Box;
        public float Scale=1,Score,Distance,Yaw,Elevation,AngularHeight;
    }
    public sealed class SupportSummary
    {
        public string PhysicalId;
        public int Tried;
        public readonly Dictionary<string,int> Reasons=new Dictionary<string,int>();
    }
    public sealed class SpatialPlan
    {
        public SpatialCandidate Best;
        public string Reason;
        public long RoomRevision;
        public bool Simulated,PhysicalValidation;
        public readonly List<(string support,string reason)> Rejected=new List<(string,string)>();
        public readonly List<SupportSummary> Supports=new List<SupportSummary>();
    }
    public static class SpatialPlacementPlanner
    {
        public static SpatialBox BoxAt(SpatialPlacementRequest request,Pose pose,float scale=1)=>new SpatialBox(pose.position+pose.rotation*(request.Content.center*scale),request.Content.size*scale,pose.rotation);
        public static SpatialPlan Plan(RoomSnapshot room,SpatialPlacementRequest request,Pose view,IEnumerable<SpatialBox> reservations)
        {
            if(request.Readable&&request.MinScale!=1)
                throw new ArgumentException("minScale must be 1 when the content includes readable panels or labels; build it smaller instead.");
            if(!new[]{"wall","tabletop","air"}.Contains(request.Destination)||!float.IsFinite(request.MinScale)||request.MinScale<.1f||request.MinScale>1||!SpatialGeometry.Finite(request.Content.center)||!SpatialGeometry.Finite(request.Content.size)||request.Content.size.minComponent()<0)
                throw new ArgumentException("Invalid placement request or non-finite content bounds.");
            if(request.PhysicalId!=null&&request.Destination=="air")throw new ArgumentException("Air has no physical support.");
            var result=new SpatialPlan{RoomRevision=room.Revision,Simulated=room.Simulated,PhysicalValidation=room.State==RoomState.Ready};
            var reserved=reservations.ToArray();
            if(room.State==RoomState.Suspended||room.State==RoomState.Loading||room.State==RoomState.WaitingForPermission){result.Reason="Room tracking or loading is suspended.";return result;}
            var scales=request.MinScale==1?new[]{1f}:new[]{1f,(1+request.MinScale)*.5f,request.MinScale};
            SpatialCandidate bestRelaxed=null;
            void Try(string destination,string id,Vector3 center,Quaternion rotation,PhysicalSurface support=null)
            {
                foreach(var scale in scales)
                {
                    var box=new SpatialBox(center,request.Content.size*scale,rotation);
                    if(support!=null)
                    {
                        var normal=support.Pose.rotation*Vector3.forward;
                        if(destination=="wall"&&Vector3.Dot(normal,view.position-support.Pose.position)<0)normal=-normal;
                        float radius=box.Corners.Max(p=>Mathf.Abs(Vector3.Dot(p-box.Center,normal)));
                        box=new SpatialBox(center+normal*(radius+(destination=="wall"?SpatialPlacementSettings.WallGap:SpatialPlacementSettings.TableGap)),box.Size,rotation);
                    }
                    var candidate=new SpatialCandidate{Destination=destination,PhysicalId=id,Scale=scale,Box=box,Pose=new Pose(box.Center-rotation*(request.Content.center*scale),rotation)};
                    string reason=ValidatePose(room,request,candidate,view,reserved);
                    bool canRelax=support!=null&&request.RelaxViewerComfort&&IsComfortReason(reason);
                    string geometryReason=canRelax?ValidateGeometry(room,candidate,reserved):null;
                    if(support!=null)
                    {
                        var summary=result.Supports.FirstOrDefault(s=>s.PhysicalId==support.PhysicalId);
                        if(summary==null){summary=new SupportSummary{PhysicalId=support.PhysicalId};result.Supports.Add(summary);}
                        summary.Tried++;
                        string summaryReason=geometryReason??reason;
                        if(summaryReason!=null){summary.Reasons.TryGetValue(summaryReason,out int count);summary.Reasons[summaryReason]=count+1;}
                    }
                    if(reason!=null)
                    {
                        if(result.Rejected.Count<40)result.Rejected.Add((id,reason));
                        if(canRelax&&geometryReason==null)
                        {
                            candidate.Relaxed=true;
                            candidate.WaivedReason=reason;
                            candidate.Score=Score(request,candidate);
                            if(bestRelaxed==null||candidate.Score>bestRelaxed.Score+.000001f||Mathf.Abs(candidate.Score-bestRelaxed.Score)<.000001f&&string.CompareOrdinal(id,bestRelaxed.PhysicalId)<0)bestRelaxed=candidate;
                        }
                        continue;
                    }
                    candidate.Score=Score(request,candidate);
                    if(result.Best==null||candidate.Score>result.Best.Score+.000001f||Mathf.Abs(candidate.Score-result.Best.Score)<.000001f&&string.CompareOrdinal(id,result.Best.PhysicalId)<0)result.Best=candidate;
                }
            }
            void Air()
            {
                foreach(float distance in SpatialPlacementSettings.AirDistances)foreach(float angle in SpatialPlacementSettings.AirAngles)
                {
                    var center=view.position+(view.rotation*Quaternion.Euler(0,angle,0)*Vector3.forward)*distance-Vector3.up*SpatialPlacementSettings.EyeDrop;
                    var facing=center-view.position;var rotation=SpatialGeometry.Yaw(request.Readable?facing:-facing);
                    Try("air",null,center,rotation);
                }
            }
            if(request.Destination=="air")Air();
            else if(room.State==RoomState.Ready)
            {
                foreach(var surface in room.Surfaces.Where(s=>s.Kind==request.Destination&&(request.PhysicalId==null||s.PhysicalId==request.PhysicalId)).OrderBy(s=>s.PhysicalId,StringComparer.Ordinal))
                {
                    var normal=surface.Pose.rotation*Vector3.forward;
                    if(surface.Kind=="wall"&&Vector3.Dot(normal,view.position-surface.Pose.position)<0)normal=-normal;
                    var min=new Vector2(surface.Polygon.Min(p=>p.x),surface.Polygon.Min(p=>p.y));var max=new Vector2(surface.Polygon.Max(p=>p.x),surface.Polygon.Max(p=>p.y));
                    var desired=Quaternion.Inverse(surface.Pose.rotation)*(view.position+view.rotation*Vector3.forward*SpatialPlacementSettings.FirstDistance-Vector3.up*SpatialPlacementSettings.EyeDrop-surface.Pose.position);
                    var usefulMin=min;var usefulMax=max;
                    if(surface.Kind=="tabletop")
                    {
                        float inset=SpatialPlacementSettings.EdgeMargin+.5f*new Vector2(request.Content.size.x,request.Content.size.z).magnitude;
                        usefulMin+=Vector2.one*inset;usefulMax-=Vector2.one*inset;
                        if(usefulMin.x>=usefulMax.x)usefulMin.x=usefulMax.x=(min.x+max.x)*.5f;
                        if(usefulMin.y>=usefulMax.y)usefulMin.y=usefulMax.y=(min.y+max.y)*.5f;
                    }
                    var points=new List<Vector2>{new Vector2(Mathf.Clamp(desired.x,usefulMin.x,usefulMax.x),Mathf.Clamp(desired.y,usefulMin.y,usefulMax.y))};
                    if(surface.Kind=="tabletop")
                        foreach(float x in SpatialPlacementSettings.TabletopInsetFractions)foreach(float y in SpatialPlacementSettings.TabletopInsetFractions)points.Add(new Vector2(Mathf.Lerp(usefulMin.x,usefulMax.x,x),Mathf.Lerp(usefulMin.y,usefulMax.y,y)));
                    foreach(float x in SpatialPlacementSettings.GridFractions)foreach(float y in SpatialPlacementSettings.GridFractions)points.Add(new Vector2(Mathf.Lerp(min.x,max.x,x),Mathf.Lerp(min.y,max.y,y)));
                    foreach(var point in points)
                    {
                        var center=surface.Pose.position+surface.Pose.rotation*new Vector3(point.x,point.y,0);
                        var facing=surface.Kind=="wall"?-normal:center-view.position;
                        var rotation=SpatialGeometry.Yaw(request.Readable?facing:-facing);
                        Try(surface.Kind,surface.PhysicalId,center,rotation,surface);
                    }
                }
            }
            if(result.Best==null)result.Best=bestRelaxed;
            bool fallback=request.Destination!="air"&&result.Best==null&&request.AllowAirFallback;
            if(fallback)Air();
            result.Reason=result.Best==null?"No compatible placement was found.":result.Best.Relaxed?$"Placed on the requested support outside the comfortable view ({result.Best.WaivedReason}).":!result.PhysicalValidation?"Air placement without physical room validation.":fallback?"No suitable support; air fallback checked only against captured geometry.":"Compatible with captured geometry; moved or new furniture is unknown.";
            return result;
        }
        static bool IsComfortReason(string reason)=>reason=="TOO_FAR"||reason=="TOO_CLOSE"||reason=="OCCLUDED"||reason=="OUTSIDE_COMFORT"||reason=="OBLIQUE_WALL"||reason=="TOO_SMALL_TO_READ";
        // Smallest ratio between a reading target's angular height and its required angle; >=1 is readable.
        internal static float ReadingRatio(SpatialPlacementRequest request,SpatialCandidate candidate)
        {
            if(request.ReadingTargets==null||request.ReadingTargets.Count==0)return candidate.AngularHeight/SpatialPlacementSettings.MinReadingAngle;
            float ratio=float.PositiveInfinity;
            foreach(var target in request.ReadingTargets)
                ratio=Mathf.Min(ratio,2*Mathf.Atan(target.Height*candidate.Scale*.5f/Mathf.Max(.0001f,candidate.Distance))*Mathf.Rad2Deg/target.MinAngle);
            return ratio;
        }
        internal static float Score(SpatialPlacementRequest request,SpatialCandidate candidate)
        {
            float orientation=Mathf.Clamp01(1-candidate.Yaw/90)*Mathf.Clamp01(1-candidate.Elevation/90);
            float distance=1-Mathf.Clamp01(Mathf.Max(SpatialPlacementSettings.PreferredMin-candidate.Distance,candidate.Distance-SpatialPlacementSettings.PreferredMax)/SpatialPlacementSettings.PreferredMin);
            float readability=request.Readable?Mathf.Clamp01(ReadingRatio(request,candidate))*Mathf.Clamp01(1-candidate.Yaw/90):1;
            return .30f*orientation+.20f*distance+.20f*readability+.15f*candidate.Scale+.15f*(request.PreviousSupport==candidate.PhysicalId?1:0);
        }
        static float minComponent(this Vector3 v)=>Mathf.Min(v.x,Mathf.Min(v.y,v.z));
        public static SpatialCandidate Snap(RoomSnapshot room,SpatialPlacementRequest request,Pose current,Pose view,IEnumerable<SpatialBox> reservations)
        {
            if(room.State!=RoomState.Ready)return null;
            var currentBox=BoxAt(request,current);
            var surfaces=room.Surfaces.Where(s=>s.Kind=="wall"||s.Kind=="tabletop").Select(s=>{
                var normal=s.Pose.rotation*Vector3.forward;
                float radius=currentBox.Corners.Max(p=>Mathf.Abs(Vector3.Dot(p-currentBox.Center,normal)));
                return(surface:s,distance:Mathf.Max(0,Mathf.Abs(Vector3.Dot(currentBox.Center-s.Pose.position,normal))-radius));
            }).OrderBy(s=>s.distance).ThenBy(s=>s.surface.PhysicalId,StringComparer.Ordinal);
            var reserved=reservations.ToArray();
            foreach(var item in surfaces)
            {
                if(item.distance>SpatialPlacementSettings.SnapDistance)continue;
                var surface=item.surface;var normal=surface.Pose.rotation*Vector3.forward;
                if(surface.Kind=="wall"&&Vector3.Dot(normal,view.position-surface.Pose.position)<0)normal=-normal;
                var center=currentBox.Center-normal*Vector3.Dot(currentBox.Center-surface.Pose.position,normal);
                var facing=surface.Kind=="wall"?-normal:center-view.position;
                var rotation=SpatialGeometry.Yaw(request.Readable?facing:-facing);
                if(Quaternion.Angle(rotation,current.rotation)>SpatialPlacementSettings.SnapAngle)continue;
                var box=new SpatialBox(center,request.Content.size,rotation);
                float radius=box.Corners.Max(p=>Mathf.Abs(Vector3.Dot(p-center,normal)));
                center+=normal*(radius+(surface.Kind=="wall"?SpatialPlacementSettings.WallGap:SpatialPlacementSettings.TableGap));
                box=new SpatialBox(center,box.Size,rotation);
                var candidate=new SpatialCandidate{Destination=surface.Kind,PhysicalId=surface.PhysicalId,Box=box,Pose=new Pose(center-rotation*request.Content.center,rotation)};
                if(ValidatePose(room,request,candidate,view,reserved)==null)return candidate;
            }
            return null;
        }
        static string ValidateVolume(SpatialBox box)
        {
            return !SpatialGeometry.Finite(box.Center)||!SpatialGeometry.Finite(box.Size)||box.Size.minComponent()<0||box.Corners.Any(p=>!SpatialGeometry.Finite(p))?"NON_FINITE":null;
        }
        // Persistent validity is independent of viewer pose, visibility and reading comfort.
        public static string ValidateGeometry(RoomSnapshot room,SpatialCandidate candidate,IEnumerable<SpatialBox> reservations)
        {
            return ValidateVolume(candidate.Box)??ValidateGeometryCore(room,candidate,reservations);
        }
        static string ValidateGeometryCore(RoomSnapshot room,SpatialCandidate candidate,IEnumerable<SpatialBox> reservations,float roomMargin=0)
        {
            var box=candidate.Box;
            var support=room.Surfaces.FirstOrDefault(s=>s.PhysicalId==candidate.PhysicalId&&s.Kind==candidate.Destination);
            if(candidate.Destination!="air"&&(support==null||!SpatialCollision.Fits(box,support,SpatialPlacementSettings.EdgeMargin)))return "SUPPORT_FOOTPRINT";
            foreach(var opening in room.Surfaces.Where(s=>s.Kind=="opening"))if(SpatialCollision.Intersects(box,opening,SpatialPlacementSettings.EdgeMargin))return "OPENING";
            if(room.State==RoomState.Ready&&!RoomContainment.InsideFloor(room,box,roomMargin))return "OUTSIDE_FLOOR";
            foreach(var item in room.Objects)if(SpatialCollision.Intersects(box,item.Box))return "FURNITURE";
            if(RoomContainment.CrossesPlane(room,box,roomMargin))return "CAPTURED_PLANE";
            foreach(var reserved in reservations)if(SpatialCollision.Intersects(box,reserved,SpatialPlacementSettings.ReservationGap))return "RESERVED";
            return null;
        }
        static void ComputeMetrics(SpatialCandidate candidate,Pose view)
        {
            var box=candidate.Box;var delta=box.Center-view.position;
            candidate.Distance=delta.magnitude;
            var local=Quaternion.Inverse(view.rotation)*delta;
            candidate.Yaw=Mathf.Abs(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg);
            candidate.Elevation=Mathf.Abs(Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg);
            candidate.AngularHeight=2*Mathf.Atan2(box.Size.y*.5f,Mathf.Max(.0001f,candidate.Distance))*Mathf.Rad2Deg;
        }
        public static string ValidatePose(RoomSnapshot room,SpatialPlacementRequest request,SpatialCandidate candidate,Pose view,IEnumerable<SpatialBox> reservations)
        {
            var box=candidate.Box;var delta=box.Center-view.position;
            var invalid=ValidateVolume(box);if(invalid!=null)return invalid;
            ComputeMetrics(candidate,view);
            if(candidate.Distance>SpatialPlacementSettings.MaxDistance)return "TOO_FAR";
            if(SpatialCollision.NearestDistance(box,view.position)<SpatialPlacementSettings.MinDistance)return "TOO_CLOSE";
            invalid=ValidateGeometryCore(room,candidate,reservations,candidate.Destination=="air"?SpatialPlacementSettings.RoomMargin:0);if(invalid!=null)return invalid;
            foreach(var point in box.Corners.Concat(new[]{box.Center}))
            {
                if(room.Objects.Any(o=>SpatialCollision.RayHits(view.position,point,o.Box))||room.Surfaces.Any(s=>s.Kind!="tabletop"&&s.Kind!="opening"&&SpatialCollision.RayHits(view.position,point,s)))return "OCCLUDED";
            }
            if(candidate.Yaw>SpatialPlacementSettings.MaxYaw||candidate.Elevation>SpatialPlacementSettings.MaxElevation)return "OUTSIDE_COMFORT";
            var front=box.Rotation*Vector3.forward;
            float incidence=Vector3.Angle(request.Readable?-front:front,-delta);
            if(candidate.Destination=="wall"&&incidence>SpatialPlacementSettings.MaxYaw)return "OBLIQUE_WALL";
            if(request.Readable&&ReadingRatio(request,candidate)<1)return "TOO_SMALL_TO_READ";
            return null;
        }
    }
}
