using System;

namespace Seyra.Voice
{
    public static class PcmEncoding
    {
        public static byte[] FloatToPcm16(float[] samples, int count)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                float value = Math.Max(-1f, Math.Min(1f, samples[i]));
                short pcm = (short)(value * 32767f);
                bytes[i * 2] = (byte)(pcm & 255);
                bytes[i * 2 + 1] = (byte)((pcm >> 8) & 255);
            }
            return bytes;
        }

        public static float[] Resample(float[] input, int count, int fromHz, int toHz)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (count < 0 || count > input.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (fromHz <= 0 || toHz <= 0) throw new ArgumentOutOfRangeException(nameof(fromHz));
            int length = (int)((long)count * toHz / fromHz);
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                double position = (double)i * fromHz / toHz;
                int left = Math.Min((int)position, count - 1);
                int right = Math.Min(left + 1, count - 1);
                result[i] = input[left] + (input[right] - input[left]) * (float)(position - left);
            }
            return result;
        }
    }
}
