using System;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Seyra.UI.Library
{
    /// <summary>Fits Meta's native panel border/affordances to an existing UI Set canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class UIMetaGrabFrame : MonoBehaviour
    {
        [SerializeField] RectTransform canvas;
        [SerializeField] Grabbable grabbable;
        [SerializeField] Transform[] corners;
        [SerializeField] BoxCollider[] borders;
        [SerializeField] BoundsClipper[] clippers;
        [SerializeField] float borderWidth = .08f;
        [SerializeField] float borderDepth = .05f;
        readonly Vector3[] canvasCorners = new Vector3[4];
        Vector2 lastSize;
        Vector3 lastCanvasPosition, lastCanvasScale;
        Quaternion lastCanvasRotation;
        bool wasGrabbed;

        public Grabbable Grabbable => grabbable;
        public bool IsGrabbed => grabbable != null && grabbable.SelectingPointsCount > 0;
        public event Action<bool> GrabChanged;
        public event Action GeometryChanged;

        public void Configure(RectTransform surface, Grabbable nativeGrab, Transform[] cornerAnchors,
            BoxCollider[] borderColliders, BoundsClipper[] borderClippers, float width, float depth)
        {
            canvas = surface; grabbable = nativeGrab; corners = cornerAnchors;
            borders = borderColliders; clippers = borderClippers;
            borderWidth = width; borderDepth = depth;
            RefreshGeometry();
        }

        void OnEnable() { RefreshGeometry(); }
        void OnDisable()
        {
            if (!wasGrabbed) return;
            wasGrabbed = false; GrabChanged?.Invoke(false);
        }
        void LateUpdate()
        {
            if (canvas == null) return;
            if (lastSize != canvas.rect.size || lastCanvasPosition != canvas.localPosition ||
                lastCanvasRotation != canvas.localRotation || lastCanvasScale != canvas.localScale)
                RefreshGeometry();
            var grabbed = IsGrabbed;
            if (grabbed != wasGrabbed) { wasGrabbed = grabbed; GrabChanged?.Invoke(grabbed); }
        }

        public void RefreshGeometry()
        {
            if (canvas == null || corners == null || corners.Length != 4 || borders == null || borders.Length != 4 ||
                clippers == null || clippers.Length != 4) return;
            var parent = transform.parent;
            if (parent == null) return;
            var changed=lastSize!=canvas.rect.size || lastCanvasPosition!=canvas.localPosition || lastCanvasRotation!=canvas.localRotation || lastCanvasScale!=canvas.localScale;
            // Canvas pivots belong to layout. Move the frame, never the user's content.
            transform.localPosition = parent.InverseTransformPoint(canvas.TransformPoint(canvas.rect.center));
            transform.localRotation = Quaternion.Inverse(parent.rotation) * canvas.rotation;
            transform.localScale = Vector3.one;
            canvas.GetWorldCorners(canvasCorners);
            var bottomLeft = transform.InverseTransformPoint(canvasCorners[0]);
            var topRight = transform.InverseTransformPoint(canvasCorners[2]);
            var size = new Vector2(Mathf.Abs(topRight.x - bottomLeft.x), Mathf.Abs(topRight.y - bottomLeft.y));
            var half = size * .5f; var band = borderWidth; var h = band * .5f;
            corners[0].localPosition = new Vector3(-half.x - h, half.y + h, 0);
            corners[1].localPosition = new Vector3(half.x + h, half.y + h, 0);
            corners[2].localPosition = new Vector3(-half.x - h, -half.y - h, 0);
            corners[3].localPosition = new Vector3(half.x + h, -half.y - h, 0);
            SetBorder(0, new Vector3(0, half.y + h, 0), new Vector3(size.x + 2 * band, band, borderDepth));
            SetBorder(1, new Vector3(0, -half.y - h, 0), new Vector3(size.x + 2 * band, band, borderDepth));
            SetBorder(2, new Vector3(-half.x - h, 0, 0), new Vector3(band, size.y, borderDepth));
            SetBorder(3, new Vector3(half.x + h, 0, 0), new Vector3(band, size.y, borderDepth));
            lastSize = canvas.rect.size; lastCanvasPosition = canvas.localPosition;
            lastCanvasRotation = canvas.localRotation; lastCanvasScale = canvas.localScale;
            if(changed) GeometryChanged?.Invoke();
        }
        void SetBorder(int index, Vector3 center, Vector3 size)
        {
            borders[index].transform.localPosition = center;
            borders[index].size = size; clippers[index].Size = size;
        }
    }
}
