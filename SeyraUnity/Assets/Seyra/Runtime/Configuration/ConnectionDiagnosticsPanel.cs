using System;
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
        [SerializeField] Button detailsButton;
        [SerializeField] LayoutElement resultsLayout;
        string configurationError;

        public void Configure(RectTransform safeRoot, TMP_Text statusText, TMP_Text resultText, Button probe, Button reconnect, Button details, LayoutElement resultLayout, OpenCodeTestChat chatView, SeyraSceneRuntime sceneRuntime)
        {
            detailsButton = details; resultsLayout = resultLayout;
            safeAreaRoot = safeRoot; status = statusText; results = resultText;
            probeButton = probe; reconnectButton = reconnect; chat = chatView; runtime = sceneRuntime;
        }
        void OnEnable()
        {
            probeButton.onClick.AddListener(Probe);
            reconnectButton.onClick.AddListener(Reconnect);
            detailsButton.onClick.AddListener(ToggleDetails);
            resultsLayout.gameObject.SetActive(false);
            Reload();
        }
        void OnDisable()
        {
            probeButton.onClick.RemoveListener(Probe);
            reconnectButton.onClick.RemoveListener(Reconnect);
            detailsButton.onClick.RemoveListener(ToggleDetails);
        }
        void Reload()
        {
            gatewayUrl = null; fingerprint = null; configurationError = null;
            try
            {
                gatewayUrl = SeyraEnvironmentCredentials.GetValue("SEYRA_GATEWAY_URL");
                fingerprint = SeyraEnvironmentCredentials.GetValue("SEYRA_GATEWAY_CERT_SHA256");
                var settings = SeyraConnectionSettings.Load();
                if (!settings.CanConnect) configurationError = ConnectionErrors.Describe(null, settings);
                if (settings.CanConnect && !settings.IsGateway) configurationError = "Directo (solo Editor) · sin pasarela TLS configurada";
            }
            catch { configurationError = "Configuración de conexión inválida"; }
        }
        void Update()
        {
            float availableHeight = safeAreaRoot.rect.height;
            resultsLayout.preferredHeight = Mathf.Max(0, availableHeight * .45f);
            string endpoint = "Sin emparejar";
            if (configurationError == null && Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var uri))
                endpoint = $"Pasarela (HTTPS) · {uri.Host}:{uri.Port} · pin {fingerprint.Substring(0, 8)}";
            status.text = endpoint + "\nChat: " + (chat != null && chat.Session != null && chat.Session.IsConnected ? "conectado" : "desconectado")
                + " · Runtime: " + (runtime != null ? runtime.BridgeStatus : "desconectado");
        }
        void ToggleDetails() => resultsLayout.gameObject.SetActive(!resultsLayout.gameObject.activeSelf);
        async void Probe()
        {
            Reload();
            resultsLayout.gameObject.SetActive(true);
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
            SeyraEnvironmentCredentials.ForceReload(); Reload();
            chat?.Session?.Reconnect();
            runtime?.ReconnectNow(true);
            results.text = "Reconexión solicitada; se consulta la sesión existente sin reenviar mutaciones.";
        }
    }
}
