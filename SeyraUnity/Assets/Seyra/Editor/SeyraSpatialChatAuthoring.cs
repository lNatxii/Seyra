using System;
using System.IO;
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
                if (existingMenu != null)
                {
                    Undo.DestroyObjectImmediate(existingMenu.gameObject);
                }
                var menuGo = (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab, appRoot.transform);
                menuGo.name = "SpatialMenu";
                menuGo.transform.localPosition = new Vector3(0f, 0.95f, 1.05f);
                menuGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                Undo.RegisterCreatedObjectUndo(menuGo, "Instantiate SpatialMenu");

                // Connect references
                var spatialChat = chatGo.GetComponent<SeyraSpatialChatWindow>();
                var chatXrWindow = chatGo.GetComponent<UIXRWindow>();
                var spatialMenu = menuGo.GetComponent<SeyraSpatialMenu>();
                var follow = menuGo.GetComponent<SeyraLazyFollow>();
                var visibility = menuGo.GetComponent<SeyraMenuVisibility>();
                if (visibility != null)
                {
                    visibility.ChatWindow = chatXrWindow;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(visibility);
                }
                if (follow != null)
                {
                    var serializedFollow = new SerializedObject(follow);
                    serializedFollow.FindProperty("head").objectReferenceValue = SeyraLazyFollow.ResolveHead();
                    serializedFollow.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(follow);
                }
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

            var statusText = CreateText(header.transform, "ConnectionLabel", "Conectado · seyra", 15, FontStyles.Normal, new Color(0.7f, 0.8f, 0.85f), font);
            statusText.GetComponent<LayoutElement>().flexibleWidth = 1;

            var runDotImg = CreateImage(header.transform, "RuntimeStatusDot", 12, 12, new Color(0.2f, 0.85f, 0.45f));
            var runStatusText = CreateText(header.transform, "RuntimeStatusLabel", "Runtime: Conectado", 14, FontStyles.Normal, new Color(0.65f, 0.75f, 0.8f), font);
            runStatusText.GetComponent<LayoutElement>().preferredWidth = 150;

            var reconnectBtn = CreateButton(header.transform, "ReconnectButton", "Reconectar", 100, 36, new Color(0.18f, 0.25f, 0.32f), font, 14);
            var closeBtn = CreateButton(header.transform, "CloseWindowButton", "✕", 36, 36, new Color(0.28f, 0.16f, 0.18f), font, 16);

            // 2. Execution Status Banner
            var banner = CreateRow(content, "ExecutionBanner", 48);
            banner.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.20f, 0.85f);
            var bannerLayout = banner.GetComponent<HorizontalLayoutGroup>();
            bannerLayout.padding = new RectOffset(12, 12, 4, 4);
            bannerLayout.spacing = 10;
            bannerLayout.childAlignment = TextAnchor.MiddleLeft;

            var execLabel = CreateText(banner.transform, "ExecutionLabel", "<b>Listo</b> · Dicta un mensaje para empezar.", 15, FontStyles.Normal, new Color(0.85f, 0.9f, 0.95f), font);
            execLabel.GetComponent<LayoutElement>().flexibleWidth = 1;

            var cancelBtn = CreateButton(banner.transform, "CancelTurnButton", "Cancelar", 95, 34, new Color(0.42f, 0.18f, 0.15f), font, 14);
            cancelBtn.gameObject.SetActive(false);

            var closeTurnBtn = CreateButton(banner.transform, "CloseTurnButton", "Cerrar turno", 110, 34, new Color(0.15f, 0.30f, 0.45f), font, 14);
            closeTurnBtn.gameObject.SetActive(false);

            // 3. Permission Request Panel (initially hidden)
            var permPanel = CreateColumn(content, "PermissionPanel", 130);
            permPanel.GetComponent<Image>().color = new Color(0.22f, 0.18f, 0.12f, 0.95f);
            var permLayout = permPanel.GetComponent<VerticalLayoutGroup>();
            permLayout.padding = new RectOffset(14, 14, 8, 8);
            permLayout.spacing = 6;
            permPanel.SetActive(false);

            var permHeading = CreateText(permPanel.transform, "PermissionHeading", "Permiso solicitado · read", 16, FontStyles.Bold, new Color(1f, 0.85f, 0.45f), font);
            var permDetailsScroll = CreateScrollView(permPanel.transform, "DetailsScroll", 60);
            var permDetails = CreateText(permDetailsScroll.content, "PermissionDetails", "{\n  \"action\": \"read\"\n}", 13, FontStyles.Normal, new Color(0.9f, 0.9f, 0.9f), font);
            permDetails.GetComponent<LayoutElement>().flexibleWidth = 1;

            var permStatus = CreateText(permPanel.transform, "PermissionStatus", "Permitir autoriza esta petición. Rechazar cancela también los pendientes.", 12, FontStyles.Italic, new Color(0.8f, 0.8f, 0.8f), font);

            var permBtns = CreateRow(permPanel.transform, "ButtonsRow", 36);
            var permBtnsLayout = permBtns.GetComponent<HorizontalLayoutGroup>();
            permBtnsLayout.spacing = 12;
            var allowBtn = CreateButton(permBtns.transform, "AllowButton", "Permitir una vez", 140, 34, new Color(0.16f, 0.45f, 0.28f), font, 14);
            var rejectBtn = CreateButton(permBtns.transform, "RejectButton", "Rechazar", 110, 34, new Color(0.55f, 0.18f, 0.18f), font, 14);

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
            var draftText = CreateText(draftScroll.content, "DraftText", "Mantén pulsado Hablar para dictar", 20, FontStyles.Normal, Color.white, font);
            draftText.richText = false;
            draftText.alignment = TextAlignmentOptions.TopLeft;
            draftText.raycastTarget = false;

            var voiceBtn = CreateButton(composer.transform, "PushToTalkButton", "Hablar", 110, 42, new Color(0.22f, 0.28f, 0.38f), font, 15);
            var sendBtn = CreateButton(composer.transform, "SendButton", "Enviar", 88, 42, new Color(0.12f, 0.48f, 0.35f), font, 16);

            // 6. Voice Status / Composer Hint Row
            var hintRow = CreateRow(content, "VoiceHintRow", 26);
            var hintLayout = hintRow.GetComponent<HorizontalLayoutGroup>();
            hintLayout.padding = new RectOffset(6, 6, 2, 2);
            var voiceHintLabel = CreateText(hintRow.transform, "VoiceHintLabel", "Mantén pulsado Hablar para dictar por voz", 13, FontStyles.Italic, new Color(0.6f, 0.7f, 0.8f), font);
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
            var prefab = PrefabUtility.SaveAsPrefabAsset(windowGo, ChatWindowPrefabPath);
            UnityEngine.Object.DestroyImmediate(windowGo);
            return prefab;
        }

        public static GameObject BuildMenuPrefab(UIPrefabCatalog catalog)
        {
            var xrWindow = (UIXRWindow)PrefabUtility.InstantiatePrefab(catalog.WindowPrefab);
            var menuGo = xrWindow.gameObject;
            menuGo.name = "SeyraSpatialMenu";
            xrWindow.Resize(new Vector2(460, 110));

            var content = xrWindow.Content;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
            }

            var font = catalog.DefaultTheme != null ? catalog.DefaultTheme.font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");

            // Header row
            var topRow = CreateRow(content, "HeaderRow", 28);
            var title = CreateText(topRow.transform, "MenuTitle", "<b>SeYRA</b> · Asistente", 16, FontStyles.Normal, new Color(0.95f, 0.96f, 0.98f), font);
            title.GetComponent<LayoutElement>().flexibleWidth = 1;

            var chatDot = CreateImage(topRow.transform, "ChatDot", 12, 12, new Color(0.2f, 0.85f, 0.45f));
            var runDot = CreateImage(topRow.transform, "RuntimeDot", 12, 12, new Color(0.2f, 0.85f, 0.45f));
            var summary = CreateText(topRow.transform, "StatusSummary", "Chat: OK · Runtime: OK", 13, FontStyles.Normal, new Color(0.7f, 0.8f, 0.85f), font);

            // Controls row
            var controlsRow = CreateRow(content, "ControlsRow", 46);
            var crLayout = controlsRow.GetComponent<HorizontalLayoutGroup>();
            crLayout.spacing = 10;
            crLayout.childAlignment = TextAnchor.MiddleCenter;

            var toggleChatBtn = CreateButton(controlsRow.transform, "ToggleChatButton", "Ocultar chat", 140, 40, new Color(0.12f, 0.44f, 0.35f), font, 15);
            toggleChatBtn.GetComponent<LayoutElement>().flexibleWidth = 1;
            var toggleChatLabel = toggleChatBtn.GetComponentInChildren<TextMeshProUGUI>();

            var recenterBtn = CreateButton(controlsRow.transform, "RecenterButton", "🎯 Reubicar", 130, 40, new Color(0.20f, 0.32f, 0.45f), font, 15);
            recenterBtn.GetComponent<LayoutElement>().flexibleWidth = 1;

            var voiceBtn = CreateButton(controlsRow.transform, "VoiceButton", "🎙 Dictar", 120, 40, new Color(0.22f, 0.28f, 0.38f), font, 15);
            voiceBtn.GetComponent<LayoutElement>().flexibleWidth = 1;

            var follow = menuGo.AddComponent<SeyraLazyFollow>();
            var serializedFollow = new SerializedObject(follow);
            serializedFollow.FindProperty("grabFrame").objectReferenceValue = xrWindow.GrabFrame;
            serializedFollow.ApplyModifiedProperties();
            var visibility = menuGo.AddComponent<SeyraMenuVisibility>();
            var serializedVisibility = new SerializedObject(visibility);
            serializedVisibility.FindProperty("follow").objectReferenceValue = follow;
            var parts = serializedVisibility.FindProperty("visualParts");
            parts.arraySize = menuGo.transform.childCount;
            for (int i = 0; i < parts.arraySize; i++)
                parts.GetArrayElementAtIndex(i).objectReferenceValue = menuGo.transform.GetChild(i).gameObject;
            var behaviours = serializedVisibility.FindProperty("visualBehaviours");
            var interaction = new Behaviour[] {
                menuGo.GetComponent<Oculus.Interaction.PointableCanvas>(),
                menuGo.GetComponent<Oculus.Interaction.RayInteractable>(),
                menuGo.GetComponent<Oculus.Interaction.PokeInteractable>()
            };
            behaviours.arraySize = interaction.Length;
            for (int i = 0; i < interaction.Length; i++)
                behaviours.GetArrayElementAtIndex(i).objectReferenceValue = interaction[i];
            serializedVisibility.ApplyModifiedProperties();
            var spatialMenu = menuGo.AddComponent<SeyraSpatialMenu>();
            var serializedMenu = new SerializedObject(spatialMenu);
            serializedMenu.FindProperty("follow").objectReferenceValue = follow;
            serializedMenu.FindProperty("toggleChatButton").objectReferenceValue = toggleChatBtn;
            serializedMenu.FindProperty("toggleChatLabel").objectReferenceValue = toggleChatLabel;
            serializedMenu.FindProperty("recenterButton").objectReferenceValue = recenterBtn;
            serializedMenu.FindProperty("voiceActionButton").objectReferenceValue = voiceBtn;
            serializedMenu.FindProperty("chatStatusDot").objectReferenceValue = chatDot.GetComponent<Image>();
            serializedMenu.FindProperty("runtimeStatusDot").objectReferenceValue = runDot.GetComponent<Image>();
            serializedMenu.FindProperty("statusSummary").objectReferenceValue = summary;
            serializedMenu.ApplyModifiedProperties();

            SeyraSpatialChatPresentation.Apply(menuGo);
            SeyraSpatialFollowSetup.ConfigureMenu(menuGo);
            var prefab = PrefabUtility.SaveAsPrefabAsset(menuGo, MenuPrefabPath);
            UnityEngine.Object.DestroyImmediate(menuGo);
            return prefab;
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
