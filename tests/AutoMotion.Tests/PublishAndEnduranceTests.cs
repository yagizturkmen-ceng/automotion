using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMotion.Models;
using AutoMotion.Services;

namespace AutoMotion.Tests;

/// <summary>
/// Yayın paketi yapılandırması (2.5) ve uzun süre açık kalma dayanıklılığı (2.4).
///
/// İkisi de "gözle bakılınca fark edilmeyen ama kullanıcıya patlayan" cinsten: yanlış publish
/// ayarı temiz makinede açılmayan bir yayın demek; sınırsız büyüyen bir koleksiyon ise masaüstünde
/// günlerce açık kalan bir uygulamada yavaş yavaş şişen bellek demek.
/// </summary>
public class PublishAndEnduranceTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AutoMotion", "Assets", "Lang")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    // ─────────────────────────────────────────────────────────── 2.5 · yayın paketi

    private static string Csproj => File.ReadAllText(Path.Combine(RepoRoot, "AutoMotion", "AutoMotion.csproj"));

    /// <summary>
    /// Paket .NET çalışma zamanını KENDİ İÇİNDE taşımalı. Bu ayarlar eskiden yalnızca elle verilen
    /// CLI bayraklarındaydı: düz `dotnet publish` çalıştıran biri çerçeveye bağımlı, temiz makinede
    /// "You must install .NET" hatasıyla açılmayan bir paket üretiyordu.
    /// </summary>
    [Fact]
    public void Publish_IsSelfContained()
    {
        var publishGroup = PublishPropertyGroup();

        Assert.Contains("<SelfContained>true</SelfContained>", publishGroup);
        Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", publishGroup);
    }

    /// <summary>Yayın ayarları normal derlemeyi etkilememeli - RuntimeIdentifier proje geneline
    /// konulursa çıktı klasörü kayar ve test projesinin beklentileri bozulur.</summary>
    [Fact]
    public void PublishSettings_AreScopedToPublishOnly()
    {
        Assert.Matches(@"<PropertyGroup\s+Condition\s*=\s*""'\$\(_IsPublishing\)'\s*==\s*'true'""", Csproj);

        // Koşulsuz bir RuntimeIdentifier tanımı olmamalı.
        var unconditional = Regex.Matches(Csproj, @"<PropertyGroup>(.*?)</PropertyGroup>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(g => g.Contains("<RuntimeIdentifier>"))
            .ToList();

        Assert.True(unconditional.Count == 0,
            "RuntimeIdentifier koşulsuz tanımlanmış - normal derleme çıktısını kaydırır.");
    }

    /// <summary>
    /// steam_appid.txt yayın paketine ASLA girmemeli: Steam üzerinden başlatılan sürümde bu dosyanın
    /// varlığı sahiplik kontrolünü atlatabilir. Eskiden bu yalnızca bir yorum satırıyla korunuyordu.
    /// </summary>
    [Fact]
    public void SteamAppIdFile_IsExcludedFromPublish()
    {
        var block = Regex.Match(Csproj, @"<Content\s+Include=""steam_appid\.txt"">(.*?)</Content>", RegexOptions.Singleline);

        Assert.True(block.Success, "steam_appid.txt Content tanımı bulunamadı.");
        Assert.Contains("<CopyToPublishDirectory>Never</CopyToPublishDirectory>", block.Groups[1].Value);
    }

    [Fact]
    public void Publish_DoesNotEmitDebugSymbols()
    {
        Assert.Contains("<DebugType>none</DebugType>", PublishPropertyGroup());
    }

    private static string PublishPropertyGroup()
    {
        var m = Regex.Match(Csproj,
            @"<PropertyGroup\s+Condition\s*=\s*""'\$\(_IsPublishing\)'\s*==\s*'true'"">(.*?)</PropertyGroup>",
            RegexOptions.Singleline);

        Assert.True(m.Success, "Yayına özel PropertyGroup bulunamadı.");
        return m.Groups[1].Value;
    }

    // ─────────────────────────────────────────────────────── 2.4 · uzun süre açık kalma

    /// <summary>
    /// Otomatik kayıt 60 saniyede bir çalışıyor - masaüstünde günlerce açık kalan bir uygulamada
    /// bu günde ~1440 yazma demek. Aynı isimle kaydetmek ESKİ dosyayı ve indeks girdisini
    /// temizlemezse kayıt klasörü sınırsız büyür (gerçek bir sızıntı vektörü: bir kez 231 yetim
    /// dosya birikmişti, bkz. SaveManager sınıf notu).
    /// </summary>
    [Fact]
    public void RepeatedAutoSave_DoesNotAccumulateFilesOrIndexEntries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AutoMotionEndurance_" + Guid.NewGuid());
        try
        {
            var manager = new SaveManager(dir);
            var core = new GameCore();
            core.AddAutoMotion(new Vehicle(10, 10, "car"));

            for (int i = 0; i < 300; i++)
                Assert.True(manager.Save("AutoSave", core.Save()));

            var saves = manager.GetAllSaves();
            Assert.Single(saves, s => s.Name == "AutoSave");

            // Klasörde tek bir kayıt dosyası + indeks kalmalı; 300 yazma 300 dosya bırakmamalı.
            int saveFiles = Directory.GetFiles(dir, "*.automotion").Length;
            Assert.True(saveFiles == 1, $"300 otomatik kayıt sonrası {saveFiles} dosya birikmiş (1 olmalı).");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Oyun döngüsü uzun süre koştuğunda motorun koleksiyonları büyümemeli. 20.000 tik ≈ 144 FPS'te
    /// ~2.5 dakika, 60 FPS'te ~5.5 dakika sürekli simülasyon - sınırsız büyüyen bir liste burada
    /// belirginleşir.
    /// </summary>
    [Fact]
    public void LongSimulationRun_KeepsEngineCollectionsBounded()
    {
        var core = new GameCore();
        for (int i = 0; i < 5; i++) core.AddAutoMotion(new Vehicle(100 + i * 40, 300, "car"));

        int vehiclesAtStart = core.AutoMotions.Count;

        for (int tick = 0; tick < 20_000; tick++)
            core.Update(1200, 800);

        Assert.Equal(vehiclesAtStart, core.AutoMotions.Count);
        Assert.True(core.Obstacles.Count <= 64, $"engel listesi büyümüş: {core.Obstacles.Count}");
        Assert.True(core.Items.Count <= 64, $"eşya listesi büyümüş: {core.Items.Count}");
        Assert.True(core.TouchEvents.Count <= 64, $"dokunma olayları birikmiş: {core.TouchEvents.Count}");
    }

    /// <summary>Kazanılan başarımlar tekrar tekrar eklenmemeli - her tikte listeye yeniden yazılsaydı
    /// uzun koşuda hem bellek hem kayıt dosyası şişerdi.</summary>
    [Fact]
    public void LongSimulationRun_DoesNotDuplicateUnlockedAchievements()
    {
        var core = new GameCore();
        core.AddAutoMotion(new Vehicle(100, 300, "car"));

        for (int tick = 0; tick < 5_000; tick++)
            core.Update(1200, 800);

        var unlocked = core.GameData.UnlockedAchievements;
        Assert.Equal(unlocked.Distinct().Count(), unlocked.Count);
        Assert.True(unlocked.Count <= GameCore.AchievementCatalog.Count,
            $"kazanılan başarım sayısı katalogdan fazla: {unlocked.Count}");
    }
}
