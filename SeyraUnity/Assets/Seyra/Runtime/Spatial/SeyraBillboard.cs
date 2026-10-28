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
        void LateUpdate()
        {
            if(GetComponent<ManagedPanel>()?.Window?.GrabFrame?.IsGrabbed==true)return;
            var head=SeyraLazyFollow.ResolveHead();if(head==null)return;
            var direction=transform.position-head.position;direction.y=0;if(direction.sqrMagnitude<.000001f)return;
            float target=Quaternion.LookRotation(direction).eulerAngles.y;
            if(Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y,target))<2)return;
            transform.rotation=Quaternion.Euler(0,Mathf.SmoothDampAngle(transform.eulerAngles.y,target,ref velocity,.15f),0);
        }
    }
}
