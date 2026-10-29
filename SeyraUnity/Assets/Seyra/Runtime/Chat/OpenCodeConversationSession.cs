using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Seyra.Networking;
using UnityEngine;

namespace Seyra.Chat
{
    public sealed class ChatMessageItem
    {
        public string Key { get; set; }
        public string Role { get; set; } // "Tú", "Seyra", "Herramienta", "Sistema"
        public string Text { get; set; }
        public bool IsTool { get; set; }
        public bool IsWorkStep { get; set; }
        public string ToolName { get; set; }
        public string ToolStatus { get; set; }
        public string ToolDetail { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Reusable OpenCode conversation engine managing HTTP sessions, turn execution, cancellation,
    /// permissions, and message synchronization with strict single-submission semantics.
    /// </summary>
    public sealed class OpenCodeConversationSession : IDisposable
    {
        public enum TurnState
        {
            Ready,
            Sending,
            Running,
            WaitingForPermission,
            Cancelling,
            Completed,
            Cancelled,
            Failed,
            Unresolved
        }

        string serverUrl = "http://127.0.0.1:4096";
        string agent = "seyra";
        JsonHttpClient http;
        Seyra.Configuration.SeyraConnectionSettings connectionSettings;
        CancellationTokenSource lifetime;
        string sessionId;

        bool sending, connecting, connected, turnUnresolved, syncing, submissionInFlight, interruptInFlight;
        bool terminalConfirmed, sessionMissing, cancelRequested, interruptAttempted, interruptAcknowledged, unknownToolOutcome;
        string activeUserId, pendingText, submissionBaselineId, pendingEchoKey;
        long activeUserCreated;
        DateTime lastProgressUtc;
        string progressSignature;
        TurnState executionState = TurnState.Ready;
        string executionDetail = "Escribe un mensaje para empezar.";

        readonly HashSet<string> submittedPermissions = new HashSet<string>();
        JObject pendingPermission;
        bool replyingPermission;

        readonly List<ChatMessageItem> messages = new List<ChatMessageItem>();
        readonly Dictionary<string, ChatMessageItem> messageByKey = new Dictionary<string, ChatMessageItem>();

        public event Action<bool, string> ConnectionChanged;
        public event Action<TurnState, string> StateChanged;
        public event Action<ChatMessageItem> MessageUpserted;
        public event Action<IReadOnlyList<ChatMessageItem>> MessagesReplaced;
        public event Action<JObject> PermissionChanged;
        public event Action<string> PermissionStatusChanged;
        public event Action ControlsRefreshRequested;
        public event Action<string, string> EchoAdopted;
        public event Action<string> ActivityChanged;

        public string CurrentActivity { get; private set; }

        void SetActivity(string value)
        {
            if (CurrentActivity == value) return;
            CurrentActivity = value;
            ActivityChanged?.Invoke(value);
        }

        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        public string Agent
        {
            get => agent;
            set => agent = value;
        }

        public string SessionId
        {
            get => sessionId;
            set => sessionId = value;
        }

        public bool IsConnected => connected;
        public bool IsConnecting => connecting;
        public bool IsSessionMissing => sessionMissing;
        public TurnState ExecutionState => executionState;
        public string ExecutionDetail => executionDetail;
        public string ActiveUserId => activeUserId;
        public string PendingText => pendingText;
        public bool HasTurn => activeUserId != null || pendingText != null;
        public bool CanSend => connected && !connecting && !submissionInFlight && !HasTurn;
        public bool CanReconnect => !connecting && !submissionInFlight && !interruptInFlight;
        public bool CanCancel => HasTurn && !terminalConfirmed && !sessionMissing && !connecting && connected && !interruptInFlight && (!cancelRequested || (interruptAttempted && !interruptAcknowledged));
        public bool CanCloseTurn => (sessionMissing || (HasTurn && terminalConfirmed)) && !connecting && !submissionInFlight && !interruptInFlight;
        public JObject PendingPermission => pendingPermission;
        public bool CanReplyPermission => pendingPermission != null && connected && !cancelRequested && !replyingPermission && !submittedPermissions.Contains((string)pendingPermission["id"]);
        public IReadOnlyList<ChatMessageItem> Messages => messages;
        public IReadOnlyCollection<string> SubmittedPermissions => submittedPermissions;

        string SessionPath => "/api/session/" + Uri.EscapeDataString(sessionId ?? "");

        public void Configure(string url, string agentProfile)
        {
            if (!string.IsNullOrWhiteSpace(url)) serverUrl = url.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(agentProfile)) agent = agentProfile;
        }

        public void SetSessionId(string id)
        {
            sessionId = id;
            if (string.IsNullOrEmpty(id))
            {
                sessionMissing = false;
            }
        }

        public void ResetTurnState()
        {
            ResetTurn();
        }

        public void Start()
        {
            if (lifetime != null) return;
            lifetime = new CancellationTokenSource();
            connectionSettings = Seyra.Configuration.SeyraConnectionSettings.Load(serverUrl);
            if (connectionSettings.CanConnect) http = new JsonHttpClient(connectionSettings.OpenCodeUrl, connectionSettings);
            SetConnected(false, "Conectando…");
            _ = ConnectAsync(lifetime.Token);
            _ = MonitorAsync(lifetime.Token);
        }

        public void Stop()
        {
            SetActivity(null);
            lifetime?.Cancel();
            http?.Dispose();
            lifetime?.Dispose();
            lifetime = null;
            http = null;

            if (HasTurn && !terminalConfirmed)
                SetState(TurnState.Unresolved, "Conexión interrumpida. Reconecta para consultar el mismo turno.");

            connected = connecting = syncing = submissionInFlight = interruptInFlight = false;
            ClearPermissions();
            ConnectionChanged?.Invoke(false, "Desconectado");
            ControlsRefreshRequested?.Invoke();
        }

        public void Dispose() => Stop();

        public void Reconnect()
        {
            if (!connecting && !submissionInFlight && !interruptInFlight && lifetime != null)
                _ = ConnectAsync(lifetime.Token);
        }

        void SetConnected(bool value, string label)
        {
            connected = value;
            ConnectionChanged?.Invoke(value, label);
            ControlsRefreshRequested?.Invoke();
        }

        void SetState(TurnState state, string detail)
        {
            executionState = state;
            executionDetail = detail;
            sending = state == TurnState.Sending || state == TurnState.Running || state == TurnState.WaitingForPermission || state == TurnState.Cancelling;
            turnUnresolved = state == TurnState.Cancelled || state == TurnState.Failed || state == TurnState.Unresolved;
            StateChanged?.Invoke(state, detail);
            ControlsRefreshRequested?.Invoke();
        }

        public async Task ConnectAsync(CancellationToken token)
        {
            connecting = true;
            SetConnected(false, "Conectando…");
            try
            {
                while (syncing) await Task.Delay(20, token);
                Seyra.Configuration.SeyraEnvironmentCredentials.ForceReload();
                connectionSettings = Seyra.Configuration.SeyraConnectionSettings.Load(serverUrl);
                http?.Dispose();
                http = connectionSettings.CanConnect ? new JsonHttpClient(connectionSettings.OpenCodeUrl, connectionSettings) : null;
                if (!connectionSettings.CanConnect) throw new Seyra.Configuration.ConnectionException(connectionSettings.ErrorCode);
                var password = connectionSettings.IsGateway ? null : Seyra.Configuration.SeyraEnvironmentCredentials.OpenCodeServerPassword;
                if (!connectionSettings.IsGateway && string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("HTTP password is missing from .env.local (" + Seyra.Configuration.SeyraEnvironmentCredentials.EnvFilePath + ").");
                var backend = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend")) : Application.persistentDataPath;

                http.SetPassword(password);
                if (string.IsNullOrEmpty(sessionId))
                {
                    var agents = await http.RequestAsync("GET", "/api/agent", null, token);
                    var profile = (agents["data"] as JArray)?.OfType<JObject>().FirstOrDefault(item => (string)item["id"] == agent);
                    if (profile == null) throw new InvalidOperationException("OpenCode did not return the selected agent profile: " + agent);
                    var request = new JObject
                    {
                        ["title"] = "Seyra · Spatial Chat",
                        ["agent"] = agent,
                        ["location"] = new JObject { ["directory"] = backend.Replace('\\', '/') }
                    };
                    if (connectionSettings.IsGateway) request.Remove("location");
                    if (profile["model"] is JObject model) request["model"] = model.DeepClone();
                    var result = await http.RequestAsync("POST", "/api/session", request, token);
                    sessionId = (string)result["data"]?["id"];
                    if (string.IsNullOrEmpty(sessionId)) throw new InvalidOperationException("OpenCode did not return an API v2 session.");
                    submittedPermissions.Clear();
                }
                await SyncAsync(token, true);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) ConnectionFailed(error);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    connecting = false;
                    ControlsRefreshRequested?.Invoke();
                }
            }
        }

