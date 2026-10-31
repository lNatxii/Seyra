using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seyra.SceneTools
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class ManagedLine : MonoBehaviour
    {
        LineContent content;
        Transform[] targets;
        Vector3[] positions, previous;
        readonly Vector3[] corners = new Vector3[4];
        Material material;
        LineRenderer line;
        bool dirty;
        public LineContent Content => content?.Copy();
        public Transform[] Targets => targets == null ? null : (Transform[])targets.Clone();
        public string Signature => content?.ToJson().ToString(Formatting.None);
        public string Status { get; private set; } = "target_missing";
        public JArray MissingTargets { get; private set; } = new JArray();
        public void Restore(LineContent value, Transform[] references)
        {
            content = value.Copy(); targets = (Transform[])references.Clone();
            line = GetComponent<LineRenderer>();
            if (material == null) material = new Material(ManagedAppearance.LoadShader(AppearanceStyle.Unlit)) { name = "Seyra managed line" };
            material.SetColor("_BaseColor", content.Color); line.sharedMaterial = material;
            line.useWorldSpace = true; line.alignment = LineAlignment.View; line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = line.endColor = Color.white; line.widthCurve = AnimationCurve.Constant(0, 1, 1);
            line.positionCount = content.Points.Length;
            positions = new Vector3[content.Points.Length]; previous = new Vector3[positions.Length]; dirty = true;
            Refresh();
        }
        public void Refresh()
        {
            if (content == null) return;
            MissingTargets = new JArray(content.Points.Where((p, i) => p.EntityId != null && (targets[i] == null || !targets[i].gameObject.activeInHierarchy)).Select(p => p.EntityId).Distinct());
            Status = MissingTargets.Count == 0 ? "visible" : "target_missing";
            line.enabled = Status == "visible";
            if (!line.enabled) return;
            for (int i = 0; i < positions.Length; i++) positions[i] = targets[i] != null ? targets[i].position : transform.TransformPoint(content.Points[i].Position);
            // Resolve panel edges against the initial centers, including adjacent panels.
            var centers = (Vector3[])positions.Clone();
            for (int i = 0; i < positions.Length; i++)
            {
                var panel = targets[i] != null ? targets[i].GetComponent<ManagedPanel>() : null;
                if (panel?.Surface == null) continue;
                panel.Surface.GetWorldCorners(corners);
                var neighbor = centers[i == 0 ? 1 : i - 1];
                float best = float.PositiveInfinity;
                for (int edge = 0; edge < 4; edge++)
                {
                    var a = corners[edge]; var delta = corners[(edge + 1) % 4] - a;
                    var point = a + delta * Mathf.Clamp01(Vector3.Dot(neighbor - a, delta) / Mathf.Max(delta.sqrMagnitude, 1e-20f));
                    float distance = (point - neighbor).sqrMagnitude;
                    if (distance < best) { best = distance; positions[i] = point; }
                }
            }
            line.widthMultiplier = content.Width * Mathf.Abs(transform.lossyScale.x);
            if (dirty || positions.Where((p, i) => (p - previous[i]).sqrMagnitude > .00000001f).Any())
            { line.SetPositions(positions); System.Array.Copy(positions, previous, positions.Length); dirty = false; }
        }
        void LateUpdate() => Refresh();
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
