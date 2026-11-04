using System;
using System.Linq;
using AutoMotion.Services;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Ses motoru testleri.
///
/// Gerçek bir ses aygıtı test ortamında bulunmayabilir (CI, uzak masaüstü). Bu yüzden testler
/// aygıta bağlı olmayan katmanları doğrular: örnek dönüşümü, seviye kırpma, sustur davranışı,
/// motor frekans tablosu. Aygıt açılamadığında motorun sessizce devre dışı kalması da ayrıca
/// test edilir - çünkü o durumda oyunun çalışmaya devam etmesi kritik.
/// </summary>
public class AudioTests
{
    // ------------------------------------------------------------------ örnek dönüşümü

    [Fact]
    public void CachedSound_ConvertsPcmToNormalisedFloats()
    {
        short[] pcm = { 0, short.MaxValue, short.MinValue, 16384 };

        var sound = new CachedSound(pcm, AudioEngine.SampleRate);

        Assert.Equal(pcm.Length, sound.Samples.Length);
        Assert.Equal(0f, sound.Samples[0], precision: 5);
        Assert.InRange(sound.Samples[1], 0.99f, 1.0f);
        Assert.Equal(-1.0f, sound.Samples[2], precision: 5);
        Assert.Equal(0.5f, sound.Samples[3], precision: 3);
    }

    [Fact]
    public void CachedSound_ProducesMonoFloatFormatMatchingTheMixer()
    {
        var sound = new CachedSound(new short[] { 1, 2, 3 }, AudioEngine.SampleRate);

        Assert.Equal(1, sound.WaveFormat.Channels);
        Assert.Equal(AudioEngine.SampleRate, sound.WaveFormat.SampleRate);
        Assert.Equal(NAudio.Wave.WaveFormatEncoding.IeeeFloat, sound.WaveFormat.Encoding);
    }

    /// <summary>Tüm örnekler -1..1 aralığında kalmalı - aksi halde mikserde kırpılma (distortion)
    /// duyulur.</summary>
    [Fact]
    public void SynthesisedClips_StayWithinValidSampleRange()
    {
        var clips = new[]
        {
            WaveSynth.RenderTone(440, 120, 0.9, WaveSynth.Wave.Sine),
            WaveSynth.RenderTone(220, 120, 0.9, WaveSynth.Wave.Square),
            WaveSynth.RenderTone(0, 120, 0.9, WaveSynth.Wave.Noise),
            WaveSynth.Mix(
                WaveSynth.RenderTone(110, 180, 0.55, WaveSynth.Wave.Sine, freqEnd: 60),
                WaveSynth.RenderTone(0, 120, 0.30, WaveSynth.Wave.Noise)),
        };

        foreach (var clip in clips)
        {
            var sound = new CachedSound(clip, AudioEngine.SampleRate);
            Assert.All(sound.Samples, s => Assert.InRange(s, -1.0f, 1.0f));
        }
    }

    // ------------------------------------------------------------------ motor frekans tablosu

    [Theory]
    [InlineData("train")]
    [InlineData("bus")]
    [InlineData("truck")]
    [InlineData("tractor")]
    [InlineData("car")]
    [InlineData("helicopter")]
    [InlineData("motorcycle")]
    public void EnginedVehicles_HaveAPositiveIdleFrequency(string type)
    {
        Assert.True(SoundEngine.IdleFrequencyFor(type) > 0f);
    }

    /// <summary>Bisikletin motoru yok - frekansı 0 olmalı ki sürekli ton hiç çalmasın.</summary>
    [Fact]
    public void Bike_HasNoEngineFrequency()
    {
        Assert.Equal(0f, SoundEngine.IdleFrequencyFor("bike"));
    }

    /// <summary>Ağır dizel araçlar hafif araçlardan PES gürlemeli - ses karakterinin araç
    /// kütlesiyle tutarlı olması gerekiyor.</summary>
    [Fact]
    public void HeavyVehicles_RumbleLowerThanLightOnes()
    {
        float train = SoundEngine.IdleFrequencyFor("train");
        float truck = SoundEngine.IdleFrequencyFor("truck");
        float car = SoundEngine.IdleFrequencyFor("car");
        float motorcycle = SoundEngine.IdleFrequencyFor("motorcycle");

        Assert.True(train < truck, "tren kamyondan pes olmalı");
        Assert.True(truck < car, "kamyon otomobilden pes olmalı");
        Assert.True(car < motorcycle, "otomobil motosikletten pes olmalı");
    }

    // ------------------------------------------------------------------ seviye ve sustur

    [Fact]
    public void Volumes_AreClampedToValidRange()
    {
        using var engine = new SoundEngine();

        engine.MasterVolume = 5f;
        Assert.Equal(1f, engine.MasterVolume);

        engine.MasterVolume = -3f;
        Assert.Equal(0f, engine.MasterVolume);
    }

    /// <summary>Sustur, kullanıcının ayarladığı seviyeyi SIFIRLAMAMALI - sesi tekrar açınca eski
    /// denge geri gelmeli.</summary>
    [Fact]
    public void Mute_PreservesTheUsersVolumeSetting()
    {
        using var engine = new SoundEngine();
        engine.MasterVolume = 0.42f;

        engine.ToggleMute();
        Assert.True(engine.IsMuted);
        Assert.Equal(0.42f, engine.MasterVolume, precision: 3);

        engine.ToggleMute();
        Assert.False(engine.IsMuted);
        Assert.Equal(0.42f, engine.MasterVolume, precision: 3);
    }

    // ------------------------------------------------------------------ aygıt yokken dayanıklılık

    /// <summary>
    /// Ses aygıtı olmayan bir ortamda (CI, uzak masaüstü) HİÇBİR çağrı hata vermemeli. Oyunun
    /// sessiz çalışması kabul edilebilir; çökmesi değil.
    /// </summary>
    [Fact]
    public void EveryCall_IsSafe_EvenWithoutAnAudioDevice()
    {
        var ex = Record.Exception(() =>
        {
            using var engine = new SoundEngine();

            engine.PlayClick();
            engine.PlaySuccess();
            engine.PlayCash();
            engine.PlayCrash();
            engine.PlayDrift();
            engine.PlayHorn("truck");
            engine.PlayRev("car");
            engine.PlayRev("bike");            // motoru yok - sessizce atlanmalı
            engine.UpdateEngineTone("car", 0.5, 1.0);
            engine.UpdateEngineTone("bike", 0.5, 1.0);
            engine.StopEngineTone();
            engine.ToggleMute();
            engine.PlayCrash();                // susturulmuşken
            engine.UpdateEngineTone("truck", 1.0, 1.0);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var engine = new SoundEngine();
        engine.PlayClick();

        var ex = Record.Exception(() => { engine.Dispose(); engine.Dispose(); });

        Assert.Null(ex);
    }

    /// <summary>Aynı klip defalarca çalınabilmeli - önbellek bir kez üretip yeniden kullanır ve
    /// eş zamanlı çalmalar birbirini KESMEMELİ (eski SoundPlayer'ın temel kısıtıydı).</summary>
    [Fact]
    public void RepeatedPlayback_UsesCacheWithoutThrowing()
    {
        using var engine = new SoundEngine();

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 50; i++) engine.PlayCash();
        });

        Assert.Null(ex);
    }
}
