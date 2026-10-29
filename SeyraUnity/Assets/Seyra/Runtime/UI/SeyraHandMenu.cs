using System.Linq;
using Oculus.Interaction.Input;
using Seyra.UI.Library;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Seyra.UI
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class SeyraHandMenu : MonoBehaviour
    {
        [SerializeField] InputActionReference toggleMenuAction;
        [SerializeField] Transform head, leftControllerAnchor;
        [SerializeField] Object leftHand;
        [SerializeField] Vector3 palmNormalLocal = Vector3.down;
        [SerializeField] float showDot = .75f, hideDot = .55f, showDelay = .15f, hideDelay = .30f;
        // Semantic palm axes: thumb, fingers, outward normal (in meters).
        [SerializeField] Vector3 palmOffset = new Vector3(.04f, .10f, 0);
        // World-space lift applied after the palm offset, independent of hand tilt (in meters).
        [SerializeField] float palmWorldLift = .06f;
        [SerializeField] Vector3 controllerOffset = new Vector3(0, .08f, .05f);
        [SerializeField] UIXRWindow window;
        IHand hand;
        InputAction subscribedAction;
        bool enabledAction, controllerShown, handMode, warned;
        HandMenuGate gate;
        Pose lastPalm;
        readonly Vector3[] corners = new Vector3[4];
#if UNITY_EDITOR
        bool testController;
        public void UseTestControllerTracking(Transform viewer, Transform anchor)
        { testController = true; head = viewer; leftControllerAnchor = anchor; handMode = controllerShown = false; gate.Reset(); }
#endif

        bool HandConnected
        {
            get
            {
#if UNITY_EDITOR
                if (testController) return false;
#endif
                return hand != null && hand.IsConnected;
            }
        }

        public bool IsVisible => window != null && window.gameObject.activeSelf;

        void OnEnable()
        {
            ResolveReferences();
            gate.Reset(); controllerShown = handMode = false;
            if (window != null) window.Close();
            subscribedAction = toggleMenuAction != null ? toggleMenuAction.action : null;
            if (subscribedAction != null)
            {
                subscribedAction.performed += OnTogglePerformed;
                enabledAction = !subscribedAction.enabled;
                if (enabledAction) subscribedAction.Enable();
            }
            else if (!warned)
            {
                Debug.LogWarning("SeyraHandMenu: assign a toggle menu InputActionReference for controller input.", this);
                warned = true;
            }
        }

        void OnDisable()
        {
            if (subscribedAction != null)
            {
                subscribedAction.performed -= OnTogglePerformed;
                if (enabledAction) subscribedAction.Disable();
            }
            subscribedAction = null; enabledAction = false;
            gate.Reset(); controllerShown = false;
            if (window != null) window.Close();
        }

        void ResolveReferences()
        {
            head = SeyraLazyFollow.ResolveHead(head);
            if (leftControllerAnchor == null) leftControllerAnchor = FindFirstObjectByType<OVRCameraRig>()?.leftControllerAnchor;
            if (window == null) window = GetComponentInChildren<UIXRWindow>(true);
            if (leftHand is IHand assigned) hand = assigned;
            if (hand == null || hand is Object source && source == null)
                hand = FindObjectsByType<Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(h => h.isActiveAndEnabled)
                    .OrderByDescending(h => h.name == "OVRHands")
                    .FirstOrDefault(h => h.Handedness == Handedness.Left);
        }

        void OnTogglePerformed(InputAction.CallbackContext context) => ToggleMenu();

        public void ToggleMenu()
        {
            if (HandConnected) return;
            controllerShown = !controllerShown;
            UpdatePose(0);
        }

        // ISDK 207 has no HandPalm joint. Build a palm frame from actual joints:
        // +X toward the thumb, +Z toward the fingers, -Y out of the left palm.
        public static bool TryPalmPose(IHand source, out Pose palm)
        {
            palm = Pose.identity;
            if (source == null || !source.GetJointPose(HandJointId.HandWristRoot, out var wrist) ||
                !source.GetJointPose(HandJointId.HandMiddle1, out var middle) ||
                !source.GetJointPose(HandJointId.HandIndex1, out var index) ||
                !source.GetJointPose(HandJointId.HandPinky1, out var pinky)) return false;
            var fingers = middle.position - wrist.position;
            var thumb = index.position - pinky.position;
            var up = Vector3.Cross(fingers, thumb);
            if (fingers.sqrMagnitude < .000001f || up.sqrMagnitude < .000001f) return false;
            palm = new Pose((wrist.position + middle.position) * .5f, Quaternion.LookRotation(fingers, up));
            return true;
        }

        void LateUpdate() => UpdatePose(Time.unscaledDeltaTime);

        void UpdatePose(float dt)
        {
            ResolveReferences();
            bool connected = HandConnected;
            if (connected != handMode) { handMode = connected; controllerShown = false; gate.Reset(); }
            bool shown = false;
            Pose pose = Pose.identity;
            if (head != null && window != null)
            {
                if (connected)
                {
                    if (hand.IsHighConfidence && TryPalmPose(hand, out lastPalm))
                    {
                        shown = gate.Tick(HandMenuPose.PalmFacing(lastPalm, palmNormalLocal, head.position), dt,
                            showDot, hideDot, showDelay, hideDelay);
                        var palmAnchor = lastPalm.position + lastPalm.rotation * new Vector3(palmOffset.x, -palmOffset.z, palmOffset.y);
                        pose = HandMenuPose.AnchorPose(new Pose(palmAnchor + Vector3.up * palmWorldLift, lastPalm.rotation), Vector3.zero, head.position);
                    }
                    else gate.Reset();
                }
                else if (leftControllerAnchor != null)
                {
                    shown = controllerShown;
                    pose = HandMenuPose.AnchorPose(new Pose(leftControllerAnchor.position, leftControllerAnchor.rotation), controllerOffset, head.position);
                }
            }
            if (!shown) { if (window != null) window.Close(); return; }
            window.transform.rotation = pose.rotation;
            ((RectTransform)window.Canvas.transform).GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * .5f;
            window.transform.position += pose.position - center;
            if (!IsVisible) window.Open();
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !handMode) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(lastPalm.position, lastPalm.rotation * palmNormalLocal.normalized * .15f);
        }
    }
}
