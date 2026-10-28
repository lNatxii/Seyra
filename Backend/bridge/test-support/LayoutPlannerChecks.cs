using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;

public static class LayoutPlannerChecks
{
    public static string Run()
    {
        int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        var view=new Pose(new Vector3(2,1.6f,3),Quaternion.Euler(0,35,0));
        foreach(string layout in new[]{"arc","plane","hub","surround","anchor"})
        {
            var spec=new JObject{["layout"]=layout,["itemSize"]=new JObject{["width"]=.3,["height"]=.16}};
            foreach(int n in layout=="hub"?new[]{3,6,9}:new[]{1,6,24})
            {
                var plan=LayoutPlanner.Plan(spec,n,view,(i,size)=>new Pose(new Vector3(i,2,3),Quaternion.identity));
                Check(plan.Fits,layout+" must fit "+n);Check(plan.Items.Count==n,"Missing poses.");Check(plan.MaxItems>=n,"Capacity underestimated.");
                var again=LayoutPlanner.Plan(spec,n,view,(i,size)=>new Pose(new Vector3(i,2,3),Quaternion.identity));
                Check(plan.Items.Select(p=>p.position).SequenceEqual(again.Items.Select(p=>p.position)),"Non-deterministic poses.");
                if(layout=="arc"||layout=="plane")
                {
                    var local=plan.Items.Select(p=>Quaternion.Inverse(plan.Root.rotation)*(p.position-plan.Root.position)).ToArray();
                    Check(n==1||local[0].x<local[1].x||plan.Columns==1,"Reading order must start on the left.");
                    Check(local.All(p=>Mathf.Abs(Mathf.Atan2(p.x,p.z))*Mathf.Rad2Deg<=50.01f),"Angular limit exceeded.");
                }
                for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)Check(!SpatialCollision.Intersects(new SpatialBox(plan.Items[i].position,new Vector3(.2999f,.1599f,.001f),plan.Items[i].rotation),new SpatialBox(plan.Items[j].position,new Vector3(.2999f,.1599f,.001f),plan.Items[j].rotation)),"Cards overlap.");
            }
        }
        var large=new JObject{["layout"]="plane",["itemSize"]=new JObject{["width"]=2,["height"]=2}};
        var rejected=LayoutPlanner.Plan(large,6,view);Check(!rejected.Fits&&rejected.MaxItems==0,"Incorrect rejection capacity.");
        Vector2[] Rectangle(float width,float height)=>new[]{new Vector2(-width/2,-height/2),new Vector2(width/2,-height/2),new Vector2(width/2,height/2),new Vector2(-width/2,height/2)};
        PhysicalSurface Wall(string id,Vector3 position,float yaw)=>new PhysicalSurface(id,"wall",Array.Empty<string>(),new Pose(position,Quaternion.Euler(0,yaw,0)),Rectangle(3.2f,3));
        var front=Wall("front",new Vector3(0,1.5f,1.6f),180);
        var door=new PhysicalSurface("door","opening",Array.Empty<string>(),new Pose(new Vector3(0,.85f,1.6f),Quaternion.Euler(0,180,0)),Rectangle(.6f,1.7f));
        var table=new PhysicalSurface("table","tabletop",Array.Empty<string>(),new Pose(new Vector3(0,1f,1.3f),Quaternion.Euler(-90,0,0)),Rectangle(2.4f,1.4f),"table");
        var floor=new PhysicalSurface("floor","floor",Array.Empty<string>(),new Pose(Vector3.zero,Quaternion.Euler(90,0,0)),Rectangle(8,8));
        var room=new RoomSnapshot(1,"layout-test",true,RoomState.Ready,new[]{front,Wall("left",new Vector3(-1.6f,1.5f,0),90),Wall("right",new Vector3(1.6f,1.5f,0),-90),Wall("back",new Vector3(0,1.5f,-1.6f),0),door,table,floor});
        var roomView=new Pose(new Vector3(0,1.6f,0),Quaternion.identity);
        var wallSpec=new JObject{["layout"]="wall",["itemSize"]=new JObject{["width"]=.24,["height"]=.55},["params"]=new JObject{["physicalId"]="front"}};
        LayoutPlanner.PlanResult Plan(JObject spec,int n)=>LayoutPlanner.Plan(spec,n,roomView,null,room,Array.Empty<SpatialBox>());
        var six=Plan(wallSpec,6);Check(six.Fits,"Six wall cards must fit: "+six.PerWall);
        Check(six.Candidates.All(c=>c.PhysicalId=="front"&&!SpatialCollision.Intersects(c.Box,door,.1f)),"Front cards must avoid the door.");
        var twenty=Plan(wallSpec,20);Check(twenty.Fits&&twenty.Candidates.Select(c=>c.PhysicalId).Distinct().Count()>1,"Twenty cards must spill: "+twenty.PerWall);
        foreach(var wallItems in twenty.Candidates.GroupBy(c=>c.PhysicalId))
        {
            var poses=wallItems.ToArray();var right=poses[0].Pose.rotation*Vector3.right;
            Check(poses.Zip(poses.Skip(1),(a,b)=>a.Pose.position.y>b.Pose.position.y+.001f||Mathf.Abs(a.Pose.position.y-b.Pose.position.y)<.001f&&Vector3.Dot(b.Pose.position-a.Pose.position,right)>0).All(v=>v),"Wall reading order must be row-major.");
        }
        var noSpill=(JObject)wallSpec.DeepClone();noSpill["params"]["allowSpill"]=false;
        var full=Plan(noSpill,24);Check(!full.Fits&&full.MaxItems<24&&(int)full.PerWall[0]["capacity"]>=0&&full.Items.Count==0,"Missing rejection capacity.");
        Check(full.PerWall.Any(w=>(string)w["physicalId"]!="front"&&(int)w["capacity"]>0),"Unused walls must report their available capacity when spill is disabled.");
        var far=Wall("far",new Vector3(0,1.5f,2.5f),180);
        var small=new JObject{["layout"]="wall",["itemSize"]=new JObject{["width"]=.24,["height"]=.2},["params"]=new JObject{["physicalId"]="far"}};
        var unreadable=LayoutPlanner.Plan(small,6,roomView,null,new RoomSnapshot(2,"far",true,RoomState.Ready,new[]{far,floor}),Array.Empty<SpatialBox>());
        Check(!unreadable.Fits&&unreadable.MaxItems==0&&unreadable.Reason.Contains("TOO_SMALL_TO_READ"),"Distant small cards must remain unreadable.");
        foreach(var pose in new[]{"standing","lying"})
        {
            var spec=new JObject{["layout"]="surface",["itemSize"]=new JObject{["width"]=.24,["height"]=.3},["params"]=new JObject{["physicalId"]="table",["pose"]=pose}};
            var planned=Plan(spec,4);Check(planned.Fits,"Four "+pose+" table cards must fit; maxItems="+planned.MaxItems);
            Check(planned.Candidates.All(c=>SpatialCollision.Fits(c.Box,table,.1f)),"Tilted footprint must remain supported.");
        }
        Check(twenty.Items.Select(p=>p.position).SequenceEqual(Plan(wallSpec,20).Items.Select(p=>p.position)),"Room layout must be deterministic.");
        return "Layout planner basic checks passed: "+checks;
    }
}
