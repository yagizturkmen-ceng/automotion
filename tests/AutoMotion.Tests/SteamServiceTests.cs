using System;
using System.Collections.Generic;
using System.Linq;
using AutoMotion.Models;
using AutoMotion.Services;

namespace AutoMotion.Tests;

/// <summary>
/// Steam entegrasyonu testleri.
///
/// Buradaki en önemli sözleşme: <b>Steam yokken oyun birebir aynı çalışmalı.</b> Geliştirme
/// sırasında, oyun Steam dışından başlatıldığında veya istemci kapalıyken Steam API'si
/// kullanılamaz - ve bu NORMAL bir durumdur, hata değil. Bu yüzden testler somut Steamworks
/// çağrılarını değil, o durumda devreye giren boş köprüyü ve köprü soyutlamasının doğru
/// kullanıldığını doğrular.
/// </summary>
public class SteamServiceTests : IDisposable
{
    /// <summary>Yapılan çağrıları kaydeden sahte köprü.</summary>
    private sealed class RecordingBridge : ISteamBridge
    {
        public bool IsAvailable { get; set; } = true;
        public string PlayerName => "TestPlayer";

        public List<string> Achievements { get; } = new();
        public Dictionary<string, int> StatValues { get; } = new();
        public Dictionary<string, string> RichPresence { get; } = new();
        public int CallbackCount { get; private set; }
        public bool WasShutdown { get; private set; }

        public void UnlockAchievement(string achievementId) => Achievements.Add(achievementId);
        public void SetStat(string statName, int value) => StatValues[statName] = value;
        public void SetRichPresence(string key, string value) => RichPresence[key] = value;
        public void RunCallbacks() => CallbackCount++;
        public void Shutdown() => WasShutdown = true;
    }

    public void Dispose() => SteamService.SetBridgeForTest(null);

    // ------------------------------------------------------------------ Steam yokken davranış

    [Fact]
    public void WhenSteamIsUnavailable_FallsBackToNullBridge_WithoutThrowing()
    {
        SteamService.Initialize(() => null);

        Assert.False(SteamService.IsAvailable);
        Assert.NotNull(SteamService.Bridge);
        Assert.IsType<NullSteamBridge>(SteamService.Bridge);
    }

    /// <summary>Köprü oluşturucu PATLASA bile (ör. steam_api64.dll yok -> DllNotFoundException)
    /// uygulama açılmaya devam etmeli.</summary>
    [Fact]
    public void WhenBridgeFactoryThrows_InitializeSwallowsAndDegrades()
    {
        var ex = Record.Exception(() =>
            SteamService.Initialize(() => throw new DllNotFoundException("steam_api64.dll")));

        Assert.Null(ex);
        Assert.False(SteamService.IsAvailable);
        Assert.IsType<NullSteamBridge>(SteamService.Bridge);
        Assert.Contains("steam_api64", SteamService.LastStatus);
    }

    /// <summary>Boş köprüye yapılan HİÇBİR çağrı hata vermemeli - oyun kodu Steam'in olup
    /// olmadığını kontrol etmek zorunda kalmasın.</summary>
    [Fact]
    public void NullBridge_AcceptsEveryCallSilently()
    {
        var bridge = new NullSteamBridge();

        var ex = Record.Exception(() =>
        {
            bridge.UnlockAchievement("first_vehicle");
            bridge.SetStat("total_vehicles", 5);
            bridge.SetRichPresence("steam_display", "#Status_Fleet");
            bridge.RunCallbacks();
            bridge.Shutdown();
        });

        Assert.Null(ex);
        Assert.False(bridge.IsAvailable);
        Assert.Equal("", bridge.PlayerName);
    }

    [Fact]
    public void WhenBridgeReportsUnavailable_ItIsNotUsed()
    {
        var unavailable = new RecordingBridge { IsAvailable = false };

        SteamService.Initialize(() => unavailable);

        Assert.False(SteamService.IsAvailable);
        Assert.IsType<NullSteamBridge>(SteamService.Bridge);
    }

    // ------------------------------------------------------------------ Steam varken davranış

