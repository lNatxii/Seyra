using System;
using System.Linq;
using Seyra.Spatial;
using UnityEngine;

public static class SpatialPlannerChecks
{
    public static string Run()
    {
        int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        var readingRatio=(Func<SpatialPlacementRequest,SpatialCandidate,float>)Delegate.CreateDelegate(typeof(Func<SpatialPlacementRequest,SpatialCandidate,float>),typeof(SpatialPlacementPlanner).GetMethod("ReadingRatio",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic));
        var readingRequest=new SpatialPlacementRequest();
        var readingCandidate=new SpatialCandidate{AngularHeight=6,Distance=1.3f};
        float legacyRatio=readingCandidate.AngularHeight/SpatialPlacementSettings.MinReadingAngle;
        bool legacyMatches=readingRatio(readingRequest,readingCandidate)==legacyRatio;
        readingRequest.ReadingTargets=Array.Empty<ReadingTarget>();
        Check(legacyMatches&&readingRatio(readingRequest,readingCandidate)==legacyRatio,"Missing or empty reading targets must preserve the whole-box reading ratio.");
        readingRequest.ReadingTargets=new[]{new ReadingTarget(.05f,SpatialPlacementSettings.MinLabelReadingAngle)};
        Check(readingRatio(readingRequest,readingCandidate)>=1,"A 0.05 m label at 1.3 m must be readable.");
        readingCandidate.Distance=3;
        Check(readingRatio(readingRequest,readingCandidate)<1,"A 0.05 m label at 3 m must be too small to read.");
        readingRequest.ReadingTargets=new[]{new ReadingTarget(.3f,SpatialPlacementSettings.MinReadingAngle)};
        readingCandidate.Distance=2;
        Check(readingRatio(readingRequest,readingCandidate)<1,"A 0.3 m panel at 2 m must remain below the 12 degree reading threshold.");
        var floor=new PhysicalSurface("floor","floor",new[]{"FLOOR"},new Pose(Vector3.zero,Quaternion.Euler(90,0,0)),new[]{new Vector2(-4,-4),new Vector2(4,-4),new Vector2(4,4),new Vector2(-4,4)});
        var wall=new PhysicalSurface("wall","wall",new[]{"WALL_FACE"},new Pose(new Vector3(0,1.5f,2),Quaternion.Euler(0,180,0)),new[]{new Vector2(-2,-1.5f),new Vector2(2,-1.5f),new Vector2(2,1.5f),new Vector2(-2,1.5f)});
        var room=new RoomSnapshot(1,"test",true,RoomState.Ready,new[]{floor,wall});
        var view=new Pose(new Vector3(0,1.6f,0),Quaternion.identity);
        var request=new SpatialPlacementRequest{Content=new Bounds(new Vector3(.2f,0,0),new Vector3(.6f,.5f,.05f)),Readable=true,Destination="wall"};
        var plan=SpatialPlacementPlanner.Plan(room,request,view,Array.Empty<SpatialBox>());
        Check(plan.Best!=null&&plan.Best.Destination=="wall","A readable wall candidate is required.");
        Check((plan.Best.Box.Center-(plan.Best.Pose.position+plan.Best.Pose.rotation*request.Content.center)).magnitude<.0001f,"Offset pivots must be preserved.");
        Check(SpatialPlacementPlanner.Snap(room,request,plan.Best.Pose,view,Array.Empty<SpatialBox>())!=null,"A nearby valid surface should produce a pure snap candidate.");
        Check(SpatialPlacementPlanner.ValidateGeometry(room,plan.Best,Array.Empty<SpatialBox>())==null,"A valid persistent placement must pass geometry-only validation.");
        var turnedView=new Pose(view.position,Quaternion.Euler(0,120,0));
        Check(SpatialPlacementPlanner.ValidatePose(room,request,plan.Best,turnedView,Array.Empty<SpatialBox>())=="OUTSIDE_COMFORT"&&SpatialPlacementPlanner.ValidateGeometry(room,plan.Best,Array.Empty<SpatialBox>())==null,"Viewer comfort must not invalidate persistent geometry.");
        var solidWall=new SpatialCandidate{Destination="wall",PhysicalId="wall",Pose=plan.Best.Pose,Box=new SpatialBox(plan.Best.Box.Center-Vector3.forward*.3f,plan.Best.Box.Size,Quaternion.Euler(0,120,0))};
        Check(SpatialPlacementPlanner.ValidatePose(room,new SpatialPlacementRequest{Content=request.Content,Readable=false},solidWall,view,Array.Empty<SpatialBox>())=="OBLIQUE_WALL","Oblique walls must also reject non-reading content.");
        var again=SpatialPlacementPlanner.Plan(room,request,view,Array.Empty<SpatialBox>());Check(plan.Best.Pose.position==again.Best.Pose.position,"Planning must be deterministic.");
        Check(SpatialPlacementPlanner.ValidatePose(room,request,plan.Best,view,new[]{plan.Best.Box})=="RESERVED","Reservations must reject overlap.");
        var concave=new[]{new Vector2(0,0),new Vector2(3,0),new Vector2(3,3),new Vector2(2,3),new Vector2(2,1),new Vector2(1,1),new Vector2(1,3),new Vector2(0,3)};
        Check(!SpatialCollision.Contains(concave,new Vector2(1.5f,2)),"Concave cutout must remain empty.");
        Check(!SpatialCollision.Fits(new SpatialBox(new Vector3(1.5f,2,0),new Vector3(2,.2f,.05f),Quaternion.identity),new PhysicalSurface("concave","wall",Array.Empty<string>(),new Pose(),concave),0),"Edges must not bridge a concave cutout.");
        var box=new SpatialBox(new Vector3(0,1,1),Vector3.one*.4f,Quaternion.Euler(0,35,0));
        Check(SpatialCollision.Intersects(box,box)&&SpatialCollision.RayHits(view.position,new Vector3(0,.4f,2),box),"Oriented collision and visibility rays must work.");
        var opening=new PhysicalSurface("door","opening",new[]{"DOOR_FRAME"},wall.Pose,wall.Polygon);
        var blocked=new RoomSnapshot(2,"test",true,RoomState.Ready,new[]{floor,wall,opening});
        request.AllowAirFallback=false;Check(SpatialPlacementPlanner.Plan(blocked,request,view,Array.Empty<SpatialBox>()).Best==null,"A wall opening must reject mounting.");
        request.Destination="air";var noMap=new RoomSnapshot(0,null,false,RoomState.AirOnly);
        Check(!SpatialPlacementPlanner.Plan(noMap,request,view,Array.Empty<SpatialBox>()).PhysicalValidation,"Air-only must not claim room validation.");
        request.Readable=false;request.MinScale=.5f;Check(SpatialPlacementPlanner.Plan(noMap,request,view,Array.Empty<SpatialBox>()).Best!=null,"3D scale candidates must be supported.");
        request.Content=new Bounds(Vector3.zero,new Vector3(float.NaN,1,1));bool invalid=false;try{SpatialPlacementPlanner.Plan(room,request,view,Array.Empty<SpatialBox>());}catch(ArgumentException){invalid=true;}Check(invalid,"Non-finite input must fail.");
        var table=new PhysicalSurface("table","tabletop",new[]{"TABLE"},new Pose(new Vector3(1.5f,.75f,0),Quaternion.Euler(-90,0,0)),new[]{new Vector2(-.4725f,-.707f),new Vector2(.4725f,-.707f),new Vector2(.4725f,.707f),new Vector2(-.4725f,.707f)});
        var tableRoom=new RoomSnapshot(3,"test",true,RoomState.Ready,new[]{floor,table});
        var tableRequest=new SpatialPlacementRequest{Content=new Bounds(Vector3.zero,new Vector3(.22f,.05f,.22f)),Destination="tabletop",RelaxViewerComfort=true,AllowAirFallback=false};
        var tablePlan=SpatialPlacementPlanner.Plan(tableRoom,tableRequest,view,Array.Empty<SpatialBox>());
        Check(tablePlan.Best!=null&&tablePlan.Best.Destination=="tabletop"&&tablePlan.Best.Relaxed&&tablePlan.Best.WaivedReason=="OUTSIDE_COMFORT","A table outside comfortable yaw must accept a geometrically valid relaxed candidate.");
        tableRequest.RelaxViewerComfort=false;
        Check(SpatialPlacementPlanner.Plan(tableRoom,tableRequest,view,Array.Empty<SpatialBox>()).Best==null,"Strict tabletop placement outside comfortable yaw must reject without air fallback.");
        tableRequest.RelaxViewerComfort=true;tableRequest.Content=new Bounds(Vector3.zero,new Vector3(3,.05f,3));
        Check(SpatialPlacementPlanner.Plan(tableRoom,tableRequest,view,Array.Empty<SpatialBox>()).Best==null,"Relaxation must not place oversized content on a table.");
        tableRequest.Content=new Bounds(Vector3.zero,new Vector3(.22f,.05f,.22f));
        var reservedTable=SpatialPlacementPlanner.Plan(tableRoom,tableRequest,view,new[]{new SpatialBox(new Vector3(1.5f,.9f,0),new Vector3(3,1,3),Quaternion.identity)});
        Check(reservedTable.Best==null&&reservedTable.Supports.Any(s=>s.PhysicalId=="table"&&s.Reasons.ContainsKey("RESERVED")),"An occupied table must reject relaxed placement and report RESERVED in its support summary.");
        var nearWallView=new Pose(new Vector3(0,1.6f,.5f),Quaternion.identity);
        var airRequest=new SpatialPlacementRequest{Destination="air",Readable=false,Content=new Bounds(Vector3.zero,new Vector3(.6f,.5f,.4f))};
        var airPlan=SpatialPlacementPlanner.Plan(room,airRequest,nearWallView,Array.Empty<SpatialBox>());
        Check(airPlan.Best!=null&&RoomContainment.Reason(room,airPlan.Best.Box,.05f)==null,"Air planning must keep a 5 cm room margin at 1.5 m from a wall.");
        var closePose=new Pose(new Vector3(0,1.5f,1.77f),Quaternion.identity);
        var closeCandidate=new SpatialCandidate{Destination="air",Pose=closePose,Box=new SpatialBox(closePose.position,airRequest.Content.size,closePose.rotation)};
        Check(SpatialPlacementPlanner.ValidateGeometry(room,closeCandidate,Array.Empty<SpatialBox>())==null&&RoomContainment.Reason(room,closeCandidate.Box,.05f)=="CAPTURED_PLANE","Persistent geometry must accept a box 3 cm from the wall without the planning margin.");
        return "Spatial planner basic checks passed: "+checks;
    }
}
