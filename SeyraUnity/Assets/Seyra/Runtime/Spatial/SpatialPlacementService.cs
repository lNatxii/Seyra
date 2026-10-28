using System;
using System.Collections.Generic;
using System.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.Spatial
{
    [DisallowMultipleComponent]
    public sealed class SpatialPlacementService : MonoBehaviour
    {
        [SerializeField] RoomContext room;
        readonly Dictionary<Transform,SpatialCandidate> placements=new Dictionary<Transform,SpatialCandidate>();
        string taskKey;
        bool taskInitialized;
        Quaternion taskYaw;
        public RoomContext Room=>room;
        public RoomSnapshot Snapshot=>room!=null?room.Snapshot:new RoomSnapshot(0,null,false,RoomState.AirOnly);
        void Awake(){if(room==null)room=FindObjectsByType<RoomContext>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.scene==gameObject.scene);}
        public static SpatialPlacementService ForScene(Scene scene)
        {
            var result=FindObjectsByType<SpatialPlacementService>(FindObjectsSortMode.None).FirstOrDefault(s=>s.gameObject.scene==scene);
            if(result!=null){if(result.GetComponent<SpatialWindowSnapping>()==null)result.gameObject.AddComponent<SpatialWindowSnapping>();return result;}
            var go=new GameObject("SeyraSpatialPlacement");SceneManager.MoveGameObjectToScene(go,scene);result=go.AddComponent<SpatialPlacementService>();go.AddComponent<SpatialWindowSnapping>();return result;
        }
        public Pose View(string key=null,bool currentYaw=false)
        {
            var head=room!=null?room.Viewer:Seyra.UI.SeyraLazyFollow.ResolveHead();
            if(head==null||room!=null&&!room.ViewerReady)throw new InvalidOperationException("A tracked viewer is required for spatial placement.");
            if(currentYaw||key==null)return new Pose(head.position,SpatialGeometry.Yaw(head.forward));
            if(!taskInitialized||taskKey!=key){taskYaw=SpatialGeometry.Yaw(head.forward);taskKey=key;taskInitialized=true;}
            return new Pose(head.position,taskYaw);
        }
        public static Bounds Measure(Transform root)
        {
            var points=new List<Vector3>();
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                var panel=t.GetComponentInParent<ManagedPanel>(true);
                if(panel!=null){if(t==panel.transform)points.AddRange(panel.EnvelopeCorners());continue;}
                var window=t.GetComponentInParent<UIXRWindow>(true);
                if(window!=null)
                {
                    if(t==window.transform&&window.Canvas!=null)
                    {
                        var corners=new Vector3[4];((RectTransform)window.Canvas.transform).GetWorldCorners(corners);points.AddRange(corners);
                        if(window.GrabFrame!=null)foreach(var border in window.GrabFrame.GetComponentsInChildren<BoxCollider>(true))points.AddRange(SpatialGeometry.Corners(new Bounds(border.center,border.size),border.transform.localToWorldMatrix));
                    }
                    continue;
                }
                if(t is RectTransform rect&&rect.GetComponentInParent<Canvas>(true)?.rootCanvas.renderMode==RenderMode.WorldSpace)
                {var corners=new Vector3[4];rect.GetWorldCorners(corners);points.AddRange(corners);}
                var filter=t.GetComponent<MeshFilter>();
                if(filter!=null&&filter.sharedMesh!=null&&t.GetComponent<MeshRenderer>()!=null)points.AddRange(SpatialGeometry.Corners(filter.sharedMesh.bounds,t.localToWorldMatrix));
            }
            var inverse=Quaternion.Inverse(root.rotation);
            return SpatialGeometry.Project(points.Select(p=>inverse*(p-root.position)),Quaternion.identity);
        }
        public static SpatialBox CurrentBox(Transform root)
        {var bounds=Measure(root);return new SpatialBox(root.position+root.rotation*bounds.center,bounds.size,root.rotation);}
        public List<SpatialBox> Reservations(Transform exclude=null,Transform excludeOther=null)
        {
            var roots=new HashSet<Transform>(placements.Keys.Where(t=>t!=null));
            foreach(var panel in FindObjectsByType<ManagedPanel>(FindObjectsSortMode.None))if(panel.gameObject.scene==gameObject.scene)roots.Add(panel.transform);
            foreach(var window in FindObjectsByType<UIXRWindow>(FindObjectsSortMode.None))
                if(window.gameObject.scene==gameObject.scene&&(window.GetComponent<Seyra.UI.SeyraSpatialChatWindow>()!=null||window.GetComponent<Seyra.UI.SeyraSpatialMenu>()!=null))roots.Add(window.transform);
            var result=new List<SpatialBox>();
            foreach(var root in roots)
            {
                if(!root.gameObject.activeInHierarchy||root==exclude||exclude!=null&&(root.IsChildOf(exclude)||exclude.IsChildOf(root))||root==excludeOther||excludeOther!=null&&(root.IsChildOf(excludeOther)||excludeOther.IsChildOf(root)))continue;
                if(roots.Any(parent=>parent!=root&&parent!=null&&root.IsChildOf(parent)))continue;
                var panel=root.GetComponent<ManagedPanel>();
                if(panel!=null&&panel.Window!=null&&!panel.Window.gameObject.activeInHierarchy)continue;
                if(panel!=null&&panel.IsPresentation&&(bool?)panel.InteractionState["visible"]==false)continue;
                try{result.Add(CurrentBox(root));}catch(ArgumentException){/* Geometry that cannot be measured never supplies an invented reservation. */}
            }
            return result;
        }
        public SpatialPlan Plan(SpatialPlacementRequest request,Pose view,Transform exclude=null,Transform excludeOther=null)=>SpatialPlacementPlanner.Plan(Snapshot,request,view,Reservations(exclude,excludeOther));
        public void Register(Transform root,SpatialCandidate candidate)=>placements[root]=candidate;
        public SpatialCandidate Placement(Transform root)=>placements.TryGetValue(root,out var candidate)?candidate:null;
        public bool NeedsPlacement(Transform root)
        {
            var known=Placement(root);if(known==null)return false;
            if(Snapshot.State!=RoomState.Ready&&known.PhysicalId!=null)return true;
            var current=new SpatialCandidate{Destination=known.Destination,PhysicalId=known.PhysicalId,Box=CurrentBox(root),Pose=new Pose(root.position,root.rotation)};
            return SpatialPlacementPlanner.ValidateGeometry(Snapshot,current,Reservations(root))!=null;
        }
        public bool PathClear(SpatialPlacementRequest request,Pose from,Pose to,Pose view,Transform exclude,Transform excludeOther=null)
        {
            // The union AABB encloses the entire linear translation, expanded by the root's
            // maximum corner radius to enclose all intermediate yaw rotations conservatively.
            float radius=request.Content.center.magnitude+request.Content.extents.magnitude;
            var min=Vector3.Min(from.position,to.position)-Vector3.one*radius;var max=Vector3.Max(from.position,to.position)+Vector3.one*radius;
            var swept=new SpatialBox((min+max)*.5f,max-min,Quaternion.identity);
            if(Snapshot.Objects.Any(o=>SpatialCollision.Intersects(swept,o.Box)))return false;
            if(Snapshot.Surfaces.Any(s=>s.Kind!="tabletop"&&s.Kind!="opening"&&SpatialCollision.Intersects(swept,s)))return false;
            return !Reservations(exclude,excludeOther).Any(r=>SpatialCollision.Intersects(swept,r,SpatialPlacementSettings.ReservationGap));
        }
    }
}
