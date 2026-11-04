using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoMotion.Services;

namespace AutoMotion.Tests;

// Not: SettingsService varsayılan yapıcıda gerçek %AppData%/AutoMotion klasörünü kullanır.
// Testler yalıtılmış geçici bir klasör alan yapıcı overload'ı üzerinden çalışır - kullanıcının
// gerçek ayar dosyasına dokunulmaz (SaveManagerTests ile aynı yaklaşım).
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AutoMotionSettingsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_tempDir, "settings.json");
    private string LegacyLanguagePath => Path.Combine(_tempDir, "language.txt");
    private string LegacyOnboardingPath => Path.Combine(_tempDir, "onboarding_shown.txt");
    private string LegacyPanelLayoutPath => Path.Combine(_tempDir, "panel_layout.json");

    // ---------------------------------------------------------------- temel okuma/yazma

    [Fact]
    public void NewInstall_UsesDefaults_AndWritesNothing()
    {
        using var settings = new SettingsService(_tempDir);

        Assert.Null(settings.Current.Language);
        Assert.False(settings.Current.OnboardingShown);
        Assert.Equal(1.0, settings.Current.DirtMultiplier);
        Assert.Equal(1.0, settings.Current.FuelMultiplier);
        Assert.Empty(settings.Current.PanelLayout);
        Assert.False(settings.MigratedFromLegacyFiles);

        // Varsayılanlar XAML'deki kontrol başlangıç değerleriyle aynı olmalı - aksi halde ilk
        // açılışta arayüz ile motor birbirinden ayrı düşerdi.
        Assert.Equal(0, settings.Current.ThemeIndex);
        Assert.False(settings.Current.Muted);
        Assert.Equal(0.8, settings.Current.MasterVolume);
        Assert.Equal(0.85, settings.Current.SfxVolume);
        Assert.False(settings.Current.LowEffectsMode);
        Assert.Equal(60, settings.Current.AutoSaveIntervalSeconds);

        // Taşınacak eski dosya yoksa açılışta diske yazma yapılmaz.
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save_ThenReload_RoundTripsAllPreferences()
    {
        using (var settings = new SettingsService(_tempDir))
        {
            settings.Current.Language = "en";
            settings.Current.OnboardingShown = true;
            settings.Current.DirtMultiplier = 2.5;
            settings.Current.FuelMultiplier = 0.5;
            settings.Current.PanelLayout["MasterPanel"] = new PanelLayoutEntry
            {
                Left = 120, Top = 340, Pinned = true, Scale = 1.3
            };

            Assert.True(settings.Save());
            Assert.Null(settings.LastError);
        }

        using var reloaded = new SettingsService(_tempDir);

        Assert.Equal("en", reloaded.Current.Language);
        Assert.True(reloaded.Current.OnboardingShown);
        Assert.Equal(2.5, reloaded.Current.DirtMultiplier);
        Assert.Equal(0.5, reloaded.Current.FuelMultiplier);

        var panel = reloaded.Current.PanelLayout["MasterPanel"];
        Assert.Equal(120, panel.Left);
        Assert.Equal(340, panel.Top);
        Assert.True(panel.Pinned);
        Assert.Equal(1.3, panel.Scale);
    }

    [Fact]
    public void Save_ThenReload_RoundTripsSessionPreferences()
    {
        // Tema, ses ve efekt tercihleri eskiden hiç kaydedilmiyordu; her açılışta sıfırlanıyorlardı.
        using (var settings = new SettingsService(_tempDir))
        {
            settings.Current.ThemeIndex = 6;
            settings.Current.Muted = true;
            settings.Current.MasterVolume = 0.35;
            settings.Current.SfxVolume = 0.1;
            settings.Current.LowEffectsMode = true;
            settings.Current.AutoSaveIntervalSeconds = 300;
            settings.Save();
        }

        using var reloaded = new SettingsService(_tempDir);

        Assert.Equal(6, reloaded.Current.ThemeIndex);
        Assert.True(reloaded.Current.Muted);
        Assert.Equal(0.35, reloaded.Current.MasterVolume);
        Assert.Equal(0.1, reloaded.Current.SfxVolume);
        Assert.True(reloaded.Current.LowEffectsMode);
        Assert.Equal(300, reloaded.Current.AutoSaveIntervalSeconds);
    }

    [Fact]
    public void Save_StampsCurrentSchemaVersion()
    {
        using var settings = new SettingsService(_tempDir);
        settings.Save();

        var written = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
        Assert.NotNull(written);

        Assert.Equal(AppSettings.CurrentSchemaVersion, written!.SchemaVersion);
    }

    [Fact]
    public void Save_OverwritesPreviousFile_WithoutLeavingTempFile()
    {
        using var settings = new SettingsService(_tempDir);
        settings.Current.Language = "tr";
        settings.Save();
        settings.Current.Language = "en";
        settings.Save();

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Equal("en", new SettingsService(_tempDir).Current.Language);
    }

    [Fact]
    public void SaveDeferred_ThenFlush_PersistsImmediately()
    {
        using var settings = new SettingsService(_tempDir);
        settings.Current.DirtMultiplier = 1.8;
        settings.SaveDeferred();

        settings.Flush();

        Assert.Equal(1.8, new SettingsService(_tempDir).Current.DirtMultiplier);
    }

    // ---------------------------------------------------------------- eski dosyalardan taşıma

    [Fact]
    public void Migration_MovesAllLegacyFilesIntoSingleSettingsFile()
    {
        File.WriteAllText(LegacyLanguagePath, "en\r\n");
        File.WriteAllText(LegacyOnboardingPath, DateTime.Now.ToString());
        File.WriteAllText(LegacyPanelLayoutPath, JsonSerializer.Serialize(
            new Dictionary<string, PanelLayoutEntry>
            {
                ["MasterPanel"] = new PanelLayoutEntry { Left = 42, Top = 84, Pinned = true, Scale = 0.9 }
            }));

        using var settings = new SettingsService(_tempDir);

        Assert.True(settings.MigratedFromLegacyFiles);
        Assert.Equal("en", settings.Current.Language);
        Assert.True(settings.Current.OnboardingShown);
        Assert.Equal(42, settings.Current.PanelLayout["MasterPanel"].Left);
        Assert.Equal(0.9, settings.Current.PanelLayout["MasterPanel"].Scale);

        // Taşınan tercihler yeni dosyaya yazılmış ve eskiler temizlenmiş olmalı.
        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(LegacyLanguagePath));
        Assert.False(File.Exists(LegacyOnboardingPath));
        Assert.False(File.Exists(LegacyPanelLayoutPath));
    }

    [Fact]
    public void Migration_SurvivesRestart()
    {
        File.WriteAllText(LegacyLanguagePath, "tr");
        using (var _ = new SettingsService(_tempDir)) { }

        using var afterRestart = new SettingsService(_tempDir);

        Assert.False(afterRestart.MigratedFromLegacyFiles); // ikinci açılışta taşınacak bir şey yok
        Assert.Equal("tr", afterRestart.Current.Language);
    }

    [Fact]
    public void Migration_IgnoresUnsupportedLanguageCode()
    {
        File.WriteAllText(LegacyLanguagePath, "kl"); // desteklenmeyen dil kodu

        using var settings = new SettingsService(_tempDir);

        Assert.Null(settings.Current.Language); // işletim sistemi diline düşülür
    }

    [Fact]
    public void Migration_ToleratesCorruptLegacyPanelLayout()
    {
        File.WriteAllText(LegacyLanguagePath, "tr");
        File.WriteAllText(LegacyPanelLayoutPath, "{ bu gecerli json degil");

        using var settings = new SettingsService(_tempDir);

        // Bozuk düzen dosyası dil tercihinin taşınmasını engellememeli.
        Assert.Equal("tr", settings.Current.Language);
        Assert.Empty(settings.Current.PanelLayout);
    }

    [Fact]
    public void Migration_DoesNotRunWhenSettingsFileAlreadyExists()
    {
        using (var seed = new SettingsService(_tempDir))
        {
            seed.Current.Language = "tr";
            seed.Save();
        }
        File.WriteAllText(LegacyLanguagePath, "en"); // sonradan ortaya çıkan eski dosya

        using var settings = new SettingsService(_tempDir);

        Assert.False(settings.MigratedFromLegacyFiles);
        Assert.Equal("tr", settings.Current.Language); // settings.json tek kaynak
    }

    // ---------------------------------------------------------------- bozuk dosya dayanıklılığı

    [Fact]
    public void CorruptSettingsFile_FallsBackToDefaults_AndIsQuarantined()
    {
        File.WriteAllText(SettingsPath, "{ yarim yazilmis dosya");

        using var settings = new SettingsService(_tempDir);

        Assert.Equal(1.0, settings.Current.DirtMultiplier);
        Assert.NotNull(settings.LastError);

        // Bozuk dosya silinmez, kenara alınır - kullanıcı elle kurtarabilsin.
        Assert.Contains(Directory.GetFiles(_tempDir),
            f => Path.GetFileName(f).StartsWith("settings.json.corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptSettingsFile_CanStillBeSavedOver()
    {
        File.WriteAllText(SettingsPath, "]bozuk[");

        using var settings = new SettingsService(_tempDir);
        settings.Current.Language = "en";

        Assert.True(settings.Save());
        Assert.Equal("en", new SettingsService(_tempDir).Current.Language);
    }

    [Fact]
    public void NullJsonContent_IsTreatedAsCorrupt()
    {
        File.WriteAllText(SettingsPath, "null");

        using var settings = new SettingsService(_tempDir);

        Assert.Equal(1.0, settings.Current.FuelMultiplier);
        Assert.Contains(Directory.GetFiles(_tempDir),
            f => Path.GetFileName(f).StartsWith("settings.json.corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingPanelLayoutField_DoesNotProduceNullDictionary()
    {
        // Eski/elle yazılmış bir ayar dosyasında alan hiç bulunmayabilir.
        File.WriteAllText(SettingsPath, "{\"Language\":\"tr\"}");

        using var settings = new SettingsService(_tempDir);

        Assert.NotNull(settings.Current.PanelLayout);
        Assert.Empty(settings.Current.PanelLayout);
    }

    [Fact]
    public void UnversionedSettingsFile_IsStampedWithCurrentSchemaVersion()
    {
        File.WriteAllText(SettingsPath, "{\"Language\":\"tr\"}"); // SchemaVersion = 0 (sürümleme öncesi)

        using var settings = new SettingsService(_tempDir);

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.Current.SchemaVersion);
        Assert.Equal("tr", settings.Current.Language);
    }
}
