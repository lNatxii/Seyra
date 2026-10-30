using System;
using System.Collections.Generic;
using System.IO;

namespace Seyra.Configuration
{
    public sealed class SeyraConnectionSettings
    {
        public bool IsGateway { get; private set; }
        public bool CanConnect => ErrorCode == null;
        public string ErrorCode { get; private set; }
        public string OpenCodeUrl { get; private set; }
        public string BridgeUrl { get; private set; }
        public string DeviceToken { get; private set; }
        public string CertificateSha256 { get; private set; }
        public string Endpoint => Uri.TryCreate(OpenCodeUrl, UriKind.Absolute, out var uri) ? uri.Host + ":" + uri.Port : "backend";
        public static SeyraConnectionSettings Load(string directUrl = "http://127.0.0.1:4096")
        {
            return Resolve(SeyraEnvironmentCredentials.GetValue, UnityEngine.Application.isEditor, directUrl);
        }
        public static SeyraConnectionSettings FromFile(string path, bool isEditor, string directUrl = "http://127.0.0.1:4096")
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path)) foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim(); if (line.StartsWith("#")) continue;
                int split = line.IndexOf('=');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim().Trim('"', '\'');
            }
            return Resolve(key => values.TryGetValue(key, out var value) ? value : null, isEditor, directUrl);
        }
        static SeyraConnectionSettings Resolve(Func<string,string> read, bool isEditor, string directUrl)
        {
            var result = new SeyraConnectionSettings();
            string url = read("SEYRA_GATEWAY_URL"), token = read("SEYRA_DEVICE_TOKEN"), pin = read("SEYRA_GATEWAY_CERT_SHA256");
            bool configured = !string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(token) || !string.IsNullOrWhiteSpace(pin);
            if (!configured)
            {
                if (!isEditor) { result.ErrorCode = "CONNECTION_UNPAIRED"; return result; }
                if (!Uri.TryCreate(directUrl, UriKind.Absolute, out var direct) || direct.Scheme != "http" || !direct.IsLoopback || !string.IsNullOrEmpty(direct.UserInfo))
                { result.ErrorCode = "CONNECTION_CONFIG_INVALID"; return result; }
                result.OpenCodeUrl = directUrl.TrimEnd('/'); result.BridgeUrl = "ws://127.0.0.1:4100"; return result;
            }
            result.IsGateway = true;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(token)
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            { result.ErrorCode = "CONNECTION_CONFIG_INVALID"; return result; }
            try { new PinnedCertificate(pin); }
            catch (ArgumentException) { result.ErrorCode = "CONNECTION_CONFIG_INVALID"; return result; }
            result.OpenCodeUrl = uri.GetLeftPart(UriPartial.Authority); result.BridgeUrl = result.OpenCodeUrl;
            result.DeviceToken = token; result.CertificateSha256 = pin;
            return result;
        }
    }
    public sealed class ConnectionException : Exception
    {
        public string Code { get; }
        public ConnectionException(string code) : base(code) { Code = code; }
    }
    public static class ConnectionErrors
    {
        public static string Describe(Exception error, SeyraConnectionSettings settings)
        {
            string code = (error as ConnectionException)?.Code ?? settings?.ErrorCode;
            if (code == "CONNECTION_UNPAIRED") return "Not paired · copy the .env.local from seyra-device.ps1 pair";
            if (code == "CONNECTION_CONFIG_INVALID") return "Invalid connection configuration";
            if (code == "CERTIFICATE_MISMATCH") return "Backend certificate not recognized · pair again";
            var http = error as Seyra.Networking.OpenCodeHttpException;
            if (settings?.IsGateway == true && http?.StatusCode == 401) return "Device unauthorized or revoked";
            if (settings?.IsGateway == true && http?.StatusCode == 429) return "Too many attempts · wait a few minutes";
            return "Cannot reach the backend at " + (settings?.Endpoint ?? "backend");
        }
    }
}
