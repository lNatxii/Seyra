using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Seyra.Spatial
{
    public sealed class RoomContext : MonoBehaviour
    {
        public const string ScenePermission="com.oculus.permission.USE_SCENE";
        [SerializeField] MRUK roomProvider;
        [SerializeField] Transform viewer;
        readonly List<MRUKRoom> subscribedRooms=new List<MRUKRoom>();
        MRUKRoom currentRoom;
        bool stopping,dirty=true,simulated,loading,paused;
        int epoch;
        float nextPoll;
        long revision;
        public RoomSnapshot Snapshot { get; private set; }=new RoomSnapshot(0,null,false,RoomState.AirOnly);
        public string Diagnostic { get; private set; }="No captured room loaded.";
        public Transform Viewer=>viewer;
        public bool ViewerReady=>viewer!=null && (simulated || (!paused && OVRManager.isHmdPresent && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)));
        public event Action<RoomSnapshot> Changed;
        public void Configure(MRUK provider,Transform head) { roomProvider=provider;viewer=head; }
        void OnEnable()
        {
            stopping=false;
            if(roomProvider==null)roomProvider=FindFirstObjectByType<MRUK>();
            if(viewer==null)viewer=FindFirstObjectByType<OVRCameraRig>()?.centerEyeAnchor;
            if(roomProvider!=null)
            {
                roomProvider.SceneLoadedEvent.AddListener(Invalidate);
                roomProvider.RoomCreatedEvent.AddListener(RoomChanged);
                roomProvider.RoomUpdatedEvent.AddListener(RoomChanged);
                roomProvider.RoomRemovedEvent.AddListener(RoomChanged);
                SubscribeRooms();
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged+=OnPlayMode;
#endif
        }
        async void Start()
        {
            if(roomProvider==null){Publish(RoomState.Error,"MRUK is not configured.");return;}
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                Publish(RoomState.WaitingForPermission,"Waiting for spatial data permission.");
                var requestEpoch=epoch;var callbacks=new PermissionCallbacks();
                callbacks.PermissionGranted+=permission=>{if(!stopping&&requestEpoch==epoch)_=LoadDeviceAsync();};
                void Denied(string permission){if(!stopping&&requestEpoch==epoch)Publish(RoomState.PermissionDenied,"Spatial data permission was denied.");}
                callbacks.PermissionDenied+=Denied;
#pragma warning disable CS0618
                callbacks.PermissionDeniedAndDontAskAgain+=Denied;
#pragma warning restore CS0618
                Permission.RequestUserPermission(ScenePermission,callbacks);
                return;
            }
            await LoadDeviceAsync();
#else
            await Task.CompletedTask;
#endif
        }
        public async Task LoadDeviceAsync(bool requestCapture=false)
        {
            if(stopping||loading)return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(ScenePermission)){Publish(RoomState.PermissionDenied,"Spatial data permission is required.");return;}
            simulated=false;loading=true;var loadEpoch=epoch;Publish(RoomState.Loading,"Loading captured scene.");
            try
            {
                var result=await roomProvider.LoadSceneFromDevice(requestCapture,true,MRUK.SceneModel.V1);
                if(stopping||loadEpoch!=epoch)return;
                if(result==MRUK.LoadDeviceResult.Success){dirty=true;loading=false;Refresh();}
                else Publish(result==MRUK.LoadDeviceResult.NoScenePermission?RoomState.PermissionDenied:result==MRUK.LoadDeviceResult.NoRoomsFound?RoomState.AirOnly:RoomState.Unavailable,"MRUK load result: "+result);
            }
            catch(Exception error){if(!stopping&&loadEpoch==epoch)Publish(RoomState.Error,error.Message);}
            finally{if(loadEpoch==epoch)loading=false;}
#else
            Publish(RoomState.Unavailable,"Device scene loading is available only on Android.");await Task.CompletedTask;
#endif
        }
#if UNITY_EDITOR
        public async Task LoadTestRoomAsync(GameObject prefab,Transform testViewer)
        {
            if(!Application.isPlaying||prefab==null||testViewer==null||loading||stopping)throw new InvalidOperationException("Explicit test loading requires Play Mode, a prefab and a viewer.");
            var loadEpoch=++epoch;loading=true;simulated=true;viewer=testViewer;roomProvider.EnableWorldLock=false;
            Publish(RoomState.Loading,"Loading explicitly requested simulated room.");
            try
            {
                var result=await roomProvider.LoadSceneFromPrefab(prefab);
                if(stopping||loadEpoch!=epoch)return;
                if(result!=MRUK.LoadDeviceResult.Success){Publish(RoomState.Error,"Test room load result: "+result);return;}
                loading=false;dirty=true;Refresh();
            }
            catch(Exception error){if(!stopping&&loadEpoch==epoch)Publish(RoomState.Error,error.Message);throw;}
            finally{if(loadEpoch==epoch)loading=false;}
        }
        void OnPlayMode(UnityEditor.PlayModeStateChange state){if(state==UnityEditor.PlayModeStateChange.ExitingPlayMode)Stop();}
