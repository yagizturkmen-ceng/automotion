using System;
using System.Collections.Generic;
using System.IO;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Basit bir runtime PCM/WAV sentezleyici. Dış ses dosyası (wav/mp3) indirmek harici kaynak
    /// seçimi, lisans/telif kontrolü ve kullanıcı onayı gerektirdiği için (bkz. REQUIREMENTS.md),
    /// bunun yerine gerçek 16-bit PCM ses verisi tamamen kod içinde üretilir - Console.Beep'in
    /// aksine gerçek WAV verisi olduğundan katmanlanabilir (mix), zarf (envelope) uygulanabilir ve
    /// System.Media.SoundPlayer ile UI thread'ini bloklamadan çalınabilir.
    /// </summary>
    public static class WaveSynth
    {
        private const int SampleRate = 44100;

        public enum Wave { Sine, Square, Triangle, Sawtooth, Noise }

        /// <summary>Tek bir nota/ton üretir; freqEnd verilirse süre boyunca frekans ona doğru kayar (sweep).</summary>
        public static short[] RenderTone(double freqStart, int durationMs, double volume, Wave wave, double? freqEnd = null, Random noiseRng = null)
        {
            int total = Math.Max(1, (int)(SampleRate * durationMs / 1000.0));
            var buf = new short[total];
            double fEnd = freqEnd ?? freqStart;
            double phase = 0;
            double noiseState = 0;
            var rng = noiseRng ?? new Random(unchecked((int)(freqStart * 997) ^ durationMs));

            for (int i = 0; i < total; i++)
            {
                double t = i / (double)total;
                double freq = freqStart + (fEnd - freqStart) * t;
                phase += freq / SampleRate;

                double raw;
                switch (wave)
                {
                    case Wave.Square:
                        raw = Math.Sin(2 * Math.PI * phase) >= 0 ? 1.0 : -1.0;
                        break;
                    case Wave.Triangle:
                        raw = 2.0 / Math.PI * Math.Asin(Math.Sin(2 * Math.PI * phase));
                        break;
                    case Wave.Sawtooth:
                        double p = phase - Math.Floor(phase);
                        raw = 2.0 * p - 1.0;
                        break;
                    case Wave.Noise:
                        double white = rng.NextDouble() * 2 - 1;
                        noiseState = noiseState * 0.72 + white * 0.28; // hafif alçak geçiren filtre - sert "statik" yerine yumuşak uğultu
                        raw = noiseState;
                        break;
                    default:
                        raw = Math.Sin(2 * Math.PI * phase);
                        break;
                }

                double env = Envelope(i, total);
                double sample = raw * volume * env;
                buf[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            }
            return buf;
        }

        /// <summary>Aynı uzunlukta birden fazla katmanı (ör. alçak "gövde" tonu + gürültü) toplayarak karıştırır.</summary>
        public static short[] Mix(params short[][] layers)
        {
            int len = 0;
            foreach (var l in layers) len = Math.Max(len, l.Length);
            var outBuf = new short[len];
            foreach (var l in layers)
            {
                for (int i = 0; i < l.Length; i++)
                {
                    int sum = outBuf[i] + l[i];
                    outBuf[i] = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
                }
            }
            return outBuf;
        }

        /// <summary>Notaları arka arkaya (sırayla) ekler.</summary>
        public static short[] Concat(params short[][] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.Length;
            var outBuf = new short[total];
            int offset = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, outBuf, offset, p.Length);
                offset += p.Length;
            }
            return outBuf;
        }

        // Tıklamayı/pat sesini önlemek için kısa doğrusal atak/sönüm zarfı; çok kısa notalarda
        // toplam sürenin oranına göre ölçeklenir.
        private static double Envelope(int i, int total)
        {
            int fadeIn = Math.Min(total / 6, (int)(SampleRate * 0.006));
            int fadeOut = Math.Min(total / 4, (int)(SampleRate * 0.02));
            if (fadeIn < 1) fadeIn = 1;
            if (fadeOut < 1) fadeOut = 1;

            if (i < fadeIn) return i / (double)fadeIn;
            if (i > total - fadeOut) return Math.Max(0, (total - i) / (double)fadeOut);
            return 1.0;
        }

        /// <summary>16-bit mono PCM örnek dizisini standart bir WAV byte dizisine sarar.</summary>
        public static byte[] ToWav(short[] samples)
        {
            const short bitsPerSample = 16;
            const short channels = 1;
            int byteRate = SampleRate * channels * bitsPerSample / 8;
            int dataSize = samples.Length * sizeof(short);

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataSize);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1); // PCM
            w.Write(channels);
            w.Write(SampleRate);
            w.Write(byteRate);
            w.Write((short)(channels * bitsPerSample / 8));
            w.Write(bitsPerSample);

            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataSize);
            foreach (var s in samples) w.Write(s);

            w.Flush();
            return ms.ToArray();
        }
    }
}
