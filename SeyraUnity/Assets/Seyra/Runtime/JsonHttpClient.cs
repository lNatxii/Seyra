using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Seyra.Networking
{
    public sealed class OpenCodeHttpException : Exception
    {
        public long StatusCode { get; }
        public OpenCodeHttpException(long code, string message) : base(message) { StatusCode = code; }
    }

    public sealed class JsonHttpClient : IDisposable
    {
        readonly string baseUrl;
        readonly HttpClient client;
        string password;
        bool disposed;

        public JsonHttpClient(string baseUrl)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            client.Timeout = TimeSpan.FromSeconds(15);
        }
        public void SetPassword(string value) { password = value; }

        public async Task<JObject> RequestAsync(string method, string path, JObject body, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(JsonHttpClient));
            token.ThrowIfCancellationRequested();
            using (var request = new HttpRequestMessage(new HttpMethod(method), baseUrl + path))
            {
                // Reused native connections can replay a POST after a lost response. Do not pool these requests.
                request.Headers.ConnectionClose = true;
                request.Headers.Accept.ParseAdd("application/json");
                if (!string.IsNullOrEmpty(password))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
                if (body != null) request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                    {
                        token.ThrowIfCancellationRequested();
                        if ((int)response.StatusCode == 401)
                            throw new InvalidOperationException("OpenCode rejected authentication. Check the connection credentials and reconnect.");
                        if (!response.IsSuccessStatusCode)
                            throw new OpenCodeHttpException((int)response.StatusCode, "OpenCode HTTP " + (int)response.StatusCode + ": " + response.ReasonPhrase);
                        var text = await response.Content.ReadAsStringAsync();
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(text)) return new JObject();
                        try { return JObject.Parse(text); }
                        catch (Exception error) when (error is Newtonsoft.Json.JsonException)
                        { throw new InvalidOperationException("Incompatible OpenCode response: expected API v2 JSON.", error); }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new OpenCodeHttpException(0, "OpenCode HTTP timed out. Outcome unknown; inspect before repeating any mutation."); }
                catch (HttpRequestException error)
                { throw new OpenCodeHttpException(0, "OpenCode HTTP connection failed. Outcome unknown; no request will be retried. " + error.Message); }
            }
        }

        public void Dispose()
        {
            disposed = true;
            password = null;
            client.Dispose();
        }
    }
}
