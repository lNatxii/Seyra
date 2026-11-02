using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class ObjectMarkSwitch : MonoBehaviour
    {
        ManagedPanel card;
        GameObject boxes;
        GameObject points;
        string lastValue;

        public void Bind(ManagedPanel card, GameObject boxes, GameObject points)
        {
            this.card = card;
            this.boxes = boxes;
            this.points = points;
            lastValue = null;
            ApplyState();
        }

        void LateUpdate() => ApplyState();

        void ApplyState()
        {
            if (card == null || card.PresentationView == null ||
                !card.PresentationView.Values.TryGetValue("checked", out var value) || value == lastValue) return;
            var on = value == "true";
            if (boxes != null) boxes.SetActive(on);
            if (points != null) points.SetActive(!on);
            lastValue = value;
        }
    }
}
