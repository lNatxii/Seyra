using System.Linq;
using Oculus.Interaction.Samples;
using Seyra.UI;
using Seyra.UI.Library;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Seyra.Editor
{
    /// <summary>Applies the suite's typography and native rounded surfaces without replacing controls.</summary>
    public static class SeyraSpatialChatPresentation
    {
        const string ThemePath = "Assets/Seyra/UI/Themes/SeyraUITheme.asset";

        public static void Apply(GameObject root)
        {
            var theme = AssetDatabase.LoadAssetAtPath<SeyraUITheme>(ThemePath);
            var window = root.GetComponent<UIXRWindow>();
            bool chat = root.GetComponent<SeyraSpatialChatWindow>() != null;
            window.Resize(chat ? new Vector2(900, 760) : new Vector2(640, 164));
            window.Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            var native = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seyra/UI/Controls/UIButtonPrimary.prefab");
            var surface = native.GetComponentsInChildren<RoundedBoxUIProperties>(true).First().GetComponent<UnityEngine.UI.Image>();

            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var role = text.name == "Title" || text.name == "MenuTitle" ? UITextRole.Heading :
                    text.name == "VoiceHintLabel" || text.name == "PermissionStatus" ? UITextRole.Help : UITextRole.Body;
                var ink = text.name.Contains("Status") || text.name.Contains("Hint") || text.name == "ConnectionLabel" || text.name == "DraftText"
                    ? UIInkRole.Muted : UIInkRole.Text;
                Get<UIStyle>(text.gameObject).Configure(theme, role, ink);
                text.raycastTarget = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.enableWordWrapping = true;
                if (text.name == "DraftText") text.overflowMode = TextOverflowModes.Overflow;
                if (role == UITextRole.Heading)
                {
                    var size = Get<UnityEngine.UI.LayoutElement>(text.gameObject);
                    size.minHeight = size.preferredHeight = 32;
                }
                if (text.GetComponentInParent<UnityEngine.UI.Button>() != null) text.fontStyle = FontStyles.Bold;
            }

            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var image = button.GetComponent<UnityEngine.UI.Image>();
                if (image == null) continue;
                Round(image, surface.material, 24);
                var danger = button.name == "CancelTurnButton" || button.name == "RejectButton";
                Get<UIStyle>(button.gameObject).Configure(theme, surface: danger ? UISurfaceRole.Danger : UISurfaceRole.Primary);
                var size = Get<UnityEngine.UI.LayoutElement>(button.gameObject);
                size.minHeight = size.preferredHeight = theme.controlHeight;
                bool compact = button.name == "CloseWindowButton";
                size.minWidth = size.preferredWidth = compact ? 48 : button.name == "AllowButton" ? 216 :
                    button.name == "PushToTalkButton" ? 196 : button.name == "ReconnectButton" ? 160 :
                    button.name == "ToggleChatButton" ? 192 : 152;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                label.text = button.name == "RecenterButton" ? "Recenter" : button.name == "VoiceButton" ? "Dictate" : label.text;
                // Action labels use the same on-action ink in either window theme.
                Object.DestroyImmediate(label.GetComponent<UIStyle>());
                label.font = theme.font;
                label.fontSize = theme.labelSize;
                label.color = Color.white;
                label.enableWordWrapping = false;
                var labelRect = label.rectTransform;
                labelRect.offsetMin = new Vector2(44, 0);
                labelRect.offsetMax = new Vector2(-12, 0);
                if (compact) { label.text = ""; label.gameObject.SetActive(false); }
                var iconName = IconName(button.name);
                var icon = button.transform.Find("ActionIcon")?.GetComponent<UnityEngine.UI.Image>();
                if (icon == null)
                {
                    var go = new GameObject("ActionIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(button.transform, false);
                    icon = go.GetComponent<UnityEngine.UI.Image>();
                }
                var path = AssetDatabase.FindAssets(iconName + " t:Sprite", new[] { "Assets/Icons" }).Select(AssetDatabase.GUIDToAssetPath).First();
                icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                icon.color = Color.white;
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(compact ? .5f : 0, .5f);
                icon.rectTransform.anchoredPosition = compact ? Vector2.zero : new Vector2(26, 0);
                icon.rectTransform.sizeDelta = Vector2.one * theme.iconSize;
            }

            foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.name.Contains("Dot"))
                {
                    Round(image, surface.material, 16);
                    var size = Get<UnityEngine.UI.LayoutElement>(image.gameObject);
                    size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 12;
                    size.flexibleHeight = size.flexibleWidth = 0;
                    image.raycastTarget = false;
                }
                if (image.name == "ExecutionBanner" || image.name == "PermissionPanel")
                {
                    Round(image, surface.material, 16);
                    Get<UIStyle>(image.gameObject).Configure(theme, surface: UISurfaceRole.Subtle,
                        padding: image.name == "PermissionPanel" ? UISpacing.Large : UISpacing.Standard, spacing: UISpacing.Small);
                    image.raycastTarget = false;
                }
                if (image.name == "DraftField")
                {
                    Round(image, surface.material, 24);
                    Get<UIStyle>(image.gameObject).Configure(theme, surface: UISurfaceRole.Field);
                    image.GetComponent<UnityEngine.UI.LayoutElement>().minWidth = 220;
                    image.raycastTarget = true;
                }
            }

            var contentLayout = Get<UnityEngine.UI.VerticalLayoutGroup>(window.Content.gameObject);
            contentLayout.spacing = theme.standardSpacing;
            contentLayout.padding = new RectOffset(24, 24, 16, 16);
            foreach (var row in root.GetComponentsInChildren<UnityEngine.UI.HorizontalLayoutGroup>(true))
            {
                row.childAlignment = TextAnchor.MiddleLeft;
                row.spacing = theme.smallSpacing;
            }
            SetHeight(root, "ComposerRow", 96);
            SetHeight(root, "ControlsRow", 48);
            SetHeight(root, "ExecutionBanner", 80);
            SetHeight(root, "PermissionPanel", 256);
            SetHeight(root, "ButtonsRow", 48);
            SetHeight(root, "VoiceHintRow", 24);
            SetHeight(root, "HeaderRow", 44);

            if (chat)
            {
                var presentation = new SerializedObject(root.GetComponent<SeyraSpatialChatWindow>());
                presentation.FindProperty("presentationTheme").objectReferenceValue = theme;
                presentation.FindProperty("messageSurfaceMaterial").objectReferenceValue = surface.material;
                presentation.ApplyModifiedPropertiesWithoutUndo();
                var details = Find(root, "DetailsScroll").GetComponent<UnityEngine.UI.ScrollRect>();
                var detailsLayout = Get<UnityEngine.UI.VerticalLayoutGroup>(details.content.gameObject);
                detailsLayout.childControlWidth = detailsLayout.childControlHeight = true;
                detailsLayout.childForceExpandWidth = true;
                detailsLayout.childForceExpandHeight = false;
                detailsLayout.padding = new RectOffset(4, 4, 4, 4);
                Get<UnityEngine.UI.ContentSizeFitter>(details.content.gameObject).verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                var detailsText = Find(root, "PermissionDetails").GetComponent<TMP_Text>();
                detailsText.alignment = TextAlignmentOptions.TopLeft;
                detailsText.overflowMode = TextOverflowModes.Overflow;
                var header = Find(root, "HeaderContainer");
                SetHeight(root, "HeaderContainer", 48);
                var title = Find(root, "Title").GetComponent<TMP_Text>();
                title.text = "SeYRA";
                title.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
                var status = Find(root, "ConnectionRow");
                if (status == null)
                {
                    status = new GameObject("ConnectionRow", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
                    status.transform.SetParent(header.transform.parent, false);
                    status.transform.SetSiblingIndex(header.transform.GetSiblingIndex() + 1);
                }
                var layout = status.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.spacing = 8;
                status.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 28;
                foreach (var name in new[] { "ChatConnectionDot", "ConnectionLabel", "RuntimeStatusDot", "RuntimeStatusLabel" })
                    Find(root, name).transform.SetParent(status.transform, false);
                Find(root, "RuntimeStatusLabel").GetComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 240;
                Find(root, "VoiceHintLabel").GetComponent<TMP_Text>().fontStyle = FontStyles.Normal;
                var ptt = root.GetComponentInChildren<Seyra.Voice.PushToTalkButton>(true);
                var serialized = new SerializedObject(ptt);
                serialized.FindProperty("idleColor").colorValue = Color.white;
                serialized.FindProperty("listeningColor").colorValue = new Color(1, .65f, .65f);
                serialized.FindProperty("processingColor").colorValue = new Color(1, .88f, .6f);
                serialized.FindProperty("unavailableColor").colorValue = theme.disabled;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Find(root, "MenuTitle").GetComponent<TMP_Text>().text = "SeYRA";
                Find(root, "MenuTitle").GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0;
                Find(root, "MenuTitle").GetComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 86;
                Find(root, "StatusSummary").GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(window.Content);
        }

        static string IconName(string name) => name == "SendButton" ? "send_" :
            name == "PushToTalkButton" || name == "VoiceButton" ? "mic_200" :
            name == "ReconnectButton" || name == "RecenterButton" ? "restart_alt_" :
            name == "ToggleChatButton" ? "chat_info_" : name == "AllowButton" || name == "CloseTurnButton" ? "check_circle_" : "cancel_";

        public static void CapturePreview(UIXRWindow source, string path, bool showPermission = false)
        {
            var preview = Object.Instantiate(source.gameObject);
            preview.name = "AssistantPresentationPreview";
            var window = preview.GetComponent<UIXRWindow>();
            foreach (var child in preview.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var chat = preview.GetComponent<SeyraSpatialChatWindow>();
            if (chat != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                foreach (var item in new[] {
                    new Seyra.Chat.ChatMessageItem { Key = "preview-user", Role = "You", Text = "Organize the workspace to get started." },
                    new Seyra.Chat.ChatMessageItem { Key = "preview-assistant", Role = "Seyra", Text = "I can help you organize it. Tell me what you need nearby and I will set up the space with you." } })
                {
                    var row = typeof(SeyraSpatialChatWindow).GetMethod("CreateRowGameObject", flags).Invoke(chat, new object[] { item });
                    typeof(SeyraSpatialChatWindow).GetMethod("UpdateRowContent", flags).Invoke(chat, new[] { row, item });
                }
                Find(preview, "PermissionPanel").SetActive(showPermission);
            }
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(window.Content);
            var go = new GameObject("AssistantPreviewCamera", typeof(Camera));
            var target = new RenderTexture(1440, 1080, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1440, 1080, TextureFormat.RGB24, false);
            try
            {
                var camera = go.GetComponent<Camera>();
                camera.cullingMask = 1 << 31;
                camera.aspect = 1440f / 1080;
                var canvas = (RectTransform)window.Canvas.transform;
                go.transform.rotation = canvas.rotation;
                go.transform.position = canvas.TransformPoint(canvas.rect.center) - canvas.forward * 2;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(canvas.rect.height * canvas.lossyScale.y * .56f,
                    canvas.rect.width * canvas.lossyScale.x * .56f / camera.aspect);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 4;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.07f, .09f, .10f);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1440, 1080), 0, 0);
                texture.Apply();
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                go.GetComponent<Camera>().targetTexture = null;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(preview);
            }
        }

        static void Round(UnityEngine.UI.Image image, Material material, float radius)
        {
            image.material = material;
            Get<RoundedBoxUIProperties>(image.gameObject).borderRadius = Vector4.one * radius;
            image.SetAllDirty();
        }

        static T Get<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
        static GameObject Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;
        static void SetHeight(GameObject root, string name, float height)
        {
            var go = Find(root, name);
            if (go != null) Get<UnityEngine.UI.LayoutElement>(go).preferredHeight = height;
        }
    }
}
