using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seyra.SceneTools
{
    [DefaultExecutionOrder(-40), DisallowMultipleComponent]
    public sealed class SeyraInteractive : MonoBehaviour
    {
        static readonly HashSet<SeyraInteractive> active = new HashSet<SeyraInteractive>();
        readonly HashSet<int> hovering = new HashSet<int>();
        readonly HashSet<int> selecting = new HashSet<int>();
        SeyraInteractionRig rig;
        GameObject highlight;
        ManagedPanel detail;
        InteractionSpec specification;
        Vector3 savedPosition, savedScale, selectionPoint;
        Quaternion savedRotation;
        float selectedAt;
        bool selectionMoved;
        public InteractionSpec Specification => specification?.Copy();
        public BoxCollider SelectionBox { get; private set; }
        public RayInteractable Ray { get; private set; }
        public bool IsHeld => false; // Grabbing and returning are implemented in step 7.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => active.Clear();
        void OnEnable() => active.Add(this);
        void OnDisable()
        {
            active.Remove(this);
            hovering.Clear(); selecting.Clear();
            if (highlight != null) highlight.SetActive(false);
            if (detail != null) detail.SetWindowVisible(false);
        }
        public void Configure(InteractionSpec spec, IEnumerable<Vector3> geometryPoints, ManagedPanel detailPanel = null)
        {
            if (spec == null || geometryPoints == null) throw new ArgumentException("Interaction configuration and geometry are required.");
            if (spec.Grabbable) throw new RoomToolException("INTERACTION_UNSUPPORTED", "Grabbing is not available in this implementation stage.");
            var local = new Bounds(); bool found = false;
            foreach (var world in geometryPoints)
            {
                var point = transform.InverseTransformPoint(world);
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) throw new ArgumentException("Invalid interaction geometry.");
                if (!found) { local = new Bounds(point, Vector3.zero); found = true; } else local.Encapsulate(point);
            }
            if (!found) throw new RoomToolException("INTERACTION_UNSUPPORTED", "An interactive object requires geometry.");
            var scale = transform.lossyScale;
            if (Mathf.Abs(scale.x) < .000001f || Mathf.Abs(scale.y) < .000001f || Mathf.Abs(scale.z) < .000001f)
                throw new RoomToolException("INTERACTION_UNSUPPORTED", "Interaction requires non-zero world scale.");
            local.size = Vector3.Max(local.size, new Vector3(.08f / Mathf.Abs(scale.x), .08f / Mathf.Abs(scale.y), .08f / Mathf.Abs(scale.z)));
            RemoveRig(); hovering.Clear(); selecting.Clear();
            specification = spec.Copy(); detail = detailPanel;
            var host = new GameObject("__SeyraInteraction"); host.SetActive(false);
            host.transform.SetParent(transform, false);
            rig = host.AddComponent<SeyraInteractionRig>();
            try
            {
                SelectionBox = host.AddComponent<BoxCollider>(); SelectionBox.isTrigger = true;
                SelectionBox.center = local.center; SelectionBox.size = local.size;
                var surface = host.AddComponent<ColliderSurface>(); surface.InjectAllColliderSurface(SelectionBox);
                Ray = host.AddComponent<RayInteractable>(); Ray.InjectAllRayInteractable(surface);
                var mesh = GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    highlight = new GameObject("Highlight", typeof(MeshFilter), typeof(MeshRenderer));
                    highlight.GetComponent<MeshFilter>().sharedMesh = mesh;
                    highlight.transform.SetParent(host.transform, false);
                    highlight.transform.localScale = Vector3.one * 1.06f;
                    highlight.transform.localPosition = -mesh.bounds.center * .06f;
                }
                else
                {
                    highlight = GameObject.CreatePrimitive(PrimitiveType.Cube); highlight.name = "Highlight";
                    // Remove the primitive collider before activating the auxiliary tree.
                    DestroyImmediate(highlight.GetComponent<Collider>());
                    highlight.transform.SetParent(host.transform, false);
                    highlight.transform.localPosition = local.center; highlight.transform.localScale = local.size * 1.06f;
                }
                var renderer = highlight.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = rig.CreateHighlightMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                highlight.SetActive(false);
                Ray.WhenPointerEventRaised += Pointer;
                if (detail != null)
                {
                    var follow = detail.GetComponent<SeyraFollowTarget>() ?? detail.gameObject.AddComponent<SeyraFollowTarget>();
                    follow.Configure(transform, SelectionBox, spec.DetailSide, spec.DetailGap);
                    SetDetailVisible(spec.DetailOpen);
                }
                host.SetActive(true);
            }
            catch { RemoveRig(); throw; }
        }
        public void SetDetailVisible(bool visible)
        {
            if (detail == null) return;
            if (visible && specification.Exclusive)
                foreach (var other in active)
                    if (other != this && other.gameObject.scene == gameObject.scene && other.detail != null) other.detail.SetWindowVisible(false);
            detail.SetWindowVisible(visible);
        }
        void Pointer(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover: hovering.Add(evt.Identifier); break;
                case PointerEventType.Unhover: hovering.Remove(evt.Identifier); break;
                case PointerEventType.Select:
                    if (selecting.Count == 0)
                    {
                        savedPosition = transform.localPosition; savedRotation = transform.localRotation; savedScale = transform.localScale;
                        selectionPoint = evt.Pose.position; selectedAt = Time.unscaledTime; selectionMoved = false;
                    }
                    else selectionMoved = true;
                    selecting.Add(evt.Identifier); break;
                case PointerEventType.Move:
                    if (selecting.Contains(evt.Identifier) && Vector3.Distance(selectionPoint, evt.Pose.position) >= .03f) selectionMoved = true;
                    break;
                case PointerEventType.Unselect:
                    if (!selecting.Remove(evt.Identifier)) break;
                    if (selecting.Count == 0 && !selectionMoved && Time.unscaledTime - selectedAt < .35f && Vector3.Distance(selectionPoint, evt.Pose.position) < .03f)
                    {
                        transform.localPosition = savedPosition; transform.localRotation = savedRotation; transform.localScale = savedScale;
                        if (detail != null) SetDetailVisible(!detail.WindowVisible);
                    }
                    break;
                case PointerEventType.Cancel: selecting.Remove(evt.Identifier); selectionMoved = true; break;
            }
            if (highlight != null) highlight.SetActive(hovering.Count > 0);
        }
        void RemoveRig()
        {
            if (Ray != null) Ray.WhenPointerEventRaised -= Pointer;
            if (rig != null)
            {
                rig.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(rig.gameObject); else DestroyImmediate(rig.gameObject);
            }
            rig = null; Ray = null; SelectionBox = null; highlight = null;
        }
        void OnDestroy() => RemoveRig();
    }
}
