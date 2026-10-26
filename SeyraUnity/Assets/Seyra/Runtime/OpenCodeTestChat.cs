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
    public sealed class OpenCodeTestChat : MonoBehaviour
    {
        [SerializeField] string serverUrl = "http://127.0.0.1:4096";
        [SerializeField] string agent = "seyra";
        [SerializeField] TMP_InputField input;
        [SerializeField] UnityEngine.UI.Button sendButton;
        [SerializeField] UnityEngine.UI.Button reconnectButton;
        [SerializeField] UnityEngine.UI.Image connectionIndicator;
        [SerializeField] TMP_Text connectionLabel;
        [SerializeField] UnityEngine.UI.ScrollRect conversation;
        [SerializeField] TMP_Text messageTemplate;
        CancellationTokenSource lifetime;
        JsonHttpClient http;
        string sessionId;
        bool sending, connecting, connected, turnUnresolved;

        void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            http = new JsonHttpClient(serverUrl);
            sendButton.onClick.AddListener(Send);
            reconnectButton.onClick.AddListener(Reconnect);
            input.onSubmit.AddListener(Submit);
            messageTemplate.gameObject.SetActive(false);
            SetConnected(false, "Desconectado");
            _ = ConnectAsync(lifetime.Token);
            _ = MonitorAsync(lifetime.Token);
        }

        void OnDisable()
        {
            sendButton.onClick.RemoveListener(Send);
            reconnectButton.onClick.RemoveListener(Reconnect);
            input.onSubmit.RemoveListener(Submit);
            lifetime?.Cancel();
            http?.Dispose();
            lifetime?.Dispose();
            sessionId = null;
            sending = connecting = connected = false;
        }

        void SetConnected(bool value, string label)
        {
            connected = value;
            connectionIndicator.color = value ? new Color(.2f, .85f, .45f) : new Color(.95f, .25f, .3f);
            connectionLabel.text = label;
            sendButton.interactable = value && !sending && !turnUnresolved;
            reconnectButton.interactable = !sending && !connecting;
        }

        void Reconnect() { if (!sending && !connecting) _ = ConnectAsync(lifetime.Token); }
        void Submit(string _) { Send(); }

        async Task ConnectAsync(CancellationToken token)
        {
            connecting = true;
            turnUnresolved = false;
            sessionId = null;
            SetConnected(false, "Conectando…");
            try
            {
                string password = null;
#if UNITY_EDITOR
                var backend = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backend"));
                password = Seyra.Configuration.OpenCodeEditorCredentials.ReadPassword(backend);
#else
                var backend = "";
#endif
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Falta la contraseña HTTP en Backend/.env.local (prueba disponible en el Editor).");
                http.SetPassword(password);
                var result = await http.RequestAsync("POST", "/api/session", new JObject {
                    ["title"] = "Seyra · OpencodeTestUI", ["agent"] = agent,
                    ["location"] = new JObject { ["directory"] = backend.Replace('\\', '/') }
                }, token);
                sessionId = (string)result["data"]?["id"];
                if (string.IsNullOrEmpty(sessionId)) throw new InvalidOperationException("OpenCode no devolvió una sesión de la API v2.");
                SetConnected(true, "Conectado · " + agent);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!token.IsCancellationRequested) { SetConnected(false, "Sin conexión"); AddMessage("Sistema", error.Message); } }
            finally { connecting = false; if (!token.IsCancellationRequested) reconnectButton.interactable = !sending; }
        }

        async Task MonitorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(3000, token);
                    if (connecting || string.IsNullOrEmpty(sessionId)) continue;
                    try
                    {
                        await http.RequestAsync("GET", "/api/session/" + Uri.EscapeDataString(sessionId), null, token);
                        SetConnected(true, sending ? "Conectado · respondiendo…" : turnUnresolved ? "Conectado · reconectar" : "Conectado · " + agent);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { if (!token.IsCancellationRequested) SetConnected(false, "Sin conexión · reconectar"); }
                }
            }
            catch (OperationCanceledException) { }
        }

        async void Send()
        {
            var text = input.text.Trim();
            if (!connected || sending || connecting || turnUnresolved || text.Length == 0) return;
            sending = true;
            var token = lifetime.Token;
            input.text = "";
            AddMessage("Tú", text);
            SetConnected(true, "Conectado · respondiendo…");
            try
            {
                // Never retry a submission: it may already have reached the model.
                var admission = await http.RequestAsync("POST", "/api/session/" + Uri.EscapeDataString(sessionId) + "/prompt", new JObject { ["text"] = text }, token);
                var userId = (string)admission["data"]?["id"];
                if (string.IsNullOrEmpty(userId)) throw new InvalidOperationException("El envío no se confirmó; no se reenvía automáticamente.");
                var deadline = DateTime.UtcNow.AddMinutes(2);
                while (DateTime.UtcNow < deadline)
                {
                    var messages = await ReadTurnAsync(userId, token);
                    var failed = messages.FirstOrDefault(m => (string)m["type"] == "assistant" && m["error"] != null);
                    if (failed != null) throw new InvalidOperationException((string)failed["error"]?["message"] ?? "El proveedor devolvió un error.");
                    var idle = messages.LastOrDefault(m => (string)m["type"] == "idle");
                    if (idle != null)
                    {
                        if ((string)idle["outcome"] != "succeeded") throw new InvalidOperationException((string)idle["error"]?["message"] ?? "El turno terminó con error.");
                        var answer = string.Join("\n", messages.Where(m => (string)m["type"] == "assistant")
                            .SelectMany(m => (m["content"] as JArray ?? new JArray()).OfType<JObject>())
                            .Where(p => (string)p["type"] == "text").Select(p => (string)p["text"]));
                        AddMessage("Seyra", string.IsNullOrWhiteSpace(answer) ? "El turno terminó sin texto." : answer);
                        return;
                    }
                    var permissions = await http.RequestAsync("GET", "/api/session/" + Uri.EscapeDataString(sessionId) + "/permission", null, token);
                    if ((permissions["data"] as JArray)?.Count > 0)
                        throw new InvalidOperationException("OpenCode está esperando un permiso. Resuélvelo en OpenCode; esta UI básica solo admite conversación. El mensaje no se reenvía.");
                    await Task.Delay(400, token);
                }
                throw new TimeoutException("La respuesta no terminó en 120 s. El resultado es desconocido; no se reenvía automáticamente. Reconecta para iniciar otra sesión.");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    AddMessage("Sistema", error.Message);
                    turnUnresolved = true;
                    if (error is OpenCodeHttpException || error.Message.Contains("autenticación")) SetConnected(false, "Sin conexión · reconectar");
                }
            }
            finally
            {
                sending = false;
                if (!token.IsCancellationRequested)
                {
                    SetConnected(connected, connected ? turnUnresolved ? "Conectado · reconectar" : "Conectado · " + agent : "Sin conexión · reconectar");
                    input.ActivateInputField();
                }
            }
        }

        async Task<List<JObject>> ReadTurnAsync(string userId, CancellationToken token)
        {
            var result = new List<JObject>();
            string cursor = null;
            for (var page = 0; page < 20; page++)
            {
                var query = cursor == null ? "?order=desc&limit=100" : "?limit=100&cursor=" + Uri.EscapeDataString(cursor);
                var response = await http.RequestAsync("GET", "/api/session/" + Uri.EscapeDataString(sessionId) + "/message" + query, null, token);
                var items = response["data"] as JArray ?? throw new InvalidOperationException("Mensajes incompatibles con la API v2.");
                foreach (var item in items.OfType<JObject>())
                {
                    if ((string)item["id"] == userId) { result.Reverse(); return result; }
                    result.Add(item);
                }
                cursor = (string)response["cursor"]?["next"];
                if (string.IsNullOrEmpty(cursor)) return new List<JObject>();
            }
            throw new InvalidOperationException("El turno supera el límite de lectura de mensajes.");
        }

        void AddMessage(string role, string text)
        {
            var row = Instantiate(messageTemplate, conversation.content);
            row.name = "Message_" + role;
            row.richText = false;
            row.text = role + "\n" + text;
            row.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(conversation.content);
            conversation.verticalNormalizedPosition = 0;
        }
    }
}
