using System;
using Seyra.UI;
using Seyra.UI.Library;
using TMPro;
using UnityEngine;

namespace Seyra.Cameras
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CameraCaptureService))]
    public sealed class CameraCaptureFeedback : MonoBehaviour
    {
        [SerializeField] AudioClip shutterClip;
        [SerializeField] AudioSource audioSource;
        [SerializeField] UIXRWindow popup;
        [SerializeField] UnityEngine.UI.RawImage popupImage;
        [SerializeField] TMP_Text popupLabel;
        [SerializeField] float popupSeconds = 3f;
        public Texture2D PreviewTexture { get; private set; }
        public event Action PreviewChanged;

        CameraCaptureService service;
        CameraCapture displayedCapture;
        float closeAt;

        void Awake()
        {
            service = GetComponent<CameraCaptureService>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            audioSource.spatialize = false;
            audioSource.volume = .8f;
            audioSource.loop = false;
            if (popup == null)
            {
                Debug.LogError("CameraCaptureFeedback: assign the SeyraPhotoPopup prefab before entering Play Mode.", this);
                return;
            }
            if (!popup.gameObject.scene.IsValid())
            {
                popup = Instantiate(popup);
                popupImage = popup.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                popupLabel = popup.GetComponentInChildren<TMP_Text>(true);
            }
            else
            {
                popup.transform.SetParent(null, true);
                if (popupImage == null) popupImage = popup.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                if (popupLabel == null) popupLabel = popup.GetComponentInChildren<TMP_Text>(true);
            }
            popup.Close();
        }
        void OnEnable()
        {
            service.PhotoTaken += OnPhotoTaken;
            service.Changed += Refresh;
            Refresh();
        }
        void OnDisable()
        {
            service.PhotoTaken -= OnPhotoTaken;
            service.Changed -= Refresh;
            ClosePopup();
        }
        void OnPhotoTaken()
        {
            audioSource.Stop();
            if (shutterClip != null) audioSource.PlayOneShot(shutterClip);
        }
        void Refresh()
        {
            var photo = service.State == CameraCaptureState.Ready ? service.Current : null;
            if (photo != null && ReferenceEquals(photo, displayedCapture)) return;
            if (displayedCapture != null && displayedCapture.Id == service.LastTakenId)
            {
                return;
            }
            ClosePopup();
            ReleasePreview();
            if (photo != null)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(photo.Jpeg))
                {
                    Destroy(texture);
                    ShowPopup("Could not read the camera image. Try again.");
                    return;
                }
                PreviewTexture = texture;
                displayedCapture = photo;
                if (popupImage != null)
                {
                    popupImage.texture = PreviewTexture;
                    popupImage.gameObject.SetActive(true);
                    var aspect = popupImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                    if (aspect != null) aspect.aspectRatio = (float)photo.Width / photo.Height;
                }
                ShowPopup("");
                PreviewChanged?.Invoke();
            }
            else if (service.State == CameraCaptureState.Failed) ShowPopup(service.Status);
        }
        void ShowPopup(string message)
        {
            if (popup == null || popupLabel == null) return;
            popupLabel.text = message;
            popupLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
            popupLabel.textWrappingMode = PreviewTexture != null ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            popup.Open();
            popup.GetComponent<SeyraLazyFollow>()?.SnapToTarget();
            closeAt = Time.unscaledTime + popupSeconds;
        }
        void ClosePopup()
        {
            if (popup != null) popup.Close();
        }
        void Update()
        {
            if (Time.unscaledTime < closeAt) return;
            ClosePopup();
            if (displayedCapture != null && displayedCapture.Id == service.LastTakenId) ReleasePreview();
        }
        void ReleasePreview()
        {
            displayedCapture = null;
            if (popupImage != null)
            {
                popupImage.texture = null;
                popupImage.gameObject.SetActive(false);
            }
            if (PreviewTexture == null) return;
            Destroy(PreviewTexture);
            PreviewTexture = null;
            PreviewChanged?.Invoke();
        }
        void OnDestroy()
        {
            ReleasePreview();
            if (popup != null) Destroy(popup.gameObject);
        }
    }
}
