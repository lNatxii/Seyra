using System;
using System.IO;
using Seyra.Configuration;
using Seyra.SceneTools;
using Seyra.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;

namespace Seyra.Editor
{
    public static class SeyraAndroidBuild
    {
        internal static bool IsPhoneTestBuild { get; private set; }
        public const string MobileScenePath = "Assets/Scenes/MobileConnectionTest.unity";
        [MenuItem("Seyra/Build/Create Mobile Connection Test Scene")]
        public static void CreateMobileConnectionTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (File.Exists(MobileScenePath)) throw new InvalidOperationException("MobileConnectionTest already exists. Remove the copy explicitly before recreating it.");
            if (!AssetDatabase.CopyAsset("Assets/Scenes/OpencodeTestUI.unity", MobileScenePath)) throw new IOException("Could not copy test scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(MobileScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                Canvas canvas = null;
                OpenCodeTestChat chat = null;
                SeyraSceneRuntime runtime = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (canvas == null) canvas = root.GetComponentInChildren<Canvas>(true);
                    if (chat == null) chat = root.GetComponentInChildren<OpenCodeTestChat>(true);
                    if (runtime == null) runtime = root.GetComponentInChildren<SeyraSceneRuntime>(true);
                }
                if (canvas == null || chat == null || runtime == null) throw new InvalidOperationException("Expected testbed canvas, chat and runtime.");
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                var safeRoot = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
                safeRoot.SetParent(canvas.transform, false); Stretch(safeRoot);
                var children = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in canvas.transform) if (child != safeRoot) children.Add(child);
                foreach (var child in children) child.SetParent(safeRoot, false);
                var chatLayout = safeRoot.Find("ChatLayout") as RectTransform;
                if (chatLayout == null) throw new InvalidOperationException("Expected ChatLayout.");
                chatLayout.anchorMin = Vector2.zero; chatLayout.anchorMax = Vector2.one;
                chatLayout.offsetMin = new Vector2(24, 20); chatLayout.offsetMax = new Vector2(-24, -1290);
                // Device clients remain unpaired until a gateway configuration is installed.
                chat.enabled = true; runtime.enabled = true;
                var panel = new GameObject("ConnectionDiagnostics", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
                panel.SetParent(safeRoot, false); panel.anchorMin = new Vector2(0, 1); panel.anchorMax = Vector2.one;
                panel.offsetMin = new Vector2(24, -1270); panel.offsetMax = new Vector2(-24, -20);
                panel.GetComponent<Image>().color = new Color(.06f, .08f, .12f, .98f);
                var layout = panel.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(18, 18, 18, 18); layout.spacing = 12; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandHeight = false;
                var status = Text(panel, "Status", 24); status.gameObject.AddComponent<LayoutElement>().preferredHeight = 220;
                var buttons = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                buttons.transform.SetParent(panel, false); buttons.GetComponent<LayoutElement>().preferredHeight = 70;
                var probe = Button(buttons.transform, "Probar TLS"); var reconnect = Button(buttons.transform, "Reconectar");
                var scroll = new GameObject("ResultsScroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
                scroll.transform.SetParent(panel, false); scroll.GetComponent<Image>().color = new Color(.1f,.12f,.16f);
                scroll.GetComponent<LayoutElement>().minHeight = 900;
                scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
                var results = Text(scroll.transform, "Results", 23);
                results.text = "Copia la configuración de prueba y pulsa Probar TLS. Desliza para leer todos los resultados.";
                var content = results.rectTransform; content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1); content.offsetMin = content.offsetMax = Vector2.zero;
                results.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scrollRect = scroll.GetComponent<ScrollRect>(); scrollRect.viewport = (RectTransform)scroll.transform; scrollRect.content = content; scrollRect.horizontal = false;
                panel.gameObject.AddComponent<ConnectionDiagnosticsPanel>().Configure(safeRoot, status, results, probe, reconnect, chat, runtime);
                foreach (var field in canvas.GetComponentsInChildren<TMP_InputField>(true)) { field.shouldHideMobileInput = false; field.shouldHideSoftKeyboard = false; }
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save mobile scene.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static TMP_Text Text(Transform parent, string name, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false); text.font = TMP_Settings.defaultFontAsset; text.fontSize = size; text.color = Color.white; text.richText = false; text.raycastTarget = false;
            return text;
        }
        static Button Button(Transform parent, string label)
        {
            var button = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false); button.GetComponent<Image>().color = new Color(.15f,.3f,.5f);
            var text = Text(button.transform, "Label", 28); text.text = label; text.alignment = TextAlignmentOptions.Center; Stretch(text.rectTransform);
            return button;
        }
        [MenuItem("Seyra/Build/Update Mobile Connection Test Scene")]
        public static void UpdateMobileConnectionTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetSceneByPath(MobileScenePath);
            bool wasLoaded = scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(MobileScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var chat in root.GetComponentsInChildren<OpenCodeTestChat>(true)) chat.enabled = true;
                    foreach (var runtime in root.GetComponentsInChildren<SeyraSceneRuntime>(true)) runtime.enabled = true;
                    foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (rect.name == "ResultsScroll") rect.GetComponent<LayoutElement>().minHeight = 900;
                        if (rect.name == "ConnectionDiagnostics")
                        {
                            rect.anchorMin = new Vector2(0,1); rect.anchorMax = Vector2.one;
                            rect.offsetMin = new Vector2(24,-1270); rect.offsetMax = new Vector2(-24,-20);
                        }
                        if (rect.name == "ChatLayout")
                        {
                            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                            rect.offsetMin = new Vector2(24,20); rect.offsetMax = new Vector2(-24,-1290);
                        }
                    }
                }
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not update mobile scene.");
            }
            finally { if (!wasLoaded) EditorSceneManager.CloseScene(scene, true); }
        }
        [MenuItem("Seyra/Build/Android APK")]
        public static void BuildAndroidApk() => BuildAndroid(false);
        [MenuItem("Seyra/Build/Android Phone Test")]
        public static void BuildAndroidPhoneTest() => BuildAndroid(true);
        static void BuildAndroid(bool phone)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (phone)
            {
                if (!File.Exists(MobileScenePath)) CreateMobileConnectionTestScene();
                UpdateMobileConnectionTestScene();
            }
            string scenePath = phone ? MobileScenePath : "Assets/Scenes/SeyraAssistant.unity";
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("Android must use IL2CPP for the pinning verification.");
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (xr == null) throw new InvalidOperationException("Android XR settings are missing.");
            var configuration = new System.Collections.Generic.Dictionary<string, byte[]>();
            foreach (var directory in new[] { "ProjectSettings", "Assets/XR" })
                foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    if (!file.Contains("~")) configuration[file] = File.ReadAllBytes(file);
            var preloadedAssets = PlayerSettings.GetPreloadedAssets();
            var orientation = PlayerSettings.defaultInterfaceOrientation;
            bool initialize = xr.InitManagerOnStart;
            bool appBundle = EditorUserBuildSettings.buildAppBundle;
            var scenes = EditorBuildSettings.scenes;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, phone ? "../../Builds/SeyraPhoneTest.apk" : "../../Builds/Seyra.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            try
            {
                IsPhoneTestBuild = phone;
                xr.InitManagerOnStart = !phone; EditorUtility.SetDirty(xr);
                if (phone) PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                EditorUserBuildSettings.buildAppBundle = false;
                AssetDatabase.SaveAssets();
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.Development });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Android build: " + report.summary.result);
                Debug.Log("Seyra APK: " + output);
            }
            finally
            {
                IsPhoneTestBuild = false;
                PlayerSettings.SetPreloadedAssets(preloadedAssets);
                xr.InitManagerOnStart = initialize; EditorUtility.SetDirty(xr);
                PlayerSettings.defaultInterfaceOrientation = orientation;
                EditorUserBuildSettings.buildAppBundle = appBundle;
                EditorBuildSettings.scenes = scenes;
                AssetDatabase.SaveAssets();
                // Build preprocessors can also serialize unrelated XR and player settings.
                foreach (var file in configuration)
                    if (!System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(file.Key), file.Value))
                        File.WriteAllBytes(file.Key, file.Value);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                // XR packages can queue settings writes after the build and asset refresh.
                EditorApplication.delayCall += () =>
                {
                    PlayerSettings.defaultInterfaceOrientation = orientation;
                    foreach (var file in configuration)
                        if (!System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(file.Key), file.Value))
                            File.WriteAllBytes(file.Key, file.Value);
                };
            }
        }
    }
}
