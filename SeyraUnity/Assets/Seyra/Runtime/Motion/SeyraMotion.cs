using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class SeyraMotion : MonoBehaviour
    {
        MotionState state = new MotionState();
        SeyraMotionSystem system;
        readonly List<UIMetaGrabFrame> grabFrames = new List<UIMetaGrabFrame>();
        readonly List<UIMetaGrabFrame> ownFrames = new List<UIMetaGrabFrame>();
        readonly List<SeyraInteractive> interactiveObjects = new List<SeyraInteractive>();
        bool ownWasHeld, releasePending, invalidDependency;
        string signature = "{}";

        public bool HasMotion => state.Spin != null || state.Position != null;
        public bool Paused { get => state.Paused; set => state.Paused = value; }
        public string Status { get; private set; } = "running";
        public string Signature => signature;
        public DrivenChannels Channels => (state.Spin != null ? DrivenChannels.Rotation : DrivenChannels.None)
            | (state.Position != null ? DrivenChannels.Position : DrivenChannels.None);
        internal PositionSpec Position => state.Position;
        internal Transform Pivot => state.Pivot;
        public MotionState Capture() => state.Copy();

        void OnEnable() { if (HasMotion) Register(); }
        void OnDisable()
        {
            DrivenTransform.Unregister(this);
            if (system != null) system.Unregister(this);
            ClearGrabCache();
            ownWasHeld = releasePending = false;
        }
        void OnTransformParentChanged() { if (system != null) system.MarkDirty(); }
        void OnTransformChildrenChanged() { if (system != null) system.MarkDirty(); }

        SeyraMotionSystem System => system != null ? system : system = SeyraMotionSystem.ForScene(gameObject.scene);
        void Register()
        {
            System.Register(this);
            DrivenTransform.Register(transform, Channels, this);
        }
        void ConfigurationChanged()
        {
            signature = new JObject { ["spin"] = state.Spin?.ToJson(), ["position"] = state.Position?.ToJson() }.ToString(Formatting.None);
            if (isActiveAndEnabled && HasMotion) Register();
            else
            {
                DrivenTransform.Unregister(this);
                if (system != null) system.Unregister(this);
                ClearGrabCache();
            }
            if (system != null) system.MarkDirty();
        }

        public void Configure(MotionSpec specification, Transform pivot = null, bool? paused = null)
        {
            if (specification == null) throw new ArgumentNullException(nameof(specification));
            var spec = specification.Copy();
            var position = spec as PositionSpec;
            DrivenChannels channel = spec is SpinSpec ? DrivenChannels.Rotation : DrivenChannels.Position;
            if ((DrivenTransform.Channels(transform, this) & channel) != 0)
                throw new RoomToolException("MOTION_DRIVEN", "This object is animated or follows a behavior. Remove its motion or behavior first, or move its parent.");
            if (position != null && state.Position != null && position.Kind != state.Position.Kind)
                throw new RoomToolException("MOTION_CHANNEL_BUSY", "Orbit and bob cannot control the same position channel.");
            if (isActiveAndEnabled) System.RequireCapacity(this);
            Vector3 offset = default;
            if (position?.Kind == "orbit")
            {
                if (position.PivotId != null)
                {
                    if (pivot == null || !pivot.gameObject.activeInHierarchy || pivot.gameObject.scene != gameObject.scene || pivot == transform || pivot.IsChildOf(transform))
                        throw new RoomToolException("MOTION_PIVOT_INVALID", "Orbit requires a live, disjoint pivot in the same scene.");
                }
                else pivot = null;
                offset = transform.localPosition - PivotLocal(position, pivot);
                if (Vector3.ProjectOnPlane(offset, position.Axis).magnitude < .001f)
                    throw new RoomToolException("MOTION_PIVOT_INVALID", "Orbit radius perpendicular to its axis must be at least 0.001 local units.");
            }
            if (System.WouldCycle(this, position ?? state.Position, position != null ? pivot : state.Pivot))
                throw new RoomToolException("MOTION_PIVOT_INVALID", "Motion pivot dependencies must not form a cycle.");
            if (spec is SpinSpec spin)
            {
                state.Spin = spin; state.BaseRotation = transform.localRotation; state.SpinAngle = 0;
            }
            else
            {
                state.Position = position; state.Pivot = pivot; state.Offset = offset;
                state.BasePosition = transform.localPosition; state.PositionAngle = state.Time = 0;
            }
            if (paused.HasValue) state.Paused = paused.Value;
            ConfigurationChanged();
        }

        public void Remove(string kind)
        {
            if (kind == "spin" && state.Spin != null) state.Spin = null;
            else if (state.Position != null && state.Position.Kind == kind) { state.Position = null; state.Pivot = null; }
            else throw new RoomToolException("MOTION_NOT_FOUND", "The object does not have that motion kind.");
            ConfigurationChanged();
        }

        public void Restore(MotionState snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            // Runtime snapshots were already validated; reparsing changes normalized float axes.
            // Saved JSON is validated by MotionState.Parse before it reaches Restore.
            var restored = snapshot.Copy();
            var channels = (restored.Spin != null ? DrivenChannels.Rotation : DrivenChannels.None)
                | (restored.Position != null ? DrivenChannels.Position : DrivenChannels.None);
            if ((DrivenTransform.Channels(transform, this) & channels) != 0)
                throw new RoomToolException("MOTION_DRIVEN", "This object is animated or follows a behavior. Remove its motion or behavior first, or move its parent.");
            if (isActiveAndEnabled) System.RequireCapacity(this);
            state = restored;
            ConfigurationChanged();
        }

        Vector3 PivotLocal(PositionSpec position, Transform pivot)
        {
            if (position.PivotId == null) return position.PivotPoint.Value;
            return transform.parent != null ? transform.parent.InverseTransformPoint(pivot.position) : pivot.position;
        }
        bool PivotMissing => state.Position?.Kind == "orbit" && state.Position.PivotId != null
            && (state.Pivot == null || !state.Pivot.gameObject.activeInHierarchy);

        public void Rebase()
        {
            if (state.Spin != null) { state.BaseRotation = transform.localRotation; state.SpinAngle = 0; }
            if (state.Position != null)
            {
                state.BasePosition = transform.localPosition;
                if (state.Position.Kind == "orbit")
                {
                    if (PivotMissing) { releasePending = true; return; }
                    state.Offset = transform.localPosition - PivotLocal(state.Position, state.Pivot);
                }
                state.PositionAngle = state.Time = 0;
            }
        }
        public void ResetMotion()
        {
            state.SpinAngle = state.PositionAngle = state.Time = 0;
            if (!PivotMissing && !invalidDependency) ApplyPose();
        }
        void ApplyPose()
        {
            PoseAtCurrentPhase(out var position, out var rotation);
            if (state.Spin != null) transform.localRotation = rotation;
            if (state.Position != null) transform.localPosition = position;
        }
        public void PoseAtCurrentPhase(out Vector3 position, out Quaternion rotation)
        {
            position = transform.localPosition; rotation = transform.localRotation;
            if (state.Spin != null) rotation = state.BaseRotation * Quaternion.AngleAxis(state.SpinAngle, state.Spin.Axis);
            if (state.Position == null || PivotMissing || invalidDependency) return;
            if (state.Position.Kind == "orbit")
                position = PivotLocal(state.Position, state.Pivot) + Quaternion.AngleAxis(state.PositionAngle, state.Position.Axis) * state.Offset;
            else position = state.BasePosition + state.Position.Axis * (state.Position.Amplitude * Mathf.Sin(2 * Mathf.PI * state.Time / state.Position.Period));
        }
        static float Advance(float phase, float speed, float dt, float period) => (float)(((double)phase + (double)speed * dt) % period);

        // Only value-type arithmetic, cached frame reads and Transform writes in the frame loop.
        public void Evaluate(float dt)
        {
            bool held = false, ownHeld = false;
            for (int i = 0; i < interactiveObjects.Count; i++)
                if (interactiveObjects[i] != null && interactiveObjects[i].isActiveAndEnabled && interactiveObjects[i].IsHeld) held = true;
            for (int i = 0; i < grabFrames.Count; i++)
                if (grabFrames[i] != null && grabFrames[i].isActiveAndEnabled && grabFrames[i].IsGrabbed) held = true;
            for (int i = 0; i < ownFrames.Count; i++)
                if (ownFrames[i] != null && ownFrames[i].isActiveAndEnabled && ownFrames[i].IsGrabbed) ownHeld = true;
            if (ownWasHeld && !ownHeld) releasePending = true;
            ownWasHeld = ownHeld;
            if (held) { Status = "held"; return; }
            if (invalidDependency) { Status = "pivot_invalid"; return; }
            if (PivotMissing) { Status = "pivot_missing"; return; }
            if (releasePending) { releasePending = false; Rebase(); }
            if (state.Paused) { Status = "paused"; return; }
            Status = "running";
            if (state.Spin != null) state.SpinAngle = Advance(state.SpinAngle, state.Spin.Speed, dt, 360);
            if (state.Position?.Kind == "orbit") state.PositionAngle = Advance(state.PositionAngle, state.Position.Speed, dt, 360);
            else if (state.Position != null) state.Time = Advance(state.Time, 1, dt, state.Position.Period);
            ApplyPose();
        }

        internal void SetDependencyInvalid(bool value) { invalidDependency = value; }
        void OwnGrabChanged(bool held)
        {
            // The frame's LateUpdate may publish release after this system has already polled it.
            // Avoid rebasing twice, while still catching a grab/release between system steps.
            if (!held && ownWasHeld) releasePending = true;
            ownWasHeld = held;
        }
        void ClearGrabCache()
        {
            for (int i = 0; i < ownFrames.Count; i++) if (ownFrames[i] != null) ownFrames[i].GrabChanged -= OwnGrabChanged;
            ownFrames.Clear(); grabFrames.Clear(); interactiveObjects.Clear();
        }
        internal void CacheGrabFrames()
        {
            // Structural work runs from MarkDirty, never from Step, including event subscriptions.
            ClearGrabCache();
            GetComponentsInChildren(true, grabFrames);
            GetComponentsInChildren(true, interactiveObjects);
            for (int i = 0; i < grabFrames.Count; i++)
            {
                var frame = grabFrames[i];
                if ((frame.Grabbable != null && frame.Grabbable.Transform == transform) || frame.transform == transform)
                { ownFrames.Add(frame); frame.GrabChanged += OwnGrabChanged; }
            }
        }
    }
}
