using UnityEngine;

namespace Seyra.UI
{
    public static class HandMenuPose
    {
        public static float PalmFacing(Pose palm, Vector3 normalLocal, Vector3 head)
        {
            return Vector3.Dot((palm.rotation * normalLocal).normalized, (head - palm.position).normalized);
        }

        public static Pose AnchorPose(Pose anchor, Vector3 offset, Vector3 head)
        {
            var position = anchor.position + anchor.rotation * offset;
            // World-space canvases present their readable face along local -Z.
            var forward = position - head;
            var rotation = forward.sqrMagnitude > .000001f
                ? Quaternion.LookRotation(forward, Vector3.up) : anchor.rotation;
            return new Pose(position, rotation);
        }
    }

    public struct HandMenuGate
    {
        public bool Visible { get; private set; }
        float elapsed;

        public void Reset() { Visible = false; elapsed = 0; }

        public bool Tick(float dot, float dt, float showDot = .75f, float hideDot = .55f,
            float showDelay = .15f, float hideDelay = .30f)
        {
            bool changing = Visible ? dot < hideDot : dot >= showDot;
            if (!changing) { elapsed = 0; return Visible; }
            elapsed += Mathf.Max(0, dt);
            if (elapsed >= (Visible ? hideDelay : showDelay)) { Visible = !Visible; elapsed = 0; }
            return Visible;
        }
    }
}
