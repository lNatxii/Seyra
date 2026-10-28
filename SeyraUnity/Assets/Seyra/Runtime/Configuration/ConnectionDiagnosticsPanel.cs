using System;
using System.IO;
using System.Collections.Generic;
using Seyra.SceneTools;
using Seyra.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Seyra.Configuration
{
    public sealed class ConnectionDiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] RectTransform safeAreaRoot;
        [SerializeField] TMP_Text status;
        [SerializeField] TMP_Text results;
        [SerializeField] Button probeButton;
        [SerializeField] Button reconnectButton;
        [SerializeField] OpenCodeTestChat chat;
        [SerializeField] SeyraSceneRuntime runtime;
        string gatewayUrl;
        string fingerprint;
        Rect lastSafeArea;
        int lastWidth, lastHeight;
        string configurationError;

        public void Configure(RectTransform safeRoot, TMP_Text statusText, TMP_Text resultText, Button probe, Button reconnect, OpenCodeTestChat chatView, SeyraSceneRuntime sceneRuntime)
        {
            safeAreaRoot = safeRoot; status = statusText; results = resultText;
            probeButton = probe; reconnectButton = reconnect; chat = chatView; runtime = sceneRuntime;
        }
        void OnEnable()
        {
            probeButton.onClick.AddListener(Probe);
            reconnectButton.onClick.AddListener(Reconnect);
            Reload();
        }
        void OnDisable()
        {
            probeButton.onClick.RemoveListener(Probe);
            reconnectButton.onClick.RemoveListener(Reconnect);
        }
        void Reload()
        {
            gatewayUrl = null; fingerprint = null; configurationError = null;
#if UNITY_EDITOR
            // Editor verification supplies explicit fixture arguments; never read the real environment file.
            configurationError = "Editor: use PinningChecks with the test certificate. No environment file is read.";
#else
            try
            {
                var values = new Dictionary<string, string>();
                string path = Path.Combine(Application.persistentDataPath, ".env.local");
                if (File.Exists(path)) foreach (var raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#")) continue;
                    int split = line.IndexOf('=');
                    if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim().Trim('"', '\'');
                }
                values.TryGetValue("SEYRA_GATEWAY_URL", out gatewayUrl);
                values.TryGetValue("SEYRA_GATEWAY_CERT_SHA256", out fingerprint);
                if (!Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    throw new ArgumentException("Invalid HTTPS origin.");
                new PinnedCertificate(fingerprint);
            }
            catch (Exception error) { configurationError = "CONNECTION_CONFIG_INVALID: " + error.GetType().Name; }
#endif
        }
        void Update()
        {
            if (Screen.width > 0 && Screen.height > 0 && (lastSafeArea != Screen.safeArea || lastWidth != Screen.width || lastHeight != Screen.height))
            {
                lastSafeArea = Screen.safeArea; lastWidth = Screen.width; lastHeight = Screen.height;
                safeAreaRoot.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
                safeAreaRoot.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
                safeAreaRoot.offsetMin = safeAreaRoot.offsetMax = Vector2.zero;
            }
            string endpoint = "Sin emparejar";
            if (configurationError == null && Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var uri))
                endpoint = $"Pasarela (sondeo TLS) · {uri.Host}:{uri.Port} · pin {fingerprint.Substring(0, 8)}";
            status.text = "Diagnóstico de conexión\n" + endpoint + "\nChat: " + (chat != null && chat.Session != null && chat.Session.IsConnected ? "conectado" : "desconectado")
                + " · Runtime: " + (runtime != null ? runtime.BridgeStatus : "desconectado") + "\n" + (configurationError ?? "Solo TLS: la pasarela y el cliente seguro están pendientes.")
                + "\nConfig: " + Application.persistentDataPath + "/.env.local";
        }
        async void Probe()
        {
            Reload();
            if (configurationError != null) { results.text = configurationError; return; }
            probeButton.interactable = false;
            results.text = "Probando TLS…";
            try
            {
                string report = await ConnectionDiagnostics.RunTlsProbe(gatewayUrl, fingerprint);
                if (this != null) results.text = report.Replace("accepted=True", "aceptado").Replace("accepted=False", "rechazado");
            }
            catch (Exception error) { if (this != null) results.text = error.GetType().Name; }
            finally { if (this != null) probeButton.interactable = true; }
        }
        void Reconnect()
        {
            Reload();
            chat?.Session?.Reconnect();
            if (runtime != null && runtime.enabled) { runtime.enabled = false; runtime.enabled = true; }
            results.text = "Reconexión solicitada. En esta entrega solo está disponible el sondeo TLS; chat y bridge seguros pendientes de 1c/1d y pasos 2–6.";
        }
    }
}
