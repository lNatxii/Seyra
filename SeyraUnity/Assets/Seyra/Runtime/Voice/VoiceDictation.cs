using System;
using System.Collections;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.Dictation;
using Meta.WitAi.Requests;
using Oculus.Voice.Dictation;
using Oculus.Voice.Dictation.Configuration;
using Seyra.Configuration;
using UnityEngine;

namespace Seyra.Voice
{
    /// <summary>
    /// Voice dictation provider using Meta Voice SDK.
    /// In Editor / Standalone: uses PC microphone and Wit.ai with SEYRA_CLIENT_TOKEN from .env.local.
    /// On Android / Quest: uses native Meta platform dictation integration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceDictation : MonoBehaviour
    {
        public const string Idle = "Mantén pulsado para hablar";
        public const string ListeningText = "Escuchando…";
        public const string Processing = "Transcribiendo…";

        [SerializeField, Range(5, 120)] float maximumSeconds = 45f;

        AppDictationExperience service;
        bool listening, accepting, finishing, finalReceived;
        string status = "", finalText = "";
        Coroutine pending, limit;
        Coroutine preparing;
        int generation;
        WitConfiguration runtimeConfiguration;

        public event Action Changed;
        public event Action<string> Partial, Final, Failed;

        public bool Available { get; private set; }
        public bool Listening => listening;
        public bool Busy => listening || finishing;
        public string Status => status;
        public string Transcription { get; private set; } = "";
        public string Provider { get; private set; } = "";

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Provider = "Meta · dictado del visor";
            if (Application.platform == RuntimePlatform.Android)
            {
                CreateService(null, true);
            }
#else
            Provider = "Wit.ai · micrófono del PC";
#endif
            Evaluate();
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

#if UNITY_ANDROID && !UNITY_EDITOR
            var available = service != null && service.HasPlatformIntegrations;
            SetStatus(available ? Idle : "El dictado de plataforma no está disponible en este dispositivo.", available);
#else
            var configured = SeyraEnvironmentCredentials.HasWitToken;
            SetStatus(configured ? Idle : "Configura SEYRA_CLIENT_TOKEN en Backend/.env.local para usar dictado.", configured);
#endif
        }

        void CreateService(WitConfiguration configuration, bool platform)
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
                minKeepAliveTimeInSeconds = platform ? 0.5f : maximumSeconds + 1f,
                minTranscriptionKeepAliveTimeInSeconds = platform ? 0.5f : maximumSeconds + 1f,
                transcribeOnly = true,
                preferredActivationOffset = 0,
                dictationConfiguration = new DictationConfiguration
                {
                    multiPhrase = false,
                    scenario = "default",
                    inputType = "text_default"
                }
            };
            service.DoNotFallbackToWit = platform;
            service.UsePlatformIntegrations = platform;

            int current = generation;
            service.DictationEvents.OnStartListening.AddListener(() => { if (current == generation) Started(); });
            service.DictationEvents.OnStoppedListening.AddListener(() => { if (current == generation) Stopped(); });
            service.DictationEvents.OnPartialTranscription.AddListener(text => { if (current == generation) Partially(text); });
            service.DictationEvents.OnFullTranscription.AddListener(text => { if (current == generation) Completed(text); });
            service.DictationEvents.OnError.AddListener((type, message) => { if (current == generation) Errored(type, message); });

            host.SetActive(true);

            if (platform)
            {
                service.UsePlatformIntegrations = true;
                var fallback = host.GetComponent<WitDictation>();
                if (fallback != null && service.HasPlatformIntegrations)
                    Destroy(fallback);
            }
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

#if UNITY_ANDROID && !UNITY_EDITOR
            SetStatus(ListeningText, true);
#else
            SetStatus("Conectando con Wit.ai… Mantén pulsado", true);
#endif
            Changed?.Invoke();

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                ActivateRequest();
#else
                ReleaseService();
                if (runtimeConfiguration != null) Destroy(runtimeConfiguration);
                runtimeConfiguration = CreateRuntimeConfiguration(maximumSeconds);
                preparing = StartCoroutine(PrepareEditorCapture());
#endif
            }
            catch (Exception error)
            {
                Errored("Activate", error.Message);
            }
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
            if (service == null) throw new InvalidOperationException("El servicio de dictado no está inicializado.");

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

            if (preparing != null)
            {
                Errored("Audio", "La grabación todavía se estaba preparando. Mantén pulsado hasta que aparezca Escuchando.");
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
                if (service == null) throw new InvalidOperationException("El servicio de dictado se ha desconectado.");
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
            SetStatus(listening ? "Grabación detenida · Suelta para enviar" : Processing, Available);
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
            finalReceived = true;
            finalText = transcription?.Trim() ?? "";
            Transcription = finalText;
            Changed?.Invoke();
            if (listening)
                SetStatus("Dictado terminado · Suelta para enviar", Available);
            else
                PublishFinal();
        }

        void PublishFinal()
        {
            accepting = listening = finishing = false;
            StopTimers();
            SetStatus(finalText.Length > 0 ? Idle : "No se ha reconocido voz. Mantén pulsado y vuelve a hablar.", Available);
            Changed?.Invoke();
            if (finalText.Length > 0)
                Final?.Invoke(finalText);
        }

        void Errored(string type, string message)
        {
            if (!accepting) return;
            accepting = listening = finishing = false;
            finalReceived = false;
            StopTimers();

            var detail = string.IsNullOrEmpty(message) ? type : message;
            detail = SeyraEnvironmentCredentials.RedactToken(detail);

            SetStatus("No se pudo dictar: " + detail, Available);
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
            string detail = reason ?? "La petición se canceló antes de recibir una transcripción.";
            if (detail.IndexOf("no audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("no data", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "No llegó audio a Wit.ai. Mantén pulsado hasta que aparezca Escuchando.";
            else if (detail.IndexOf("Failed to start audio", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "No se pudo iniciar el micrófono. Revisa los permisos de micrófono.";

            Errored("Cancelado", detail);
        }

        IEnumerator ResetIfSilent(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            pending = null;
            if (accepting) Errored("Timeout", "No llegó la transcripción final. Revisa la conexión y vuelve a dictar.");
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
                Errored("Audio", "El Voice SDK no ha podido crear la entrada de micrófono.");
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
                CreateService(runtimeConfiguration, false);
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
                throw new InvalidOperationException("Unity no detecta ningún micrófono. Revisa los dispositivos de audio.");

#if UNITY_EDITOR
            string selected = SeyraEnvironmentCredentials.PreferredMicrophone;
#else
            string selected = "";
#endif
            var mic = Meta.WitAi.Data.AudioBuffer.Instance.MicInput as Meta.WitAi.Lib.Mic;
            if (mic == null)
                throw new InvalidOperationException("El Voice SDK no ha podido crear la entrada de micrófono.");

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
            Provider = "Wit.ai · micrófono del PC";
            Evaluate();
            Changed?.Invoke();
        }
    }
}
