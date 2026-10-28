using UnityEngine;

namespace Seyra.Configuration
{
    // Anchors track the physical safe area; the keyboard offset is in canvas units.
    [RequireComponent(typeof(RectTransform))]
    public sealed class MobileKeyboardInset : MonoBehaviour
    {
        RectTransform root;
        Canvas canvas;
        void Awake()
        {
            root = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }
        void LateUpdate()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            float keyboardTop = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.yMax : 0;
            float inset = Mathf.Max(0, keyboardTop - safe.yMin);
            root.offsetMin = new Vector2(0, inset / Mathf.Max(.001f, canvas.scaleFactor));
            root.offsetMax = Vector2.zero;
        }
    }
}
