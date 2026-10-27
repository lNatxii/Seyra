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

        [MenuItem("Seyra/Voz/Configurar Wit.ai para Play Mode")]
        public static void Open() => GetWindow<WitDictationSettingsWindow>("Voz · Wit.ai");

        void OnEnable()
        {
            minSize = new Vector2(520, 340);
            microphone = SeyraEnvironmentCredentials.PreferredMicrophone;
            devices = Microphone.devices;
            SeyraEnvironmentCredentials.ForceReload();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Dictado en Play Mode (PC)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "En Play Mode se utiliza el micrófono del PC y Wit.ai para transcribir voz a texto.\n" +
                "En el visor Quest se utiliza el dictado nativo de plataforma de Meta.",
                MessageType.Info);

            EditorGUILayout.Space();

            bool hasToken = SeyraEnvironmentCredentials.HasWitToken;
            if (hasToken)
            {
                EditorGUILayout.HelpBox(
                    "Token cargado desde Backend/.env.local (SEYRA_CLIENT_TOKEN):\n" +
                    SeyraEnvironmentCredentials.RedactToken(SeyraEnvironmentCredentials.WitClientToken),
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No se encontró SEYRA_CLIENT_TOKEN en Backend/.env.local.\n" +
                    "Configura la variable SEYRA_CLIENT_TOKEN con tu Client Access Token de Wit.ai.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Abrir Wit.ai"))
            {
                Application.OpenURL("https://wit.ai/");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dispositivo de entrada de audio", EditorStyles.boldLabel);

            var choices = new string[devices.Length + 1];
            choices[0] = "Automático (primer micrófono disponible)";
            Array.Copy(devices, 0, choices, 1, devices.Length);

            int selected = string.IsNullOrEmpty(microphone) ? 0 : Array.IndexOf(devices, microphone) + 1;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("Micrófono del PC", selected, choices);
            if (EditorGUI.EndChangeCheck())
            {
                microphone = next > 0 ? devices[next - 1] : "";
            }

            if (!string.IsNullOrEmpty(microphone) && Array.IndexOf(devices, microphone) < 0)
            {
                EditorGUILayout.HelpBox("El micrófono guardado no está conectado: " + microphone, MessageType.Warning);
            }

            if (GUILayout.Button("Actualizar lista de dispositivos"))
            {
                devices = Microphone.devices;
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Guardar y aplicar"))
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

                ShowNotification(new GUIContent("Configuración guardada"));
            }

            EditorGUILayout.HelpBox(
                "Selecciona tu micrófono físico para evitar capturar por accidente la entrada virtual de Oculus.\n" +
                "Mantén pulsado el botón en el chat para grabar y suelta para enviar.",
                MessageType.None);
        }
    }
}
