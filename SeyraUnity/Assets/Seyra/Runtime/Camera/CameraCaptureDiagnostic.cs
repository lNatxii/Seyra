using TMPro;
using UnityEngine;

namespace Seyra.Cameras
{
    // TEMPORARY: opt-in step 4 diagnostic. Remove from the scene after Quest validation.
    [RequireComponent(typeof(CameraCaptureService), typeof(CameraCaptureFeedback))]
    public sealed class CameraCaptureDiagnostic : MonoBehaviour
    {
        CameraCaptureService service;
        CameraCaptureFeedback feedback;
        TMP_Text label;
        GameObject panel;
        float started;
        void Start()
        {
            service = GetComponent<CameraCaptureService>();
            feedback = GetComponent<CameraCaptureFeedback>();
            panel = new GameObject("TEMP Camera Metrics", typeof(Canvas));
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640, 80);
            rect.localScale = Vector3.one * .001f;
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panel.transform, false);
            label = textObject.GetComponent<TMP_Text>();
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(640, 80);
            label.text = "TEMP Camera test: press A to capture, B to clear.";
            PlacePanel();
            service.Changed += Refresh;
        }
        void PlacePanel()
        {
            var head = UI.SeyraLazyFollow.ResolveHead();
            if (head == null) return;
            panel.transform.SetPositionAndRotation(head.position + head.forward * 1.2f - head.up * .3f,
                head.rotation);
        }
        void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.One)) CapturePhoto();
            if (OVRInput.GetDown(OVRInput.Button.Two)) service.Clear();
        }
        [ContextMenu("Capture Photo (TEMP Quest Diagnostic)")]
        public void CapturePhoto()
        {
            if (service == null || service.IsBusy) return;
            started = Time.realtimeSinceStartup;
            PlacePanel();
            service.Capture();
        }
        void Refresh()
        {
            label.text = service.Status;
            var photo = service.Current;
            if (photo == null) return;
            label.text = $"TEMP JPEG: {photo.Width} × {photo.Height}, {photo.Jpeg.Length} bytes, " +
                $"{Time.realtimeSinceStartup - started:F2} s. A: retake / B: clear";
            if (feedback.PreviewTexture == null)
                label.text += "\nPreview is unavailable. Check CameraCaptureFeedback configuration.";
        }
        void OnDestroy()
        {
            if (service != null) service.Changed -= Refresh;
            if (panel != null) Destroy(panel);
        }
    }
}
