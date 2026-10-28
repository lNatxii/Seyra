using Seyra.UI.Library;
using System.Linq;
using UnityEngine;

namespace Seyra.UI
{
    /// <summary>Delayed, grab-aware following in the head's yaw frame.</summary>
    [DisallowMultipleComponent]
    public class SeyraLazyFollow : MonoBehaviour
    {
        public enum FollowState { Resting, Waiting, Moving, Held }

        [SerializeField] Transform head;
        [SerializeField] UIMetaGrabFrame grabFrame;
        [SerializeField] Vector3 restOffset = new Vector3(0, -.40f, .80f);
        [SerializeField] float maxYawOffset = 35f;
        [SerializeField] float maxPositionError = .30f;
        [SerializeField] float maxHeightError = .25f;
        [SerializeField] float triggerDelay = .6f;
        [SerializeField] float smoothTime = .20f;
        [SerializeField] float maxSpeed = 1.8f;
        [SerializeField] float maxAngularSpeed = 180f;
        [SerializeField] float snapDistance = 3f;
        [SerializeField] float minDistance = .45f;
        [SerializeField] float maxDistance = 1.30f;

        Vector3 defaultOffset, velocity;
        float outsideTime;
        bool initialized, simulatedGrab, warned, layoutRoot;
        UIMetaGrabFrame subscribedFrame;

        public FollowState State { get; private set; }
        public Transform Head { get => head; set { head = value; initialized = false; } }
        public Vector3 RestOffset => restOffset;
        bool IsHeld => simulatedGrab || (grabFrame != null && grabFrame.IsGrabbed) || (layoutRoot && GetComponentsInChildren<UIMetaGrabFrame>().Any(f => f.IsGrabbed));

        void Awake() { defaultOffset = restOffset; }
        void OnEnable()
        {
            initialized = false;
            velocity = Vector3.zero;
            outsideTime = 0;
            State = FollowState.Resting;
            if (grabFrame == null) grabFrame = GetComponent<UIXRWindow>()?.GrabFrame;
            subscribedFrame = grabFrame;
            if (subscribedFrame != null) subscribedFrame.GrabChanged += OnGrabChanged;
        }
        void OnDisable()
        {
            if (subscribedFrame != null) subscribedFrame.GrabChanged -= OnGrabChanged;
            subscribedFrame = null;
            simulatedGrab = false;
        }

        public static Transform ResolveHead(Transform explicitHead = null)
        {
            if (explicitHead != null) return explicitHead;
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
            return Camera.main != null ? Camera.main.transform : null;
        }

        static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        bool TryHead(out Transform resolved)
        {
            resolved = ResolveHead(head);
            if (resolved != null && IsFinite(resolved.position) && IsFinite(resolved.eulerAngles)) return true;
            if (!warned)
            {
                Debug.LogWarning("SeyraLazyFollow: no valid head pose; following is suspended.", this);
                warned = true;
            }
            return false;
        }

        public Pose ComputeTarget()
        {
            var resolved = ResolveHead(head);
            return resolved == null ? new Pose(transform.position, transform.rotation) : ComputeTarget(resolved);
        }

        // Override only target planning when extending following with room-aware placement.
        protected virtual Pose ComputeTarget(Transform resolvedHead)
        {
            var yaw = Quaternion.Euler(0, resolvedHead.eulerAngles.y, 0);
            var position = resolvedHead.position + yaw * restOffset;
            var direction = position - resolvedHead.position;
            var rotation = layoutRoot ? yaw : direction.sqrMagnitude > .000001f
                ? Quaternion.LookRotation(direction, Vector3.up) : yaw;
            return new Pose(position, rotation);
        }

        public void ConfigureOffset(Vector3 offset) { layoutRoot = true; restOffset = defaultOffset = offset; initialized = true; velocity = Vector3.zero; outsideTime = 0; }
        public void ResetOffset() { restOffset = defaultOffset; }

        public void SnapToTarget()
        {
            if (IsHeld || !TryHead(out var resolved)) return;
            ApplySnap(ComputeTarget(resolved));
        }

        void ApplySnap(Pose target)
        {
            transform.SetPositionAndRotation(target.position, target.rotation);
            initialized = true;
            velocity = Vector3.zero;
            outsideTime = 0;
            State = FollowState.Resting;
        }

        public void SimulateGrab(bool held)
        {
            simulatedGrab = held;
            OnGrabChanged(held);
        }

        void OnGrabChanged(bool held)
        {
            if (IsHeld)
            {
                State = FollowState.Held;
                velocity = Vector3.zero;
                outsideTime = 0;
            }
            else if (State == FollowState.Held)
            {
                if (TryHead(out var resolved))
                {
                    var offset = Quaternion.Inverse(Quaternion.Euler(0, resolved.eulerAngles.y, 0)) *
                        (transform.position - resolved.position);
                    var horizontal = new Vector2(offset.x, offset.z);
                    float distance = horizontal.magnitude;
                    horizontal = (distance > .00001f ? horizontal / distance : Vector2.up) *
                        Mathf.Clamp(distance, minDistance, maxDistance);
                    restOffset = new Vector3(horizontal.x, offset.y, horizontal.y);
                    initialized = true;
                }
                State = FollowState.Resting;
                outsideTime = 0;
                velocity = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            if (IsHeld) { OnGrabChanged(true); return; }
            if (State == FollowState.Held) { OnGrabChanged(false); return; }
            if (!TryHead(out var resolved)) return;
            var target = ComputeTarget(resolved);
            if (!initialized || Vector3.Distance(transform.position, target.position) > snapDistance)
            {
                ApplySnap(target);
                return;
            }

            var currentDirection = Vector3.ProjectOnPlane(transform.position - resolved.position, Vector3.up);
            var desiredDirection = Vector3.ProjectOnPlane(target.position - resolved.position, Vector3.up);
            var error = transform.position - target.position;
            bool outside = Vector3.Angle(currentDirection, desiredDirection) > maxYawOffset ||
                Vector3.ProjectOnPlane(error, Vector3.up).magnitude > maxPositionError ||
                Mathf.Abs(error.y) > maxHeightError;
            float dt = Time.unscaledDeltaTime;
            if (State != FollowState.Moving)
            {
                if (!outside) { State = FollowState.Resting; outsideTime = 0; return; }
                State = FollowState.Waiting;
                outsideTime += dt;
                if (outsideTime < triggerDelay) return;
                State = FollowState.Moving;
            }

            transform.position = Vector3.SmoothDamp(transform.position, target.position, ref velocity,
                smoothTime, maxSpeed, dt);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target.rotation, maxAngularSpeed * dt);
            if (Vector3.Distance(transform.position, target.position) < .02f &&
                Quaternion.Angle(transform.rotation, target.rotation) < 2f) ApplySnap(target);
        }
    }
}
