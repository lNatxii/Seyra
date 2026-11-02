using Seyra.SceneTools;
using Seyra.UI;
using UnityEngine;

namespace Seyra.Spatial
{
    [DefaultExecutionOrder(50),DisallowMultipleComponent]
    public sealed class SeyraBillboard:MonoBehaviour
    {
        float velocity;
        void OnEnable()=>DrivenTransform.Register(transform,DrivenChannels.Rotation,this);
        void OnDisable()=>DrivenTransform.Unregister(this);
        bool Facing(out float yaw)
        {
            yaw=0;var head=SeyraLazyFollow.ResolveHead();if(head==null)return false;
            var direction=transform.position-head.position;direction.y=0;if(direction.sqrMagnitude<.000001f)return false;
            yaw=Quaternion.LookRotation(direction).eulerAngles.y;return true;
        }
        public void Snap()
        {
            if(Facing(out var yaw))transform.rotation=Quaternion.Euler(0,yaw,0);
            velocity=0;
        }
        void LateUpdate()
        {
            if(GetComponent<ManagedPanel>()?.Window?.GrabFrame?.IsGrabbed==true)return;
            if(!Facing(out var target))return;
            if(Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y,target))<2)return;
            transform.rotation=Quaternion.Euler(0,Mathf.SmoothDampAngle(transform.eulerAngles.y,target,ref velocity,.15f),0);
        }
    }
}
