using System;
using Seyra.Spatial;
using Seyra.UI;
using UnityEngine;

namespace Seyra.SceneTools
{
    [DefaultExecutionOrder(60), DisallowMultipleComponent]
    public sealed class SeyraFollowTarget : MonoBehaviour
    {
        ManagedPanel panel;
        BoxCollider selection;
        public Transform Target { get; private set; }
        public string Side { get; private set; } = "above";
        public float Gap { get; private set; } = .05f;
        public void Configure(Transform target, BoxCollider selectionBox, string side, float gap)
        {
            if (target == null || selectionBox == null || (side != "above" && side != "left" && side != "right") ||
                float.IsNaN(gap) || float.IsInfinity(gap) || gap < 0 || gap > .5f)
                throw new ArgumentException("Invalid detail follow configuration.");
            panel = GetComponent<ManagedPanel>();
            if (panel == null || panel.Window == null) throw new ArgumentException("A detail requires a managed window.");
            Target = target; selection = selectionBox; Side = side; Gap = gap;
            panel.SetGrabEnabled(false);
            if (GetComponent<SeyraBillboard>() == null) gameObject.AddComponent<SeyraBillboard>();
            Snap();
        }
        void OnEnable() => DrivenTransform.Register(transform, DrivenChannels.Position, this);
        void OnDisable() => DrivenTransform.Unregister(this);
        bool Visible => Target != null && selection != null && panel != null && panel.WindowVisible && panel.Window.gameObject.activeInHierarchy;
        void LateUpdate() { if (Visible) Follow(); }
        public void Snap()
        {
            if (!Visible) return;
            Follow();
            GetComponent<SeyraBillboard>()?.Snap();
            Follow();
        }
        void Follow()
        {
            var head = SeyraLazyFollow.ResolveHead();
            if (head == null) return;
            Vector3 right = head.right; right.y = 0;
            if (right.sqrMagnitude < .000001f) right = Vector3.right;
            else right.Normalize();
            var box = new Bounds(selection.center, selection.size);
            var matrix = selection.transform.localToWorldMatrix;
            Vector3 center = matrix.MultiplyPoint3x4(box.center);
            float maxY = float.NegativeInfinity, maxSide = float.NegativeInfinity;
            var axis = Side == "left" ? -right : right;
            for (int i = 0; i < 8; i++)
            {
                var corner = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? box.min.x : box.max.x,
                    (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z));
                maxY = Mathf.Max(maxY, corner.y);
                maxSide = Mathf.Max(maxSide, Vector3.Dot(corner - center, axis));
            }
            var surface = panel.Surface;
            var rect = surface.rect;
            var halfWidth = surface.TransformVector(Vector3.right * rect.width * .5f);
            var halfHeight = surface.TransformVector(Vector3.up * rect.height * .5f);
            Vector3 desired;
            if (Side == "above") desired = new Vector3(center.x, maxY + Gap + Mathf.Abs(halfWidth.y) + Mathf.Abs(halfHeight.y), center.z);
            else desired = center + axis * (maxSide + Gap + Mathf.Abs(Vector3.Dot(halfWidth, axis)) + Mathf.Abs(Vector3.Dot(halfHeight, axis)));
            transform.position += desired - surface.TransformPoint(rect.center);
        }
    }
}
