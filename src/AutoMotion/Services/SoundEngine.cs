using System;
using System.Collections.Generic;
using AutoMotion.Utilities;

namespace AutoMotion.Services
{
    /// <summary>
    /// Oyunun ses arayüzü: "korna çal", "para sesi çal" gibi OYUN kavramlarını bilir; ses aygıtı ve
    /// karıştırma işini <see cref="AudioEngine"/>'e devreder.
    ///
    /// Ses verisi hâlâ tamamen kod içinde üretiliyor (bkz. Utilities/WaveSynth.cs) - dış ses dosyası
    /// indirmek lisans/telif doğrulaması gerektirdiğinden bilinçli bir tercih. Değişen şey ÇALMA
    /// katmanı: eskiden System.Media.SoundPlayer aynı anda TEK ses çalabiliyor (her yeni çalma bir
    /// öncekini kesiyordu - 10 araçlık filoda seslerin çoğu hiç duyulmuyordu) ve yalnızca topyekûn
    /// sustur sunuyordu. Artık NAudio mikseri sayesinde sesler üst üste binebiliyor ve Ana/Efekt/
    /// Müzik seviyeleri ayrı ayrı ayarlanabiliyor.
    /// </summary>
    public class SoundEngine : IDisposable
    {
        private readonly AudioEngine _audio = new();
        private readonly Dictionary<string, CachedSound> _cache = new();
        private readonly object _cacheGate = new();

        private float _masterVolume = 0.8f;

        public bool IsMuted { get; private set; }

        /// <summary>Ses aygıtı açılabildi mi (aygıt yoksa oyun sessiz ama sorunsuz çalışır).</summary>
        public bool IsAudioAvailable => _audio.IsAvailable;

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
            // Sustur, seviyeleri SIFIRLAMAZ: kullanıcının ayarladığı denge korunur, yalnızca ana
            // çıkış kısılır. Ses tekrar açılınca eski seviyeler geri gelir.
            _audio.MasterVolume = IsMuted ? 0f : _masterVolume;
            if (IsMuted) _audio.StopEngineTone();
        }

