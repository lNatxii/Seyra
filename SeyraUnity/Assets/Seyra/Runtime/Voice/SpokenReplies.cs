using System;
using System.Threading;
using System.Threading.Tasks;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.TTS.Integrations;
using Meta.WitAi.TTS.Utilities;
using Seyra.Configuration;
using UnityEngine;

namespace Seyra.Voice
{
    public enum SpokenReplyState { Unavailable, Idle, Preparing, Speaking }

    public sealed class SpokenReplies : MonoBehaviour
    {
        [SerializeField] string voiceName = "wit$Rebecca";
        [SerializeField] string voiceStyle = "default";
        [SerializeField, Range(50, 200)] int speed = 100;
        const string MutedKey = "Seyra.Voice.Muted";
        const string MissingVoice = "Voice is not set up. Add WITAI_CLIENT_TOKEN on the PC.";
        const string FailedVoice = "Could not read the reply aloud. The text is still in the chat.";
        const string FailedContentVoice = "Could not read this aloud.";
        const string UnavailableVoice = "Voice is not available.";
        GameObject host;
        TTSSpeaker speaker;
        WitConfiguration configuration;
        CancellationTokenSource preparation;
        int generation;
        bool muted, muteLoaded;
        bool submitted;
        object owner;

        public static SpokenReplies Active { get; private set; }
        public object FailedOwner { get; private set; }
        public Func<bool> ContentPlaybackBlocked { get; set; }
        public bool IsReading(object candidate) => candidate != null && owner == candidate &&
            (State == SpokenReplyState.Preparing || State == SpokenReplyState.Speaking);

        public bool SimulateOutput { get; set; }
        public string LastSpokenText { get; private set; } = "";
        public SpokenReplyState State { get; private set; } = SpokenReplyState.Idle;
        public bool Muted
        {
            get
            {
                if (!muteLoaded) { muted = PlayerPrefs.GetInt(MutedKey, 0) != 0; muteLoaded = true; }
                return muted;
            }
        }
        public string Status { get; private set; } = "";
        public event Action Changed;

        void OnEnable() { Active = this; Changed?.Invoke(); }
        void OnDisable() { if (Active == this) Active = null; Stop(); }
        void OnApplicationPause(bool paused) { if (paused) Stop(); }
        void OnDestroy() { Stop(); ReleaseHost(); }

        public void SpeakReply(string reply)
        {
            StopPlayback(false);
            FailedOwner = null;
            SetStatus("");
            if (Muted || !isActiveAndEnabled) return;
            var text = SpeechText.Prepare(reply);
            if (text.Length > 0) StartPlayback(text);
        }

        public void SpeakContent(object contentOwner, string text)
        {
            if (string.IsNullOrEmpty(text) || !isActiveAndEnabled) return;
            if (ContentPlaybackBlocked != null && ContentPlaybackBlocked()) return;
            StopPlayback(false);
            FailedOwner = null;
            owner = contentOwner;
            StartPlayback(text);
        }

        public void Stop() => StopPlayback(true);

        // Replacement and failure publish only their final state, keeping owner subscriptions alive.
        void StopPlayback(bool notify)
        {
            generation++;
            owner = null;
            preparation?.Cancel(); preparation?.Dispose(); preparation = null;
            submitted = false;
            try { if (speaker != null) speaker.Stop(); } catch { }
            LastSpokenText = "";
            State = SpokenReplyState.Idle;
            if (notify) Changed?.Invoke();
        }

        public void SetMuted(bool muted)
        {
            this.muted = muted;
            muteLoaded = true;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            if (muted) Stop(); else Changed?.Invoke();
        }

        public void ToggleMuted() => SetMuted(!Muted);

        void StartPlayback(string text)
        {
            State = SpokenReplyState.Preparing;
            SetStatus("");
            if (SimulateOutput)
            {
                LastSpokenText = text;
                State = SpokenReplyState.Speaking;
                Changed?.Invoke();
                return;
            }
            preparation = new CancellationTokenSource();
            _ = PrepareAndSpeakAsync(generation, preparation.Token, text);
        }

