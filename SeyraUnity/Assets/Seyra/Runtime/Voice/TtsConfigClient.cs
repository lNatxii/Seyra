using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seyra.Configuration;

namespace Seyra.Voice
{
    public sealed class TtsConfigClient : IDisposable
    {
        readonly SeyraConnectionSettings settings;
        readonly HttpClient http;

        public TtsConfigClient(SeyraConnectionSettings settings)
        {
            if (settings == null || !settings.IsGateway || !settings.CanConnect)
                throw new ArgumentException("TTS requires valid gateway settings.", nameof(settings));
            this.settings = settings;
            var pin = new PinnedCertificate(settings.CertificateSha256);
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) => pin.Matches(certificate);
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }

        public async Task<string> GetClientTokenAsync(CancellationToken cancellation)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, settings.OpenCodeUrl + "/_seyra/tts/config"))
            {
                request.Headers.ConnectionClose = true;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 503) return null;
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("TTS configuration request failed.");
                    try
                    {
                        var data = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        return data["clientToken"]?.Type == JTokenType.String ? (string)data["clientToken"] : null;
                    }
                    catch (Newtonsoft.Json.JsonException) { throw new InvalidOperationException("Invalid TTS configuration response."); }
                }
            }
        }

        public void Dispose() => http.Dispose();
    }
}
