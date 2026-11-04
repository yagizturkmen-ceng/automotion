using System;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Gündüz↔gece geçişinin akıcılığı (kalan işler 3.4 / B-1).
///
/// Önceki durum karışıktı: karanlık katmanı 19:00'dan itibaren yumuşakça iniyordu ama FARLAR ve
/// gece filtresi sert bir eşiğe (saat >= 20) bağlıydı - yani ekran kararırken farlar 20:00'de bir
/// anda yanıyordu. Artık üçü de aynı sürekli katsayıdan besleniyor. Bu testler o sürekliliği
/// sabitler: gözle fark edilmesi zor, ama bir sıçrama geri gelirse burada yakalanır.
/// </summary>
public class DayNightTests
{
    [Theory]
    [InlineData(0.0, 1.0)]    // gece yarısı
    [InlineData(3.0, 1.0)]
    [InlineData(6.0, 1.0)]    // şafağın başı: hâlâ tam karanlık
    [InlineData(6.5, 0.5)]    // şafağın ortası
    [InlineData(7.0, 0.0)]    // gündüz başladı
    [InlineData(12.0, 0.0)]   // öğle
    [InlineData(19.0, 0.0)]   // alacakaranlığın başı: hâlâ tam aydınlık
    [InlineData(19.5, 0.5)]   // alacakaranlığın ortası
    [InlineData(20.0, 1.0)]   // gece başladı
    [InlineData(23.5, 1.0)]
    public void NightFactor_MatchesTheDayCycle(double hour, double expected)
    {
        Assert.Equal(expected, DayNight.NightFactorAt(hour), precision: 3);
    }

    /// <summary>
    /// ASIL GARANTİ: gün boyunca hiçbir noktada sıçrama olmamalı. Sert eşik geri gelirse (ör.
    /// biri farları yeniden `saat >= 20` ile bağlarsa) bu test kırılır.
    /// </summary>
    [Fact]
    public void NightFactor_IsContinuousAcrossTheWholeDay()
    {
        const double step = 1.0 / 60.0;      // birer dakika
        double previous = DayNight.NightFactorAt(0);
        double worstJump = 0;
        double worstAt = 0;

        for (double t = step; t <= 24.0; t += step)
        {
            double current = DayNight.NightFactorAt(t);
            double jump = Math.Abs(current - previous);
            if (jump > worstJump) { worstJump = jump; worstAt = t; }
            previous = current;
        }

        // Bir dakikada en fazla 1/60 değişebilir (geçişler bir saat sürüyor); tolerans payıyla.
        Assert.True(worstJump <= 0.02,
            $"gün içinde sıçrama var: saat {worstAt:0.00}'de {worstJump:0.###} kadar zıpladı");
    }

    [Fact]
    public void NightFactor_RampsMonotonically()
    {
        // Alacakaranlık boyunca sürekli artmalı
        for (double t = DayNight.DuskStart; t < DayNight.NightStart - 0.01; t += 0.05)
            Assert.True(DayNight.NightFactorAt(t + 0.05) >= DayNight.NightFactorAt(t),
                $"alacakaranlıkta karanlık azalmış: saat {t:0.00}");

        // Şafak boyunca sürekli azalmalı
        for (double t = DayNight.DawnStart; t < DayNight.DayStart - 0.01; t += 0.05)
            Assert.True(DayNight.NightFactorAt(t + 0.05) <= DayNight.NightFactorAt(t),
                $"şafakta karanlık artmış: saat {t:0.00}");
    }

    [Fact]
    public void NightFactor_StaysWithinRange_ForAnyInput()
    {
        foreach (double t in new[] { -5.0, -0.5, 0.0, 13.7, 24.0, 25.5, 49.0 })
            Assert.InRange(DayNight.NightFactorAt(t), 0.0, 1.0);
    }

    /// <summary>Gündüz saatlerinde far/gece katmanı hiç çizilmemeli; alacakaranlık başlar
    /// başlamaz devreye girmeli.</summary>
    [Fact]
    public void IsDarkEnough_TurnsOnAsDuskBegins_NotAtAFixedHour()
    {
        Assert.False(DayNight.IsDarkEnough(DayNight.NightFactorAt(12.0)), "öğlen karanlık sayılmamalı");
        Assert.False(DayNight.IsDarkEnough(DayNight.NightFactorAt(19.0)), "alacakaranlığın ilk anı henüz karanlık değil");
        Assert.True(DayNight.IsDarkEnough(DayNight.NightFactorAt(19.2)), "alacakaranlık ilerledikçe devreye girmeli");
        Assert.True(DayNight.IsDarkEnough(DayNight.NightFactorAt(22.0)), "gece karanlık sayılmalı");
    }
}
