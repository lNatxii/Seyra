using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Configuration;
using Seyra.Networking;

namespace Seyra.Voice
{
    public sealed class AsrState
    {
        public int Version { get; internal set; }
        public string Partial { get; internal set; }
        public string Final { get; internal set; }
        public bool Done { get; internal set; }
        public string Error { get; internal set; }
    }

    public sealed class MuseTranscriptionClient : IDisposable
    {
        readonly SeyraConnectionSettings settings;
        readonly HttpClient http;
        volatile bool certificateMismatch;

        public MuseTranscriptionClient(SeyraConnectionSettings settings)
        {
            if (settings == null || !settings.IsGateway || !settings.CanConnect)
                throw new ArgumentException("ASR requires valid gateway settings.", nameof(settings));
            this.settings = settings;
            var pin = new PinnedCertificate(settings.CertificateSha256);
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
            { bool matches = pin.Matches(certificate); if (!matches) certificateMismatch = true; return matches; };
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }

        async Task<JObject> RequestAsync(string method, string path, byte[] pcm, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(new HttpMethod(method), settings.OpenCodeUrl + "/_seyra/asr/" + path))
            {
                request.Headers.ConnectionClose = true;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
                if (pcm != null)
                {
                    request.Content = new ByteArrayContent(pcm);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                }
                else if (method == "POST") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            // Only a known machine code may leave this transport; never echo response bodies.
                            string code = null;
                            try { code = (string)JObject.Parse(text)["error"]; } catch (Newtonsoft.Json.JsonException) { }
                            throw new OpenCodeHttpException((int)response.StatusCode, IsAsrCode(code) ? code : "ASR_HTTP_ERROR");
                        }
                        return JObject.Parse(text);
                    }
                }
                catch (HttpRequestException) when (certificateMismatch) { throw new ConnectionException("CERTIFICATE_MISMATCH"); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new OpenCodeHttpException(0, "ASR_TIMEOUT"); }
            }
        }

        internal static bool IsAsrCode(string code) => code == "ASR_NOT_CONFIGURED" || code == "ASR_UPSTREAM"
            || code == "ASR_RATE_LIMITED" || code == "ASR_TIMEOUT" || code == "ASR_IDLE" || code == "ASR_BACKLOG";

        public async Task<string> StartAsync(CancellationToken token)
        {
            var state = await RequestAsync("POST", "start", null, token).ConfigureAwait(false);
            string id = (string)state["asrId"];
            if (string.IsNullOrEmpty(id)) throw new IOException("ASR session is missing.");
            return id;
        }
        public async Task SendAudioAsync(string asrId, byte[] pcm, CancellationToken token)
        { await RequestAsync("POST", "audio?asrId=" + Uri.EscapeDataString(asrId), pcm, token).ConfigureAwait(false); }

        public async Task<AsrState> PollAsync(string asrId, int after, CancellationToken token)
        {
            var state = await RequestAsync("GET", "events?asrId=" + Uri.EscapeDataString(asrId) + "&after=" + after + "&wait=10", null, token).ConfigureAwait(false);
            return new AsrState { Version = (int)state["version"], Partial = (string)state["partial"], Final = (string)state["final"],
                Done = (bool)state["done"], Error = (string)state["error"] };
        }
        public async Task StopAsync(string asrId, CancellationToken token = default)
        { await RequestAsync("POST", "stop?asrId=" + Uri.EscapeDataString(asrId), null, token).ConfigureAwait(false); }
        public async Task CancelAsync(string asrId, CancellationToken token = default)
        { await RequestAsync("POST", "cancel?asrId=" + Uri.EscapeDataString(asrId), null, token).ConfigureAwait(false); }
        public void Dispose() => http.Dispose();
    }
}
