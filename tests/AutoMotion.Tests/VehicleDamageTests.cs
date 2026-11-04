using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Kaporta hasar izlerinin kademelenmesi (kalan işler 3.4 / A-8).
///
/// "Kaç iz çizilecek" kararı eşiklere dayanıyor ve eşikler gözle ancak aracı tek tek hasarlayarak
/// kontrol edilebilirdi. Buradaki testler davranışı sabitliyor: sağlam araçta hiç iz yok, hasar
/// arttıkça iz sayısı artıyor, hurdada tavana oturuyor.
/// </summary>
public class VehicleDamageTests
{
    [Theory]
    [InlineData(1.0)]    // sapasağlam
    [InlineData(0.9)]
    [InlineData(0.7)]    // hafif hasar - henüz iz yok
    [InlineData(0.65)]   // eşiğin tam üstü
    public void HealthyVehicle_ShowsNoDamageMarks(double healthRatio)
    {
        Assert.Equal(0, VehicleDamage.MarkCount(healthRatio));
    }

    [Fact]
    public void DamageBeyondThreshold_StartsShowingMarks()
    {
        Assert.True(VehicleDamage.MarkCount(0.6) > 0, "eşiği geçen hasarda iz görünmeli");
    }

    [Fact]
    public void MoreDamage_MeansMoreMarks()
    {
        int light = VehicleDamage.MarkCount(0.6);
        int heavy = VehicleDamage.MarkCount(0.25);
        int wreck = VehicleDamage.MarkCount(0.0);

        Assert.True(light < heavy, $"ağır hasar daha çok iz bırakmalı: {light} → {heavy}");
        Assert.True(heavy <= wreck, $"hurda en az ağır hasar kadar iz taşımalı: {heavy} → {wreck}");
    }

    [Fact]
    public void MarkCount_NeverExceedsTheCap()
    {
        Assert.Equal(VehicleDamage.MaxMarks, VehicleDamage.MarkCount(0.0));
        Assert.InRange(VehicleDamage.MarkCount(-5), 0, VehicleDamage.MaxMarks);
    }

    /// <summary>Bozuk girdiler çizim döngüsünü patlatmamalı (negatif/aşırı/NaN oran).</summary>
    [Theory]
    [InlineData(2.0, 0)]
    [InlineData(-1.0, VehicleDamage.MaxMarks)]
    [InlineData(double.NaN, 0)]
    public void MarkCount_HandlesOutOfRangeInput(double healthRatio, int expected)
    {
        Assert.Equal(expected, VehicleDamage.MarkCount(healthRatio));
    }

    /// <summary>İz sayısı hasarla birlikte tekdüze artmalı - arada geri düşen bir nokta olursa
    /// araç onarılırken izler artıyormuş gibi görünürdü.</summary>
    [Fact]
    public void MarkCount_IncreasesMonotonicallyAsHealthDrops()
    {
        int previous = 0;
        for (double health = 1.0; health >= 0.0; health -= 0.02)
        {
            int marks = VehicleDamage.MarkCount(health);
            Assert.True(marks >= previous, $"can {health:0.00} iken iz sayısı azaldı: {previous} → {marks}");
            previous = marks;
        }
    }
}
