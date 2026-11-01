using UnityEngine;
using UnityEngine.UI;

namespace Seyra.Cameras
{
    // TEMPORARY: opt-in step 4 diagnostic. Remove from the scene after Quest validation.
    [RequireComponent(typeof(CameraCaptureService))]
    public sealed class CameraCaptureDiagnostic : MonoBehaviour
    {
        CameraCaptureService service;
        RawImage preview;
        Text label;
        Texture2D texture;
        GameObject panel;
        float started;
        void Start()
        {
            service = GetComponent<CameraCaptureService>();
            panel = new GameObject("TEMP Camera JPEG Diagnostic", typeof(Canvas));
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640, 540);
            rect.localScale = Vector3.one * .001f;
            var imageObject = new GameObject("JPEG", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(panel.transform, false);
            preview = imageObject.GetComponent<RawImage>();
            preview.rectTransform.sizeDelta = new Vector2(640, 480);
            preview.rectTransform.anchoredPosition = new Vector2(0, 30);
            preview.enabled = false;
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panel.transform, false);
            label = textObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.sizeDelta = new Vector2(640, 60);
            label.rectTransform.anchoredPosition = new Vector2(0, -240);
            label.text = "TEMP Camera test: press A to capture, B to clear.";
            PlacePanel();
            service.Changed += Refresh;
        }
        void PlacePanel()
        {
            var head = UnityEngine.Camera.main;
            if (head == null) return;
            panel.transform.SetPositionAndRotation(head.transform.position + head.transform.forward * 1.2f,
                head.transform.rotation);
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
            if (texture != null) Destroy(texture);
            texture = null;
            preview.texture = null;
            preview.enabled = false;
            label.text = service.Status;
            var photo = service.Current;
            if (photo == null) return;
            texture = new Texture2D(2, 2);
            if (!texture.LoadImage(photo.Jpeg)) return;
            preview.texture = texture;
            preview.enabled = true;
            preview.rectTransform.sizeDelta = new Vector2(640, 640f * photo.Height / photo.Width);
            label.text = $"TEMP JPEG: {photo.Width} × {photo.Height}, {photo.Jpeg.Length} bytes, " +
                $"{Time.realtimeSinceStartup - started:F2} s. A: retake / B: clear";
        }
        void OnDestroy()
        {
            if (service != null) service.Changed -= Refresh;
            if (texture != null) Destroy(texture);
            if (panel != null) Destroy(panel);
        }
    }
}
