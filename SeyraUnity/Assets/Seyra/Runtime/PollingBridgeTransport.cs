using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Configuration;

namespace Seyra.Networking
{
    public sealed class PollingBridgeTransport : IBridgeTransport
    {
        readonly SeyraConnectionSettings settings;
        readonly HttpClient http;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();
        CancellationTokenSource operation;
        volatile string sessionId;
        string dispatchSession;
        volatile bool certificateMismatch;
        public bool IsConnected => sessionId != null;
        public event Action<string> Message;
        public event Action<string> Status;
        public PollingBridgeTransport(SeyraConnectionSettings settings)
        {
            this.settings = settings;
            var pin = new PinnedCertificate(settings.CertificateSha256);
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
            { bool matches = pin.Matches(certificate); if (!matches) certificateMismatch = true; return matches; };
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(32) };
        }
        public void Start(string url) { _ = RunAsync(); }
        public void Pump() { for (int i = 0; i < 64 && inbox.TryDequeue(out var action); i++) action(); }
        async Task<string> Request(string method, string path, string body, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(new HttpMethod(method), settings.OpenCodeUrl + path))
            {
                request.Headers.ConnectionClose = true;
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.DeviceToken);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) throw new OpenCodeHttpException((int)response.StatusCode, "Gateway rejected polling request.");
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (HttpRequestException) when (certificateMismatch) { throw new ConnectionException("CERTIFICATE_MISMATCH"); }
            }
        }
        async Task RunAsync()
        {
            while (!lifetime.IsCancellationRequested)
            {
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    operation = attempt;
                    try
                    {
                        string id = (string)JObject.Parse(await Request("POST", "/_seyra/bridge/hello", "{}", attempt.Token).ConfigureAwait(false))["sessionId"];
                        if (string.IsNullOrEmpty(id)) throw new IOException("Gateway session is missing.");
                        sessionId = id; inbox.Enqueue(() => Status?.Invoke("Runtime conectado (HTTPS)"));
                        while (!attempt.IsCancellationRequested && sessionId == id)
                        {
                            string text = await Request("GET", "/_seyra/bridge/next?wait=25&sessionId=" + Uri.EscapeDataString(id), null, attempt.Token).ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(text)) continue;
                            if (inbox.Count >= 64) throw new IOException("Too many pending commands.");
                            var message = JObject.Parse(text);
                            if ((string)message["type"] != "tool_request") throw new IOException("Invalid bridge envelope.");
                            inbox.Enqueue(() =>
                            {
                                if (sessionId != id || lifetime.IsCancellationRequested) return;
                                dispatchSession = id;
                                try { Message?.Invoke(text); } finally { dispatchSession = null; }
                            });
                        }
                    }
                    catch (Exception error)
                    {
                        if (!lifetime.IsCancellationRequested) inbox.Enqueue(() => Status?.Invoke(ConnectionErrors.Describe(error, settings) + " · reconectando"));
                    }
                    finally { sessionId = null; operation = null; }
                }
                if (!lifetime.IsCancellationRequested) try { await wake.WaitAsync(2000, lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
        public async Task SendAsync(string text)
        {
            string id = dispatchSession ?? sessionId;
            if (id == null || id != sessionId) throw new IOException("Runtime disconnected before confirmation.");
            try { await Request("POST", "/_seyra/bridge/result?sessionId=" + Uri.EscapeDataString(id), text, lifetime.Token); }
            catch { sessionId = null; try { operation?.Cancel(); } catch (ObjectDisposedException) { } throw; }
        }
        public void ReconnectNow()
        {
            if (IsConnected) return;
            try { operation?.Cancel(); } catch (ObjectDisposedException) { }
            try { wake.Release(); } catch (SemaphoreFullException) { }
        }
        public void Dispose() { lifetime.Cancel(); sessionId = null; http.Dispose(); }
    }
}
