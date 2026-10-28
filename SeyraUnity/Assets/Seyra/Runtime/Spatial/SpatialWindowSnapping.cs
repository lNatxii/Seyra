using System;
using System.Collections.Generic;
using System.Linq;
using Seyra.SceneTools;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.Spatial
{
    // Snaps released managed windows to a nearby wall or table. The chat and menu belong to SpatialFollowController.
    [DisallowMultipleComponent,DefaultExecutionOrder(100),RequireComponent(typeof(SpatialPlacementService))]
    public sealed class SpatialWindowSnapping : MonoBehaviour
    {
        const float DiscoveryInterval=.25f, PreviewInterval=.1f, PreviewWidth=.006f;
        static readonly Color SnapColor=Color.white, InvalidColor=new Color(1,.55f,0);
        sealed class Subscription{public UIMetaGrabFrame Frame;public Action<bool> Handler;}
        sealed class SurfaceOffset{public SpatialCandidate Candidate;public Pose Offset;}
        readonly Dictionary<Transform,Subscription> windows=new Dictionary<Transform,Subscription>();
        readonly Dictionary<Transform,SurfaceOffset> snapped=new Dictionary<Transform,SurfaceOffset>();
        readonly HashSet<Transform> released=new HashSet<Transform>();
        SpatialPlacementService service;
        LineRenderer preview;
        Material previewMaterial;
        float nextDiscovery,nextPreview;
        long revision=-1;
        public LineRenderer Preview=>preview;
        public int WindowCount{get{Discover();return windows.Count;}}
        void Awake()
        {
            service=GetComponent<SpatialPlacementService>();
            var go=new GameObject("SnapPreview");go.transform.SetParent(transform,false);go.SetActive(false);
            preview=go.AddComponent<LineRenderer>();preview.loop=true;preview.useWorldSpace=true;preview.positionCount=4;preview.startWidth=preview.endWidth=PreviewWidth;
            var shader=Shader.Find("Sprites/Default")??Shader.Find("Universal Render Pipeline/Unlit");
            if(shader!=null)preview.sharedMaterial=previewMaterial=new Material(shader);
        }
        void OnDestroy()
        {
            foreach(var subscription in windows.Values)if(subscription.Frame!=null)subscription.Frame.GrabChanged-=subscription.Handler;
            windows.Clear();snapped.Clear();released.Clear();
            if(previewMaterial!=null)Destroy(previewMaterial);
        }
        public bool IsManaged(Transform root)=>root!=null&&Eligible(root);
        bool Eligible(Transform root)
        {
            var panel=root.GetComponent<ManagedPanel>();
            if(panel==null||root.GetComponent<GeneratedObject>()==null||!root.gameObject.activeInHierarchy||root.gameObject.scene!=gameObject.scene||Frame(root)==null)return false;
            if(DrivenTransform.Channels(root)!=DrivenChannels.None)return false;
            // Cards of a following layout are moved by the layout root, which also owns their grab.
            var layout=root.parent!=null?root.parent.GetComponentInParent<SeyraLayoutGroup>():null;
            return layout==null||(string)layout.Specification?["behavior"]!="lazy_follow";
        }
        static UIMetaGrabFrame Frame(Transform root){var window=root.GetComponent<ManagedPanel>()?.Window;return window!=null?window.GrabFrame:null;}
        static bool Grabbed(Transform root){var frame=Frame(root);return frame!=null&&frame.IsGrabbed;}
        void Discover()
        {
            foreach(var root in windows.Keys.ToArray())
            {
                if(root!=null&&Eligible(root)&&windows[root].Frame==Frame(root))continue;
                var subscription=windows[root];if(subscription.Frame!=null)subscription.Frame.GrabChanged-=subscription.Handler;
                windows.Remove(root);released.Remove(root);snapped.Remove(root);
            }
            foreach(var root in snapped.Keys.ToArray())if(root==null||!windows.ContainsKey(root))snapped.Remove(root);
            foreach(var panel in FindObjectsByType<ManagedPanel>(FindObjectsSortMode.None))
            {
                var root=panel.transform;
                if(windows.ContainsKey(root)||!Eligible(root))continue;
                var subscription=new Subscription{Frame=Frame(root)};subscription.Handler=held=>GrabChanged(root,held);
                subscription.Frame.GrabChanged+=subscription.Handler;windows[root]=subscription;
            }
        }
        void GrabChanged(Transform root,bool held)
        {
            if(root==null)return;
            if(held)released.Remove(root);else released.Add(root);
        }
        void LateUpdate()
        {
            float now=Time.unscaledTime;
            if(now>=nextDiscovery){nextDiscovery=now+DiscoveryInterval;Discover();}
            // Releases are applied here, after the interaction SDK has written its last pose for the frame.
            foreach(var root in released.ToArray()){released.Remove(root);if(root!=null&&!Grabbed(root))Release(root);}
            var snapshot=service.Snapshot;
            if(snapshot.Revision!=revision){revision=snapshot.Revision;Revalidate(snapshot);}
            UpdatePreview(now);
        }
        // Test entry point: runs the release logic in the current frame without native grab events.
        public string SimulateRelease(Transform root)
        {
            if(root==null||!Eligible(root))return "unmanaged";
            Discover();released.Remove(root);return Release(root);
        }
        string Release(Transform root)
        {
            if(!Eligible(root))return "unmanaged";
            snapped.TryGetValue(root,out var previous);snapped.Remove(root);
            var snapshot=service.Snapshot;
            if(snapshot.State==RoomState.Loading||snapshot.State==RoomState.Suspended||snapshot.State==RoomState.WaitingForPermission)return "unchanged";
            var request=Request(root);var current=new Pose(root.position,root.rotation);var reserved=service.Reservations(root);
            var snap=snapshot.State==RoomState.Ready?Snap(snapshot,request,current,reserved):null;
            if(snap!=null)
            {
                root.SetPositionAndRotation(snap.Pose.position,snap.Pose.rotation);service.Register(root,snap);
                var surface=Surface(snapshot,snap);
                snapped[root]=new SurfaceOffset{Candidate=snap,Offset=new Pose(Quaternion.Inverse(surface.Pose.rotation)*(snap.Pose.position-surface.Pose.position),Quaternion.Inverse(surface.Pose.rotation)*snap.Pose.rotation)};
                return "snapped";
            }
            var air=new SpatialCandidate{Destination="air",Pose=current,Box=SpatialPlacementPlanner.BoxAt(request,current)};
            if(snapshot.State!=RoomState.Ready||SpatialPlacementPlanner.ValidateGeometry(snapshot,air,reserved)==null){service.Register(root,air);return "air";}
            var last=service.Placement(root);
            if(last==null)return "unchanged";
            root.SetPositionAndRotation(last.Pose.position,last.Pose.rotation);
            if(previous!=null&&previous.Candidate==last)snapped[root]=previous;
            return "restored";
        }
        static SpatialPlacementRequest Request(Transform root)=>new SpatialPlacementRequest{Content=SpatialPlacementService.Measure(root),Readable=true};
        static PhysicalSurface Surface(RoomSnapshot snapshot,SpatialCandidate candidate)=>snapshot.Surfaces.FirstOrDefault(s=>s.PhysicalId==candidate.PhysicalId&&s.Kind==candidate.Destination);
        SpatialCandidate Snap(RoomSnapshot snapshot,SpatialPlacementRequest request,Pose current,List<SpatialBox> reserved)
        {
            Pose view;
            try{view=service.View(currentYaw:true);}catch(InvalidOperationException){return null;}
            return SpatialPlacementPlanner.Snap(snapshot,request,current,view,reserved);
        }
        void Revalidate(RoomSnapshot snapshot)
        {
            if(snapshot.State!=RoomState.Ready)return;
            foreach(var pair in snapped.ToArray())
            {
                var root=pair.Key;var offset=pair.Value;
                if(root==null||service.Placement(root)!=offset.Candidate){snapped.Remove(root);continue;}
                if(Grabbed(root))continue;
                // A missing surface or an invalid recomputed pose is reported by NeedsPlacement; nothing moves.
                var surface=Surface(snapshot,offset.Candidate);if(surface==null)continue;
                var pose=offset.Offset.GetTransformedBy(surface.Pose);
                var candidate=new SpatialCandidate{Destination=offset.Candidate.Destination,PhysicalId=offset.Candidate.PhysicalId,Pose=pose,Box=SpatialPlacementPlanner.BoxAt(Request(root),pose)};
                if(SpatialPlacementPlanner.ValidateGeometry(snapshot,candidate,service.Reservations(root))!=null)continue;
                root.SetPositionAndRotation(pose.position,pose.rotation);service.Register(root,candidate);offset.Candidate=candidate;
            }
        }
        void UpdatePreview(float now)
        {
            var root=windows.Keys.FirstOrDefault(r=>r!=null&&Grabbed(r));
            if(root==null){HidePreview();return;}
            if(now<nextPreview)return;
            nextPreview=now+PreviewInterval;
            var snapshot=service.Snapshot;var request=Request(root);var current=new Pose(root.position,root.rotation);var reserved=service.Reservations(root);
            var snap=snapshot.State==RoomState.Ready?Snap(snapshot,request,current,reserved):null;
            if(snap!=null){DrawPreview(snap.Box,SnapColor);return;}
            var air=new SpatialCandidate{Destination="air",Pose=current,Box=SpatialPlacementPlanner.BoxAt(request,current)};
            if(snapshot.State!=RoomState.Ready||SpatialPlacementPlanner.ValidateGeometry(snapshot,air,reserved)==null){HidePreview();return;}
            DrawPreview(air.Box,InvalidColor);
        }
        void DrawPreview(SpatialBox box,Color color)
        {
            var half=box.Size*.5f;
            preview.SetPosition(0,box.Center+box.Rotation*new Vector3(-half.x,-half.y,0));preview.SetPosition(1,box.Center+box.Rotation*new Vector3(half.x,-half.y,0));
            preview.SetPosition(2,box.Center+box.Rotation*new Vector3(half.x,half.y,0));preview.SetPosition(3,box.Center+box.Rotation*new Vector3(-half.x,half.y,0));
            preview.startColor=preview.endColor=color;if(previewMaterial!=null)previewMaterial.color=color;
            if(!preview.gameObject.activeSelf)preview.gameObject.SetActive(true);
        }
        void HidePreview()
        {
            nextPreview=0;
            if(preview!=null&&preview.gameObject.activeSelf)preview.gameObject.SetActive(false);
        }
    }
}
