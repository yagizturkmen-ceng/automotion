using System;
using AutoMotion.Models;

namespace AutoMotion.Tests;

/// <summary>
/// Marka-nötr adlandırmaya geçişin testleri.
///
/// Görseller kullanıcının kendi özgün çizimleri, ancak dosya adları ve arayüz etiketleri gerçek
/// üretici markalarına atıfta bulunuyordu ("car_bmw.png", "Mercedes C-Serisi"). Steam'de bu bir
/// kaldırılma (DMCA) riski olduğundan adlandırma nötrleştirildi. Vehicle.Brand kayıt dosyalarında
/// saklandığı için eski kayıtların bozulmaması kritik.
/// </summary>
public class VehicleVisualsTests
{
    [Theory]
    [InlineData("car", "sedan")]
    [InlineData("car", "sport")]
    [InlineData("car", "luxury")]
    [InlineData("truck", "hauler")]
    [InlineData("truck", "rig")]
    [InlineData("truck", "freight")]
    [InlineData("motorcycle", "sport")]
    [InlineData("motorcycle", "race")]
    [InlineData("bus", "coach")]
    [InlineData("bus", "intercity")]
    [InlineData("bus", "midi")]
    [InlineData("tractor", "farm")]
    public void CurrentVariants_ResolveToAVisual(string type, string variant)
    {
        var visual = VehicleVisuals.Get(type, variant);

        Assert.NotNull(visual);
        Assert.Equal($"/Assets/Vehicles/{type}/{type}_{variant}.png", visual.ImagePath);
        Assert.False(string.IsNullOrWhiteSpace(visual.Label));
    }

    /// <summary>Hiçbir varyant anahtarı, etiketi veya görsel yolu gerçek bir üretici markası
    /// içermemeli - telif/marka riskinin geri sızmasına karşı koruma.</summary>
    [Theory]
    [InlineData("car", "sedan")]
    [InlineData("car", "sport")]
    [InlineData("car", "luxury")]
    [InlineData("truck", "hauler")]
    [InlineData("truck", "rig")]
    [InlineData("truck", "freight")]
    [InlineData("motorcycle", "sport")]
    [InlineData("motorcycle", "race")]
    [InlineData("bus", "coach")]
    [InlineData("bus", "intercity")]
    [InlineData("bus", "midi")]
    [InlineData("tractor", "farm")]
    public void Variants_ContainNoManufacturerBrandNames(string type, string variant)
    {
        string[] brands = { "audi", "bmw", "mercedes", "scania", "volvo", "neoplan",
                            "iveco", "kawasaki", "honda", "newholland", "new holland",
                            "travego", "tourliner", "fireblade", "ninja", "tgx" };

        var visual = VehicleVisuals.Get(type, variant);
        string haystack = $"{visual.Brand} {visual.Label} {visual.ImagePath}".ToLowerInvariant();

        foreach (var brand in brands)
            Assert.False(haystack.Contains(brand), $"'{brand}' marka adı hâlâ geçiyor: {haystack}");
    }

    /// <summary>REGRESYON: marka adlı ESKİ anahtarlar taşıyan kayıtlar hâlâ doğru görsele
    /// çözümlenmeli - aksi halde eski kaydını açan oyuncunun araçları görselini kaybedip jenerik
    /// emoji çizimine düşerdi.</summary>
    [Theory]
    [InlineData("car", "audi", "sedan")]
    [InlineData("car", "bmw", "sport")]
    [InlineData("car", "mercedes", "luxury")]
    [InlineData("truck", "man", "hauler")]
    [InlineData("truck", "scania", "rig")]
    [InlineData("truck", "volvo", "freight")]
    [InlineData("motorcycle", "honda", "sport")]
    [InlineData("motorcycle", "kawasaki", "race")]
    [InlineData("bus", "mercedes", "coach")]
    [InlineData("bus", "neoplan", "intercity")]
    [InlineData("bus", "iveco", "midi")]
    [InlineData("tractor", "newholland", "farm")]
    public void LegacyBrandKeys_StillResolve_AndMigrate(string type, string legacy, string expected)
    {
        Assert.Equal(expected, VehicleVisuals.MigrateBrandKey(type, legacy));

        var visual = VehicleVisuals.Get(type, legacy);
        Assert.NotNull(visual);
        Assert.Equal(expected, visual.Brand);
    }

    /// <summary>"mercedes" hem otomobilde hem otobüste vardı - migrasyon tipe göre farklı
    /// sonuç vermeli, yoksa eski otobüs kayıtları otomobil görseline düşerdi.</summary>
    [Fact]
    public void LegacyMigration_IsTypeAware_ForSharedBrandKey()
    {
        Assert.Equal("luxury", VehicleVisuals.MigrateBrandKey("car", "mercedes"));
        Assert.Equal("coach", VehicleVisuals.MigrateBrandKey("bus", "mercedes"));
    }

    [Fact]
    public void MigrateBrandKey_LeavesUnknownAndEmptyKeysUntouched()
    {
        Assert.Equal("", VehicleVisuals.MigrateBrandKey("car", ""));
        Assert.Equal("ozel_gorsel", VehicleVisuals.MigrateBrandKey("car", "ozel_gorsel"));
        Assert.Equal("sedan", VehicleVisuals.MigrateBrandKey("car", "sedan")); // zaten güncel
    }

    /// <summary>Eski bir kayıttan yüklenen araç, Brand alanını GÜNCEL anahtarla normalleştirmeli -
    /// böylece bir sonraki kaydetmede marka adı diske geri yazılmaz.</summary>
    [Fact]
    public void Deserialize_NormalizesLegacyBrandKey()
    {
        var vehicle = new Vehicle(0, 0, "truck");
        vehicle.Deserialize(new AutoMotionData { Type = "truck", Brand = "scania", Scale = 1.0 });

        Assert.Equal("rig", vehicle.Brand);
        Assert.Equal("Ağır Vasıta Çekici", vehicle.DisplayModel);
    }
}
