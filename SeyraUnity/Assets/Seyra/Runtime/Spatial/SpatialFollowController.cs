using System;
using System.Linq;
using Seyra.UI;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.Spatial
{
    // Owns the application anchor, never Generated content. Native grabs retain priority.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class SpatialFollowController : MonoBehaviour
    {
        public enum FollowMode { Adaptive, Pinned, Suspended }
        public enum FollowState { Stable, Waiting, Plan, Moving, Cooldown, Recovery }
        [SerializeField] Transform head;
        [SerializeField] SeyraSpatialChatWindow chat;
        [SerializeField] UIXRWindow chatWindow,menuWindow;
        [SerializeField] SpatialPlacementService placement;
        bool pinned,paused,focused=true,initialized,wasHeld,roomInitialized;
        float outsideTime,stableTime,lastInteraction=float.NegativeInfinity,cooldown;
        Quaternion previousHead;
        string previousRoom;
        long movingRevision;
        SpatialPlacementRequest moveRequest;
        Pose destination;
        UIMetaGrabFrame chatGrab,menuGrab;
        TMP_InputField[] inputs=Array.Empty<TMP_InputField>();
        ScrollRect[] scrolls=Array.Empty<ScrollRect>();
        SpatialFollowInteraction[] interactions=Array.Empty<SpatialFollowInteraction>();
#if UNITY_EDITOR
        bool testTracking;
        public void UseTestTracking(bool value){testTracking=value;initialized=false;}
#endif
        public FollowMode Mode { get; private set; }=FollowMode.Adaptive;
        public FollowState State { get; private set; }=FollowState.Stable;
        public string Status { get; private set; }="Seguimiento adaptativo";
        public void Configure(Transform viewer,SeyraSpatialChatWindow conversation,UIXRWindow window,UIXRWindow menu,SpatialPlacementService service)
        {Unsubscribe();head=viewer;chat=conversation;chatWindow=window;menuWindow=menu;placement=service;initialized=false;if(isActiveAndEnabled)Subscribe();}
        void OnEnable()=>Subscribe();
        void Subscribe()
        {
            if(chatWindow==null)chatWindow=GetComponentsInChildren<UIXRWindow>(true).FirstOrDefault(w=>w.GetComponent<SeyraSpatialChatWindow>()!=null);
            if(menuWindow==null)menuWindow=GetComponentsInChildren<UIXRWindow>(true).FirstOrDefault(w=>w.GetComponent<SeyraSpatialMenu>()!=null);
            if(chat==null&&chatWindow!=null)chat=chatWindow.GetComponent<SeyraSpatialChatWindow>();
            head=SeyraLazyFollow.ResolveHead(head);
            if(placement==null)placement=SpatialPlacementService.ForScene(gameObject.scene);
            chatGrab=chatWindow?.GrabFrame;menuGrab=menuWindow?.GrabFrame;
            chatGrab?.Grabbable?.InjectOptionalTargetTransform(transform);menuGrab?.Grabbable?.InjectOptionalTargetTransform(transform);
            if(chatGrab!=null)chatGrab.GrabChanged+=GrabChanged;if(menuGrab!=null)menuGrab.GrabChanged+=GrabChanged;
            foreach(var legacy in GetComponentsInChildren<SeyraLazyFollow>(true))legacy.enabled=false;
            if(chatWindow!=null)
            {
                inputs=chatWindow.GetComponentsInChildren<TMP_InputField>(true);scrolls=chatWindow.GetComponentsInChildren<ScrollRect>(true);
                foreach(var scroll in scrolls)if(scroll.GetComponent<SpatialFollowInteraction>()==null)scroll.gameObject.AddComponent<SpatialFollowInteraction>();
                interactions=chatWindow.GetComponentsInChildren<SpatialFollowInteraction>(true);
            }
        }
        void Unsubscribe(){if(chatGrab!=null)chatGrab.GrabChanged-=GrabChanged;if(menuGrab!=null)menuGrab.GrabChanged-=GrabChanged;chatGrab=null;menuGrab=null;}
        void OnDisable(){Unsubscribe();ResetWait();}
        void OnApplicationPause(bool value)=>paused=value;
        void OnApplicationFocus(bool value)=>focused=value;
        void GrabChanged(bool held)
        {
            lastInteraction=Time.unscaledTime;ResetWait();
            if(held)wasHeld=true;
            else if(wasHeld){pinned=true;wasHeld=false;Status="Fijado · pulsa Seguirme";}
        }
        void ResetWait(){outsideTime=0;stableTime=0;State=FollowState.Stable;}
        bool Held=>chatGrab!=null&&chatGrab.IsGrabbed||menuGrab!=null&&menuGrab.IsGrabbed;
        public bool InteractionBlocked=>Held||chat!=null&&(chat.IsDictating||chat.Dictation!=null&&chat.Dictation.Busy||chat.PermissionPanelOpen)||
            inputs.Any(i=>i!=null&&i.isActiveAndEnabled&&i.isFocused)||scrolls.Any(s=>s!=null&&s.isActiveAndEnabled&&s.velocity.sqrMagnitude>.0001f)||
            interactions.Any(i=>i!=null&&i.isActiveAndEnabled&&(i.Held||Time.unscaledTime-i.LastActivity<SpatialPlacementSettings.InteractionDelay));
        bool TrackingReady()
        {
            if(head==null||!SpatialGeometry.Finite(head.forward)||new Vector2(head.forward.x,head.forward.z).sqrMagnitude<.000001f)return false;
#if UNITY_EDITOR
            if(testTracking)return head!=null;
#endif
            return !paused&&focused&&head!=null&&(placement.Room!=null?placement.Room.ViewerReady:OVRManager.isHmdPresent&&OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter));
        }
        public void ResumeFollow(){pinned=false;ResetWait();lastInteraction=Time.unscaledTime;Status="Seguimiento adaptativo";}
        void LateUpdate()=>Tick(Time.unscaledDeltaTime);
        public void Tick(float dt)
        {
            if(!float.IsFinite(dt)||dt<=0||chatWindow==null||placement==null)return;
            var room=placement.Snapshot;
            if(!TrackingReady()||!SpatialGeometry.Finite(head.position)||room.State==RoomState.Loading||room.State==RoomState.Suspended||room.State==RoomState.WaitingForPermission)
            {Mode=FollowMode.Suspended;if(State!=FollowState.Recovery)ResetWait();initialized=false;Status="Seguimiento suspendido";return;}
            Mode=pinned?FollowMode.Pinned:FollowMode.Adaptive;
            if(pinned)Status="Fijado · pulsa Seguirme";else if(State==FollowState.Stable&&Status=="Seguimiento suspendido")Status="Seguimiento adaptativo";
            if(!initialized){previousHead=head.rotation;if(!roomInitialized){previousRoom=room.RoomId;roomInitialized=true;}initialized=true;}
            if(room.State==RoomState.Ready&&room.RoomId!=previousRoom){previousRoom=room.RoomId;State=FollowState.Recovery;Status="Habitación cambiada · pulsa Reubicar";}
            float angularSpeed=Quaternion.Angle(head.rotation,previousHead)/dt;previousHead=head.rotation;
            if(InteractionBlocked){lastInteraction=Time.unscaledTime;if(State!=FollowState.Recovery)ResetWait();return;}
            if(Mode==FollowMode.Pinned||State==FollowState.Recovery||!chatWindow.gameObject.activeInHierarchy)return;
            if(Time.unscaledTime-lastInteraction<SpatialPlacementSettings.InteractionDelay){ResetWait();return;}
            if(State==FollowState.Moving)
            {
                if(room.Revision!=movingRevision){Recover("La habitación cambió durante el movimiento");return;}
                var view=new Pose(head.position,SpatialGeometry.Yaw(head.forward));
                if(!placement.PathClear(moveRequest,new Pose(chatWindow.transform.position,chatWindow.transform.rotation),destination,view,chatWindow.transform,menuWindow?.transform))
                {Recover("El recorrido está ocupado");return;}
                float smoothing=1-Mathf.Exp(-dt/SpatialPlacementSettings.ResponseTime);
                var next=new Pose(Vector3.MoveTowards(chatWindow.transform.position,Vector3.Lerp(chatWindow.transform.position,destination.position,smoothing),SpatialPlacementSettings.FollowSpeed*dt),
                    Quaternion.RotateTowards(chatWindow.transform.rotation,Quaternion.Slerp(chatWindow.transform.rotation,destination.rotation,smoothing),SpatialPlacementSettings.FollowAngularSpeed*dt));
                MoveGroup(next);
                if(Vector3.Distance(next.position,destination.position)<.005f&&Quaternion.Angle(next.rotation,destination.rotation)<.5f){State=FollowState.Cooldown;cooldown=SpatialPlacementSettings.Cooldown;}
                return;
            }
            if(State==FollowState.Cooldown){cooldown-=dt;if(cooldown<=0){State=FollowState.Stable;Status="Seguimiento adaptativo";}return;}
            var delta=Quaternion.Inverse(SpatialGeometry.Yaw(head.forward))*(chatWindow.transform.position-head.position);
            float yaw=Mathf.Abs(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg),distance=delta.magnitude;
            bool outside=yaw>SpatialPlacementSettings.TriggerYaw||distance<SpatialPlacementSettings.TriggerMin||distance>SpatialPlacementSettings.TriggerMax;
            bool comfortable=yaw<=SpatialPlacementSettings.ComfortYaw&&distance>=SpatialPlacementSettings.ComfortMin&&distance<=SpatialPlacementSettings.ComfortMax;
            if(comfortable){ResetWait();return;}
            if(!outside){ResetWait();return;}
            State=FollowState.Waiting;outsideTime+=dt;stableTime=angularSpeed<SpatialPlacementSettings.HeadSpeed?stableTime+dt:0;
            if(outsideTime<SpatialPlacementSettings.FollowDelay||stableTime<SpatialPlacementSettings.HeadStableTime)return;
            State=FollowState.Plan;
            if(yaw>SpatialPlacementSettings.LongTurn){Recover("El chat quedó detrás");return;}
            if(!TryTarget(out var plan)){Recover("No hay un destino compatible");return;}
            destination=plan.Best.Pose;movingRevision=room.Revision;
            if(!placement.PathClear(moveRequest,new Pose(chatWindow.transform.position,chatWindow.transform.rotation),destination,new Pose(head.position,SpatialGeometry.Yaw(head.forward)),chatWindow.transform,menuWindow?.transform))
            {Recover("El recorrido está ocupado");return;}
            State=FollowState.Moving;Status="Reubicando chat";
        }
        SpatialPlacementRequest GroupRequest()
        {
            var content=SpatialPlacementService.Measure(chatWindow.transform);
            if(menuWindow!=null&&menuWindow.gameObject.activeInHierarchy)
            {
                var inverse=Quaternion.Inverse(chatWindow.transform.rotation);
                foreach(var p in SpatialPlacementService.CurrentBox(menuWindow.transform).Corners)content.Encapsulate(inverse*(p-chatWindow.transform.position));
            }
            return new SpatialPlacementRequest{Content=content,Readable=true,Destination="air"};
        }
        bool TryTarget(out SpatialPlan plan)
        {
            moveRequest=GroupRequest();var view=new Pose(head.position,SpatialGeometry.Yaw(head.forward));
            plan=placement.Plan(moveRequest,view,chatWindow.transform,menuWindow?.transform);return plan.Best!=null;
        }
        void MoveGroup(Pose pose)
        {
            var rotation=pose.rotation*Quaternion.Inverse(chatWindow.transform.rotation);
            transform.SetPositionAndRotation(pose.position+rotation*(transform.position-chatWindow.transform.position),rotation*transform.rotation);
        }
        void Recover(string reason){State=FollowState.Recovery;Status=reason+" · pulsa Reubicar";}
        public bool Recenter()
        {
            if(chatWindow==null||placement==null||!TrackingReady()||InteractionBlocked)return false;
            bool opened=chatWindow.gameObject.activeSelf;chatWindow.Open();
            if(!TryTarget(out var plan)){if(!opened)chatWindow.Close();Status="Sin destino compatible · se conserva la posición";return false;}
            MoveGroup(plan.Best.Pose);ResetWait();State=FollowState.Cooldown;cooldown=SpatialPlacementSettings.Cooldown;
            Status=plan.PhysicalValidation?"Compatible con la habitación capturada":"En el aire · sin validación física";
            return true;
        }
    }
}