        async Task MonitorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(HasTurn && connected ? 500 : 3000, token);
                    if (http == null || connecting || syncing || submissionInFlight || interruptInFlight || string.IsNullOrEmpty(sessionId)) continue;
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
                    catch (Exception error)
                    {
                        if (!token.IsCancellationRequested) ConnectionFailed(error);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        public async Task SendPromptAsync(string text)
        {
            text = text?.Trim() ?? "";
            if (!connected || connecting || HasTurn || submissionInFlight || text.Length == 0) return;
            if (lifetime == null) return;
            var token = lifetime.Token;
            submissionInFlight = true;
            pendingText = text;
            pendingEchoKey = "pending-" + Guid.NewGuid().ToString("N");
            cancelRequested = interruptAttempted = interruptAcknowledged = terminalConfirmed = unknownToolOutcome = false;
            activeUserCreated = 0;
            progressSignature = null;
            lastProgressUtc = DateTime.UtcNow;
            SetState(TurnState.Sending, "Enviando el mensaje una sola vez…");
            UpsertMessage(pendingEchoKey, "Tú", text);
            try
            {
                while (syncing) await Task.Delay(20, token);
                var before = await ReadMessagesAsync(token);
                submissionBaselineId = (string)before.LastOrDefault()?["id"];
                if (cancelRequested)
                {
                    terminalConfirmed = true;
                    SetState(TurnState.Cancelled, "Cancelado antes de enviar. No se ejecutaron acciones de este turno.");
                    return;
                }
                // Never retry admission: the server may execute tools even if its response is lost.
                var admission = await http.RequestAsync("POST", SessionPath + "/prompt", new JObject { ["text"] = text }, token);
                activeUserId = (string)admission["data"]?["id"];
                activeUserCreated = (long?)admission["data"]?["time"]?["created"] ?? 0;
                if (string.IsNullOrEmpty(activeUserId))
                    throw new InvalidOperationException("Submission was not confirmed. Reconnect to inspect the session; do not resend.");
                AdoptEcho(activeUserId);
                SetState(cancelRequested ? TurnState.Cancelling : TurnState.Running, cancelRequested ? "Esperando detener el turno…" : "El asistente está trabajando.");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) ConnectionFailed(error);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    submissionInFlight = false;
                    ControlsRefreshRequested?.Invoke();
                    if (cancelRequested && connected && !terminalConfirmed) await InterruptAsync(token);
                }
            }
        }

