using System;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Hava durumu yoğunluğunun rampayla değişmesi (kalan işler 3.4 / B-2).
///
/// Yağmur/kar eskiden bayrak değişir değişmez tam şiddetle başlıyordu. Artık yoğunluk hedefine
/// rampayla yaklaşıyor. Bu hesabın üç sessiz kırılma noktası var ve testler onları sabitliyor:
/// hedefi aşmak (titreme), kare hızına göre farklı hızlanmak (144 FPS'te iki kat hızlı hava),
/// hedefe hiç ulaşamamak (sonsuza kadar 0.999).
/// </summary>
public class RampTests
{
    private const double PerFrame = 1.0 / 120.0;   // MainWindow.WeatherRampPerFrame

    [Fact]
    public void Approach_MovesTowardTarget()
    {
        double v = Ramp.Approach(0.0, 1.0, PerFrame, 1.0);

        Assert.True(v > 0 && v < 1, $"hedefe doğru ilerlemeli ama aşmamalı: {v}");
        Assert.Equal(PerFrame, v, precision: 6);
    }

    [Fact]
    public void Approach_NeverOvershootsTheTarget()
    {
        // Devasa bir kare atlaması (uygulama donup açıldığında olur) hedefi aşmamalı.
        Assert.Equal(1.0, Ramp.Approach(0.0, 1.0, PerFrame, 10_000));
        Assert.Equal(0.0, Ramp.Approach(1.0, 0.0, PerFrame, 10_000));
    }

    [Fact]
    public void Approach_ReachesTheTargetExactly()
    {
        double v = 0;
        for (int i = 0; i < 1000 && v < 1.0; i++) v = Ramp.Approach(v, 1.0, PerFrame, 1.0);

        Assert.Equal(1.0, v); // 0.999... diye takılı kalmamalı
    }

    /// <summary>
    /// KARE HIZINDAN BAĞIMSIZLIK: 144 FPS'te (deltaFrames≈0.42) iki kat daha çok kare işlenir ama
    /// hava aynı SÜREDE açılmalı. Aksi halde yüksek yenileme hızlı ekranda yağmur iki kat hızlı
    /// bastırırdı - gözle fark edilmesi zor, ölçmesi kolay bir hata.
    /// </summary>
    [Fact]
    public void Approach_AdvancesAtTheSameRealSpeed_RegardlessOfFrameRate()
    {
        double at60 = 0, at144 = 0;

        for (int i = 0; i < 60; i++) at60 = Ramp.Approach(at60, 1.0, PerFrame, 1.0);          // 1 saniye @60
        for (int i = 0; i < 144; i++) at144 = Ramp.Approach(at144, 1.0, PerFrame, 60.0 / 144); // 1 saniye @144

        Assert.Equal(at60, at144, precision: 6);
    }

    [Fact]
    public void Approach_WorksInBothDirections()
    {
        Assert.True(Ramp.Approach(0.5, 1.0, PerFrame, 1.0) > 0.5, "yukarı çıkabilmeli");
        Assert.True(Ramp.Approach(0.5, 0.0, PerFrame, 1.0) < 0.5, "aşağı inebilmeli");
    }

    [Fact]
    public void Approach_StaysAtTargetOnceReached()
    {
        Assert.Equal(1.0, Ramp.Approach(1.0, 1.0, PerFrame, 1.0));
        Assert.Equal(0.0, Ramp.Approach(0.0, 0.0, PerFrame, 5.0));
    }

    /// <summary>Bozuk girdiler (sıfır/negatif hız, NaN) rampayı kilitlememeli - hava durumu
    /// sonsuza kadar yarı yolda kalmaktansa doğrudan hedefe otursun.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Approach_FallsBackToTarget_ForInvalidRate(double rate)
    {
        Assert.Equal(1.0, Ramp.Approach(0.0, 1.0, rate, 1.0));
    }

    [Fact]
    public void Approach_TreatsNegativeDeltaAsNoTime()
    {
        Assert.Equal(0.25, Ramp.Approach(0.25, 1.0, PerFrame, -5.0));
    }

    /// <summary>Seçilen hızla tam şiddete ulaşma süresi makul olmalı: aniden değil ama
    /// beklemeye de dönüşmeden (~2 saniye).</summary>
    [Fact]
    public void WeatherRamp_ReachesFullIntensityInAboutTwoSeconds()
    {
        double v = 0;
        int frames = 0;
        while (v < 1.0 && frames < 10_000) { v = Ramp.Approach(v, 1.0, PerFrame, 1.0); frames++; }

        double seconds = frames / 60.0;
        Assert.InRange(seconds, 1.0, 3.0);
    }
}
