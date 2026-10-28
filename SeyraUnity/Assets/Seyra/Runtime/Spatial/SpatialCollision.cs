using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seyra.Spatial
{
    // Pure geometry shared by planning and swept follow validation. No physics scene queries.
    public static class SpatialCollision
    {
        const float Epsilon=.00001f;
        public static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        { var d=b-a;return Vector2.Distance(p,a+d*(d.sqrMagnitude<Epsilon?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude))); }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        public static bool Contains(IReadOnlyList<Vector2> polygon,Vector2 p,float margin=0)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                var a=polygon[j];var b=polygon[i];var distance=SegmentDistance(p,a,b);
                if(distance<margin-Epsilon)return false;
                if(distance<Epsilon)return margin<=Epsilon;
                if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }
        public static bool SegmentsCross(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            float ab1=Cross(b-a,c-a),ab2=Cross(b-a,d-a),cd1=Cross(d-c,a-c),cd2=Cross(d-c,b-c);
            return ab1*ab2<-Epsilon*Epsilon&&cd1*cd2<-Epsilon*Epsilon;
        }
        public static Vector2[] Footprint(SpatialBox box,PhysicalSurface surface)
        {
            var inv=Quaternion.Inverse(surface.Pose.rotation);
            var points=box.Corners.Select(p=>inv*(p-surface.Pose.position)).ToArray();
            var min=new Vector2(points.Min(p=>p.x),points.Min(p=>p.y));var max=new Vector2(points.Max(p=>p.x),points.Max(p=>p.y));
            return new[]{min,new Vector2(max.x,min.y),max,new Vector2(min.x,max.y)};
        }
        public static bool Fits(SpatialBox box,PhysicalSurface surface,float margin)
        {
            var footprint=Footprint(box,surface);
            if(footprint.Any(p=>!Contains(surface.Polygon,p,margin)))return false;
            for(int i=0;i<4;i++)for(int j=0;j<surface.Polygon.Count;j++)
            {
                var a=footprint[i];var b=footprint[(i+1)%4];var c=surface.Polygon[j];var d=surface.Polygon[(j+1)%surface.Polygon.Count];
                if(SegmentsCross(a,b,c,d)||SegmentDistance(c,a,b)<margin-Epsilon||SegmentDistance(d,a,b)<margin-Epsilon)return false;
            }
            return true;
        }
        static Vector3[] Axes(Quaternion rotation)=>new[]{rotation*Vector3.right,rotation*Vector3.up,rotation*Vector3.forward};
        public static bool Intersects(SpatialBox a,SpatialBox b,float margin=0)
        {
            var aa=Axes(a.Rotation);var ba=Axes(b.Rotation);var axes=aa.Concat(ba).Concat(aa.SelectMany(x=>ba.Select(y=>Vector3.Cross(x,y))));
            foreach(var axis in axes)
            {
                if(axis.sqrMagnitude<Epsilon)continue;
                var n=axis.normalized;float radius=0;
                for(int i=0;i<3;i++)radius+=Mathf.Abs(Vector3.Dot(n,aa[i]))*(a.Size[i]+margin)*.5f+Mathf.Abs(Vector3.Dot(n,ba[i]))*(b.Size[i]+margin)*.5f;
                if(Mathf.Abs(Vector3.Dot(n,a.Center-b.Center))>=radius-Epsilon)return false;
            }
            return true;
        }
        public static bool Intersects(SpatialBox box,PhysicalSurface surface,float margin=0)
        {
            var inv=Quaternion.Inverse(surface.Pose.rotation);var points=box.Corners.Select(p=>inv*(p-surface.Pose.position)).ToArray();
            if(points.Min(p=>p.z)>margin||points.Max(p=>p.z)<-margin)return false;
            var f=Footprint(box,surface);float minX=f[0].x-margin,maxX=f[2].x+margin,minY=f[0].y-margin,maxY=f[2].y+margin;
            if(surface.Polygon.Any(p=>p.x>=minX&&p.x<=maxX&&p.y>=minY&&p.y<=maxY)||f.Any(p=>Contains(surface.Polygon,p)))return true;
            for(int i=0;i<4;i++)for(int j=0;j<surface.Polygon.Count;j++)if(SegmentsCross(f[i],f[(i+1)%4],surface.Polygon[j],surface.Polygon[(j+1)%surface.Polygon.Count]))return true;
            return false;
        }
        public static bool RayHits(Vector3 origin,Vector3 end,SpatialBox box)
        {
            var inverse=Quaternion.Inverse(box.Rotation);var a=inverse*(origin-box.Center);var d=inverse*(end-origin);var h=box.Size*.5f;float near=0,far=1;
            for(int i=0;i<3;i++)
            {
                if(Mathf.Abs(d[i])<Epsilon){if(Mathf.Abs(a[i])>h[i])return false;continue;}
                float x=(-h[i]-a[i])/d[i],y=(h[i]-a[i])/d[i];if(x>y)(x,y)=(y,x);
                near=Mathf.Max(near,x);far=Mathf.Min(far,y);if(near>far)return false;
            }
            return far>.0001f&&near<.9999f;
        }
        public static bool RayHits(Vector3 origin,Vector3 end,PhysicalSurface surface)
        {
            var inv=Quaternion.Inverse(surface.Pose.rotation);var a=inv*(origin-surface.Pose.position);var b=inv*(end-surface.Pose.position);
            if(Mathf.Abs(b.z-a.z)<Epsilon)return false;
            float t=-a.z/(b.z-a.z);if(t<=.0001f||t>=.9999f)return false;var p=Vector3.Lerp(a,b,t);
            return Contains(surface.Polygon,new Vector2(p.x,p.y));
        }
        public static float NearestDistance(SpatialBox box,Vector3 point)
        {
            var p=Quaternion.Inverse(box.Rotation)*(point-box.Center);var h=box.Size*.5f;
            return (p-new Vector3(Mathf.Clamp(p.x,-h.x,h.x),Mathf.Clamp(p.y,-h.y,h.y),Mathf.Clamp(p.z,-h.z,h.z))).magnitude;
        }
    }
}
