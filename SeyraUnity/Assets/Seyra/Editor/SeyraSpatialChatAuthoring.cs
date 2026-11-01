using System;
using System.IO;
using System.Linq;
using Seyra.Chat;
using Seyra.UI;
using Seyra.UI.Library;
using Seyra.Voice;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Seyra.Editor
{
    public static class SeyraSpatialChatAuthoring
    {
        public const string ChatWindowPrefabPath = "Assets/Seyra/UI/Panels/SeyraSpatialChatWindow.prefab";
        public const string MenuPrefabPath = "Assets/Seyra/UI/Panels/SeyraSpatialMenu.prefab";
        public const string PhotoPopupPrefabPath = "Assets/Seyra/UI/Panels/SeyraPhotoPopup.prefab";
        public const string CameraShutterPath = "Assets/Seyra/Audio/camera_shutter.wav";
        public const string CatalogPath = "Assets/Seyra/UI/Catalog/SeyraUIPrefabCatalog.asset";
        public const string SeyraAssistantScenePath = "Assets/Scenes/SeyraAssistant.unity";

        [MenuItem("Seyra/UI/Build Spatial Chat and Menu Prefabs")]
        public static void BuildPrefabs()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building prefabs.");
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath);
            if (catalog == null || catalog.WindowPrefab == null)
                throw new InvalidOperationException("UIPrefabCatalog with WindowPrefab is required at " + CatalogPath);

            var previous = SceneManager.GetActiveScene();
            var authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(authoringScene);
            try
            {
                BuildChatWindowPrefab(catalog);
                BuildMenuPrefab(catalog);
                AssetDatabase.SaveAssets();
                Debug.Log("Seyra UI: Spatial Chat and Menu prefabs built successfully.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(authoringScene, true);
            }
        }

        [MenuItem("Seyra/UI/Setup Spatial Chat in SeyraAssistant")]
        public static void SetupInSeyraAssistant()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before configuring scene.");
            var activeScene = SceneManager.GetActiveScene();
            bool needReopen = activeScene.path != SeyraAssistantScenePath;
            Scene scene = needReopen ? EditorSceneManager.OpenScene(SeyraAssistantScenePath, OpenSceneMode.Single) : activeScene;

            try
            {
                // Ensure Prefabs exist
                var chatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChatWindowPrefabPath);
                var menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
                if (chatPrefab == null || menuPrefab == null)
                {
                    BuildPrefabs();
                    chatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChatWindowPrefabPath);
                    menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
                }

                // Create dedicated application anchor outside Generated
                var appRoot = GameObject.Find("SeyraAssistantApp");
                if (appRoot == null)
                {
                    appRoot = new GameObject("SeyraAssistantApp");
                    Undo.RegisterCreatedObjectUndo(appRoot, "Create SeyraAssistantApp");
                }

                // Instantiate or refresh SpatialChatWindow
                var existingChat = appRoot.GetComponentInChildren<SeyraSpatialChatWindow>(true);
                if (existingChat != null)
                {
                    Undo.DestroyObjectImmediate(existingChat.gameObject);
                }
                var chatGo = (GameObject)PrefabUtility.InstantiatePrefab(chatPrefab, appRoot.transform);
                chatGo.name = "SpatialChatWindow";
                chatGo.transform.localPosition = new Vector3(0f, 1.35f, 1.25f);
                chatGo.transform.localRotation = Quaternion.identity;
                Undo.RegisterCreatedObjectUndo(chatGo, "Instantiate SpatialChatWindow");

                // Instantiate or refresh SpatialMenu
                var existingMenu = appRoot.GetComponentInChildren<SeyraSpatialMenu>(true);
                var existingHandMenu = appRoot.GetComponentInChildren<SeyraHandMenu>(true);
                if (existingHandMenu != null) Undo.DestroyObjectImmediate(existingHandMenu.gameObject);
                else if (existingMenu != null) Undo.DestroyObjectImmediate(existingMenu.gameObject);
                var menuGo = (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab, appRoot.transform);
                menuGo.name = "SpatialMenu";
                menuGo.transform.localPosition = new Vector3(0f, 0.95f, 1.05f);
                menuGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                Undo.RegisterCreatedObjectUndo(menuGo, "Instantiate SpatialMenu");

                // Connect references
                var spatialChat = chatGo.GetComponent<SeyraSpatialChatWindow>();
                var chatXrWindow = chatGo.GetComponent<UIXRWindow>();
                var spatialMenu = menuGo.GetComponentInChildren<SeyraSpatialMenu>(true);
                if (spatialMenu != null)
                {
                    var serializedMenu = new SerializedObject(spatialMenu);
                    serializedMenu.FindProperty("chatWindow").objectReferenceValue = spatialChat;
                    serializedMenu.FindProperty("chatXrWindow").objectReferenceValue = chatXrWindow;
                    serializedMenu.ApplyModifiedProperties();
                }

                // Ensure EventSystem has InputSystemUIInputModule alongside PointableCanvasModule
                var eventSystem = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (eventSystem != null && eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                    EditorUtility.SetDirty(eventSystem.gameObject);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                SeyraSpatialFollowSetup.ConfigureScene(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Seyra: Spatial Chat and Menu configured and saved in SeyraAssistant scene.");
            }
            finally
            {
            }
        }

        public static GameObject BuildChatWindowPrefab(UIPrefabCatalog catalog)
        {
            var xrWindow = (UIXRWindow)PrefabUtility.InstantiatePrefab(catalog.WindowPrefab);
            var windowGo = xrWindow.gameObject;
            windowGo.name = "SeyraSpatialChatWindow";
            xrWindow.Resize(new Vector2(780, 560));

            var content = xrWindow.Content;
            // Clear any default sample children from Content
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
            }

            var font = catalog.DefaultTheme != null ? catalog.DefaultTheme.font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

            // 1. Header Container
            var header = CreateRow(content, "HeaderContainer", 52);
            var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 10;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;

            var dotImg = CreateImage(header.transform, "ChatConnectionDot", 14, 14, new Color(0.2f, 0.85f, 0.45f));
            var titleText = CreateText(header.transform, "Title", "Seyra", 22, FontStyles.Bold, new Color(0.95f, 0.96f, 0.98f), font);
            titleText.GetComponent<LayoutElement>().preferredWidth = 70;

            var statusText = CreateText(header.transform, "ConnectionLabel", "Connected · seyra", 15, FontStyles.Normal, new Color(0.7f, 0.8f, 0.85f), font);
            statusText.GetComponent<LayoutElement>().flexibleWidth = 1;

            var runDotImg = CreateImage(header.transform, "RuntimeStatusDot", 12, 12, new Color(0.2f, 0.85f, 0.45f));
            var runStatusText = CreateText(header.transform, "RuntimeStatusLabel", "Runtime: Connected", 14, FontStyles.Normal, new Color(0.65f, 0.75f, 0.8f), font);
            runStatusText.GetComponent<LayoutElement>().preferredWidth = 150;

            var reconnectBtn = CreateButton(header.transform, "ReconnectButton", "Reconnect", 100, 36, new Color(0.18f, 0.25f, 0.32f), font, 14);
            var closeBtn = CreateButton(header.transform, "CloseWindowButton", "✕", 36, 36, new Color(0.28f, 0.16f, 0.18f), font, 16);

            // 2. Execution Status Banner
            var banner = CreateRow(content, "ExecutionBanner", 48);
            banner.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.20f, 0.85f);
            var bannerLayout = banner.GetComponent<HorizontalLayoutGroup>();
            bannerLayout.padding = new RectOffset(12, 12, 4, 4);
            bannerLayout.spacing = 10;
            bannerLayout.childAlignment = TextAnchor.MiddleLeft;

            var execLabel = CreateText(banner.transform, "ExecutionLabel", "<b>Ready</b> · Dictate a message to get started.", 15, FontStyles.Normal, new Color(0.85f, 0.9f, 0.95f), font);
            execLabel.GetComponent<LayoutElement>().flexibleWidth = 1;

            var cancelBtn = CreateButton(banner.transform, "CancelTurnButton", "Cancel", 95, 34, new Color(0.42f, 0.18f, 0.15f), font, 14);
            cancelBtn.gameObject.SetActive(false);

            var closeTurnBtn = CreateButton(banner.transform, "CloseTurnButton", "Close turn", 110, 34, new Color(0.15f, 0.30f, 0.45f), font, 14);
            closeTurnBtn.gameObject.SetActive(false);

            // 3. Permission Request Panel (initially hidden)
            var permPanel = CreateColumn(content, "PermissionPanel", 130);
            permPanel.GetComponent<Image>().color = new Color(0.22f, 0.18f, 0.12f, 0.95f);
            var permLayout = permPanel.GetComponent<VerticalLayoutGroup>();
            permLayout.padding = new RectOffset(14, 14, 8, 8);
            permLayout.spacing = 6;
            permPanel.SetActive(false);

            var permHeading = CreateText(permPanel.transform, "PermissionHeading", "Permission requested · read", 16, FontStyles.Bold, new Color(1f, 0.85f, 0.45f), font);
            var permDetailsScroll = CreateScrollView(permPanel.transform, "DetailsScroll", 60);
            var permDetails = CreateText(permDetailsScroll.content, "PermissionDetails", "{\n  \"action\": \"read\"\n}", 13, FontStyles.Normal, new Color(0.9f, 0.9f, 0.9f), font);
            permDetails.GetComponent<LayoutElement>().flexibleWidth = 1;

            var permStatus = CreateText(permPanel.transform, "PermissionStatus", "Allow authorizes this request. Reject also cancels pending requests.", 12, FontStyles.Italic, new Color(0.8f, 0.8f, 0.8f), font);

            var permBtns = CreateRow(permPanel.transform, "ButtonsRow", 36);
            var permBtnsLayout = permBtns.GetComponent<HorizontalLayoutGroup>();
            permBtnsLayout.spacing = 12;
            var allowBtn = CreateButton(permBtns.transform, "AllowButton", "Allow once", 140, 34, new Color(0.16f, 0.45f, 0.28f), font, 14);
            var rejectBtn = CreateButton(permBtns.transform, "RejectButton", "Reject", 110, 34, new Color(0.55f, 0.18f, 0.18f), font, 14);

            // 4. Conversation Scroll Area
            var convScroll = CreateScrollView(content, "ConversationScroll", 0);
            convScroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            var messagesContainer = convScroll.content;
            var msgLayout = messagesContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            msgLayout.padding = new RectOffset(6, 6, 8, 8);
            msgLayout.spacing = 10;
            msgLayout.childControlWidth = true;
            msgLayout.childControlHeight = true;
            msgLayout.childForceExpandWidth = true;
            msgLayout.childForceExpandHeight = false;
            var fitter = messagesContainer.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 5. Composer Row with Voice Push-to-Talk
            var composer = CreateRow(content, "ComposerRow", 96);
            var compLayout = composer.GetComponent<HorizontalLayoutGroup>();
            compLayout.spacing = 8;
            compLayout.childAlignment = TextAnchor.MiddleLeft;

            var draftField = new GameObject("DraftField", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            draftField.transform.SetParent(composer.transform, false);
            var draftSize = draftField.GetComponent<LayoutElement>();
            draftSize.flexibleWidth = 1;
            draftSize.minWidth = 220;
            draftSize.minHeight = draftSize.preferredHeight = 96;
            var draftScroll = CreateScrollView(draftField.transform, "DraftScroll", 0);
            var draftRect = (RectTransform)draftScroll.transform;
            draftRect.anchorMin = Vector2.zero;
            draftRect.anchorMax = Vector2.one;
            draftRect.offsetMin = new Vector2(16, 8);
            draftRect.offsetMax = new Vector2(-16, -8);
            var draftLayout = draftScroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
            draftLayout.childControlWidth = draftLayout.childControlHeight = true;
            draftLayout.childForceExpandWidth = true;
            draftLayout.childForceExpandHeight = false;
            draftScroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var draftText = CreateText(draftScroll.content, "DraftText", "Hold Talk to dictate", 20, FontStyles.Normal, Color.white, font);
            draftText.richText = false;
            draftText.alignment = TextAlignmentOptions.TopLeft;
            draftText.raycastTarget = false;

            var voiceBtn = CreateButton(composer.transform, "PushToTalkButton", "Talk", 110, 42, new Color(0.22f, 0.28f, 0.38f), font, 15);
            var sendBtn = CreateButton(composer.transform, "SendButton", "Send", 88, 42, new Color(0.12f, 0.48f, 0.35f), font, 16);

            // 6. Voice Status / Composer Hint Row
            var hintRow = CreateRow(content, "VoiceHintRow", 26);
            var hintLayout = hintRow.GetComponent<HorizontalLayoutGroup>();
            hintLayout.padding = new RectOffset(6, 6, 2, 2);
            var voiceHintLabel = CreateText(hintRow.transform, "VoiceHintLabel", "Hold Talk to dictate a message", 13, FontStyles.Italic, new Color(0.6f, 0.7f, 0.8f), font);
            voiceHintLabel.GetComponent<LayoutElement>().flexibleWidth = 1;

            // Voice Dictation and PushToTalk components
            var voiceDictation = windowGo.AddComponent<VoiceDictation>();
            var ptt = voiceBtn.gameObject.AddComponent<PushToTalkButton>();

            var serializedPtt = new SerializedObject(ptt);
            serializedPtt.FindProperty("label").objectReferenceValue = voiceBtn.GetComponentInChildren<TextMeshProUGUI>();
            serializedPtt.FindProperty("background").objectReferenceValue = voiceBtn.GetComponent<Image>();
            serializedPtt.ApplyModifiedProperties();

            // Wire up SeyraSpatialChatWindow
            var chatWin = windowGo.AddComponent<SeyraSpatialChatWindow>();
            var serializedChat = new SerializedObject(chatWin);
            serializedChat.FindProperty("chatConnectionDot").objectReferenceValue = dotImg.GetComponent<Image>();
            serializedChat.FindProperty("chatConnectionLabel").objectReferenceValue = statusText;
            serializedChat.FindProperty("runtimeStatusDot").objectReferenceValue = runDotImg.GetComponent<Image>();
            serializedChat.FindProperty("runtimeStatusLabel").objectReferenceValue = runStatusText;
            serializedChat.FindProperty("reconnectButton").objectReferenceValue = reconnectBtn;
            serializedChat.FindProperty("closeWindowButton").objectReferenceValue = closeBtn;

            serializedChat.FindProperty("executionBanner").objectReferenceValue = banner;
            serializedChat.FindProperty("executionLabel").objectReferenceValue = execLabel;
            serializedChat.FindProperty("cancelButton").objectReferenceValue = cancelBtn;
            serializedChat.FindProperty("closeTurnButton").objectReferenceValue = closeTurnBtn;

            serializedChat.FindProperty("permissionPanel").objectReferenceValue = permPanel;
            serializedChat.FindProperty("permissionHeading").objectReferenceValue = permHeading;
            serializedChat.FindProperty("permissionDetails").objectReferenceValue = permDetails;
            serializedChat.FindProperty("permissionStatus").objectReferenceValue = permStatus;
            serializedChat.FindProperty("allowPermissionButton").objectReferenceValue = allowBtn;
            serializedChat.FindProperty("rejectPermissionButton").objectReferenceValue = rejectBtn;
            serializedChat.FindProperty("permissionScroll").objectReferenceValue = permDetailsScroll;

            serializedChat.FindProperty("conversationScroll").objectReferenceValue = convScroll;
            serializedChat.FindProperty("messagesContainer").objectReferenceValue = messagesContainer;

            serializedChat.FindProperty("draftText").objectReferenceValue = draftText;
            serializedChat.FindProperty("draftScroll").objectReferenceValue = draftScroll;
            serializedChat.FindProperty("sendButton").objectReferenceValue = sendBtn;
            serializedChat.FindProperty("composerHintLabel").objectReferenceValue = voiceHintLabel;

            serializedChat.FindProperty("voiceDictation").objectReferenceValue = voiceDictation;
            serializedChat.FindProperty("pushToTalkButton").objectReferenceValue = ptt;
            serializedChat.ApplyModifiedProperties();

            serializedPtt.Update();
            serializedPtt.FindProperty("chat").objectReferenceValue = chatWin;
            serializedPtt.FindProperty("dictation").objectReferenceValue = voiceDictation;
            serializedPtt.ApplyModifiedProperties();

            SeyraSpatialChatPresentation.Apply(windowGo);
            AddSpokenReplyControls(windowGo);
            var prefab = PrefabUtility.SaveAsPrefabAsset(windowGo, ChatWindowPrefabPath);
            UnityEngine.Object.DestroyImmediate(windowGo);
            return prefab;
        }

        [MenuItem("Seyra/UI/Update Voice Toggle Controls")]
        public static void UpdateVoiceToggleControls()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before updating prefabs.");
            foreach (var path in new[] { ChatWindowPrefabPath, MenuPrefabPath })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (path == ChatWindowPrefabPath) AddSpokenReplyControls(root);
                    else ConfigureHandMenuPresentation(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        static Sprite VoiceSprite(bool muted) => AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Icons/volume_" + (muted ? "off" : "up") + "_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48.png");

        static void AddSpokenReplyControls(GameObject root)
        {
            var chat = root.GetComponent<SeyraSpatialChatWindow>();
            var settings = new SerializedObject(chat);
            var close = (Button)settings.FindProperty("closeWindowButton").objectReferenceValue;
            var header = close.transform.parent;
            var replay = header.Find("ReplayVoiceButton");
            if (replay != null) UnityEngine.Object.DestroyImmediate(replay.gameObject);
            var output = root.transform.Find("VoiceOutput");
            if (output == null)
            {
                output = new GameObject("VoiceOutput").transform;
                output.SetParent(root.transform, false);
            }
            var voice = output.GetComponent<SpokenReplies>() ?? output.gameObject.AddComponent<SpokenReplies>();
            var previous = root.GetComponent<SpokenReplies>();
            if (previous != null)
            {
                EditorUtility.CopySerialized(previous, voice);
                UnityEngine.Object.DestroyImmediate(previous);
            }
            var existing = header.Find("VoiceToggleButton") ?? header.Find("MuteVoiceButton");
            var button = existing != null ? existing.GetComponent<Button>() : UnityEngine.Object.Instantiate(close, header);
            button.name = "VoiceToggleButton";
            var index = close.transform.GetSiblingIndex();
            if (button.transform.GetSiblingIndex() < index) index--;
            button.transform.SetSiblingIndex(index);
            button.onClick = new Button.ButtonClickedEvent();
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true)) text.gameObject.SetActive(false);
            var icon = button.transform.Find("ActionIcon");
            if (icon == null) icon = UnityEngine.Object.Instantiate(close.transform.Find("ActionIcon"), button.transform);
            icon.name = "ActionIcon";
            var iconRect = (RectTransform)icon;
            var sourceRect = (RectTransform)close.transform.Find("ActionIcon");
            iconRect.anchorMin = sourceRect.anchorMin; iconRect.anchorMax = sourceRect.anchorMax;
            iconRect.anchoredPosition = sourceRect.anchoredPosition; iconRect.sizeDelta = sourceRect.sizeDelta;
            var image = icon.GetComponent<Image>(); image.sprite = VoiceSprite(false);
            var layout = button.GetComponent<LayoutElement>();
            var sourceLayout = close.GetComponent<LayoutElement>();
            layout.minWidth = sourceLayout.minWidth; layout.preferredWidth = sourceLayout.preferredWidth;
            settings.FindProperty("spokenReplies").objectReferenceValue = voice;
            settings.FindProperty("voiceToggleButton").objectReferenceValue = button;
            settings.FindProperty("voiceToggleIcon").objectReferenceValue = image;
            settings.FindProperty("voiceOnSprite").objectReferenceValue = VoiceSprite(false);
            settings.FindProperty("voiceOffSprite").objectReferenceValue = VoiceSprite(true);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Seyra/UI/Build Photo Popup")]
        public static void BuildPhotoPopup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the photo popup.");
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath);
            if (catalog == null || catalog.WindowPrefab == null || catalog.DefaultTheme == null)
                throw new InvalidOperationException("A window prefab and default theme are required in the UI catalog.");
            AssetDatabase.ImportAsset(CameraShutterPath);
            var importer = AssetImporter.GetAtPath(CameraShutterPath) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Camera shutter WAV is required at " + CameraShutterPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.SaveAndReimport();

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var window = (UIXRWindow)PrefabUtility.InstantiatePrefab(catalog.WindowPrefab);
                var root = window.gameObject;
                root.name = "SeyraPhotoPopup";
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * .5f;
                window.Resize(new Vector2(480, 420));
                if (window.GrabFrame != null)
                {
                    if (window.GrabFrame.Grabbable != null) window.GrabFrame.Grabbable.enabled = false;
                    window.GrabFrame.gameObject.SetActive(false);
                    window.ConfigureGrab(null);
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var raycaster in root.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true)) raycaster.enabled = false;

                var content = window.Content;
                for (int i = content.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
                var layout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ??
                    content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layout.padding = new RectOffset(16, 16, 16, 16);
                layout.spacing = 8;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;

                var imageSlot = new GameObject("PhotoImageSlot", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
                imageSlot.transform.SetParent(content, false);
                var imageSize = imageSlot.GetComponent<UnityEngine.UI.LayoutElement>();
                imageSize.minWidth = imageSize.preferredWidth = 448;
                imageSize.minHeight = imageSize.preferredHeight = 336;
                var imageObject = new GameObject("PhotoImage", typeof(RectTransform), typeof(UnityEngine.UI.RawImage),
                    typeof(UnityEngine.UI.AspectRatioFitter));
                imageObject.transform.SetParent(imageSlot.transform, false);
                imageObject.GetComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
                var aspect = imageObject.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = 4f / 3f;
                var label = CreateText(content, "PhotoLabel", "Seyra will see this when you send it.", 16,
                    FontStyles.Normal, Color.white, catalog.DefaultTheme.font);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.enableAutoSizing = true;
                label.fontSizeMin = 12;
                label.fontSizeMax = 16;
                var labelSize = label.GetComponent<UnityEngine.UI.LayoutElement>();
                labelSize.minWidth = labelSize.preferredWidth = 448;
                labelSize.minHeight = labelSize.preferredHeight = 44;

                var follow = root.AddComponent<SeyraLazyFollow>();
                var followSettings = new SerializedObject(follow);
                followSettings.FindProperty("restOffset").vector3Value = new Vector3(.18f, -.12f, .60f);
                followSettings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)window.Canvas.transform);
                root.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PhotoPopupPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Seyra: Photo popup prefab built successfully.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Seyra/UI/Build Hand Menu Prefab")]
        public static void BuildHandMenuPrefab()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the hand menu.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try { BuildMenuPrefab(AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath)); AssetDatabase.SaveAssets(); }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
        }

        public static GameObject BuildMenuPrefab(UIPrefabCatalog catalog)
        {
            var root = new GameObject("SeyraHandMenu");
            var hand = root.AddComponent<SeyraHandMenu>();
            var window = (UIXRWindow)PrefabUtility.InstantiatePrefab(catalog.WindowPrefab, root.transform);
            var menuGo = window.gameObject;
            menuGo.name = "SpatialMenu";
            menuGo.transform.localPosition = Vector3.zero;
            menuGo.transform.localRotation = Quaternion.identity;
            menuGo.transform.localScale = Vector3.one * .5f;
            // The library canvas uses 0.001 m/unit; half-scale gives 0.15 x 0.18 m.
            window.Resize(new Vector2(300, 360));
            if (window.GrabFrame != null)
            {
                foreach (var collider in window.GrabFrame.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                if (window.GrabFrame.Grabbable != null) window.GrabFrame.Grabbable.enabled = false;
                window.GrabFrame.gameObject.SetActive(false);
                window.ConfigureGrab(null);
            }
            var content = window.Content;
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var font = catalog.DefaultTheme.font;
            var top = CreateRow(content, "StatusRow", 60);
            var topLayout = top.GetComponent<HorizontalLayoutGroup>();
            topLayout.spacing = 4;
            topLayout.childAlignment = TextAnchor.MiddleCenter;
            var chatDot = CreateImage(top.transform, "ChatDot", 8, 8, Color.gray).GetComponent<Image>();
            var runtimeDot = CreateImage(top.transform, "RuntimeDot", 8, 8, Color.gray).GetComponent<Image>();
            chatDot.raycastTarget = runtimeDot.raycastTarget = false;
            var summary = CreateText(top.transform, "StatusSummary", "Room: no map", 12, FontStyles.Normal, Color.white, font);
            summary.enableAutoSizing = true;
            summary.fontSizeMin = 7; summary.fontSizeMax = 12;
            summary.textWrappingMode = TextWrappingModes.NoWrap;
            summary.overflowMode = TextOverflowModes.Ellipsis;
            summary.GetComponent<LayoutElement>().minWidth = 0;
            summary.GetComponent<LayoutElement>().flexibleWidth = 1;
            var gridGo = new GameObject("ActionGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            gridGo.transform.SetParent(content, false);
            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(120, 120); grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.MiddleCenter;
            gridGo.GetComponent<LayoutElement>().minHeight = gridGo.GetComponent<LayoutElement>().preferredHeight = 252;
            window.Factory.Configure(catalog, content);
            Button Action(string name, string label)
            {
                var view = window.Factory.InstantiateButton((RectTransform)gridGo.transform, new UIButtonOptions { Name = name, Label = label });
                view.Button.name = name;
                foreach (var size in view.GetComponentsInChildren<LayoutElement>(true))
                { size.minWidth = size.minHeight = 0; size.preferredWidth = size.preferredHeight = 120; }
                var text = view.GetComponentInChildren<TMP_Text>(true);
                text.fontSize = 18; text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = 18;
                text.textWrappingMode = TextWrappingModes.Normal; text.alignment = TextAlignmentOptions.Center;
                return view.Button;
            }
            var toggle = Action("ToggleChatButton", "Open chat");
            var voice = Action("VoiceButton", "Voice");
            var voiceToggle = Action("VoiceToggleButton", "Voice output");
            var bring = Action("BringChatButton", "Bring chat");
            var menu = menuGo.AddComponent<SeyraSpatialMenu>();
            var serialized = new SerializedObject(menu);
            void Reference(string name, UnityEngine.Object value) => serialized.FindProperty(name).objectReferenceValue = value;
            Reference("voiceToggleButton", voiceToggle);
            Reference("toggleChatButton", toggle);
            Reference("voiceActionButton", voice); Reference("bringChatButton", bring);
            Reference("chatStatusDot", chatDot); Reference("runtimeStatusDot", runtimeDot); Reference("statusSummary", summary);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var handSettings = new SerializedObject(hand);
            handSettings.FindProperty("window").objectReferenceValue = window;
            var reference = ResolveMenuActionReference();
            handSettings.FindProperty("toggleMenuAction").objectReferenceValue = reference;
            handSettings.ApplyModifiedPropertiesWithoutUndo();
            ConfigureHandMenuPresentation(root);
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)window.Canvas.transform);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MenuPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        public static void ConfigureHandMenuPresentation(GameObject root)
        {
            var menu = root.GetComponentInChildren<SeyraSpatialMenu>(true);
            var window = menu.GetComponent<UIXRWindow>();
            var grid = window.Content.GetComponentInChildren<GridLayoutGroup>(true);
            foreach (var child in grid.GetComponentsInChildren<Button>(true).Where(b => b.name == "ScanRoomButton").ToArray())
                UnityEngine.Object.DestroyImmediate(child.GetComponentInParent<UIInstance>()?.gameObject ?? child.gameObject);
            window.Resize(new Vector2(312, 144));
            var column = window.Content.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(16, 16, 12, 12); column.spacing = 8;
            grid.cellSize = new Vector2(64, 64); grid.spacing = new Vector2(8, 0);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4;
            var gridSize = grid.GetComponent<LayoutElement>(); gridSize.minHeight = gridSize.preferredHeight = 64;
            var status = menu.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "StatusSummary");
            status.text = "Chat offline";
            var rowSize = status.transform.parent.GetComponent<LayoutElement>(); rowSize.minHeight = rowSize.preferredHeight = 36;
            var existingToggle = grid.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "VoiceToggleButton");
            if (existingToggle == null)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath);
                window.Factory.Configure(catalog, window.Content);
                var view = window.Factory.InstantiateButton((RectTransform)grid.transform, new UIButtonOptions { Name = "VoiceToggleButton", Label = "Voice output" });
                view.Button.name = "VoiceToggleButton";
                existingToggle = view.Button;
            }
            var toggleRoot = existingToggle.GetComponentInParent<UIInstance>()?.transform ?? existingToggle.transform;
            var bringButton = grid.GetComponentsInChildren<Button>(true).First(b => b.name == "BringChatButton");
            var bringRoot = bringButton.GetComponentInParent<UIInstance>()?.transform ?? bringButton.transform;
            var index = bringRoot.GetSiblingIndex();
            if (toggleRoot.GetSiblingIndex() < index) index--;
            toggleRoot.SetSiblingIndex(index);
            var serialized = new SerializedObject(menu);
            void Reference(string name, UnityEngine.Object value) => serialized.FindProperty(name).objectReferenceValue = value;
            Reference("voiceToggleButton", existingToggle);
            Sprite Icon(string name)
            {
                var path = "Assets/Icons/" + name + "_200dp_FFFFFF_FILL0_wght400_GRAD0_opsz48.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                return sprite != null ? sprite : throw new InvalidOperationException("A Sprite icon is required at " + path);
            }
            Image ConfigureAction(string name, Sprite sprite, float cellSize, float iconSize)
            {
                var button = grid.GetComponentsInChildren<Button>(true).First(b => b.name == name);
                var view = button.GetComponent<UIActionButton>();
                view.SetIcon(sprite);
                foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.gameObject.SetActive(false);
                foreach (var size in view.GetComponentsInChildren<LayoutElement>(true))
                { size.minWidth = size.minHeight = 0; size.preferredWidth = size.preferredHeight = cellSize; }
                var icon = view.IconTransform;
                var iconLayout = icon.parent.GetComponent<HorizontalLayoutGroup>();
                iconLayout.childAlignment = TextAnchor.MiddleCenter; iconLayout.spacing = 0;
                iconLayout.padding = new RectOffset();
                iconLayout.childForceExpandWidth = iconLayout.childForceExpandHeight = false;
                var iconLayoutElement = icon.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
                iconLayoutElement.minWidth = iconLayoutElement.minHeight = iconLayoutElement.preferredWidth = iconLayoutElement.preferredHeight = iconSize;
                iconLayoutElement.flexibleWidth = iconLayoutElement.flexibleHeight = 0;
                var image = icon.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
                return image;
            }
            var show = Icon("chat"); var hide = Icon("text_ad_off"); var mic = Icon("mic"); var micOff = Icon("mic_off");
            Reference("toggleChatIcon", ConfigureAction("ToggleChatButton", show, 64, 40));
            Reference("voiceIcon", ConfigureAction("VoiceButton", mic, 64, 40));
            ConfigureAction("BringChatButton", Icon("recenter"), 64, 40);
            Reference("showChatSprite", show); Reference("hideChatSprite", hide);
            Reference("microphoneSprite", mic); Reference("microphoneOffSprite", micOff);
            Reference("voiceToggleIcon", ConfigureAction("VoiceToggleButton", VoiceSprite(false), 64, 40));
            Reference("voiceOnSprite", VoiceSprite(false)); Reference("voiceOffSprite", VoiceSprite(true));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        public static UnityEngine.InputSystem.InputActionReference ResolveMenuActionReference()
        {
            const string path = "Assets/Seyra/Input/SeyraInput.inputactions";
            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(path);
            var action = actions != null ? actions.FindAction("QuestButtons/Menu") ?? actions.FindAction("Menu/ToggleMenu") : null;
            if (action == null) throw new InvalidOperationException("A menu toggle action is required in SeyraInput.");
            var reference = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<UnityEngine.InputSystem.InputActionReference>(
                AssetDatabase.LoadAllAssetsAtPath(path)), r => r.action != null && r.action.id == action.id);
            return reference != null ? reference : throw new InvalidOperationException("The imported menu toggle action reference is required.");
        }
        static GameObject CreateRow(Transform parent, string name, float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.clear;
            var group = go.GetComponent<HorizontalLayoutGroup>();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = 1;
            return go;
        }

        static GameObject CreateColumn(Transform parent, string name, float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.clear;
            var group = go.GetComponent<VerticalLayoutGroup>();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = 1;
            return go;
        }

        static GameObject CreateImage(Transform parent, string name, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            return go;
        }

        static TMP_Text CreateText(Transform parent, string name, string text, float size, FontStyles style, Color color, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.font = font;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;
            return tmp;
        }

        static Button CreateButton(Transform parent, string name, string label, float width, float height, Color color, TMP_FontAsset font, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var le = go.GetComponent<LayoutElement>();
            if (width > 0) le.preferredWidth = width;
            le.preferredHeight = height;

            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)txtGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4, 2);
            rect.offsetMax = new Vector2(-4, -2);

            var tmp = txtGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.font = font;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            return btn;
        }

        static ScrollRect CreateScrollView(Transform parent, string name, float height)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var scroll = root.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            if (height > 0)
            {
                var le = root.GetComponent<LayoutElement>();
                le.preferredHeight = height;
                le.flexibleWidth = 1;
            }

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            var vpRect = (RectTransform)viewport.transform;
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = vpRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.2f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = Vector2.one;
            cRect.pivot = new Vector2(0.5f, 1);
            cRect.sizeDelta = Vector2.zero;

            scroll.viewport = vpRect;
            scroll.content = cRect;
            return scroll;
        }

    }
}
