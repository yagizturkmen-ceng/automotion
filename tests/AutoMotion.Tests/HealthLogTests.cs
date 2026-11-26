using System;

namespace AutoMotion.Tests;

/// <summary>
/// Uzun koşu sağlık kaydının biçimi (Kalan İşler 2.4). Satırlar 24 saat boyunca elle okunacağı
/// için süre ve sayaçların kararlı, okunur bir düzende yazılması gerekiyor.
/// </summary>
public class HealthLogTests
{
    [Theory]
    [InlineData(0, 0, "0sa 00dk")]
    [InlineData(0, 30, "0sa 30dk")]
    [InlineData(5, 7, "5sa 07dk")]
    [InlineData(26, 0, "26sa 00dk")] // 24 saati aşan koşuda gün'e taşmamalı, saat artmaya devam etmeli
    public void FormatUptime_ShowsTotalHoursAndPaddedMinutes(int hours, int minutes, string expected)
    {
        Assert.Equal(expected, MainWindow.FormatUptime(new TimeSpan(hours, minutes, 0)));
    }

    [Fact]
    public void HealthSample_Format_ContainsEveryCounter()
    {
        var sample = new MainWindow.HealthSample
        {
            WorkingSetMb = 281, PrivateMb = 240, GcHeapMb = 37,
            Handles = 2065, Threads = 23, Vehicles = 5, Particles = 12
        };

        string line = sample.Format(TimeSpan.FromHours(3));

        foreach (var part in new[] { "3sa 00dk", "281 MB", "özel 240", "GC 37", "handle 2065", "thread 23", "araç 5", "parçacık 12" })
            Assert.Contains(part, line);
    }

    [Fact]
    public void HealthLogInterval_KeepsADayWellUnderTheLogSizeLimit()
    {
        // session.log 256 KB'ta sıfırlanıyor; 24 saatlik koşunun tamamı tek dosyada kalmalı.
        double linesPerDay = TimeSpan.FromHours(24) / MainWindow.HealthLogInterval;
        Assert.InRange(linesPerDay, 12, 200);
        Assert.True(linesPerDay * 200 < 256 * 1024);
    }
}
