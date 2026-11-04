using System;
using System.Collections.Generic;
using AutoMotion.Models;

namespace AutoMotion.Tests;

/// <summary>
/// Yere çarpınca ezilip yaylanarak toparlanma ("yumuşak top" hissi).
///
/// Bu davranışın üç kırılgan yeri var ve üçü de gözle ancak yavaş çekimde fark edilir:
/// ezilmenin çarpma şiddetiyle orantılı olması, toparlanırken hedefi AŞMASI (kısa bir esneme -
/// canlılık hissini veren asıl ayrıntı) ve sonunda gerçekten durması (sonsuza kadar titrememesi).
/// </summary>
public class SquashTests
{
    private static readonly List<Vehicle> NoOthers = new();
    private static readonly List<Obstacle> NoObstacles = new();

    /// <summary>Aracı yerden yüksekte bırakıp zemine çarpana kadar simüle eder; çarpma anındaki
    /// en yüksek ezilme ile sonraki kareleri döndürür.</summary>
    private static (double peakSquash, List<double> timeline) DropAndRecord(double dropHeight, int frames = 240)
    {
        const double canvasW = 1200, canvasH = 800;
        var v = new Vehicle(canvasW / 2, canvasH - dropHeight, "car");
        v.VelocityX = 0;
        v.VelocityY = 0;

        var timeline = new List<double>();
        double peak = 0;

        for (int i = 0; i < frames; i++)
        {
            v.Update(i, NoOthers, NoObstacles, canvasW, canvasH);
            timeline.Add(v.SquashAmount);
            if (v.SquashAmount > peak) peak = v.SquashAmount;
        }

        return (peak, timeline);
    }

    [Fact]
    public void LandingOnTheGround_SquashesTheVehicle()
    {
        var (peak, _) = DropAndRecord(dropHeight: 400);

        Assert.True(peak > 0.05, $"yere çarpınca gözle görülür şekilde ezilmeli, en yüksek ezilme {peak:0.###}");
    }

    /// <summary>Yüksekten düşen araç, alçaktan düşenden daha çok ezilmeli - ezilme çarpma
    /// şiddetiyle orantılı.</summary>
    [Fact]
    public void HarderImpact_SquashesMore()
    {
        var (shallow, _) = DropAndRecord(dropHeight: 120);
        var (deep, _) = DropAndRecord(dropHeight: 600);

        Assert.True(deep > shallow,
            $"yüksekten düşüş daha çok ezmeli: {deep:0.###} (yüksek) vs {shallow:0.###} (alçak)");
    }

    [Fact]
    public void Squash_NeverExceedsTheCap()
    {
        var (peak, _) = DropAndRecord(dropHeight: 5000);

        Assert.InRange(peak, 0.0, 0.85);
    }

    /// <summary>
    /// ASIL DETAY: toparlanırken sıfırı aşıp kısa bir an GERİLMELİ (negatif değer). Düz üstel
    /// sönümde bu olmaz ve araç "yaylanmaz", sadece söner. Bu test yay davranışını sabitler.
    /// </summary>
    [Fact]
    public void Recovery_OvershootsIntoStretch()
    {
        var (_, timeline) = DropAndRecord(dropHeight: 500);

        double mostNegative = double.MaxValue;
        foreach (double s in timeline) if (s < mostNegative) mostNegative = s;

        Assert.True(mostNegative < -0.01,
            $"toparlanırken kısa bir esneme (negatif ezilme) olmalı, en düşük değer {mostNegative:0.###}");
    }

    /// <summary>
    /// Yaylanma SÖNMELİ: araç sonsuza kadar nabız gibi atmamalı.
    ///
    /// Düşürerek test edilemez - araç arka arkaya sekerken her temasta yeniden eziliyor, yani
    /// ölçülen şey yayın sönümü değil sekmelerin sürmesi olurdu. Onun yerine yere oturmuş bir
    /// araca ezilme doğrudan enjekte edilip yalnızca yayın davranışı izleniyor.
    /// </summary>
    [Fact]
    public void Squash_SettlesBackToNeutral()
    {
        const double canvasW = 1200, canvasH = 800;
        var v = new Vehicle(canvasW / 2, canvasH - 30, "car");

        // Önce zemine otursun (sekmeleri bitsin).
        for (int i = 0; i < 300; i++) v.Update(i, NoOthers, NoObstacles, canvasW, canvasH);

        v.SquashAmount = 0.8; // sert bir çarpma kadar ezilme

        double peakAfterInjection = 0;
        for (int i = 0; i < 120; i++) // ~2 saniye
        {
            v.Update(300 + i, NoOthers, NoObstacles, canvasW, canvasH);
            if (i > 90 && Math.Abs(v.SquashAmount) > peakAfterInjection)
                peakAfterInjection = Math.Abs(v.SquashAmount);
        }

        Assert.True(peakAfterInjection < 0.05,
            $"yaylanma sönmemiş, 1.5 sn sonra hâlâ {peakAfterInjection:0.###} genlikte titriyor");
    }

    /// <summary>Hiç çarpma olmadan ezilme oluşmamalı (zeminde duran araç kendiliğinden nefes
    /// almasın - rölanti titreşimi ayrı bir mekanizma).</summary>
    [Fact]
    public void RestingVehicle_DoesNotSquashOnItsOwn()
    {
        const double canvasW = 1200, canvasH = 800;
        var v = new Vehicle(canvasW / 2, canvasH - 30, "car");

        for (int i = 0; i < 120; i++) v.Update(i, NoOthers, NoObstacles, canvasW, canvasH);

        double settled = Math.Abs(v.SquashAmount);
        Assert.True(settled < 0.05, $"duran araçta kalıcı ezilme olmamalı: {settled:0.###}");
    }
}
