using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Dictation;
using Meta.WitAi.Requests;
using Oculus.Voice.Dictation;
using Oculus.Voice.Dictation.Configuration;
using Seyra.Configuration;
using Seyra.Networking;
using UnityEngine;

namespace Seyra.Voice
{
    /// <summary>
    /// Gateway dictation uses Muse; the Editor can use Wit.ai in direct mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceDictation : MonoBehaviour
    {
        public const string Idle = "Hold to talk";
        public const string ListeningText = "Listening…";
        public const string Processing = "Transcribing…";

        [SerializeField, Range(5, 120)] float maximumSeconds = 45f;

        AppDictationExperience service;
        bool listening, accepting, finishing, finalReceived;
        string status = "", finalText = "";
        Coroutine pending, limit;
        Coroutine preparing;
        int generation;
        WitConfiguration runtimeConfiguration;
        SeyraConnectionSettings settings;
        bool useMuse, useWit;
        MicrophonePcmCapture capture;
        MuseOperation muse;
        readonly ConcurrentQueue<Action> inbox = new ConcurrentQueue<Action>();
#if UNITY_ANDROID && !UNITY_EDITOR
        UnityEngine.Android.PermissionCallbacks microphonePermission;
#endif

        sealed class MuseOperation
        {
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public readonly List<byte> Audio = new List<byte>();
            public readonly object Gate = new object();
            public MuseTranscriptionClient Client;
            public volatile bool Stopping, Terminal;
            public string Id;
        }

        public event Action Changed;
        public event Action Ended;
        public event Action<string> Partial, Final, Failed;

        public bool Available { get; private set; }
        public bool Listening => listening;
        public bool Busy => listening || finishing;
        public string Status => status;
        public string Transcription { get; private set; } = "";
        public string Provider { get; private set; } = "";

        void Awake()
        {
            SelectProvider();
            Evaluate();
        }

        void SelectProvider()
        {
            settings = SeyraConnectionSettings.Load();
            useMuse = settings.IsGateway && settings.CanConnect;
            useWit = !useMuse && Application.isEditor;
            Provider = useMuse ? "Muse Voice Transcribe · gateway" : useWit ? "Wit.ai · PC microphone" : "No voice provider";
        }

        void OnDisable() => Cancel();

        void OnDestroy()
        {
            ReleaseService();
            if (runtimeConfiguration != null)
            {
                Destroy(runtimeConfiguration);
                runtimeConfiguration = null;
            }
        }

        public void Evaluate()
        {
            if (Busy) return;
            if (useMuse) { SetStatus(Idle, true); return; }
            if (!useWit) { SetStatus(ConnectionErrors.Describe(null, settings), false); return; }
            var configured = SeyraEnvironmentCredentials.HasWitToken;
            SetStatus(configured ? Idle : "Set SEYRA_CLIENT_TOKEN in Backend/.env.local to use dictation.", configured);
        }

        void CreateService(WitConfiguration configuration)
        {
            ReleaseService();
            var host = new GameObject("DictationHost");
            host.transform.SetParent(transform, false);
            host.SetActive(false);

            service = host.AddComponent<AppDictationExperience>();
            service.RuntimeDictationConfiguration = new WitDictationRuntimeConfiguration
            {
                witConfiguration = configuration,
                maxRecordingTime = maximumSeconds,
                minKeepAliveTimeInSeconds = maximumSeconds + 1f,
                minTranscriptionKeepAliveTimeInSeconds = maximumSeconds + 1f,
                transcribeOnly = true,
                preferredActivationOffset = 0,
                dictationConfiguration = new DictationConfiguration
                {
                    multiPhrase = false,
                    scenario = "default",
                    inputType = "text_default"
                }
            };
            service.DoNotFallbackToWit = false;
            service.UsePlatformIntegrations = false;

            int current = generation;
            service.DictationEvents.OnStartListening.AddListener(() => { if (current == generation) Started(); });
            service.DictationEvents.OnStoppedListening.AddListener(() => { if (current == generation) Stopped(); });
            service.DictationEvents.OnPartialTranscription.AddListener(text => { if (current == generation) Partially(text); });
            service.DictationEvents.OnFullTranscription.AddListener(text => { if (current == generation) Completed(text); });
            service.DictationEvents.OnError.AddListener((type, message) => { if (current == generation) Errored(type, message); });

            host.SetActive(true);

        }

        void ReleaseService()
        {
            ++generation;
            if (service == null) return;
            var host = service.gameObject;
            service = null;
            host.SetActive(false);
            Destroy(host);
        }

        public void Begin()
        {
            if (!isActiveAndEnabled || Busy) return;
            Evaluate();
            if (!Available) return;

            StopTimers();
            accepting = true;
            listening = true;
            finalReceived = false;
            finalText = "";
            Transcription = "";
            ++generation;
            SetStatus(useMuse ? "Connecting…" : "Connecting to Wit.ai… Keep holding", true);
            Changed?.Invoke();

            try
            {
                if (useMuse) { BeginMuse(); return; }
                ReleaseService();
                if (runtimeConfiguration != null) Destroy(runtimeConfiguration);
                runtimeConfiguration = CreateRuntimeConfiguration(maximumSeconds);
                preparing = StartCoroutine(PrepareEditorCapture());
            }
            catch (Exception error)
            {
                Errored("Activate", error.Message);
            }
        }

        void BeginMuse()
        {
            int current = generation;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                microphonePermission = new UnityEngine.Android.PermissionCallbacks();
                microphonePermission.PermissionGranted += _ =>
                {
                    if (current == generation && accepting && listening) StartMuseCapture(current);
                };
                microphonePermission.PermissionDenied += _ =>
                {
                    if (current == generation) Errored("Microphone", "Microphone permission denied. Enable it in Settings → Privacy.");
                };
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, microphonePermission);
                return;
            }
#endif
            StartMuseCapture(current);
        }

        void StartMuseCapture(int current)
        {
            try
            {
                capture = new MicrophonePcmCapture();
                capture.Start();
                muse = new MuseOperation { Client = new MuseTranscriptionClient(settings) };
                limit = StartCoroutine(StopAtLimit());
                _ = RunMuseAsync(muse, current);
            }
            catch (Exception error) { Errored("Microphone", DescribeMuseError(error)); }
        }

        void Update()
        {
            for (int i = 0; i < 64 && inbox.TryDequeue(out var action); i++) action();
            if (capture == null || !accepting) return;
            try { EnqueueAudio(capture.Poll()); }
            catch (Exception error) { Errored("Microphone", DescribeMuseError(error)); }
        }

        void EnqueueAudio(byte[] pcm)
        {
            if (pcm.Length == 0 || muse == null) return;
            lock (muse.Gate)
            {
                if (muse.Audio.Count + pcm.Length > 96000) throw new ConnectionException("ASR_BACKLOG");
                muse.Audio.AddRange(pcm);
            }
        }

        void DispatchMuse(int current, Action action)
        {
            inbox.Enqueue(() => { if (current == generation && accepting) action(); });
        }

        async Task RunMuseAsync(MuseOperation operation, int current)
        {
            Task sending = null, polling = null;
            bool completed = false;
            try
            {
                operation.Id = await operation.Client.StartAsync(operation.Lifetime.Token).ConfigureAwait(false);
                operation.Lifetime.Token.ThrowIfCancellationRequested();
                DispatchMuse(current, Started);
                sending = SendMuseAsync(operation);
                polling = PollMuseAsync(operation, current);
                // Observe the first failure immediately, then cancel the other loop in finally.
                await await Task.WhenAny(sending, polling).ConfigureAwait(false);
                await Task.WhenAll(sending, polling).ConfigureAwait(false);
                completed = true;
            }
            catch (Exception error)
            {
                if (!operation.Lifetime.IsCancellationRequested)
                    DispatchMuse(current, () => Errored("Muse", DescribeMuseError(error)));
            }
            finally
            {
                operation.Terminal = true;
                operation.Lifetime.Cancel();
                if (sending != null && polling != null)
                    try { await Task.WhenAll(sending, polling).ConfigureAwait(false); } catch (Exception) { }
                if (!completed && operation.Id != null)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        try { await operation.Client.CancelAsync(operation.Id, timeout.Token).ConfigureAwait(false); } catch (Exception) { }
                }
                operation.Client.Dispose();
                operation.Lifetime.Dispose();
                lock (operation.Gate) operation.Audio.Clear();
            }
        }

        async Task SendMuseAsync(MuseOperation operation)
        {
            var token = operation.Lifetime.Token;
            while (!operation.Terminal)
            {
                token.ThrowIfCancellationRequested();
                byte[] audio;
                bool stopping;
                lock (operation.Gate)
                {
                    int count = Math.Min(65536, operation.Audio.Count);
                    audio = operation.Audio.GetRange(0, count).ToArray();
                    operation.Audio.RemoveRange(0, count);
                    stopping = operation.Stopping;
                }
                if (audio.Length > 0)
                    await operation.Client.SendAudioAsync(operation.Id, audio, token).ConfigureAwait(false);
                else if (stopping)
                {
                    await operation.Client.StopAsync(operation.Id, token).ConfigureAwait(false);
                    return;
                }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        async Task PollMuseAsync(MuseOperation operation, int current)
        {
            int version = -1;
            while (!operation.Terminal)
            {
                var state = await operation.Client.PollAsync(operation.Id, version, operation.Lifetime.Token).ConfigureAwait(false);
                if (state.Error != null) throw new ConnectionException(MuseTranscriptionClient.IsAsrCode(state.Error) ? state.Error : "ASR_UPSTREAM");
                if (state.Done)
                {
                    operation.Terminal = true;
                    DispatchMuse(current, () => Completed(state.Final));
                    return;
                }
                if (state.Version > version && !string.IsNullOrEmpty(state.Partial))
                    DispatchMuse(current, () => Partially(state.Partial));
                version = state.Version;
            }
        }

        void ReleaseMuse()
        {
            capture?.Dispose(); capture = null;
            var operation = muse; muse = null;
            if (operation == null) return;
            try { operation.Lifetime.Cancel(); } catch (ObjectDisposedException) { }
            lock (operation.Gate) operation.Audio.Clear();
        }

        string DescribeMuseError(Exception error)
        {
            var code = (error as ConnectionException)?.Code ?? error.Message;
            switch (code)
            {
                case "ASR_NOT_CONFIGURED": return "The backend has no META_API_KEY";
                case "ASR_UPSTREAM": return "Muse Voice Transcribe did not respond. Try again";
                case "ASR_RATE_LIMITED": return "Transcription limit reached. Wait a moment";
                case "ASR_TIMEOUT": return "Transcription timed out";
                case "ASR_IDLE": case "ASR_BACKLOG": return "Audio was interrupted. Dictate again";
                case "MIC_NOT_STARTED": return "Could not start the microphone. Check microphone permissions.";
                case "MIC_READ_FAILED": case "MIC_BUFFER_OVERRUN": return "Microphone audio was interrupted. Dictate again";
            }
            if (error is OpenCodeHttpException httpError && httpError.StatusCode == 410)
                return "The voice session expired. Dictate again";
            return ConnectionErrors.Describe(error, settings);
        }

        WitConfiguration CreateRuntimeConfiguration(float maxSec)
        {
            var token = SeyraEnvironmentCredentials.WitClientToken;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("SEYRA_CLIENT_TOKEN is missing from .env.local.");

            var config = ScriptableObject.CreateInstance<WitConfiguration>();
            config.name = "Seyra Wit (Runtime)";
            config.hideFlags = HideFlags.HideAndDontSave;
            config.useConduit = false;
            config.useIntentAttributes = false;
            config.RequestTimeoutMs = Mathf.CeilToInt((maxSec + 20f) * 1000f);
            config.SetClientAccessToken(token);
            return config;
        }

        void ActivateRequest()
        {
            if (!accepting) return;
            if (service == null) throw new InvalidOperationException("The dictation service is not initialized.");

            var requestEvents = new VoiceServiceRequestEvents();
            int current = generation;

            requestEvents.OnCancel.AddListener(request =>
            {
                if (current == generation) RequestCanceled(request.Results?.Message);
            });
            requestEvents.OnFailed.AddListener(request =>
            {
                if (current == generation) Errored("Wit.ai", request.Results?.Message);
            });
            requestEvents.OnSuccess.AddListener(request =>
            {
                if (current == generation) Completed(request.Results?.Transcription);
            });

            service.ActivateImmediately(requestEvents);
            if (accepting && listening) limit = StartCoroutine(StopAtLimit());
        }

        public void End()
        {
            if (!listening) return;
            listening = false;
            finishing = true;
            StopLimitTimer();

            if (useMuse)
            {
                if (finalReceived) { PublishFinal(); return; }
                try
                {
                    if (capture != null) { EnqueueAudio(capture.Stop()); capture = null; }
                    if (muse != null) muse.Stopping = true;
                    else { Cancel(); return; } // Permission was still pending; no session exists.
                    SetStatus(Processing, Available);
                    if (accepting && pending == null) pending = StartCoroutine(ResetIfSilent(15f));
                }
                catch (Exception error) { Errored("Microphone", DescribeMuseError(error)); }
                return;
            }

            if (preparing != null)
            {
                Errored("Audio", "Recording was still preparing. Keep holding until Listening appears.");
                return;
            }

            if (finalReceived)
            {
                PublishFinal();
                return;
            }

            SetStatus(Processing, Available);
            try
            {
                if (service == null) throw new InvalidOperationException("The dictation service disconnected.");
                service.Deactivate();
            }
            catch (Exception error)
            {
                Errored("Deactivate", error.Message);
                return;
            }

            if (accepting && pending == null) pending = StartCoroutine(ResetIfSilent(15f));
        }

        public void Cancel()
        {
            var wasActive = accepting || listening || finishing;
            ++generation;
            ReleaseMuse();
            accepting = listening = finishing = false;
            finalReceived = false;
            StopTimers();
            try
            {
                if (service != null && (service.Active || service.IsRequestActive))
                    service.Cancel();
            }
            catch (Exception) { }

            SetStatus(Available ? Idle : status, Available);
            if (wasActive) Ended?.Invoke();
        }

        public void SimulateTranscriptionResult(string text)
        {
            Transcription = text?.Trim() ?? "";
            finalText = Transcription;
            finalReceived = true;
            Changed?.Invoke();
            PublishFinal();
        }

        void Started()
        {
            if (!accepting || finishing) return;
            SetStatus(ListeningText, Available);
        }

        void Stopped()
        {
            if (!accepting) return;
            SetStatus(listening ? "Recording stopped · Release to finish" : Processing, Available);
        }

        void Partially(string transcription)
        {
            if (!accepting || finalReceived) return;
            Transcription = transcription?.Trim() ?? "";
            Changed?.Invoke();
            Partial?.Invoke(Transcription);
        }

        void Completed(string transcription)
        {
            if (!accepting || finalReceived) return;
            if (useMuse) ReleaseMuse();
            finalReceived = true;
            finalText = transcription?.Trim() ?? "";
            Transcription = finalText;
            Changed?.Invoke();
            if (listening)
                SetStatus("Dictation complete · Release to finish", Available);
            else
                PublishFinal();
        }

        void PublishFinal()
        {
            ReleaseMuse();
            accepting = listening = finishing = false;
            StopTimers();
            SetStatus(finalText.Length > 0 ? Idle : "No speech recognized. Hold and speak again.", Available);
            Changed?.Invoke();
            if (finalText.Length > 0)
                Final?.Invoke(finalText);
            else
                Ended?.Invoke();
        }

        void Errored(string type, string message)
        {
            if (!accepting) return;
            ++generation;
            ReleaseMuse();
            accepting = listening = finishing = false;
            finalReceived = false;
            StopTimers();

            var detail = string.IsNullOrEmpty(message) ? type : message;
            detail = SeyraEnvironmentCredentials.RedactToken(detail);

            SetStatus("Could not transcribe: " + detail, Available);
            Failed?.Invoke(detail);

            try
            {
                if (service != null && (service.Active || service.IsRequestActive))
                    service.Cancel();
            }
            catch (Exception) { }
        }

        void RequestCanceled(string reason)
        {
            if (!accepting) return;
            string detail = reason ?? "The request was cancelled before a transcription arrived.";
            if (detail.IndexOf("no audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("no data", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "Wit.ai received no audio. Keep holding until Listening appears.";
            else if (detail.IndexOf("Failed to start audio", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "Could not start the microphone. Check microphone permissions.";

            Errored("Cancelled", detail);
        }

        IEnumerator ResetIfSilent(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            pending = null;
            if (accepting) Errored("Timeout", "The final transcription did not arrive. Check the connection and dictate again.");
        }

        IEnumerator StopAtLimit()
        {
            yield return new WaitForSecondsRealtime(maximumSeconds);
            limit = null;
            End();
        }

        void StopTimers()
        {
            if (preparing != null) { StopCoroutine(preparing); preparing = null; }
            if (pending != null) { StopCoroutine(pending); pending = null; }
            StopLimitTimer();
        }

        void StopLimitTimer()
        {
            if (limit == null) return;
            StopCoroutine(limit);
            limit = null;
        }

        void SetStatus(string value, bool available)
        {
            var changed = status != value || Available != available;
            status = value;
            Available = available;
            if (changed) Changed?.Invoke();
        }

        IEnumerator PrepareEditorCapture()
        {
            yield return null;
            Meta.WitAi.Lib.Mic mic = null;
            try
            {
                mic = Meta.WitAi.Data.AudioBuffer.Instance.MicInput as Meta.WitAi.Lib.Mic;
            }
            catch (Exception error)
            {
                Errored("Audio", error.Message);
            }

            if (mic == null)
            {
                preparing = null;
                Errored("Audio", "The Voice SDK could not create the microphone input.");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 6f;
            while (accepting && listening && Time.realtimeSinceStartup < deadline &&
                   (mic.Devices.Count == 0 || mic.ActivationState == Meta.Voice.VoiceAudioInputState.Activating))
            {
                yield return null;
            }

            preparing = null;
            if (!accepting || !listening) yield break;

            try
            {
                ConfigureEditorMicrophone();
                CreateService(runtimeConfiguration);
                ActivateRequest();
            }
            catch (Exception error)
            {
                Errored("Audio", error.Message);
            }
        }

        void ConfigureEditorMicrophone()
        {
            var devices = Microphone.devices;
            if (devices.Length == 0)
                throw new InvalidOperationException("Unity detected no microphone. Check your audio devices.");

#if UNITY_EDITOR
            string selected = SeyraEnvironmentCredentials.PreferredMicrophone;
#else
            string selected = "";
#endif
            var mic = Meta.WitAi.Data.AudioBuffer.Instance.MicInput as Meta.WitAi.Lib.Mic;
            if (mic == null)
                throw new InvalidOperationException("The Voice SDK could not create the microphone input.");

            int index = (!string.IsNullOrEmpty(selected) && mic.Devices != null) ? mic.Devices.IndexOf(selected) : 0;
            if (index < 0) index = 0;

            mic.ChangeMicDevice(index);
            Provider = "Wit.ai · " + mic.CurrentDeviceName;
            Changed?.Invoke();
        }

        public void ReloadEditorSettings()
        {
            Cancel();
            ReleaseService();
            SelectProvider();
            Evaluate();
            Changed?.Invoke();
        }
    }
}
