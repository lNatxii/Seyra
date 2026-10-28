using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using Labels=Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels;

namespace Seyra.Spatial
{
    public static class RoomSnapshotBuilder
    {
        public static RoomSnapshot Build(MRUKRoom room,long revision,bool simulated)
        {
            var surfaces=new List<PhysicalSurface>();var objects=new List<PhysicalObject>();int skipped=0;
            float floor=room.FloorAnchors.Count>0?room.FloorAnchors.Min(a=>a.transform.position.y):0;
            foreach(var anchor in room.Anchors.OrderBy(a=>a.Anchor.Uuid))
            {
                if(anchor.HasAnyLabel(Labels.GLOBAL_MESH|Labels.INVISIBLE_WALL_FACE))continue;
                var id="physical:"+anchor.Anchor.Uuid;var labels=Enum.GetValues(typeof(Labels)).Cast<Labels>().Where(label=>label!=0&&((int)label&((int)label-1))==0&&anchor.HasAnyLabel(label)).Select(label=>label.ToString()).ToArray();
                var matrix=anchor.transform.localToWorldMatrix;
                if(anchor.VolumeBounds.HasValue)
                {
                    var local=anchor.VolumeBounds.Value;
                    var points=SpatialGeometry.Corners(local,matrix);
                    // MRUK 207 planes use local XY, normal +Z. Volumes may use +Z vertically;
                    // derive all dimensions from transformed corners and select the vertical box axis.
                    var axes=new[]{matrix.MultiplyVector(Vector3.right),matrix.MultiplyVector(Vector3.up),matrix.MultiplyVector(Vector3.forward)};
                    // A singular or sheared volume cannot form an oriented box; skip the whole anchor and keep the room.
                    if(axes.Any(a=>a.sqrMagnitude<1e-10f) || Mathf.Abs(Vector3.Dot(axes[0].normalized,axes[1].normalized))>0.0001f ||
                        Mathf.Abs(Vector3.Dot(axes[0].normalized,axes[2].normalized))>0.0001f || Mathf.Abs(Vector3.Dot(axes[1].normalized,axes[2].normalized))>0.0001f)
                    {skipped++;continue;}
                    var rotation=Quaternion.LookRotation(axes[2],axes[1]);
                    var projected=SpatialGeometry.Project(points,rotation);
                    Vector3? facing=anchor.HasAnyLabel(Labels.COUCH|Labels.BED|Labels.SCREEN)?room.GetFacingDirection(anchor):null;
                    var box=new SpatialBox(rotation*projected.center,projected.size,rotation);
                    objects.Add(new PhysicalObject(id,labels,box,facing));
                    int vertical=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(Vector3.Dot(axes[i].normalized,Vector3.up))).First();
                    var normal=axes[vertical].normalized;if(normal.y<0)normal=-normal;
                    var half=local.extents[vertical]*axes[vertical].magnitude;
                    var center=matrix.MultiplyPoint3x4(local.center)+normal*half;
                    if(Vector3.Angle(normal,Vector3.up)<=20 && !anchor.HasAnyLabel(Labels.COUCH|Labels.BED) &&
                        (anchor.HasAnyLabel(Labels.TABLE)||(anchor.HasAnyLabel(Labels.STORAGE)&&center.y-floor>=.4f&&center.y-floor<=1.3f)))
                    {
                        var pose=new Pose(center,Quaternion.LookRotation(normal,axes[(vertical+1)%3]));
                        var inv=Quaternion.Inverse(pose.rotation);
                        var polygon=points.Where(p=>Mathf.Abs(Vector3.Dot(p-center,normal))<.0001f).Select(p=>inv*(p-center)).Select(p=>new Vector2(p.x,p.y)).ToArray();
                        polygon=polygon.OrderBy(p=>Mathf.Atan2(p.y,p.x)).ToArray();
                        surfaces.Add(new PhysicalSurface(id,"tabletop",labels,pose,polygon,id));
                    }
                }
                if(!anchor.PlaneRect.HasValue)continue;
                string kind=anchor.HasAnyLabel(Labels.DOOR_FRAME|Labels.WINDOW_FRAME)?"opening":anchor.HasAnyLabel(Labels.FLOOR)?"floor":anchor.HasAnyLabel(Labels.CEILING)?"ceiling":anchor.HasAnyLabel(Labels.WALL_FACE|Labels.INNER_WALL_FACE)?"wall":null;
                if(kind==null)continue;
                var rect=anchor.PlaneRect.Value;
                var boundary=anchor.PlaneBoundary2D.Count>=3?anchor.PlaneBoundary2D.ToArray():new[]{rect.min,new Vector2(rect.xMax,rect.yMin),rect.max,new Vector2(rect.xMin,rect.yMax)};
                var origin=matrix.MultiplyPoint3x4(new Vector3(rect.center.x,rect.center.y,0));
                var right=matrix.MultiplyVector(Vector3.right);var up=matrix.MultiplyVector(Vector3.up);
                var orientation=Quaternion.LookRotation(Vector3.Cross(right,up),up);var inverse=Quaternion.Inverse(orientation);
                var polygon2=boundary.Select(p=>inverse*(matrix.MultiplyPoint3x4(new Vector3(p.x,p.y,0))-origin)).Select(p=>new Vector2(p.x,p.y));
                surfaces.Add(new PhysicalSurface(id,kind,labels,new Pose(origin,orientation),polygon2));
            }
            return new RoomSnapshot(revision,"room:"+room.Anchor.Uuid,simulated,RoomState.Ready,surfaces,objects,skipped);
        }
    }
}
