using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.SceneTools;
using Seyra.UI;
using UnityEngine;

namespace Seyra.Spatial
{
    [DisallowMultipleComponent]
    public sealed class SeyraLayoutGroup:MonoBehaviour
    {
        JObject specification;
        public JObject Specification=>specification==null?null:(JObject)specification.DeepClone();
        public void Configure(JObject value)
        {
            specification=value==null?null:(JObject)value.DeepClone();Refresh();
        }
        void OnEnable()=>Refresh();
        void OnDisable()
        {
            DrivenTransform.Unregister(this);
            var follow=GetComponent<SeyraLazyFollow>();if(follow!=null)follow.enabled=false;
        }
        public void Refresh()
        {
            bool live=isActiveAndEnabled;string behavior=(string)specification?["behavior"];
            foreach(Transform child in transform)
            {
                var billboard=child.GetComponent<SeyraBillboard>();
                if(behavior=="billboard"&&billboard==null)billboard=child.gameObject.AddComponent<SeyraBillboard>();
                if(billboard!=null)billboard.enabled=live&&behavior=="billboard";
            }
            var follow=GetComponent<SeyraLazyFollow>();
            if(behavior=="lazy_follow"&&follow==null)follow=gameObject.AddComponent<SeyraLazyFollow>();
            if(follow!=null)
            {
                follow.enabled=live&&behavior=="lazy_follow";
                if(follow.enabled)
                {
                    var head=SeyraLazyFollow.ResolveHead();
                    if(head!=null)follow.ConfigureOffset(Quaternion.Inverse(Quaternion.Euler(0,head.eulerAngles.y,0))*(transform.position-head.position));
                }
            }
            DrivenTransform.Unregister(this);if(live&&behavior=="lazy_follow")DrivenTransform.Register(transform,DrivenChannels.Position|DrivenChannels.Rotation,this);
        }
    }
}
