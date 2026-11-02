using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class ObjectMarkTarget : MonoBehaviour
    {
        readonly HashSet<int> hovering = new HashSet<int>();
        readonly HashSet<int> selecting = new HashSet<int>();
        SeyraInteractionRig rig;
        GameObject highlight;
        Material material;
        bool selected, label;
        public event Action Tapped;
        public RayInteractable Ray { get; private set; }
        public bool ShellVisible => highlight != null && highlight.activeInHierarchy;

        public void Configure(bool isLabel)
        {
            RemoveRig();
            hovering.Clear(); selecting.Clear();
            label = isLabel;
            var host = new GameObject("__SeyraInteraction"); host.SetActive(false);
            host.transform.SetParent(transform, false);
            rig = host.AddComponent<SeyraInteractionRig>();
            try
            {
                var collider = host.AddComponent<BoxCollider>(); collider.isTrigger = true;
                var panel = isLabel ? GetComponent<ManagedPanel>() : null;
                var mesh = isLabel ? null : GetComponent<MeshFilter>().sharedMesh;
                if (isLabel)
                {
                    var surface = panel.Surface;
                    collider.center = transform.InverseTransformPoint(surface.TransformPoint(surface.rect.center));
                    collider.size = new Vector3(surface.rect.width * .001f, surface.rect.height * .001f, .02f);
                }
                else { collider.center = mesh.bounds.center; collider.size = mesh.bounds.size; }
                var raySurface = host.AddComponent<ColliderSurface>(); raySurface.InjectAllColliderSurface(collider);
                Ray = host.AddComponent<RayInteractable>(); Ray.InjectAllRayInteractable(raySurface);
                Ray.InjectOptionalMovementProvider(host.AddComponent<MoveRelativeToTargetProvider>());
                if (isLabel)
                {
                    highlight = GameObject.CreatePrimitive(PrimitiveType.Cube); highlight.name = "Highlight";
                    DestroyImmediate(highlight.GetComponent<Collider>());
                    highlight.transform.SetParent(host.transform, false);
                    highlight.transform.localPosition = collider.center + Vector3.forward * .002f;
                    highlight.transform.localScale = new Vector3(collider.size.x + .012f, collider.size.y + .012f, .001f);
                }
                else
                {
                    highlight = new GameObject("Highlight", typeof(MeshFilter), typeof(MeshRenderer));
                    highlight.GetComponent<MeshFilter>().sharedMesh = mesh;
                    highlight.transform.SetParent(host.transform, false);
                    highlight.transform.localScale = Vector3.one * 1.06f;
                    highlight.transform.localPosition = -mesh.bounds.center * .06f;
                }
                var renderer = highlight.GetComponent<MeshRenderer>();
                material = rig.CreateHighlightMaterial(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                if (isLabel) renderer.sortingOrder = -1;
                RefreshShell();
                Ray.WhenPointerEventRaised += Pointer;
                host.SetActive(true);
            }
            catch { RemoveRig(); throw; }
        }
        public void SetSelected(bool value) { selected = value; RefreshShell(); }
        public void SetPickable(bool pickable)
        {
            if (rig != null && rig.gameObject.activeSelf != pickable)
            {
                if (!pickable) { hovering.Clear(); selecting.Clear(); RefreshShell(); }
                rig.gameObject.SetActive(pickable);
            }
        }
        void Pointer(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover: hovering.Add(evt.Identifier); break;
                case PointerEventType.Unhover: hovering.Remove(evt.Identifier); break;
                case PointerEventType.Select: selecting.Add(evt.Identifier); break;
                case PointerEventType.Unselect: if (selecting.Remove(evt.Identifier)) Tapped?.Invoke(); break;
                case PointerEventType.Cancel: selecting.Remove(evt.Identifier); hovering.Remove(evt.Identifier); break;
            }
            RefreshShell();
        }
        void RefreshShell()
        {
            if (highlight == null) return;
            material.SetColor("_BaseColor", selected ? (label ? new Color(1, 1, 1, .95f) : new Color(0, 0, 0, .45f)) : new Color(1, 1, 1, label ? .35f : .25f));
            highlight.SetActive(selected || hovering.Count > 0);
        }
        void OnDisable() { hovering.Clear(); selecting.Clear(); RefreshShell(); }
        void RemoveRig()
        {
            if (Ray != null) Ray.WhenPointerEventRaised -= Pointer;
            if (rig != null)
            {
                rig.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(rig.gameObject); else DestroyImmediate(rig.gameObject);
            }
            rig = null; Ray = null; highlight = null; material = null;
        }
        void OnDestroy() => RemoveRig();
    }
}
