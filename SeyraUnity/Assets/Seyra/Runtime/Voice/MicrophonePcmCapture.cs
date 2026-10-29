using System;
using UnityEngine;

namespace Seyra.Voice
{
    // All methods run on Unity's main thread, including disposal.
    public sealed class MicrophonePcmCapture : IDisposable
    {
        AudioClip clip;
        int position;
        bool started;
        float beganAt, lastPoll;

        public int Frequency => clip != null ? clip.frequency : 0;

        public void Start()
        {
            Dispose();
            position = 0; started = false;
            beganAt = lastPoll = Time.realtimeSinceStartup;
            clip = Microphone.Start(null, true, 4, 16000);
            if (clip == null) throw new InvalidOperationException("MIC_NOT_STARTED");
        }

        public byte[] Poll()
        {
            if (clip == null) return Array.Empty<byte>();
            float now = Time.realtimeSinceStartup;
            int next = Microphone.GetPosition(null);
            if (next < 0 || !Microphone.IsRecording(null)) throw new InvalidOperationException("MIC_NOT_STARTED");
            if (!started && next == 0)
            {
                if (now - beganAt >= 1f) throw new InvalidOperationException("MIC_NOT_STARTED");
                return Array.Empty<byte>();
            }
            started = true;
            if (now - lastPoll >= (float)clip.samples / clip.frequency)
                throw new InvalidOperationException("MIC_BUFFER_OVERRUN");
            lastPoll = now;
            int count = (next - position + clip.samples) % clip.samples;
            if (count == 0) return Array.Empty<byte>();
            var interleaved = new float[count * clip.channels];
            // AudioClip.GetData wraps reads past the end of the circular clip.
            if (!clip.GetData(interleaved, position)) throw new InvalidOperationException("MIC_READ_FAILED");
            position = next;
            var mono = new float[count];
            for (int i = 0; i < count; i++)
            {
                for (int channel = 0; channel < clip.channels; channel++)
                    mono[i] += interleaved[i * clip.channels + channel];
                mono[i] /= clip.channels;
            }
            var resampled = PcmEncoding.Resample(mono, count, clip.frequency, 16000);
            return PcmEncoding.FloatToPcm16(resampled, resampled.Length);
        }

        public byte[] Stop()
        {
            try { return Poll(); }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (clip == null) return;
            try { Microphone.End(null); }
            finally { UnityEngine.Object.Destroy(clip); clip = null; }
        }
    }
}
