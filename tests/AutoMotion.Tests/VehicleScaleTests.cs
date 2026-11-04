using AutoMotion.Models;

namespace AutoMotion.Tests;

/// <summary>
/// Araç boyutlarının gerçek dünya oranlarını koruması.
///
/// Boyutlar tek bir ölçeğe dayanır (<see cref="Vehicle.PixelsPerMetre"/>). Gözle bakınca "büyük/
/// küçük" doğru görünse bile oranların kayması kolay: biri tabloda bir sayıyı elle değiştirdiğinde
/// otobüs kamyondan kısa kalabilir. Bu testler oranları sabitler.
/// </summary>
public class VehicleScaleTests
{
    /// <summary>Aracın gerçek dünya uzunluğu. Width, kullanıcı ölçeğini (Vehicle.Scale, varsayılan
    /// 2.0) da içerdiğinden ona bölünür - ölçek tüm araçlara aynı uygulandığı için oranları
    /// bozmaz, ama mutlak metre karşılaştırması için çıkarılmalı.</summary>
    private static double Metres(Vehicle v) => v.Width / (Vehicle.PixelsPerMetre * v.Scale);

    private static Vehicle Make(string type, string brand = "") => new(0, 0, type, brand);

    [Fact]
    public void TypeSizes_FollowRealWorldOrder()
    {
        double bike = Metres(Make("bike"));
        double motorcycle = Metres(Make("motorcycle"));
        double car = Metres(Make("car"));
        double truck = Metres(Make("truck"));
        double bus = Metres(Make("bus"));
        double train = Metres(Make("train"));

        Assert.True(bike < motorcycle, "bisiklet motosikletten kısa olmalı");
        Assert.True(motorcycle < car, "motosiklet otomobilden kısa olmalı");
        Assert.True(car < truck, "otomobil kamyondan kısa olmalı");
        Assert.True(truck < bus, "kamyon otobüsten kısa olmalı");
        Assert.True(bus < train, "otobüs trenden kısa olmalı");
    }

    /// <summary>Tip tablosu gerçek uzunluklara oturmalı: otomobil ~4.5 m, otobüs ~12 m.</summary>
    [Theory]
    [InlineData("car", 4.5)]
    [InlineData("truck", 8.0)]
    [InlineData("bus", 12.0)]
    [InlineData("bike", 1.8)]
    [InlineData("train", 20.0)]
    public void TypeSizes_MatchTheirRealLength(string type, double expectedMetres)
    {
        // Tolerans, boyutun tam sayı piksele yuvarlanmasından gelen sapma içindir.
        Assert.InRange(Metres(Make(type)), expectedMetres - 0.15, expectedMetres + 0.15);
    }

    /// <summary>
    /// Asıl düzeltme: aynı tipin farklı modelleri artık aynı boyda DEĞİL. Midibüs turizm
    /// otobüsünden belirgin kısa, lüks sedan spor kupeden uzun olmalı.
    /// </summary>
    [Fact]
    public void BrandedVehicles_DifferInSizeWithinTheSameType()
    {
        double coach = Metres(Make("bus", "coach"));
        double midi = Metres(Make("bus", "midi"));

        Assert.True(midi < coach * 0.8, $"midibüs turizm otobüsünden belirgin kısa olmalı: {midi:0.0} m / {coach:0.0} m");

        double luxury = Metres(Make("car", "luxury"));
        double sport = Metres(Make("car", "sport"));
        Assert.True(luxury > sport, $"lüks sedan spor kupeden uzun olmalı: {luxury:0.0} m / {sport:0.0} m");
    }

    [Theory]
    [InlineData("bus", "coach", 12.5)]
    [InlineData("bus", "midi", 8.5)]
    [InlineData("car", "sedan", 4.7)]
    [InlineData("truck", "rig", 6.5)]
    public void BrandedVehicles_UseTheirRealLength(string type, string brand, double expectedMetres)
    {
        Assert.InRange(Metres(Make(type, brand)), expectedMetres - 0.15, expectedMetres + 0.15);
    }

    /// <summary>Marka boyutu uygulanırken en/boy oranı korunmalı - aksi halde araç ezilmiş
    /// veya gereksiz yüksek görünürdü.</summary>
    [Fact]
    public void BrandSizing_PreservesAspectRatio()
    {
        var typeDefault = Make("bus");
        var midi = Make("bus", "midi");

        double typeAspect = typeDefault.Width / (double)typeDefault.Height;
        double midiAspect = midi.Width / (double)midi.Height;

        Assert.Equal(typeAspect, midiAspect, precision: 1);
    }

    /// <summary>Bilinmeyen marka, tipin varsayılan boyutunda kalmalı (görselsiz/emoji araçlar).</summary>
    [Fact]
    public void UnknownBrand_FallsBackToTypeSize()
    {
        Assert.Equal(Make("car").Width, Make("car", "boyle-bir-marka-yok").Width);
    }

    /// <summary>Dorseler de aynı gerçek-dünya ölçeğine oturmalı: açık yarı-römork, kasalı
    /// römorktan belirgin uzun; hepsi bir çekiciden (6.5 m) uzun olmalı ya da ona yakın.</summary>
    [Fact]
    public void TrailerLengths_FollowRealWorldOrder()
    {
        var t = Vehicle.TrailerRealLengthMetres;

        Assert.True(t["flatbed"] > t["tanker"], "açık dorse tankerden uzun olmalı");
        Assert.True(t["tanker"] > t["wagon"], "tanker kasalı römorktan uzun olmalı");
        Assert.True(t["wagon"] > t["equipment"], "kasalı römork ekipman römorkundan uzun olmalı");

        // Bir tırın toplam uzunluğu (çekici + dorse) gerçekçi aralıkta kalmalı.
        double cekici = Metres(Make("truck", "rig"));
        double toplam = cekici + t["flatbed"];
        Assert.InRange(toplam, 16.0, 22.0);
    }
}
