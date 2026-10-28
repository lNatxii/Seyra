using UnityEngine;
using UnityEngine.EventSystems;

namespace Seyra.Spatial
{
    public sealed class SpatialFollowInteraction : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IScrollHandler, IPointerDownHandler, IPointerUpHandler
    {
        public bool Held { get; private set; }
        public float LastActivity { get; private set; }=float.NegativeInfinity;
        void Touch()=>LastActivity=Time.unscaledTime;
        public void OnBeginDrag(PointerEventData data){Held=true;Touch();}
        public void OnEndDrag(PointerEventData data){Held=false;Touch();}
        public void OnScroll(PointerEventData data)=>Touch();
        public void OnPointerDown(PointerEventData data){Held=true;Touch();}
        public void OnPointerUp(PointerEventData data){Held=false;Touch();}
        void OnDisable(){Held=false;Touch();}
    }
}
