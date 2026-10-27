using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Networking;
using TMPro;
using UnityEngine;

namespace Seyra.UI
{
    public sealed partial class OpenCodeTestChat : MonoBehaviour
    {
        public enum TurnState { Ready, Sending, Running, WaitingForPermission, Cancelling, Completed, Cancelled, Failed, Unresolved }
        [SerializeField] string serverUrl = "http://127.0.0.1:4096";
        [SerializeField] string agent = "seyra";
        [SerializeField] TMP_InputField input;
        [SerializeField] UnityEngine.UI.Button sendButton;
        [SerializeField] UnityEngine.UI.Button reconnectButton;
        [SerializeField] UnityEngine.UI.Image connectionIndicator;
        [SerializeField] TMP_Text connectionLabel;
        [SerializeField] UnityEngine.UI.ScrollRect conversation;
        [SerializeField] TMP_Text messageTemplate;
        [SerializeField] TMP_Text executionLabel;
        [SerializeField] UnityEngine.UI.Button cancelButton;
        [SerializeField] UnityEngine.UI.Button closeTurnButton;
        CancellationTokenSource lifetime;
        JsonHttpClient http;
        string sessionId;
        bool sending, connecting, connected, turnUnresolved, syncing, submissionInFlight, interruptInFlight;
        bool terminalConfirmed, sessionMissing, cancelRequested, interruptAttempted, interruptAcknowledged, unknownToolOutcome;
        string activeUserId, pendingText, submissionBaselineId, pendingEchoKey;
        long activeUserCreated;
        DateTime lastProgressUtc;
        string progressSignature;
        TurnState executionState = TurnState.Ready;
        string executionDetail = "Escribe un mensaje para empezar.";
        public TurnState ExecutionState => executionState;
        bool HasTurn => activeUserId != null || pendingText != null;
        string SessionPath => "/api/session/" + Uri.EscapeDataString(sessionId);

        void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            http = new JsonHttpClient(serverUrl);
            sendButton.onClick.AddListener(Send);
            reconnectButton.onClick.AddListener(Reconnect);
            cancelButton?.onClick.AddListener(CancelTurn);
            closeTurnButton?.onClick.AddListener(CloseTurn);
            input.onSubmit.AddListener(Submit);
            messageTemplate.gameObject.SetActive(false);
            BindPermissions();
            SetConnected(false, "Conectando…");
            _ = ConnectAsync(lifetime.Token);
            _ = MonitorAsync(lifetime.Token);
        }

        void OnDisable()
        {
            sendButton.onClick.RemoveListener(Send);
            reconnectButton.onClick.RemoveListener(Reconnect);
            cancelButton?.onClick.RemoveListener(CancelTurn);
            closeTurnButton?.onClick.RemoveListener(CloseTurn);
            input.onSubmit.RemoveListener(Submit);
            UnbindPermissions();
            lifetime?.Cancel();
            http?.Dispose();
            lifetime?.Dispose();
            // Retain session, turn identity and rendered rows across disable/re-enable.
            if (HasTurn && !terminalConfirmed) SetState(TurnState.Unresolved, "Conexión interrumpida. Reconecta para consultar el mismo turno.");
            connected = connecting = syncing = submissionInFlight = interruptInFlight = false;
            ClearPermissions();
        }

        void SetConnected(bool value, string label)
        {
            connected = value;
            connectionIndicator.color = value ? new Color(.2f, .85f, .45f) : new Color(.95f, .25f, .3f);
            connectionLabel.text = label;
            RefreshControls();
        }

        void Reconnect() { if (!connecting && !submissionInFlight && !interruptInFlight) _ = ConnectAsync(lifetime.Token); }
        void Submit(string _) => Send();

