using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.Spatial
{
    /// <summary>Deterministic floating layouts. No scene objects or room access.</summary>
    public static class LayoutPlanner
    {
        public static readonly string[] Layouts={"arc","plane","hub","surround","anchor"};
        public static string[] Behaviors(string layout)=>layout=="anchor"?new[]{"fixed_facing","billboard"}:layout=="arc"||layout=="hub"?new[]{"fixed_facing","billboard"}:layout=="plane"?new[]{"fixed_facing","lazy_follow"}:new[]{"fixed_facing"};
        public static float Number(JObject a,string key,float fallback,float min,float max)
        {
            if(a[key]==null)return fallback;
            if(a[key].Type!=JTokenType.Float&&a[key].Type!=JTokenType.Integer)throw new ArgumentException(key+" must be numeric.");
            float v=(float)(double)a[key];if(!float.IsFinite(v)||v<min||v>max)throw new ArgumentException(key+" is outside its range.");return v;
        }
        static void Keys(JObject a,params string[] keys){if(a.Properties().Any(p=>!keys.Contains(p.Name)))throw new ArgumentException("Unknown layout field.");}
        public static Vector2 Size(JToken token)
        {
            var a=token as JObject??throw new ArgumentException("LAYOUT_SIZE_INVALID: size is required.");Keys(a,"width","height");
            if(a["width"]==null||a["height"]==null)throw new ArgumentException("LAYOUT_SIZE_INVALID: width and height are required.");
            return new Vector2(Number(a,"width",0,.16f,2),Number(a,"height",0,.04f,2));
        }
        public static JObject Normalize(JObject a,int count)
        {
            string layout=(string)a["layout"],behavior=(string)a["behavior"]??(layout=="anchor"?"billboard":"fixed_facing");
            if(!Layouts.Contains(layout))throw new ArgumentException("Unsupported layout; wall and surface belong to 23b.");
            if(count<1||count>24)throw new ArgumentException("count must be 1..24.");
            if(!Behaviors(layout).Contains(behavior)||behavior=="lazy_follow"&&count>3)throw new ArgumentException("LAYOUT_BEHAVIOR_INVALID: valid behaviors: "+string.Join(", ",Behaviors(layout))+"; lazy_follow requires 1..3 items.");
            Size(a["itemSize"]);if(a["centerSize"]!=null&&a["centerSize"].Type!=JTokenType.Null){if(layout!="hub")throw new ArgumentException("centerSize is only valid for hub.");Size(a["centerSize"]);}
            if(layout=="hub"&&(count<3||count>9))throw new ArgumentException("hub requires a center and 2..8 satellites.");
            var p=a["params"]==null?new JObject():a["params"] as JObject??throw new ArgumentException("params must be an object.");
            Keys(p,layout=="arc"||layout=="surround"?new[]{"radius","rows"}:layout=="plane"?new[]{"distance","arrangement","columns","side"}:layout=="hub"?new[]{"distance"}:Array.Empty<string>());
            if(p["radius"]!=null)Number(p,"radius",0,layout=="arc"?1.2f:1.4f,layout=="arc"?2.6f:3);
            if(p["rows"]!=null&&(p["rows"].Type!=JTokenType.Integer||(int)p["rows"]<1||(int)p["rows"]>(layout=="arc"?3:2)))throw new ArgumentException("Invalid rows.");
            Number(p,"distance",1.6f,.8f,3);
            if(p["columns"]!=null&&(p["columns"].Type!=JTokenType.Integer||(int)p["columns"]<1||(int)p["columns"]>6))throw new ArgumentException("Invalid columns.");
            if(p["arrangement"]!=null&&!new[]{"row","column","grid"}.Contains((string)p["arrangement"]))throw new ArgumentException("Invalid arrangement.");
            if(p["columns"]!=null&&((string)p["arrangement"]??"grid")!="grid")throw new ArgumentException("columns requires grid.");
            if(p["side"]!=null&&(behavior!="lazy_follow"||!new[]{"left","center","right"}.Contains((string)p["side"])))throw new ArgumentException("side requires lazy_follow.");
            return new JObject{["layout"]=layout,["behavior"]=behavior,["itemSize"]=a["itemSize"].DeepClone(),["centerSize"]=a["centerSize"]?.DeepClone(),["gap"]=Number(a,"gap",layout=="anchor"?.05f:.08f,0,.5f),["params"]=p.DeepClone()};
        }
        public sealed class PlanResult
        {
            public bool Fits;public int MaxItems,Rows,Columns;public float Radius;public Pose Root;
            public readonly List<Pose> Items=new List<Pose>();public readonly List<string> Warnings=new List<string>();
        }
        public static PlanResult Plan(JObject spec,int count,Pose view,Func<int,Vector2,Pose> anchor=null)
        {
            spec=Normalize(spec,count);var result=Build(spec,count,view,anchor);
            result.MaxItems=0;
            int min=(string)spec["layout"]=="hub"?3:1,max=(string)spec["layout"]=="hub"?9:(string)spec["behavior"]=="lazy_follow"?3:24;
            if((string)spec["layout"]=="anchor")result.MaxItems=24;
            else for(int n=min;n<=max;n++)if(Build(spec,n,view,null).Fits)result.MaxItems=n;
            return result;
        }
        static PlanResult Build(JObject s,int n,Pose view,Func<int,Vector2,Pose> anchor)
        {
            var r=new PlanResult{Root=new Pose(view.position+Vector3.down*.1f,Quaternion.Euler(0,view.rotation.eulerAngles.y,0))};
            string layout=(string)s["layout"];var size=Size(s["itemSize"]);var center=s["centerSize"]?.Type==JTokenType.Object?Size(s["centerSize"]):size;
            var p=(JObject)s["params"];float gap=(float)s["gap"],w=size.x,h=size.y;
            var local=new List<Pose>();
            void Add(float x,float y,float z,float yaw=0)=>local.Add(new Pose(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0)));
            if(layout=="anchor")
            {
                if(anchor==null)throw new ArgumentException("Anchor targets must be resolved before planning.");
                for(int i=0;i<n;i++)r.Items.Add(anchor(i,size));r.Fits=true;r.Rows=1;r.Columns=n;return r;
            }
            if(layout=="plane")
            {
                string arrangement=(string)p["arrangement"]??"grid";int cols=arrangement=="row"?n:arrangement=="column"?1:(int?)p["columns"]??Math.Min(6,Mathf.CeilToInt(Mathf.Sqrt(n*1.6f)));
                cols=Math.Min(cols,n);int rows=(n+cols-1)/cols;float distance=Number(p,"distance",(string)s["behavior"]=="lazy_follow"?.9f:1.6f,.8f,3);
                if(cols*w+(cols-1)*gap>2*distance*Mathf.Tan(40*Mathf.Deg2Rad)+.00001f||rows*h+(rows-1)*gap>1.40001f)return r;
                float side=(string)s["behavior"]=="lazy_follow"?((string)p["side"]=="left"?-.35f:(string)p["side"]=="center"?0:.35f):0;
                // Following rotates around the group's center, with children at z=0.
                if((string)s["behavior"]=="lazy_follow")r.Root.position+=r.Root.rotation*new Vector3(side,0,distance);
                for(int i=0;i<n;i++)Add((i%cols-(Math.Min(cols,n-i/cols*cols)-1)*.5f)*(w+gap)+((string)s["behavior"]=="lazy_follow"?0:side),((rows-1)*.5f-i/cols)*(h+gap),(string)s["behavior"]=="lazy_follow"?0:distance);
                r.Rows=rows;r.Columns=cols;
            }
            if(layout=="arc"||layout=="surround")
            {
                bool arc=layout=="arc";int firstRows=(int?)p["rows"]??(arc?(n<=4?1:2):1),lastRows=p["rows"]!=null?firstRows:arc?3:2;
                bool found=false;
                for(int rows=firstRows;rows<=lastRows&&!found;rows++)
                {
                    if(rows*h+(rows-1)*gap>(arc?1.4f:float.MaxValue)+.00001f)continue;
                    int cols=(n+rows-1)/rows;
                    float radius=p["radius"]!=null?(float)p["radius"]:arc?1.2f:Mathf.Max(1.6f,cols*(w+gap)/(2*Mathf.PI));
                    if(arc&&p["radius"]==null)while((cols-1)*.5f*(w+gap)/radius+w*.5f/radius>50*Mathf.Deg2Rad&&radius<2.199f)radius+=.1f;
                    if(arc?((cols-1)*.5f*(w+gap)/radius+w*.5f/radius>50*Mathf.Deg2Rad+.00001f):radius>3||cols*(w+gap)>2*Mathf.PI*radius+.00001f)continue;
                    for(int row=0;row<rows;row++)
                    {
                        int length=Math.Min(cols,n-row*cols);for(int c=0;c<length;c++){float a=arc?(c-(length-1)*.5f)*(w+gap)/radius:c*2*Mathf.PI/length;Add(radius*Mathf.Sin(a),((rows-1)*.5f-row)*(h+gap),radius*Mathf.Cos(a),a*Mathf.Rad2Deg);}
                    }
                    r.Rows=rows;r.Columns=cols;r.Radius=radius;found=true;
                }
                if(!found)return r;
            }
            if(layout=="hub")
            {
                float d=Number(p,"distance",1.7f,.8f,3),a=(center.x+w)*.5f+gap,b=(center.y+h)*.5f+gap;int m=n-1;
                bool found=false;
                for(int iteration=0;iteration<100;iteration++,a*=1.1f,b*=1.1f)
                {
                    local.Clear();Add(0,0,d);for(int k=0;k<m;k++){float angle=(90-k*360f/m)*Mathf.Deg2Rad;Add(a*Mathf.Cos(angle),b*Mathf.Sin(angle),d);}
                    bool overlap=false;
                    for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)
                    {var si=i==0?center:size;var sj=j==0?center:size;var delta=local[i].position-local[j].position;if(Mathf.Abs(delta.x)<(si.x+sj.x)*.5f+gap-.00001f&&Mathf.Abs(delta.y)<(si.y+sj.y)*.5f+gap-.00001f)overlap=true;}
                    float width=local.Select((v,i)=>v.position.x+(i==0?center.x:w)*.5f).Max()-local.Select((v,i)=>v.position.x-(i==0?center.x:w)*.5f).Min();
                    float height=local.Select((v,i)=>v.position.y+(i==0?center.y:h)*.5f).Max()-local.Select((v,i)=>v.position.y-(i==0?center.y:h)*.5f).Min();
                    if(width>2*d+.00001f||height>1.50001f)return r;if(!overlap){found=true;break;}
                }
                if(!found)return r;r.Rows=1;r.Columns=n;
            }
            foreach(var pose in local)r.Items.Add(new Pose(r.Root.position+r.Root.rotation*pose.position,r.Root.rotation*pose.rotation));
            r.Fits=true;return r;
        }
    }
}