        async Task SyncAsync(CancellationToken token, bool hydrate = false)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                var messagesResponse = await ReadMessagesAsync(token, hydrate ? null : activeUserId);
                var session = (await http.RequestAsync("GET", SessionPath, null, token))["data"] as JObject
                    ?? throw new InvalidOperationException("Invalid API v2 session.");
                sessionMissing = false;
                SetConnected(true, "Conectado · " + agent);
                if (pendingText != null && activeUserId == null && !terminalConfirmed && !submissionInFlight)
                {
                    var baselineIndex = submissionBaselineId == null ? -1 : messagesResponse.FindIndex(m => (string)m["id"] == submissionBaselineId);
                    if (submissionBaselineId != null && baselineIndex < 0)
                        throw new InvalidOperationException("Submission baseline is no longer available; outcome unknown.");
                    var candidates = messagesResponse.Skip(baselineIndex + 1).Where(m => (string)m["type"] == "user").ToArray();
                    if (candidates.Length == 1 && (string)candidates[0]["text"] == pendingText)
                    {
                        activeUserId = (string)candidates[0]["id"];
                        activeUserCreated = (long?)candidates[0]["time"]?["created"] ?? 0;
                        AdoptEcho(activeUserId);
                    }
                    else
                    {
                        SetState(TurnState.Unresolved, "No se puede confirmar qué ocurrió con el envío. No se repetirá. Cancela para detener cualquier trabajo pendiente.");
                    }
                }
                RenderMessages(messagesResponse);
                if (activeUserId != null)
                {
                    var index = messagesResponse.FindIndex(m => (string)m["id"] == activeUserId);
                    if (index < 0) throw new InvalidOperationException("Active message is missing; no mutation will be retried.");
                    activeUserCreated = (long?)messagesResponse[index]["time"]?["created"] ?? activeUserCreated;
                    var turn = messagesResponse.Skip(index + 1).ToList();
                    var signature = string.Join("|", turn.Select(m => (string)m["id"] + ":" + (string)m["time"]?["completed"] + ":" +
                        string.Join(",", (m["content"] as JArray ?? new JArray()).OfType<JObject>()
                            .Where(p => (string)p["type"] != "reasoning").Select(p => (string)p["type"] + ":" + ((string)p["text"])?.Length + ":" + (string)p["state"]?["status"]))));
                    if (signature != progressSignature) { progressSignature = signature; lastProgressUtc = DateTime.UtcNow; }
                    var idle = turn.LastOrDefault(m => (string)m["type"] == "idle");
                    var sessionIdle = (long?)session["time"]?["idle"] ?? 0;
                    var outcome = (string)idle?["outcome"];
                    if (outcome == null && activeUserCreated > 0 && sessionIdle >= activeUserCreated) outcome = (string)session["outcome"];
                    if (outcome != null && !terminalConfirmed)
                    {
                        FinishTurn(outcome, turn);
                        // Session-level completion can arrive without an idle message.
                        RenderMessages(messagesResponse);
                    }
                    if (!terminalConfirmed)
                    {
                        await RefreshPermissionsAsync(token);
                        if (pendingPermission != null) lastProgressUtc = DateTime.UtcNow;
                        if (cancelRequested && interruptAttempted && !interruptInFlight && !interruptAcknowledged)
                            SetState(TurnState.Unresolved, "Cancelación sin confirmar. Puedes volver a pulsar Cancelar para solicitar la detención; no se repetirán las herramientas.");
                        else if (cancelRequested) SetState(TurnState.Cancelling, "Cancelación solicitada. Esperando confirmación del servidor; las acciones anteriores permanecen.");
                        else if (pendingPermission != null) SetState(TurnState.WaitingForPermission, "El turno espera tu decisión en el panel de permisos.");
                        else if ((DateTime.UtcNow - lastProgressUtc).TotalSeconds >= 120)
                            SetState(TurnState.Unresolved, "Sin progreso confirmado durante 120 s. Inspeccionando el mismo turno; no se reenviará nada.");
                        else SetState(TurnState.Running, "El asistente está trabajando. Puedes cancelar las acciones siguientes.");
                    }
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    syncing = false;
                    ControlsRefreshRequested?.Invoke();
                }
            }
            if (cancelRequested && !interruptAttempted && !terminalConfirmed) await InterruptAsync(token);
        }

        void FinishTurn(string outcome, List<JObject> turnMessages)
        {
            terminalConfirmed = true;
            SetActivity(null);
            ClearPermissions();
            if (outcome == "succeeded" && !unknownToolOutcome)
            {
                SetState(TurnState.Completed, cancelRequested ? "El turno ya había terminado antes de la cancelación." : "Turno completado. Resultados confirmados por el servidor.");
                ResetTurn();
            }
            else if (outcome == "interrupted" || outcome == "cancelled")
            {
                SetState(TurnState.Cancelled, "Turno interrumpido. Las acciones ya ejecutadas permanecen. Cierra el turno antes de continuar.");
            }
            else
            {
                var failed = turnMessages.FirstOrDefault(m => m["error"] != null);
                SetState(TurnState.Failed, unknownToolOutcome ? "Hay resultados de herramientas sin confirmar. Inspecciona la escena antes de repetir cambios y cierra el turno." : (string)failed?["error"]?["message"] ?? "Turn outcome: " + outcome);
                var detail = ((string)failed?["error"]?["message"])?.Trim();
                var friendlyError = "No he podido completar la petición.";
                if (!string.IsNullOrEmpty(detail) && detail.Length <= 200 && !detail.StartsWith("{", StringComparison.Ordinal) && !detail.StartsWith("[", StringComparison.Ordinal))
                    friendlyError += "\n" + detail;
                UpsertMessage("turn-error-" + activeUserId, "Sistema", unknownToolOutcome
                    ? "No he podido confirmar si se aplicó el último cambio. Revisa lo que ves antes de pedirlo otra vez."
                    : friendlyError);
            }
        }

        public async Task CancelTurnAsync()
        {
            if (!HasTurn || terminalConfirmed || interruptInFlight || (cancelRequested && (!interruptAttempted || interruptAcknowledged))) return;
            interruptAttempted = false;
            cancelRequested = true;
            SetState(TurnState.Cancelling, "Solicitando cancelación. No se deshacen las acciones ya ejecutadas.");
            if (submissionInFlight) return;
            if (connected && lifetime != null) await InterruptAsync(lifetime.Token);
        }

        async Task InterruptAsync(CancellationToken token)
        {
            if (interruptAttempted || interruptInFlight || terminalConfirmed || !HasTurn) return;
            interruptInFlight = interruptAttempted = true;
            ControlsRefreshRequested?.Invoke();
            try
            {
                var result = await http.RequestAsync("POST", SessionPath + "/interrupt", new JObject(), token);
                if (result["interrupted"]?.Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid interrupt acknowledgement; cancellation is unconfirmed.");
                interruptAcknowledged = true;
                if (activeUserId == null)
                {
                    while (syncing) await Task.Delay(20, token);
                    await SyncAsync(token);
                    if (activeUserId == null)
                    {
                        terminalConfirmed = true;
                        SetState(TurnState.Cancelled, "El servidor confirmó la detención. El resultado del envío sigue sin conocerse: inspecciona la escena antes de repetirlo.");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) ConnectionFailed(error);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    interruptInFlight = false;
                    ControlsRefreshRequested?.Invoke();
                }
            }
        }

        public void CloseTurn()
        {
            if ((!HasTurn && !sessionMissing) || (!terminalConfirmed && !sessionMissing) || submissionInFlight || interruptInFlight) return;
            UpsertMessage("closed-" + (activeUserId ?? pendingEchoKey), "Sistema", "Turno cerrado explícitamente. No se ha repetido ni revertido ninguna acción.");
            ResetTurn();
            ClearPermissions();
            SetState(TurnState.Ready, "Puedes enviar una nueva petición.");
            if (sessionMissing)
            {
                sessionId = null;
                sessionMissing = false;
                Reconnect();
            }
        }

        void ResetTurn()
        {
            SetActivity(null);
            activeUserId = pendingText = pendingEchoKey = submissionBaselineId = null;
            activeUserCreated = 0;
            cancelRequested = interruptAttempted = interruptAcknowledged = unknownToolOutcome = false;
            ControlsRefreshRequested?.Invoke();
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

        void ClearPermissions()
        {
            pendingPermission = null;
            replyingPermission = false;
            PermissionChanged?.Invoke(null);
            PermissionStatusChanged?.Invoke(null);
        }

        async Task RefreshPermissionsAsync(CancellationToken token)
        {
            var response = await http.RequestAsync("GET", "/api/session/" + Uri.EscapeDataString(sessionId) + "/permission", null, token);
            var requests = response["data"] as JArray ?? throw new InvalidOperationException("Invalid v2 permission list.");
            var values = requests.OfType<JObject>().ToArray();
            if (values.Length != requests.Count || values.Any(p => string.IsNullOrWhiteSpace((string)p["id"]) ||
                string.IsNullOrWhiteSpace((string)p["action"]) || (string)p["sessionID"] != sessionId ||
                !(p["resources"] is JArray resources) || resources.Any(r => r.Type != JTokenType.String)))
                throw new InvalidOperationException("Invalid permission request or request from another session.");

            var next = values.FirstOrDefault();
            var changed = !JToken.DeepEquals(next, pendingPermission);
            pendingPermission = next;
            if (next != null) SetActivity(null);
            PermissionChanged?.Invoke(next);

            if (next == null) return;
            if (changed)
            {
                PermissionStatusChanged?.Invoke("Permitir autoriza esta petición. Rechazar cancela también los permisos pendientes de esta sesión.");
            }
            ControlsRefreshRequested?.Invoke();
        }

        public async Task ReplyPermissionAsync(bool allow)
        {
            if (pendingPermission == null || replyingPermission || cancelRequested || !connected || lifetime == null) return;
            var id = (string)pendingPermission["id"];
            if (!submittedPermissions.Add(id)) return;
            var token = lifetime.Token;
            replyingPermission = true;
            ControlsRefreshRequested?.Invoke();
            PermissionStatusChanged?.Invoke("Enviando decisión…");
            try
            {
                await http.RequestAsync("POST", "/api/session/" + Uri.EscapeDataString(sessionId) + "/permission/" + Uri.EscapeDataString(id) + "/reply",
                    new JObject { ["decision"] = allow ? "once" : "reject" }, token);
                if (!token.IsCancellationRequested && (string)pendingPermission?["id"] == id)
                    PermissionStatusChanged?.Invoke("Decisión enviada. Esperando confirmación del servidor…");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    if ((string)pendingPermission?["id"] == id)
                        PermissionStatusChanged?.Invoke("No se confirmó la decisión; no se reenvía. Resuélvela en OpenCode si sigue pendiente.");
                    UpsertMessage("permission-error-" + id, "Sistema", error.Message);
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    replyingPermission = false;
                    ControlsRefreshRequested?.Invoke();
                }
            }
        }

        void AdoptEcho(string userId)
        {
            if (pendingEchoKey != null && messageByKey.TryGetValue(pendingEchoKey, out var row))
            {
                var oldKey = pendingEchoKey;
                messageByKey.Remove(pendingEchoKey);
                row.Key = "user-" + userId;
                messageByKey[row.Key] = row;
                EchoAdopted?.Invoke(oldKey, row.Key);
            }
        }

        void RenderMessages(IEnumerable<JObject> messageTokens)
        {
            string activity = null;
            var tokens = messageTokens.ToList();
            for (var start = 0; start < tokens.Count;)
            {
                var end = start + 1;
                while (end < tokens.Count && (string)tokens[end]["type"] != "user") end++;
                var group = tokens.GetRange(start, end - start);
                var userId = (string)group[0]["type"] == "user" ? (string)group[0]["id"] : null;
                var lastTextAssistant = group.LastOrDefault(m => (string)m["type"] == "assistant" &&
                    (m["content"] as JArray)?.OfType<JObject>().Any(p => (string)p["type"] == "text" &&
                        !string.IsNullOrWhiteSpace((string)p["text"])) == true);
                var ended = userId != activeUserId || terminalConfirmed || group.Any(m => (string)m["type"] == "idle");
                var showActivity = userId != null && userId == activeUserId && !ended && pendingPermission == null;
                if (showActivity) activity = ChatActivityText.Thinking;
                foreach (var message in group)
                {
                    var id = (string)message["id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    if ((string)message["type"] == "user")
                    {
                        UpsertMessage("user-" + id, "Tú", (string)message["text"] ?? "");
                    }
                    if ((string)message["type"] != "assistant") continue;
                    var parts = (message["content"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
                    var text = string.Join("\n", parts.Where(p => (string)p["type"] == "text").Select(p => (string)p["text"]));
                    var isWorkStep = message != lastTextAssistant || (!ended && parts.Any(p => (string)p["type"] == "tool"));
                    if (!string.IsNullOrWhiteSpace(text)) UpsertMessage("assistant-" + id, "Seyra", text, isWorkStep: isWorkStep);
                    for (var index = 0; index < parts.Length; index++)
                    {
                        var part = parts[index];
                        if (showActivity)
                        {
                            if ((string)part["type"] == "text" && isWorkStep && !string.IsNullOrWhiteSpace((string)part["text"]))
                                activity = ChatActivityText.Line((string)part["text"]);
                            else if ((string)part["type"] == "tool")
                            {
                                var description = ChatActivityText.Describe((string)part["name"]);
                                if (description != null)
                                {
                                    var status = (string)part["state"]?["status"];
                                    activity = status == "pending" || status == "running" ? description : ChatActivityText.Thinking;
                                }
                            }
                        }
                        if ((string)part["type"] != "tool") continue;
                        var key = "tool-" + id + "-" + ((string)part["id"] ?? index.ToString());
                        var (summary, isUnknown) = ToolSummary(part, userId == activeUserId);
                        if (isUnknown) unknownToolOutcome = true;
                        UpsertMessage(key, "Herramienta", summary, true, (string)part["name"]);
                    }
                }
                start = end;
            }
            SetActivity(activity);
        }

        (string Summary, bool UnknownOutcome) ToolSummary(JObject part, bool trackOutcome)
        {
            var name = (string)part["name"] ?? "tool";
            var state = part["state"] as JObject;
            var status = (string)state?["status"];
            string label = status == "completed" ? "completada" : status == "error" ? "falló" : status == "pending" ? "pendiente" : "en ejecución";
            var detail = "";
            bool unknown = false;
            if (name.StartsWith("seyra_", StringComparison.Ordinal) && status == "completed")
            {
                var payload = (state?["content"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["type"] == "text");
                try
                {
                    var result = JObject.Parse((string)payload?["text"] ?? "{}");
                    if (trackOutcome && (bool?)result["outcomeUnknown"] == true) unknown = true;
                    if ((bool?)result["success"] == false)
                    {
                        label = (bool?)result["outcomeUnknown"] == true ? "resultado desconocido" : "rechazada";
                        detail = (string)result["error"]?["code"];
                    }
                    else if ((bool?)result["success"] == true)
                    {
                        label = "confirmada";
                        detail = (string)result["data"]?["name"] ?? (string)result["data"]?["slot"];
                        if (result["data"]?["deleted"]?.Type == JTokenType.Boolean)
                            detail = (detail ?? "Objeto") + ((bool)result["data"]["deleted"] ? " · en papelera" : " · disponible");
                    }
                    else { label = "respuesta sin confirmar"; if (trackOutcome) unknown = true; }
                }
                catch (JsonException) { label = "respuesta sin confirmar"; if (trackOutcome) unknown = true; }
            }
            if (status == "error") detail = (string)state?["error"]?["message"];
            var text = ToolTitle(name) + " · " + label + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail.Substring(0, Math.Min(detail.Length, 200)));
            return (text, unknown);
        }

        public static string ToolTitle(string name)
        {
            switch (name)
            {
                case "seyra_scene_hierarchy": return "Consultar jerarquía";
                case "seyra_scene_find": return "Buscar objetos";
                case "seyra_scene_get": return "Consultar objeto";
                case "seyra_create_gameobject": return "Crear contenedor";
                case "seyra_create_primitive": return "Crear primitiva";
                case "seyra_rename_object": return "Renombrar objeto";
                case "seyra_set_transform": return "Mover, girar o escalar";
                case "seyra_set_parent": return "Cambiar de contenedor";
                case "seyra_request_begin": return "Abrir petición";
                case "seyra_request_end": return "Cerrar petición";
                case "seyra_request_list": return "Consultar peticiones";
                case "seyra_delete_object": return "Enviar a papelera";
                case "seyra_restore_object": return "Recuperar objeto";
                case "seyra_history_list": return "Consultar historial";
                case "seyra_undo": return "Deshacer";
                case "seyra_content_save": return "Guardar contenido";
                case "seyra_content_load": return "Cargar contenido";
                case "seyra_content_list": return "Consultar guardados";
                case "seyra_set_appearance": return "Cambiar apariencia";
                case "seyra_scene_bounds": return "Consultar dimensiones";
                case "seyra_place_relative": return "Colocar relativamente";
                case "seyra_create_panel": return "Crear panel";
                case "seyra_update_panel": return "Actualizar panel";
                case "seyra_ui_catalog": return "Consultar catálogo UI";
                case "seyra_ui_media_list": return "Consultar medios UI";
                case "seyra_ui_present": return "Presentar panel UI";
                case "seyra_ui_update": return "Actualizar presentación";
                case "seyra_ui_list": return "Listar presentaciones";
                case "seyra_ui_get": return "Consultar presentación";
                case "seyra_ui_close": return "Ocultar presentación";
                case "seyra_ui_open": return "Mostrar presentación";
                case "seyra_ui_remove": return "Eliminar presentación";
                case "read": return "Leer archivo";
                default: return name;
            }
        }

        void UpsertMessage(string key, string role, string text, bool isTool = false, string toolName = null, bool isWorkStep = false)
        {
            if (messageByKey.TryGetValue(key, out var existing))
            {
                if (existing.Text == text && existing.Role == role && existing.IsWorkStep == isWorkStep &&
                    existing.IsTool == isTool && existing.ToolName == toolName) return;
                existing.Role = role;
                existing.Text = text;
                existing.IsTool = isTool;
                existing.ToolName = toolName;
                existing.IsWorkStep = isWorkStep;
                MessageUpserted?.Invoke(existing);
                return;
            }

            var item = new ChatMessageItem
            {
                Key = key,
                Role = role,
                Text = text,
                IsTool = isTool,
                ToolName = toolName,
                IsWorkStep = isWorkStep
            };
            messageByKey[key] = item;
            messages.Add(item);
            MessageUpserted?.Invoke(item);
        }

        void ConnectionFailed(Exception error)
        {
            SetActivity(null);
            sessionMissing = error is OpenCodeHttpException failure && failure.StatusCode == 404;
            SetConnected(false, sessionMissing ? "Sesión no disponible" : Seyra.Configuration.ConnectionErrors.Describe(error, connectionSettings));
            if (sessionMissing || (HasTurn && !terminalConfirmed))
            {
                SetState(TurnState.Unresolved, sessionMissing
                    ? "La sesión ya no está disponible. Cierra este turno para iniciar otra conversación."
                    : "Resultado sin confirmar. Reconecta para inspeccionar la misma sesión; no se reenviará la petición.");
            }
            UpsertMessage("connection-error", "Sistema", connectionSettings?.IsGateway == true || error is Seyra.Configuration.ConnectionException
                ? Seyra.Configuration.ConnectionErrors.Describe(error, connectionSettings)
                : Seyra.Configuration.SeyraEnvironmentCredentials.RedactToken(error.Message));
        }
    }
}
