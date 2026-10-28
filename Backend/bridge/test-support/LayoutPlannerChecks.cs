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
        return "Layout planner basic checks passed: "+checks;
    }
}