#endif
        void OnApplicationPause(bool value)
        {
            paused=value;
            // Permission dialogs also pause Android. Preserve permission/loading diagnostics
            // when there is no captured room to suspend or revalidate.
            if(simulated||roomProvider==null||roomProvider.Rooms.Count==0)return;
            if(value)Publish(RoomState.Suspended,"Application is paused.");else dirty=true;
        }
        void Update()
        {
            if(stopping||loading||Time.unscaledTime<nextPoll)return;
            nextPoll=Time.unscaledTime+.5f;
            if(simulated||Snapshot.State==RoomState.Ready||Snapshot.State==RoomState.Suspended||dirty||(roomProvider!=null&&roomProvider.Rooms.Count>0))Refresh();
        }
        void Invalidate()
        {
            if(stopping)return;
            Publish(RoomState.Suspended,"Captured geometry changed; waiting for a fresh snapshot.");dirty=true;
        }
        void RoomChanged(MRUKRoom room){Invalidate();SubscribeRooms();}
        void AnchorChanged(MRUKAnchor anchor){Invalidate();}
        void SubscribeRooms()
        {
            foreach(var room in subscribedRooms)if(room!=null){room.AnchorCreatedEvent.RemoveListener(AnchorChanged);room.AnchorUpdatedEvent.RemoveListener(AnchorChanged);room.AnchorRemovedEvent.RemoveListener(AnchorChanged);}
            subscribedRooms.Clear();
            if(roomProvider==null)return;
            foreach(var room in roomProvider.Rooms){subscribedRooms.Add(room);room.AnchorCreatedEvent.AddListener(AnchorChanged);room.AnchorUpdatedEvent.AddListener(AnchorChanged);room.AnchorRemovedEvent.AddListener(AnchorChanged);}
        }
        void Refresh()
        {
            if(roomProvider==null||stopping||loading)return;
            if(roomProvider.Rooms.Count==0){dirty=false;Publish(RoomState.AirOnly,"No captured room loaded.");return;}
            if(!ViewerReady){Publish(RoomState.Suspended,"Positional viewer tracking is unavailable.");return;}
            MRUKRoom selected=null;
            foreach(var room in roomProvider.Rooms)if(room.IsPositionInRoom(viewer.position,true)){selected=room;break;}
            if(selected==null){Publish(RoomState.AirOnly,"The viewer is outside the captured rooms.");currentRoom=null;return;}
            if(!simulated&&roomProvider.EnableWorldLock&&!roomProvider.IsWorldLockActive){Publish(RoomState.Suspended,"Waiting for room localization and World Lock.");return;}
            if(!dirty&&currentRoom==selected&&(Snapshot.State==RoomState.Ready||Snapshot.State==RoomState.Error))return;
            currentRoom=selected;dirty=false;SubscribeRooms();
            try{Snapshot=RoomSnapshotBuilder.Build(selected,++revision,simulated);Diagnostic=simulated?"Explicitly simulated captured geometry.":"Captured geometry only; moved furniture is unknown.";Changed?.Invoke(Snapshot);}
            catch(Exception error){Publish(RoomState.Error,error.Message);}
        }
        void Publish(RoomState state,string diagnostic)
        {
            dirty=false;
            if(Snapshot.State==state&&Diagnostic==diagnostic&&Snapshot.Simulated==simulated)return;
            Snapshot=new RoomSnapshot(++revision,null,simulated,state);Diagnostic=diagnostic;Changed?.Invoke(Snapshot);
        }
        void Stop()
        {
            if(stopping)return;stopping=true;loading=false;++epoch;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged-=OnPlayMode;
#endif
            if(roomProvider!=null){roomProvider.SceneLoadedEvent.RemoveListener(Invalidate);roomProvider.RoomCreatedEvent.RemoveListener(RoomChanged);roomProvider.RoomUpdatedEvent.RemoveListener(RoomChanged);roomProvider.RoomRemovedEvent.RemoveListener(RoomChanged);}
            foreach(var room in subscribedRooms)if(room!=null){room.AnchorCreatedEvent.RemoveListener(AnchorChanged);room.AnchorUpdatedEvent.RemoveListener(AnchorChanged);room.AnchorRemovedEvent.RemoveListener(AnchorChanged);}
            subscribedRooms.Clear();Publish(RoomState.Unavailable,"Room context stopped.");
        }
        void OnDisable()=>Stop();
        void OnDestroy()=>Stop();
    }
}
