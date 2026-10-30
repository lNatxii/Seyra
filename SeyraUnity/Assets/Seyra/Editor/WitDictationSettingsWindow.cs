using System;
using Seyra.Configuration;
using Seyra.Voice;
using UnityEditor;
using UnityEngine;

namespace Seyra.Editor
{
    public sealed class WitDictationSettingsWindow : EditorWindow
    {
        string microphone;
        string[] devices = Array.Empty<string>();

        [MenuItem("Seyra/Voice/Configure Wit.ai for Play Mode")]
        public static void Open() => GetWindow<WitDictationSettingsWindow>("Voice · Wit.ai");

        void OnEnable()
        {
            minSize = new Vector2(520, 340);
            microphone = SeyraEnvironmentCredentials.PreferredMicrophone;
            devices = Microphone.devices;
            SeyraEnvironmentCredentials.ForceReload();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Play Mode dictation (PC)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Play Mode uses the PC microphone and Wit.ai to transcribe speech to text.\n" +
                "On Quest, Meta platform dictation is used.",
                MessageType.Info);

            EditorGUILayout.Space();

            bool hasToken = SeyraEnvironmentCredentials.HasWitToken;
            if (hasToken)
            {
                EditorGUILayout.HelpBox(
                    "Token loaded from Backend/.env.local (SEYRA_CLIENT_TOKEN):\n" +
                    SeyraEnvironmentCredentials.RedactToken(SeyraEnvironmentCredentials.WitClientToken),
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "SEYRA_CLIENT_TOKEN was not found in Backend/.env.local.\n" +
                    "Set SEYRA_CLIENT_TOKEN to your Wit.ai Client Access Token.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Open Wit.ai"))
            {
                Application.OpenURL("https://wit.ai/");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Audio input device", EditorStyles.boldLabel);

            var choices = new string[devices.Length + 1];
            choices[0] = "Automatic (first available microphone)";
            Array.Copy(devices, 0, choices, 1, devices.Length);

            int selected = string.IsNullOrEmpty(microphone) ? 0 : Array.IndexOf(devices, microphone) + 1;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("PC microphone", selected, choices);
            if (EditorGUI.EndChangeCheck())
            {
                microphone = next > 0 ? devices[next - 1] : "";
            }

            if (!string.IsNullOrEmpty(microphone) && Array.IndexOf(devices, microphone) < 0)
            {
                EditorGUILayout.HelpBox("The saved microphone is not connected: " + microphone, MessageType.Warning);
            }

            if (GUILayout.Button("Refresh device list"))
            {
                devices = Microphone.devices;
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Save and apply"))
            {
                SeyraEnvironmentCredentials.PreferredMicrophone = microphone;
                SeyraEnvironmentCredentials.ForceReload();

                if (Application.isPlaying)
                {
                    foreach (var voice in UnityEngine.Object.FindObjectsByType<VoiceDictation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        voice.ReloadEditorSettings();
                    }
                }

                ShowNotification(new GUIContent("Settings saved"));
            }

            EditorGUILayout.HelpBox(
                "Select your physical microphone to avoid capturing the Oculus virtual input by accident.\n" +
                "Hold the chat button to record and release to finish.",
                MessageType.None);
        }
    }
}