    [Fact]
    public void WhenSteamIsAvailable_BridgeIsUsed()
    {
        var bridge = new RecordingBridge();

        SteamService.Initialize(() => bridge);

        Assert.True(SteamService.IsAvailable);
        Assert.Same(bridge, SteamService.Bridge);
        Assert.Equal("TestPlayer", SteamService.Bridge.PlayerName);
    }

    [Fact]
    public void Shutdown_ClosesBridge_AndRevertsToNull()
    {
        var bridge = new RecordingBridge();
        SteamService.Initialize(() => bridge);

        SteamService.Shutdown();

        Assert.True(bridge.WasShutdown);
        Assert.False(SteamService.IsAvailable);
        Assert.IsType<NullSteamBridge>(SteamService.Bridge);
    }

    [Fact]
    public void UpdateRichPresence_SetsFleetKeysAndDisplayToken()
    {
        var bridge = new RecordingBridge();
        SteamService.Initialize(() => bridge);

        SteamService.UpdateRichPresence(vehicleCount: 7, money: 2500);

        Assert.Equal("7", bridge.RichPresence["vehicles"]);
        Assert.Equal("2500", bridge.RichPresence["money"]);
        // steam_display, iş ortağı panelindeki yerelleştirme belirtecine karşılık gelmeli
        Assert.StartsWith("#", bridge.RichPresence["steam_display"]);
    }

    // ------------------------------------------------------------------ başarım adlandırması

    [Theory]
    [InlineData("first_vehicle", "ACH_FIRST_VEHICLE")]
    [InlineData("fleet_15", "ACH_FLEET_15")]
    [InlineData("master_mechanic", "ACH_MASTER_MECHANIC")]
    public void ToSteamAchievementName_UsesUppercaseAchPrefix(string id, string expected)
    {
        Assert.Equal(expected, SteamService.ToSteamAchievementName(id));
    }

    [Fact]
    public void ToSteamAchievementName_HandlesEmptyInput()
    {
        Assert.Equal("", SteamService.ToSteamAchievementName(null));
        Assert.Equal("", SteamService.ToSteamAchievementName("   "));
    }

    /// <summary>
    /// Oyundaki HER başarımın benzersiz, geçerli bir Steam API adı üretmesi gerekir. Bu ad iş
    /// ortağı panelinde elle tanımlanacağından, çakışma ya da geçersiz karakter yayın sonrası
    /// düzeltilmesi zor bir hataya dönüşür.
    /// </summary>
    [Fact]
    public void EveryGameAchievement_MapsToAUniqueValidSteamName()
    {
        var names = GameCore.AchievementCatalog
            .Select(a => SteamService.ToSteamAchievementName(a.Id))
            .ToList();

        Assert.Equal(GameCore.AchievementCatalog.Count, names.Distinct().Count());

        foreach (var name in names)
        {
            Assert.StartsWith("ACH_", name);
            Assert.All(name, ch => Assert.True(char.IsLetterOrDigit(ch) || ch == '_',
                $"Steam başarım adı geçersiz karakter içeriyor: {name}"));
        }
    }

    /// <summary>Başarım kimlikleri kayıt dosyalarında saklandığından (GameData.UnlockedAchievements)
    /// benzersiz olmalı - aksi halde iki başarım birbirini ezerdi.</summary>
    [Fact]
    public void AchievementIds_AreUnique()
    {
        var ids = GameCore.AchievementCatalog.Select(a => a.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>Her başarımın çeviri anahtarı olmalı - sabit metin OLMAMALI.</summary>
    [Fact]
    public void EveryAchievement_UsesATranslationKey()
    {
        foreach (var achievement in GameCore.AchievementCatalog)
        {
            Assert.StartsWith("achv.", achievement.LabelKey);
            Assert.DoesNotContain(" ", achievement.LabelKey);
        }
    }

    // ------------------------------------------------------------------ istatistik adları

    [Fact]
    public void StatNames_AreDistinctAndNonEmpty()
    {
        var stats = new[]
        {
            SteamService.Stats.TotalVehicles,
            SteamService.Stats.TotalEarnings,
            SteamService.Stats.TotalDistanceKm
        };

        Assert.All(stats, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(stats.Length, stats.Distinct().Count());
    }
}