        // ------------------------------------------------------------------ seviyeler (0..1)

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Math.Clamp(value, 0f, 1f);
                if (!IsMuted) _audio.MasterVolume = _masterVolume;
            }
        }

        public float SfxVolume
        {
            get => _audio.SfxVolume;
            set => _audio.SfxVolume = value;
        }

        public float MusicVolume
        {
            get => _audio.MusicVolume;
            set => _audio.MusicVolume = value;
        }

        // ------------------------------------------------------------------ efektler

        private void Play(string key, Func<short[]> build)
        {
            if (IsMuted) return;

            CachedSound sound;
            lock (_cacheGate)
            {
                if (!_cache.TryGetValue(key, out sound))
                {
                    sound = new CachedSound(build(), AudioEngine.SampleRate);
                    _cache[key] = sound;
                }
            }
            _audio.PlayOneShot(sound);
        }

        public void PlayClick() =>
            Play("click", () => WaveSynth.RenderTone(1100, 28, 0.18, WaveSynth.Wave.Sine));

        public void PlaySuccess() =>
            Play("success", () => WaveSynth.Concat(
                WaveSynth.RenderTone(880, 70, 0.24, WaveSynth.Wave.Triangle),
                WaveSynth.RenderTone(1320, 110, 0.22, WaveSynth.Wave.Triangle)));

        public void PlayCash() =>
            // "Cha-ching": iki yükselen üçgen-dalga nota + hafif tiz parıltı katmanı (bozuk para hissi)
            Play("cash", () => WaveSynth.Concat(
                WaveSynth.RenderTone(1200, 70, 0.35, WaveSynth.Wave.Triangle),
                WaveSynth.Mix(
                    WaveSynth.RenderTone(1600, 100, 0.32, WaveSynth.Wave.Triangle),
                    WaveSynth.RenderTone(3200, 100, 0.10, WaveSynth.Wave.Sine))));

        public void PlayCrash() =>
            // Alçak "gövde" darbesi (sine, hızlı sönümlü) + kısa gürültü patlaması
            Play("crash", () => WaveSynth.Mix(
                WaveSynth.RenderTone(110, 180, 0.55, WaveSynth.Wave.Sine, freqEnd: 60),
                WaveSynth.RenderTone(0, 120, 0.30, WaveSynth.Wave.Noise)));

        public void PlayDrift() =>
            // Tiz frekanstan pes frekansa hızlı kayan testere dalga + hafif gürültü (lastik cıyaklaması)
            Play("drift", () => WaveSynth.Mix(
                WaveSynth.RenderTone(1400, 90, 0.28, WaveSynth.Wave.Sawtooth, freqEnd: 500),
                WaveSynth.RenderTone(0, 90, 0.12, WaveSynth.Wave.Noise)));

        public void PlayHorn(string vehicleType)
        {
            var (freq, pattern) = vehicleType switch
            {
                "truck" => (350, 2),   // "moo" tarzı çift ton kamyon kornası
                "bus" => (300, 2),
                "train" => (220, 3),   // uzun tren düdüğü hissi
                _ => (650, 1)          // kısa, tiz otomobil kornası
            };

            Play("horn_" + vehicleType, () =>
            {
                var notes = new short[pattern][];
                for (int i = 0; i < pattern; i++)
                    notes[i] = WaveSynth.RenderTone(freq - i * 20, 150, 0.30, WaveSynth.Wave.Square);
                return WaveSynth.Concat(notes);
            });
        }

        /// <summary>Tek seferlik kısa gaz sesi - sürekli motor tonundan farklı olarak anlık bir
        /// tepki için (ör. araç sevildiğinde). Araç tipine göre perdesi değişir.</summary>
        public void PlayRev(string vehicleType)
        {
            float baseFreq = IdleFrequencyFor(vehicleType);
            if (baseFreq <= 0f) return; // motoru olmayan araç (bisiklet)

            Play("rev_" + vehicleType, () => WaveSynth.Concat(
                WaveSynth.RenderTone(baseFreq, 90, 0.26, WaveSynth.Wave.Triangle, freqEnd: baseFreq * 1.9),
                WaveSynth.RenderTone(baseFreq * 1.9, 70, 0.20, WaveSynth.Wave.Triangle, freqEnd: baseFreq * 1.2)));
        }

        // ------------------------------------------------------------------ motor sesi (sürekli)

        /// <summary>Her araç tipinin rölanti frekansı - ağır dizel pes gürler, motosiklet tiz vınlar.
        /// 0 döndürmek "bu araçta motor yok" demektir.</summary>
        public static float IdleFrequencyFor(string vehicleType) => vehicleType switch
        {
            "truck" => 62f,
            "bus" => 58f,
            "train" => 44f,
            "tractor" => 54f,
            "helicopter" => 120f,
            "motorcycle" => 150f,
            "bike" => 0f,
            _ => 88f
        };

        /// <summary>
        /// Sürekli motor sesini günceller. Eskiden motor sesi 12 karede bir tetiklenen KESİK bir
        /// "bip" dizisiydi; artık hıza göre perdesi sürekli değişen tek bir ton.
        /// </summary>
        /// <param name="vehicleType">Rölanti frekansını belirler.</param>
        /// <param name="rpmRatio">0 = rölanti, 1 = azami devir.</param>
        /// <param name="loudness">0 = duyulmaz (motor kapalı/araç yok), 1 = tam.</param>
        public void UpdateEngineTone(string vehicleType, double rpmRatio, double loudness)
        {
            if (IsMuted) { _audio.StopEngineTone(); return; }

            float baseFreq = IdleFrequencyFor(vehicleType);
            if (baseFreq <= 0f) { _audio.StopEngineTone(); return; }

            // 0.32 tavanı: motor sesi sürekli çaldığı için efektlerden belirgin daha kısık olmalı,
            // aksi halde korna/çarpışma gibi anlık sesleri bastırır.
            _audio.SetEngineTone(baseFreq, (float)rpmRatio, (float)Math.Clamp(loudness, 0, 1) * 0.32f);
        }

        public void StopEngineTone() => _audio.StopEngineTone();

        /// <summary>Sahne tamamen boşken ses aygıtını uykuya alır (boşta CPU için - bkz.
        /// AudioEngine.Suspend). Herhangi bir ses çalınmak istendiğinde kendiliğinden uyanır.</summary>
        public void SuspendAudioDevice()
        {
            _audio.StopEngineTone();
            _audio.Suspend();
        }

        public void Dispose()
        {
            _audio.Dispose();
            lock (_cacheGate) _cache.Clear();
        }
    }
}