        async Task PrepareAndSpeakAsync(int current, CancellationToken cancellation, string text)
        {
            try
            {
                if (speaker == null)
                {
                    var settings = SeyraConnectionSettings.Load();
                    string clientToken;
                    if (settings.IsGateway && settings.CanConnect)
                    {
                        using (var client = new TtsConfigClient(settings))
                            clientToken = await client.GetClientTokenAsync(cancellation);
                    }
                    else if (Application.isEditor) clientToken = SeyraEnvironmentCredentials.WitClientToken;
                    else { if (current == generation) { State = SpokenReplyState.Unavailable; FailedOwner = owner; SetStatus(UnavailableVoice); } return; }
                    if (current != generation || cancellation.IsCancellationRequested || !isActiveAndEnabled) return;
                    if (string.IsNullOrWhiteSpace(clientToken)) { State = SpokenReplyState.Unavailable; FailedOwner = owner; SetStatus(MissingVoice); return; }
                    CreateHost(clientToken);
                }
                if (current != generation || !isActiveAndEnabled) return;
                // Per-request callbacks retain their generation even after another reply starts.
                var events = new TTSSpeakerClipEvents();
                events.OnPlaybackStart.AddListener((_, clip) =>
                {
                    if (current != generation) return;
                    State = SpokenReplyState.Speaking; Changed?.Invoke();
                });
                events.OnLoadFailed.AddListener((_, clip, error) =>
                {
                    if (current != generation) return;
                    var diagnostic = error == "Audio source is missing" ? "Audio source is missing" : "SDK load error (details omitted)";
                    Debug.LogWarning("SpokenReplies: OnLoadFailed: " + diagnostic);
                    Fail();
                });
                events.OnPlaybackCancelled.AddListener((_, clip, reason) =>
                { if (current == generation && !speaker.IsActive) { State = SpokenReplyState.Idle; Changed?.Invoke(); } });
                LastSpokenText = text;
                submitted = true;
                speaker.Speak(text, events);
            }
            catch (OperationCanceledException) when (current != generation || cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (current != generation) return;
                Debug.LogWarning("SpokenReplies: speech preparation failed (" + error.GetType().Name + ").");
                Fail();
            }
        }

        void Update()
        {
            if (submitted && speaker != null && !speaker.IsActive)
            {
                submitted = false;
                State = SpokenReplyState.Idle;
                Changed?.Invoke();
            }
        }

        void CreateHost(string clientToken)
        {
            configuration = ScriptableObject.CreateInstance<WitConfiguration>();
            configuration.hideFlags = HideFlags.HideAndDontSave;
            configuration.useConduit = false;
            configuration.useIntentAttributes = false;
            configuration.SetClientAccessToken(clientToken);
            host = new GameObject("SpokenRepliesHost");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var service = host.AddComponent<TTSWit>();
            service.Configuration = configuration;
            var request = service.RequestSettings;
            request.useEvents = false;
            request.audioMaxDuration = 30f;
            service.RequestSettings = request;
            host.AddComponent<TTSSpeechSplitter>();
            var audioSource = host.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            speaker = host.AddComponent<TTSSpeaker>();
            speaker.SetVoiceOverride(new TTSWitVoiceSettings { voice = voiceName, style = voiceStyle, speed = speed });
            host.SetActive(true);
        }

        void Fail()
        {
            var failed = owner;
            StopPlayback(false);
            ReleaseHost();
            FailedOwner = failed;
            SetStatus(failed != null ? FailedContentVoice : FailedVoice);
        }

        public void SimulateFailure() { if (SimulateOutput) Fail(); }

        void ReleaseHost()
        {
            if (configuration != null) configuration.SetClientAccessToken("");
            if (host != null) { host.SetActive(false); Destroy(host); }
            speaker = null; host = null;
            if (configuration != null) Destroy(configuration);
            configuration = null;
        }

        void SetStatus(string message)
        {
            var clientToken = configuration != null ? configuration.GetClientAccessToken() : null;
            if (!string.IsNullOrEmpty(clientToken)) message = message.Replace(clientToken, "[redacted]");
            Status = SeyraEnvironmentCredentials.RedactToken(message);
            Changed?.Invoke();
        }
    }
}
