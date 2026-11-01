using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.Spatial
{
    public static partial class LayoutPlanner
    {
        public static bool IsRoomLayout(string layout)=>layout=="wall"||layout=="surface";
        public static PlanResult Plan(JObject spec,int count,Pose view,Func<int,Vector2,Pose> anchor,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations)
        {
            spec=Normalize(spec,count);
            if(!IsRoomLayout((string)spec["layout"]))
            {
                PlanResult free;
                if(room==null||room.State!=RoomState.Ready)
                {
                    free=Plan(spec,count,view,anchor);free.Warnings.Add("room_not_validated");return free;
                }
                if((string)spec["layout"]=="anchor"||(string)spec["behavior"]=="lazy_follow")
                {
                    free=Plan(spec,count,view,anchor);
                    for(int i=0;i<free.Items.Count;i++)
                        if(!ItemContained(free.Items[i],CardSize(spec,i),(string)spec["behavior"],view,room))free.Warnings.Add("outside_room:"+i);
                }
                else free=FreeInRoom(spec,count,view,room,reservations);
                free.RoomRevision=room.Revision;free.Simulated=room.Simulated;return free;
            }
            if(room==null||room.State!=RoomState.Ready)throw new ArgumentException("ROOM_UNAVAILABLE: a ready room is required.");
            string kind=(string)spec["layout"]=="wall"?"wall":"tabletop",id=(string)spec["params"]?["physicalId"];
            if(id!=null&&!room.Surfaces.Any(s=>s.PhysicalId==id&&s.Kind==kind))
                throw new ArgumentException(room.Surfaces.Any(s=>s.PhysicalId==id)||room.Objects.Any(o=>o.PhysicalId==id)?"INVALID_ARGUMENT: wrong support kind.":"ROOM_REFERENCE_STALE: support is no longer available.");
            var result=kind=="wall"?Wall(spec,count,view,room,reservations):Surface(spec,count,view,room,reservations);
            result.RoomRevision=room.Revision;result.Simulated=room.Simulated;
            result.Root=new Pose(view.position+Vector3.down*.1f,SpatialGeometry.Yaw(view.rotation*Vector3.forward));
            if(!result.Fits)result.Items.Clear();
            return result;
        }
        static Vector2 CardSize(JObject spec,int index)=>index==0&&(string)spec["layout"]=="hub"&&spec["centerSize"]?.Type==JTokenType.Object?Size(spec["centerSize"]):Size(spec["itemSize"]);
        static SpatialBox ItemBox(Pose pose,Vector2 size,string behavior)=>new SpatialBox(pose.position,new Vector3(size.x,size.y,behavior=="billboard"?size.x:.05f),pose.rotation);
        static bool ItemContained(Pose pose,Vector2 size,string behavior,Pose view,RoomSnapshot room)
        {
            var box=ItemBox(pose,size,behavior);
            return RoomContainment.Reason(room,box,SpatialPlacementSettings.RoomMargin)==null
                &&!room.Surfaces.Any(s=>s.Kind=="wall"&&SpatialCollision.RayHits(view.position,box.Center,s));
        }
        static bool Contained(PlanResult plan,JObject spec,Pose view,RoomSnapshot room)
        {
            for(int i=0;i<plan.Items.Count;i++)if(!ItemContained(plan.Items[i],CardSize(spec,i),(string)spec["behavior"],view,room))return false;
            return true;
        }
        static int Overlaps(PlanResult plan,JObject spec,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations)
        {
            int overlaps=0;
            for(int i=0;i<plan.Items.Count;i++)
            {
                var box=ItemBox(plan.Items[i],CardSize(spec,i),(string)spec["behavior"]);
                if(room.Objects.Any(o=>SpatialCollision.Intersects(box,o.Box,SpatialPlacementSettings.ReservationGap))
                    ||reservations!=null&&reservations.Any(b=>SpatialCollision.Intersects(box,b,SpatialPlacementSettings.ReservationGap)))overlaps++;
            }
            return overlaps;
        }
        // Capacity uses the same bounded search, stopping at its first contained variant.
        static PlanResult SearchRoom(JObject spec,int count,Pose view,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations,bool capacityOnly)
        {
            var original=Build(spec,count,view,null);if(!original.Fits)return null;
            string layout=(string)spec["layout"];bool radial=layout=="arc"||layout=="surround";
            var parameters=(JObject)spec["params"];
            float start=radial?original.Radius:((float?)parameters["distance"]??(layout=="hub"?1.7f:1.6f));
            float minimum=layout=="arc"?SpatialPlacementSettings.ArcMinRadiusInRoom:layout=="surround"?SpatialPlacementSettings.SurroundMinRadiusInRoom:.8f;
            int lastRows=parameters["rows"]==null?(layout=="arc"?3:layout=="surround"?2:original.Rows):original.Rows;
            PlanResult best=null;int bestOverlaps=int.MaxValue;float bestYaw=0,bestSize=start;
            foreach(float yaw in SpatialPlacementSettings.RoomSearchYaws)
            for(int rows=original.Rows;rows<=lastRows;rows++)
            {
                int steps=Mathf.CeilToInt((start-minimum)/SpatialPlacementSettings.RoomSearchStep);
                for(int step=0;step<=steps;step++)
                {
                    float size=Mathf.Max(minimum,start-step*SpatialPlacementSettings.RoomSearchStep);
                    var variant=(JObject)spec.DeepClone();var p=(JObject)variant["params"];
                    p[radial?"radius":"distance"]=size;if(radial)p["rows"]=rows;
                    var plan=Build(variant,count,new Pose(view.position,Quaternion.Euler(0,yaw,0)*view.rotation),null);
                    if(!plan.Fits||!Contained(plan,spec,view,room))continue;
                    if(capacityOnly)return plan;
                    int overlaps=Overlaps(plan,spec,room,reservations);
                    if(overlaps>=bestOverlaps)continue;
                    best=plan;bestOverlaps=overlaps;bestYaw=yaw;bestSize=size;
                    if(overlaps==0)goto Found;
                }
            }
        Found:
            if(best!=null)
            {
                if(bestYaw!=0||best.Rows!=original.Rows||Mathf.Abs(bestSize-start)>.00001f)
                    best.Warnings.Add(FormattableString.Invariant($"room_adjusted:yaw={bestYaw:0},rows={best.Rows},size={bestSize:0.00}"));
                if(bestOverlaps>0)best.Warnings.Add("overlaps_existing:"+bestOverlaps);
            }
            return best;
        }
        static PlanResult FreeInRoom(JObject spec,int count,Pose view,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations)
        {
            var original=Build(spec,count,view,null);
            if(!original.Fits)return Plan(spec,count,view);
            var result=SearchRoom(spec,count,view,room,reservations,false)??new PlanResult{Root=original.Root,
                Reason="LAYOUT_DOES_NOT_FIT: the layout does not fit inside the captured room; use a wall layout, fewer cards or smaller cards."};
            int min=(string)spec["layout"]=="hub"?3:1,max=(string)spec["layout"]=="hub"?9:24;
            for(int n=min;n<=max;n++)
            {
                if(SearchRoom(spec,n,view,room,reservations,true)==null)break;
                result.MaxItems=n;
            }
            return result;
        }
        sealed class WallGrid
        {
            public PhysicalSurface Surface;public Vector3 Anchor;public float Score;public bool Preferred;
            public int Rows,Columns;public readonly List<(int row,int col,SpatialCandidate candidate)> Cells=new List<(int,int,SpatialCandidate)>();
        }
        static SpatialPlacementRequest CardRequest(Vector2 size,string kind)=>new SpatialPlacementRequest{Destination=kind,AllowAirFallback=false,Readable=true,Content=new Bounds(Vector3.zero,new Vector3(size.x,size.y,.05f))};
        static SpatialCandidate Supported(SpatialPlacementRequest request,PhysicalSurface surface,Vector3 center,Quaternion rotation,Pose view)
        {
            var normal=surface.Pose.rotation*Vector3.forward;
            if(surface.Kind=="wall"&&Vector3.Dot(normal,view.position-surface.Pose.position)<0)normal=-normal;
            var box=new SpatialBox(center,request.Content.size,rotation);
            float radius=box.Corners.Max(p=>Mathf.Abs(Vector3.Dot(p-center,normal)));
            center+=normal*(radius+(surface.Kind=="wall"?SpatialPlacementSettings.WallGap:SpatialPlacementSettings.TableGap));
            return new SpatialCandidate{Destination=surface.Kind,PhysicalId=surface.PhysicalId,Pose=new Pose(center,rotation),Box=new SpatialBox(center,box.Size,rotation)};
        }
        static Vector3 WallAnchor(PhysicalSurface wall,Pose view)
        {
            var normal=wall.Pose.rotation*Vector3.forward;var direction=view.rotation*Vector3.forward;
            float dot=Vector3.Dot(direction,normal),t=Mathf.Abs(dot)>.00001f?Vector3.Dot(wall.Pose.position-view.position,normal)/dot:-1;
            var point=t>0?view.position+direction*t:view.position+direction*1.3f;
            var local=Quaternion.Inverse(wall.Pose.rotation)*(point-wall.Pose.position);
            local.x=Mathf.Clamp(local.x,wall.Polygon.Min(p=>p.x)+.1f,wall.Polygon.Max(p=>p.x)-.1f);
            local.y=Mathf.Clamp(local.y,wall.Polygon.Min(p=>p.y)+.1f,wall.Polygon.Max(p=>p.y)-.1f);local.z=0;
            return wall.Pose.position+wall.Pose.rotation*local;
        }
        static WallGrid Grid(PhysicalSurface wall,Vector3 anchor,Pose view,RoomSnapshot room,SpatialPlacementRequest request,float gap,IReadOnlyList<SpatialBox> reservations)
        {
            var grid=new WallGrid{Surface=wall,Anchor=anchor};
            var normal=wall.Pose.rotation*Vector3.forward;if(Vector3.Dot(normal,view.position-wall.Pose.position)<0)normal=-normal;
            var rotation=SpatialGeometry.Yaw(-normal);var validationView=new Pose(view.position,SpatialGeometry.Yaw(anchor-view.position));
            float floor=room.Surfaces.Where(s=>s.Kind=="floor").SelectMany(s=>s.Corners).Select(p=>p.y).DefaultIfEmpty(0).Min();
            var corners=wall.Corners;float bottom=Mathf.Max(floor+.8f,corners.Min(p=>p.y)+.1f),top=Mathf.Min(floor+2.2f,corners.Max(p=>p.y)-.1f);
            float left=wall.Polygon.Min(p=>p.x)+.1f,right=wall.Polygon.Max(p=>p.x)-.1f,w=request.Content.size.x,h=request.Content.size.y;
            grid.Columns=Math.Max(0,Mathf.FloorToInt((right-left+gap)/(w+gap)));grid.Rows=Math.Max(0,Mathf.FloorToInt((top-bottom+gap)/(h+gap)));
            var scoreCandidate=Supported(request,wall,anchor,rotation,view);
            SpatialPlacementPlanner.ValidatePose(room,request,scoreCandidate,view,reservations);
            // An obstructed anchor does not invalidate free cells elsewhere on this wall.
            var delta=scoreCandidate.Box.Center-view.position;var local=Quaternion.Inverse(view.rotation)*delta;
            scoreCandidate.Distance=delta.magnitude;
            scoreCandidate.Yaw=Mathf.Abs(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg);
            scoreCandidate.Elevation=Mathf.Abs(Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg);
            scoreCandidate.AngularHeight=2*Mathf.Atan2(h*.5f,Mathf.Max(.0001f,delta.magnitude))*Mathf.Rad2Deg;
            grid.Preferred=Vector3.Angle(normal,-delta)<=SpatialPlacementSettings.MaxYaw;
            grid.Score=SpatialPlacementPlanner.Score(request,scoreCandidate);
            // Local u may run opposite to the viewer's reading direction.
            bool reverse=Vector3.Dot(wall.Pose.rotation*Vector3.right,rotation*Vector3.right)<0;
            for(int row=0;row<grid.Rows;row++)for(int col=0;col<grid.Columns;col++)
            {
                float u=(left+right)*.5f+((reverse?grid.Columns-1-col:col)-(grid.Columns-1)*.5f)*(w+gap);
                var point=wall.Pose.position+wall.Pose.rotation*new Vector3(u,0,0);point.y=(bottom+top)*.5f+((grid.Rows-1)*.5f-row)*(h+gap);
                var candidate=Supported(request,wall,point,rotation,view);
                string reason=SpatialPlacementPlanner.ValidatePose(room,request,candidate,validationView,reservations);
                if(reason==null)grid.Cells.Add((row,col,candidate));
            }
            return grid;
        }
        static Vector3? SharedCorner(PhysicalSurface a,PhysicalSurface b)
        {
            foreach(var p in a.Corners.OrderBy(p=>p.y))foreach(var q in b.Corners.OrderBy(p=>p.y))if(Vector3.Distance(p,q)<=.3f)return (p+q)*.5f;
            return null;
        }
        static List<SpatialCandidate> SelectCells(WallGrid grid,int count,bool compact)
        {
            if(count==0)return new List<SpatialCandidate>();
            var chosen=grid.Cells.OrderBy(c=>(c.candidate.Box.Center-grid.Anchor).sqrMagnitude).ThenBy(c=>c.row).ThenBy(c=>c.col).Take(count).ToList();
            if(compact)
            {
                var shapes=(from rows in Enumerable.Range(1,Math.Max(1,grid.Rows)) let cols=(count+rows-1)/rows where cols<=grid.Columns select new{rows,cols}).OrderBy(s=>s.rows*s.cols-count).ThenBy(s=>s.cols<s.rows?1:0).ThenBy(s=>Math.Abs(s.cols-s.rows));
                foreach(var shape in shapes)
                {
                    var windows=new List<(List<(int row,int col,SpatialCandidate candidate)> cells,float distance)>();
                    for(int y=0;y<=grid.Rows-shape.rows;y++)for(int x=0;x<=grid.Columns-shape.cols;x++)
                    {
                        var cells=grid.Cells.Where(c=>c.row>=y&&c.row<y+shape.rows&&c.col>=x&&c.col<x+shape.cols).ToList();
                        if(cells.Count!=shape.rows*shape.cols)continue;
                        var center=cells.Aggregate(Vector3.zero,(sum,c)=>sum+c.candidate.Box.Center)/cells.Count;
                        windows.Add((cells,(center-grid.Anchor).sqrMagnitude));
                    }
                    if(windows.Count==0)continue;chosen=windows.OrderBy(w=>w.distance).First().cells.Take(count).ToList();break;
                }
            }
            return chosen.OrderBy(c=>c.row).ThenBy(c=>c.col).Select(c=>c.candidate).ToList();
        }
        static PlanResult Wall(JObject spec,int count,Pose view,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations)
        {
            var result=new PlanResult();var request=CardRequest(Size(spec["itemSize"]),"wall");float gap=(float)spec["gap"];
            var grids=room.Surfaces.Where(s=>s.Kind=="wall").OrderBy(s=>s.PhysicalId,StringComparer.Ordinal).Select(s=>Grid(s,WallAnchor(s,view),view,room,request,gap,reservations)).ToList();
            string id=(string)spec["params"]?["physicalId"];bool spill=(bool?)spec["params"]?["allowSpill"]??true;
            var first=id!=null?grids.First(g=>g.Surface.PhysicalId==id):grids.Where(g=>g.Preferred&&g.Cells.Count>0).OrderByDescending(g=>g.Score).ThenBy(g=>g.Surface.PhysicalId,StringComparer.Ordinal).FirstOrDefault();
            var ordered=new List<WallGrid>();if(first!=null)ordered.Add(first);
            if(spill&&first!=null)
            {
                var remaining=grids.Where(g=>g!=first).ToList();
                while(remaining.Count>0)
                {
                    var adjacent=remaining.Select(g=>(grid:g,corner:ordered.Select(o=>SharedCorner(o.Surface,g.Surface)).FirstOrDefault(c=>c.HasValue))).Where(g=>g.corner.HasValue)
                        .OrderByDescending(g=>g.grid.Score).ThenBy(g=>g.grid.Surface.PhysicalId,StringComparer.Ordinal).ToList();
                    if(adjacent.Count==0)break;var next=adjacent[0];remaining.Remove(next.grid);
                    var point=next.corner.Value;point.y=first.Anchor.y;
                    ordered.Add(Grid(next.grid.Surface,point,view,room,request,gap,reservations));
                }
            }
            var used=new Dictionary<string,int>();var reserved=reservations.ToList();
            foreach(var grid in ordered)
            {
                // Cross-wall corner reservations must also be respected.
                foreach(var cell in grid.Cells.ToArray())
                {
                    if(reserved.Any(b=>SpatialCollision.Intersects(cell.candidate.Box,b,SpatialPlacementSettings.ReservationGap)))grid.Cells.Remove(cell);
                    else reserved.Add(cell.candidate.Box);
                }
                result.MaxItems+=grid.Cells.Count;
                var selected=SelectCells(grid,Math.Min(Math.Max(0,count-result.Candidates.Count),grid.Cells.Count),grid==first);
                result.Candidates.AddRange(selected);used[grid.Surface.PhysicalId]=selected.Count;
                if(grid==first){result.Rows=grid.Rows;result.Columns=grid.Columns;}
            }
            foreach(var grid in grids){var active=ordered.FirstOrDefault(g=>g.Surface==grid.Surface);result.PerWall.Add(new JObject{["physicalId"]=grid.Surface.PhysicalId,["capacity"]=active?.Cells.Count??grid.Cells.Count,["used"]=used.TryGetValue(grid.Surface.PhysicalId,out int n)?n:0});}
            result.Fits=result.Candidates.Count==count;
            result.Reason=result.Fits?null:"LAYOUT_DOES_NOT_FIT: insufficient free readable cells; use taller cards for TOO_SMALL_TO_READ, or group/select items.";
            if(result.Fits)result.Items.AddRange(result.Candidates.Select(c=>c.Pose));
            return result;
        }
        static PlanResult Surface(JObject spec,int count,Pose view,RoomSnapshot room,IReadOnlyList<SpatialBox> reservations)
        {
            var result=new PlanResult();var size=Size(spec["itemSize"]);float gap=(float)spec["gap"];bool lying=(string)spec["params"]?["pose"]=="lying";
            int columns=Mathf.CeilToInt(count/(lying?Mathf.Ceil(Mathf.Sqrt(count)):Mathf.Min(2,count))),rows=Mathf.CeilToInt((float)count/columns);
            var rotation=SpatialGeometry.Yaw(view.rotation*Vector3.forward)*Quaternion.Euler(lying?60:15,0,0);
            var card=CardRequest(size,"tabletop");var tilted=new SpatialBox(Vector3.zero,card.Content.size,Quaternion.Euler(lying?60:15,0,0));
            float depth=tilted.Corners.Max(p=>p.z)-tilted.Corners.Min(p=>p.z);
            string id=(string)spec["params"]?["physicalId"];
            PhysicalSurface surface;
            if(id==null)
            {
                var block=new SpatialPlacementRequest{Destination="tabletop",AllowAirFallback=false,Content=new Bounds(Vector3.zero,new Vector3(columns*size.x+(columns-1)*gap,tilted.Corners.Max(p=>p.y)-tilted.Corners.Min(p=>p.y),rows*depth+(rows-1)*gap))};
                var chosen=SpatialPlacementPlanner.Plan(room,block,view,reservations);
                surface=room.Surfaces.FirstOrDefault(s=>s.PhysicalId==chosen.Best?.PhysicalId&&s.Kind=="tabletop");
            }
            else surface=room.Surfaces.First(s=>s.PhysicalId==id&&s.Kind=="tabletop");
            if(surface==null){result.Reason="LAYOUT_DOES_NOT_FIT: no tabletop supports the complete block.";return result;}
            rotation=SpatialGeometry.Yaw(surface.Pose.position-view.position)*Quaternion.Euler(lying?60:15,0,0);
            var frame=SpatialGeometry.Yaw(surface.Pose.position-view.position);var bounds=SpatialGeometry.Project(surface.Corners,frame);
            // Fits retains the existing 10 cm support margin, including for lying cards.
            int cols=Math.Max(0,Mathf.FloorToInt((bounds.size.x-.2f+gap)/(size.x+gap)));
            int maxRows=Math.Max(0,Mathf.FloorToInt((bounds.size.z-.2f+gap)/(depth+gap)));if(!lying)maxRows=Math.Min(2,maxRows);
            var free=new List<SpatialCandidate>();var occupied=reservations.ToList();
            for(int r=0;r<maxRows;r++)for(int c=0;c<cols;c++)
            {
                var local=new Vector3(bounds.center.x+(c-(cols-1)*.5f)*(size.x+gap),bounds.center.y,bounds.min.z+.1f+depth*.5f+r*(depth+gap));
                var candidate=Supported(card,surface,frame*local,rotation,view);
                if(SpatialPlacementPlanner.ValidatePose(room,card,candidate,view,occupied)==null){free.Add(candidate);occupied.Add(candidate.Box);}
            }
            result.MaxItems=free.Count;result.Rows=maxRows;result.Columns=cols;result.Fits=free.Count>=count;
            if(result.Fits){result.Candidates.AddRange(free.Take(count));result.Items.AddRange(result.Candidates.Select(c=>c.Pose));}
            else result.Reason="LAYOUT_DOES_NOT_FIT: insufficient free readable tabletop cells.";
            return result;
        }
    }
}