        async Task ConnectAsync(CancellationToken token)
        {
            connecting = true;
            SetConnected(false, "Conectando…");
            try
            {
                while (syncing) await Task.Delay(20, token);
#if UNITY_EDITOR
                var backend = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend"));
                var password = Seyra.Configuration.OpenCodeEditorCredentials.ReadPassword(backend);
#else
                var backend = "";
                string password = null;
#endif
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("HTTP password is missing from Backend/.env.local (Editor testing only).");
                http.SetPassword(password);
                if (string.IsNullOrEmpty(sessionId))
                {
                    var agents = await http.RequestAsync("GET", "/api/agent", null, token);
                    var profile = (agents["data"] as JArray)?.OfType<JObject>().FirstOrDefault(item => (string)item["id"] == agent);
                    if (profile == null) throw new InvalidOperationException("OpenCode did not return the selected agent profile: " + agent);
                    var request = new JObject {
                        ["title"] = "Seyra · OpencodeTestUI", ["agent"] = agent,
                        ["location"] = new JObject { ["directory"] = backend.Replace('\\', '/') }
                    };
                    if (profile["model"] is JObject model) request["model"] = model.DeepClone();
                    var result = await http.RequestAsync("POST", "/api/session", request, token);
                    sessionId = (string)result["data"]?["id"];
                    if (string.IsNullOrEmpty(sessionId)) throw new InvalidOperationException("OpenCode did not return an API v2 session.");
                    submittedPermissions.Clear();
                }
                await SyncAsync(token, true);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!token.IsCancellationRequested) ConnectionFailed(error); }
            finally { if (!token.IsCancellationRequested) { connecting = false; RefreshControls(); } }
        }

        async Task MonitorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(HasTurn && connected ? 500 : 3000, token);
                    if (connecting || syncing || submissionInFlight || interruptInFlight || string.IsNullOrEmpty(sessionId)) continue;
                    try
                    {
                        if (HasTurn) await SyncAsync(token);
                        else
                        {
                            await http.RequestAsync("GET", SessionPath, null, token);
                            sessionMissing = false;
                            SetConnected(true, "Conectado · " + agent);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { if (!token.IsCancellationRequested) ConnectionFailed(error); }
                }
            }
            catch (OperationCanceledException) { }
        }

        async Task<List<JObject>> ReadMessagesAsync(CancellationToken token, string stopId = null)
        {
            var result = new List<JObject>();
            string cursor = null;
            for (var page = 0; page < 20; page++)
            {
                var query = cursor == null ? "?order=desc&limit=100" : "?limit=100&cursor=" + Uri.EscapeDataString(cursor);
                var response = await http.RequestAsync("GET", SessionPath + "/message" + query, null, token);
                var items = response["data"] as JArray ?? throw new InvalidOperationException("Invalid API v2 message list.");
                foreach (var item in items.OfType<JObject>())
                {
                    result.Add(item);
                    if (stopId != null && (string)item["id"] == stopId) { result.Reverse(); return result; }
                }
                var next = (string)response["cursor"]?["next"];
                if (items.Count == 0 || string.IsNullOrEmpty(next)) { result.Reverse(); return result; }
                if (next == cursor) throw new InvalidOperationException("Message cursor did not advance.");
                cursor = next;
            }
            throw new InvalidOperationException("Conversation exceeds the 2000-message reading limit; no submission was retried.");
        }

        void ConnectionFailed(Exception error)
        {
            sessionMissing = error is OpenCodeHttpException failure && failure.StatusCode == 404;
            SetConnected(false, sessionMissing ? "Sesión no disponible" : "Sin conexión · reconectar");
            if (sessionMissing || (HasTurn && !terminalConfirmed)) SetState(TurnState.Unresolved,
                sessionMissing ? "La sesión ya no está disponible. Cierra este turno para iniciar otra conversación." : "Resultado sin confirmar. Reconecta para inspeccionar la misma sesión; no se reenviará la petición.");
            UpsertMessage("connection-error", "Sistema", error.Message);
            RefreshControls();
        }
    }
}
