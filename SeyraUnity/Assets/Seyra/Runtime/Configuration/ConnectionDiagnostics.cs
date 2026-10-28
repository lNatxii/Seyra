using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Seyra.Configuration
{
    // Diagnostic requests never carry credentials or exercise the production bridge.
    public static class ConnectionDiagnostics
    {
        static readonly SemaphoreSlim probeLock = new SemaphoreSlim(1, 1);
        public static async Task<string> RunTlsProbe(string gatewayUrl, string fingerprint)
        {
            var endpoint = new Uri(gatewayUrl);
            if (endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.Query))
                throw new ArgumentException("Expected an HTTPS origin without credentials, path or query.");
            new PinnedCertificate(fingerprint);
            await probeLock.WaitAsync();
            var restoreGlobal = PinnedCertificate.CaptureGlobalState();
            var lines = new List<string>();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool valid = pass == 0;
                    var pin = valid ? fingerprint : (fingerprint[0] == '0' ? "1" : "0") + fingerprint.Substring(1);
                    string label = valid ? "matching" : "wrong";
                    foreach (var path in new[] { "/", "/_seyra/bridge/hello", "/_seyra/bridge/next?wait=25&sessionId=pinning-fixture", "/_seyra/bridge/result" })
                    {
                        int count = 0;
                        bool accepted = false;
                        string failure = "none";
                        using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                        using (var timeout = new CancellationTokenSource(10000))
                        {
                            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) => { Interlocked.Increment(ref count); return new PinnedCertificate(pin).Matches(certificate); };
                            using (var client = new HttpClient(handler))
                            using (var request = new HttpRequestMessage(path.Contains("/hello") || path.Contains("/result") ? HttpMethod.Post : HttpMethod.Get, new Uri(endpoint, path)))
                            {
                                if (request.Method == HttpMethod.Post) request.Content = new StringContent("{}");
                                try { using (var response = await client.SendAsync(request, timeout.Token)) accepted = true; /* Any HTTP response confirms TLS; auth is separate. */ }
                                catch (Exception error) { failure = Describe(error); }
                            }
                        }
                        lines.Add($"HTTPS {path.Split('?')[0]} {label}: accepted={accepted}; callbacks={count}; exception={failure}");
                    }
                    foreach (bool global in new[] { false, true })
                    {
                        int count = 0;
                        bool accepted = false;
                        string failure = "none";
                        ServicePointManager.ServerCertificateValidationCallback = null;
                        if (global) PinnedCertificate.InstallGlobal(endpoint.Host, pin);
                        int before = PinnedCertificate.GlobalCallbacks;
                        using (var socket = new ClientWebSocket())
                        using (var timeout = new CancellationTokenSource(10000))
                        {
                            socket.Options.Proxy = null;
                            if (!global) socket.Options.RemoteCertificateValidationCallback = (_, certificate, chain, errors) => { Interlocked.Increment(ref count); return new PinnedCertificate(pin).Matches(certificate); };
                            try
                            {
                                var uri = new UriBuilder(endpoint) { Scheme = "wss", Path = "/_seyra/bridge" }.Uri;
                                await socket.ConnectAsync(uri, timeout.Token);
                                accepted = socket.State == WebSocketState.Open;
                            }
                            catch (Exception error) { failure = Describe(error); }
                            finally { socket.Abort(); }
                        }
                        if (global) count = PinnedCertificate.GlobalCallbacks - before;
                        lines.Add($"WSS {(global ? "global" : "per-socket")} {label}: accepted={accepted}; callbacks={count}; exception={failure}");
                    }
                }
                return string.Join("\n", lines);
            }
            finally
            {
                restoreGlobal();
                probeLock.Release();
            }
        }
        static string Describe(Exception error)
        {
            // Exception types are sufficient for diagnostics and cannot leak request credentials.
            return error.GetType().Name + (error.InnerException == null ? "" : " / " + error.InnerException.GetType().Name);
        }
    }
}
