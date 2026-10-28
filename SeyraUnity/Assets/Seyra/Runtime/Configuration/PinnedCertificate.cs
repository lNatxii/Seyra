using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Seyra.Configuration
{
    public sealed class PinnedCertificate
    {
        readonly string pin;
        public PinnedCertificate(string fingerprint)
        {
            if (fingerprint == null || fingerprint.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(fingerprint, "\\A[0-9a-fA-F]{64}\\z"))
                throw new ArgumentException("Expected a SHA-256 certificate fingerprint.");
            pin = fingerprint.ToLowerInvariant();
        }
        public bool Matches(X509Certificate certificate)
        {
            if (certificate == null) return false;
            string actual = Hash(certificate);
            int difference = 0;
            for (int i = 0; i < pin.Length; i++) difference |= pin[i] ^ actual[i];
            return difference == 0;
        }
        public static string Hash(X509Certificate certificate)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(certificate.GetRawCertData())).Replace("-", "").ToLowerInvariant();
        }
        static string globalHost;
        static PinnedCertificate globalPin;
        static bool installed;
        static int callbacks;
        public static int GlobalCallbacks => Volatile.Read(ref callbacks);
        public static void InstallGlobal(string host, string fingerprint)
        {
            globalHost = host;
            globalPin = new PinnedCertificate(fingerprint);
            if (installed && ServicePointManager.ServerCertificateValidationCallback == ValidateGlobal) return;
            ServicePointManager.ServerCertificateValidationCallback = ValidateGlobal;
            installed = true;
        }
        internal static Action CaptureGlobalState()
        {
            string host = globalHost;
            var certificate = globalPin;
            bool wasInstalled = installed;
            var callback = ServicePointManager.ServerCertificateValidationCallback;
            return () =>
            {
                globalHost = host; globalPin = certificate; installed = wasInstalled;
                ServicePointManager.ServerCertificateValidationCallback = callback;
            };
        }
        static bool ValidateGlobal(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            Interlocked.Increment(ref callbacks);
            var host = (sender as HttpWebRequest)?.RequestUri.Host ?? sender as string;
            if (!string.Equals(host, globalHost, StringComparison.OrdinalIgnoreCase)) return errors == SslPolicyErrors.None;
            return globalPin.Matches(certificate);
        }
    }
}
