using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AutoMotion.Services
{
    /// <summary>
    /// Önceden üretilmiş (WaveSynth) bir ses klibinin float örnekleri. Bir kez üretilip birden çok
    /// kez, AYNI ANDA çalınabilir - her çalma kendi okuma konumunu tutan ayrı bir sağlayıcı alır.
    /// </summary>
    public sealed class CachedSound
    {
        public float[] Samples { get; }
        public WaveFormat WaveFormat { get; }

        /// <summary>16-bit mono PCM'i (WaveSynth çıktısı) mikserin float biçimine çevirir.</summary>
        public CachedSound(short[] pcm, int sampleRate)
        {
            Samples = new float[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                Samples[i] = pcm[i] / 32768f;

            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }
    }

    /// <summary>Bir <see cref="CachedSound"/>'un tek seferlik çalınması.</summary>
    internal sealed class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound _sound;
        private int _position;

        public CachedSoundSampleProvider(CachedSound sound) => _sound = sound;

        public WaveFormat WaveFormat => _sound.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _sound.Samples.Length - _position;
            int toCopy = Math.Min(available, count);
            if (toCopy <= 0) return 0;

            Array.Copy(_sound.Samples, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }
    }

    /// <summary>
    /// Sürekli çalan, devire (RPM) bağlı motor sesi.
    ///
    /// Neden hazır bir klibi döngüye almak yerine doğrudan sentez: motor sesinin perdesi hıza göre
    /// SÜREKLİ değişmeli. Sabit bir klibi yeniden örnekleyerek perdesini değiştirmek hem döngü
    /// noktalarında tık sesi üretir hem de hız değiştikçe bozulur. Burada osilatör doğrudan hedef
    /// frekansta çalıştığı için geçişler kusursuz akıcıdır.
    ///
    /// Eskiden motor sesi 12 karede bir tetiklenen KESİK bir "bip" dizisiydi ve SoundPlayer aynı
    /// anda tek ses çalabildiği için birden fazla araçta çoğu duyulmadan kesiliyordu.
    /// </summary>
    internal sealed class EngineToneProvider : ISampleProvider
    {
        private readonly int _sampleRate;
        private double _phase, _phase2;

        private volatile float _targetFrequency = 90f;
        private volatile float _targetAmplitude;

        private float _frequency = 90f;
        private float _amplitude;

        public EngineToneProvider(int sampleRate)
        {
            _sampleRate = sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        /// <summary>Hedef frekans (Hz) ve ses şiddeti. Gerçek değerler bunlara doğru yumuşakça kayar,
        /// böylece gaz verildiğinde/kesildiğinde tık sesi olmaz.</summary>
        public void Set(float frequency, float amplitude)
        {
            _targetFrequency = Math.Clamp(frequency, 20f, 4000f);
            _targetAmplitude = Math.Clamp(amplitude, 0f, 1f);
        }

        /// <summary>Faz (0..1) -> üçgen dalga (-1..1), transandantal fonksiyon KULLANMADAN.
        ///
        /// Önceden gövde dalgası <c>Asin(Sin(2πp))</c> ile üretiliyordu; bu, saniyede 44.100 örnek
        /// için iki transandantal çağrı demekti ve ölçülebilir CPU yakıyordu. Üçgen dalga saf
        /// aritmetikle birebir aynı şekilde elde edilebilir.</summary>
        private static float Triangle(double phase)
        {
            double t = phase - Math.Floor(phase);
            return (float)(4.0 * Math.Abs(t - 0.5) - 1.0);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Yumuşatma katsayısı sabittir - her çağrıda Exp hesaplamaya gerek yok.
            const float smooth = 0.00075f; // ~30 ms zaman sabiti @44.1 kHz

            double phaseStep = 0, phaseStep2 = 0;
            float freq = _frequency, amp = _amplitude;
            float targetFreq = _targetFrequency, targetAmp = _targetAmplitude;

            for (int i = 0; i < count; i++)
            {
                freq += (targetFreq - freq) * smooth;
                amp += (targetAmp - amp) * smooth;

                phaseStep = freq / _sampleRate;
                phaseStep2 = (freq * 2.02) / _sampleRate; // hafif detune edilmiş ikinci harmonik

                _phase += phaseStep;
                _phase2 += phaseStep2;
                if (_phase > 1e6) _phase -= Math.Floor(_phase);
                if (_phase2 > 1e6) _phase2 -= Math.Floor(_phase2);

                // Üçgen gövde + zayıf ikinci harmonik: sinüsten daha "motor", kareden yumuşak
                float body = Triangle(_phase);
                float harmonic = Triangle(_phase2) * 0.25f;

                buffer[offset + i] = (body + harmonic) * amp * 0.5f;
            }

            _frequency = freq;
            _amplitude = amp;
            return count;
        }
    }

    /// <summary>
    /// Ses aygıtı ve karıştırıcı katmanı.
    ///
    /// Eskiden sesler <c>System.Media.SoundPlayer</c> ile çalınıyordu; bu sınıfın iki temel kısıtı
    /// vardı: (a) AYNI ANDA TEK ses çalabiliyor - her yeni çalma bir öncekini kesiyordu, 10 araçlık
    /// bir filoda seslerin çoğu hiç duyulmuyordu; (b) ses seviyesi ayarı yoktu, yalnızca topyekûn
    /// sustur vardı. Ticari bir oyunda Ana/Efekt/Müzik seviyeleri standart beklentidir.
    ///
    /// NAudio zaten bağımlılıklarda olduğundan (ses tepkimeli görsel için) ek maliyet yok.
    ///
    /// Ses aygıtı açılamazsa (aygıt yok, sürücü sorunu, uzak masaüstü oturumu) motor sessizce
    /// devre dışı kalır - oyun çalışmaya devam eder. Steam köprüsündeki ile aynı yaklaşım.
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        public const int SampleRate = 44100;

        private IWavePlayer _output;
        private MixingSampleProvider _sfxBus;
        private MixingSampleProvider _musicBus;
        private VolumeSampleProvider _masterVolume;
        private VolumeSampleProvider _sfxVolume;
        private VolumeSampleProvider _musicVolume;
        private EngineToneProvider _engineTone;

        private readonly object _gate = new();
        private bool _disposed;

        /// <summary>Aygıt açılabildi mi? False ise tüm çalma çağrıları sessizce yok sayılır.</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>Aynı anda çalabilecek azami efekt sayısı. Sınırsız bırakılırsa yoğun bir
        /// çarpışma anında onlarca klip üst üste binip hem kulak tırmalar hem CPU yakar.</summary>
        private const int MaxConcurrentSfx = 16;

        public AudioEngine()
        {
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

                // ReadFully: kaynak bittiğinde sessizlik üretmeye devam et - kalıcı bir veri yolu
                // (bus) için şart, aksi halde ilk ses bitince mikser kapanır.
                _sfxBus = new MixingSampleProvider(format) { ReadFully = true };
                _musicBus = new MixingSampleProvider(format) { ReadFully = true };

                _engineTone = new EngineToneProvider(SampleRate);
                _sfxBus.AddMixerInput(_engineTone);

                _sfxVolume = new VolumeSampleProvider(_sfxBus) { Volume = 0.85f };
                _musicVolume = new VolumeSampleProvider(_musicBus) { Volume = 0.55f };

                var masterMix = new MixingSampleProvider(format) { ReadFully = true };
                masterMix.AddMixerInput(_sfxVolume);
                masterMix.AddMixerInput(_musicVolume);

                _masterVolume = new VolumeSampleProvider(masterMix) { Volume = 0.8f };

                // Gecikme (latency) düşük tutuluyor: bir çarpışma sesinin görsel çarpışmadan
                // belirgin sonra gelmesi kalitesiz hissettirir.
                _output = new WaveOutEvent { DesiredLatency = 120 };
                _output.Init(_masterVolume);
                _output.Play();

                IsAvailable = true;
            }
            catch (Exception ex)
            {
                // Ses aygıtı yoksa oyun sessiz çalışır - açılışı engellemez.
                System.Diagnostics.Debug.WriteLine("Ses motoru başlatılamadı: " + ex.Message);
                IsAvailable = false;
                DisposeInternals();
            }
        }

        // ------------------------------------------------------------------ seviyeler (0..1)

        public float MasterVolume
        {
            get => _masterVolume?.Volume ?? 0f;
            set { if (_masterVolume != null) _masterVolume.Volume = Math.Clamp(value, 0f, 1f); }
        }

        public float SfxVolume
        {
            get => _sfxVolume?.Volume ?? 0f;
            set { if (_sfxVolume != null) _sfxVolume.Volume = Math.Clamp(value, 0f, 1f); }
        }

        public float MusicVolume
        {
            get => _musicVolume?.Volume ?? 0f;
            set { if (_musicVolume != null) _musicVolume.Volume = Math.Clamp(value, 0f, 1f); }
        }

        // ------------------------------------------------------------------ çalma

        // ------------------------------------------------------------------ askıya alma

        private bool _suspended;

        /// <summary>
        /// Ses aygıtını durdurur. Sahnede hiçbir şey yokken (bkz. MainWindow.UpdateRenderLoopState)
        /// çağrılır: NAudio'nun çıkış thread'i sessizlik üretmek için bile sürekli çalışır ve bu,
        /// ölçülen boşta CPU'yu ~%0.25'ten ~%1.25'e çıkarıyordu. Masaüstünde saatlerce açık kalan
        /// bir uygulamada bu boşa yanan CPU'dur.
        ///
        /// Herhangi bir ses çalınmak istendiğinde otomatik olarak geri açılır.
        /// </summary>
        public void Suspend()
        {
            lock (_gate)
            {
                if (_disposed || _suspended || _output == null) return;
                _suspended = true;
                try { _output.Pause(); } catch { }
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                if (_disposed || !_suspended || _output == null) return;
                _suspended = false;
                try { _output.Play(); } catch { }
            }
        }

        /// <summary>Bir efekti çalar. Aynı anda birden fazla efekt çalabilir (eski SoundPlayer'ın
        /// aksine) - üst sınıra ulaşıldığında yeni çağrı sessizce atlanır.</summary>
        public void PlayOneShot(CachedSound sound)
        {
            if (!IsAvailable || sound == null) return;

            // Ses istendiğine göre aygıt uykudaysa hemen uyandırılmalı.
            Resume();

            lock (_gate)
            {
                if (_disposed) return;
                // MixerInputs sayımı motor tonunu da içerir (+1)
                if (_sfxBus.MixerInputs is ICollection<ISampleProvider> inputs
                    && inputs.Count >= MaxConcurrentSfx + 1) return;

                try { _sfxBus.AddMixerInput(new CachedSoundSampleProvider(sound)); }
                catch { /* mikser o an okuma yapıyor olabilir - bir sesi kaçırmak kritik değil */ }
            }
        }

        /// <summary>
        /// Sürekli motor sesini günceller.
        /// </summary>
        /// <param name="baseFrequency">Aracın rölanti frekansı (ağır dizel düşük, motosiklet yüksek).</param>
        /// <param name="rpmRatio">0 = rölanti, 1 = azami devir.</param>
        /// <param name="amplitude">0 = sessiz (motor kapalı/araç yok).</param>
        public void SetEngineTone(float baseFrequency, float rpmRatio, float amplitude)
        {
            if (!IsAvailable) return;
            if (amplitude > 0f) Resume();
            // Devir arttıkça frekans 2.6 katına kadar çıkar - gaz verildiğinde belirgin bir
            // "tizleşme" duyulur.
            float freq = baseFrequency * (1f + Math.Clamp(rpmRatio, 0f, 1f) * 1.6f);
            _engineTone.Set(freq, amplitude);
        }

        public void StopEngineTone() => _engineTone?.Set(90f, 0f);

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                IsAvailable = false;
                DisposeInternals();
            }
        }

        private void DisposeInternals()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;
        }
    }
}
