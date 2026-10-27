using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SeyraMenuVisibility : MonoBehaviour
    {
        public enum VisibilityState { Shown, Hidden }

        [SerializeField] bool autoHide = true;
        [SerializeField] SeyraLazyFollow follow;
        [SerializeField] UIXRWindow chatWindow;
        [SerializeField] GameObject[] visualParts;
        [SerializeField] Behaviour[] visualBehaviours;
        [SerializeField] float enterHalfYaw = 30, enterHalfPitch = 25;
        [SerializeField] float exitHalfYaw = 40, exitHalfPitch = 33;
        [SerializeField] float maxChatDistance = 4, minFacingDot = .2f;
        [SerializeField] float hideDelay = .30f, showDelay = .45f;
        readonly Vector3[] corners = new Vector3[4];
        float conditionTime;

        public VisibilityState State { get; private set; }
        public bool IsChatInView { get; private set; }
        public UIXRWindow ChatWindow { get => chatWindow; set => chatWindow = value; }
        public bool AutoHide
        {
            get => autoHide;
            set { autoHide = value; if (!value) ResetVisibility(); }
        }

        void Awake()
        {
            if (follow == null) follow = GetComponent<SeyraLazyFollow>();
            if (visualParts == null || visualParts.Length == 0)
            {
                visualParts = new GameObject[transform.childCount];
                for (int i = 0; i < visualParts.Length; i++) visualParts[i] = transform.GetChild(i).gameObject;
            }
        }

        void OnEnable() => ResetVisibility();
        void OnDisable() => ResetVisibility();

        void ResetVisibility()
        {
            IsChatInView = false;
            conditionTime = 0;
            SetShown(true);
        }

        void SetShown(bool shown)
        {
            bool reappearing = shown && State == VisibilityState.Hidden;
            // Place the menu before restoring rendering and interaction.
            if (reappearing && follow != null) follow.SnapToTarget();
            State = shown ? VisibilityState.Shown : VisibilityState.Hidden;
            if (visualParts != null)
                foreach (var part in visualParts)
                    if (part != null && part != gameObject) part.SetActive(shown);
            if (visualBehaviours != null)
                foreach (var behaviour in visualBehaviours)
                    if (behaviour != null && behaviour != this && behaviour != follow) behaviour.enabled = shown;
        }

        public bool EvaluateChatInView(Transform head, bool wasInView)
        {
            if (head == null || chatWindow == null || !chatWindow.gameObject.activeInHierarchy || chatWindow.Canvas == null)
                return false;
            var canvas = (RectTransform)chatWindow.Canvas.transform;
            canvas.GetWorldCorners(corners);
            var center = (corners[0] + corners[1] + corners[2] + corners[3]) * .25f;
            var direction = center - head.position;
            float distance = direction.magnitude;
            if (!float.IsFinite(distance) || distance < .1f || distance > maxChatDistance ||
                Vector3.Dot(canvas.forward, direction / distance) <= minFacingDot) return false;
            var local = Quaternion.Inverse(head.rotation) * direction;
            float yaw = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
            float pitch = Mathf.Abs(Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg);
            return local.z > 0 && yaw <= (wasInView ? exitHalfYaw : enterHalfYaw) &&
                pitch <= (wasInView ? exitHalfPitch : enterHalfPitch);
        }

        void LateUpdate()
        {
            var head = SeyraLazyFollow.ResolveHead(follow != null ? follow.Head : null);
            if (!autoHide || head == null || !float.IsFinite(head.position.sqrMagnitude) ||
                !float.IsFinite(head.eulerAngles.sqrMagnitude) || chatWindow == null || !chatWindow.gameObject.activeInHierarchy)
            {
                ResetVisibility();
                return;
            }
            bool inView = EvaluateChatInView(head, IsChatInView);
            if (inView != IsChatInView) conditionTime = 0;
            IsChatInView = inView;
            bool shouldHide = inView && (follow == null || follow.State != SeyraLazyFollow.FollowState.Held);
            if (shouldHide == (State == VisibilityState.Hidden)) { conditionTime = 0; return; }
            conditionTime += Time.unscaledDeltaTime;
            if (conditionTime < (shouldHide ? hideDelay : showDelay)) return;
            SetShown(!shouldHide);
            conditionTime = 0;
        }
    }
}
