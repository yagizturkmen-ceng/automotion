using System;
using System.Linq;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Başarım kutlaması kıvılcım patlaması (kalan işler 3.4 / C-1).
///
/// Patlama birkaç sayıdan ibaret ve yanlış olduklarında sonuç ya hiç görünmez ya da ekranı basan
/// bir gürültü olur - ikisi de ancak başarım kazanılarak fark edilirdi. Testler tasarım kararlarını
/// sabitliyor: yukarı açılan bir yelpaze, sınırlı sayıda, kutlama paletinde.
/// </summary>
public class CelebrationTests
{
    private static readonly Random Fixed = new(1234);

    [Fact]
    public void Burst_ProducesTheRequestedNumberOfSparks()
    {
        Assert.Equal(Celebration.BurstSize, Celebration.BuildBurst(new Random(1)).Count);
        Assert.Equal(5, Celebration.BuildBurst(new Random(1), 5).Count);
    }

    /// <summary>Kutlama YUKARI açılmalı - aşağı düşen kıvılcım kutlama gibi durmaz.</summary>
    [Fact]
    public void EverySpark_MovesUpward()
    {
        var burst = Celebration.BuildBurst(new Random(7), 40);

        Assert.All(burst, s => Assert.True(s.VelocityY < 0,
            $"kıvılcım aşağı gidiyor: VelocityY={s.VelocityY:0.##}"));
    }

    /// <summary>Yelpaze gerçekten AÇILMALI: hepsi tek noktaya gitmemeli, hem sola hem sağa
    /// savrulan kıvılcımlar olmalı.</summary>
    [Fact]
    public void Burst_SpreadsToBothSides()
    {
        var burst = Celebration.BuildBurst(new Random(3), 40);

        Assert.Contains(burst, s => s.VelocityX < -0.2);
        Assert.Contains(burst, s => s.VelocityX > 0.2);
    }

    [Fact]
    public void Sparks_HaveVisibleSizeAndSpeed()
    {
        foreach (var s in Celebration.BuildBurst(Fixed, 30))
        {
            Assert.InRange(s.Size, 2.0, 6.0);

            double speed = Math.Sqrt(s.VelocityX * s.VelocityX + s.VelocityY * s.VelocityY);
            Assert.InRange(speed, 1.0, 5.0); // görünmeyecek kadar yavaş da, fırlayacak kadar hızlı da değil
        }
    }

    [Fact]
    public void Sparks_UseTheCelebrationPalette()
    {
        Assert.All(Celebration.BuildBurst(Fixed, 30),
            s => Assert.Contains(s.Color, Celebration.Palette));
    }

    [Fact]
    public void Burst_HandlesDegenerateCounts()
    {
        Assert.Empty(Celebration.BuildBurst(Fixed, 0));
        Assert.Empty(Celebration.BuildBurst(Fixed, -5));
    }

    [Fact]
    public void Burst_RequiresARandomSource()
    {
        Assert.Throws<ArgumentNullException>(() => Celebration.BuildBurst(null!));
    }
}
