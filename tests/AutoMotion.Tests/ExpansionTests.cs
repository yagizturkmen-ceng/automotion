using AutoMotion.Expansions;
using AutoMotion.Models;

namespace AutoMotion.Tests;

// Not: Traffic/Economy eklentileri DateTime.Now tabanlı zamanlayıcılar kullanır (bkz.
// SpawnInterval/PayoutInterval) ve zaman enjekte edilebilir değil. Bu yüzden testler saatin
// henüz dolmadığı ("hemen Enable sonrası") güvenli/deterministik davranışlara odaklanır;
// gerçek 5-10sn'lik bekleme gerektiren spawn/payout senaryoları burada test edilmez.
public class ExpansionTests
{
    [Fact]
    public void TrafficExpansion_Disabled_Update_DoesNotSpawnVehicles()
    {
        var core = new GameCore();
        var expansion = new TrafficExpansion();

        expansion.Tick(core, gameTime: 0, canvasWidth: 800, canvasHeight: 600);

        Assert.Empty(core.AutoMotions);
    }

    [Fact]
    public void TrafficExpansion_EnabledThenImmediateUpdate_DoesNotSpawnBeforeIntervalElapses()
    {
        var core = new GameCore();
        var expansion = new TrafficExpansion();

        expansion.Enable(core);
        expansion.Tick(core, gameTime: 0, canvasWidth: 800, canvasHeight: 600);

        Assert.Empty(core.AutoMotions);
    }

    [Fact]
    public void TrafficExpansion_Disable_IsIdempotentAndLeavesNoOrphanState()
    {
        var core = new GameCore();
        var expansion = new TrafficExpansion();

        expansion.Enable(core);
        expansion.Disable(core);
        expansion.Disable(core); // ikinci kapatma no-op olmalı, hata fırlatmamalı

        Assert.False(expansion.IsEnabled);
        Assert.Empty(core.AutoMotions);
    }

    [Fact]
    public void EconomyExpansion_EnabledThenImmediateUpdate_DoesNotPayoutBeforeIntervalElapses()
    {
        var core = new GameCore();
        core.AddAutoMotion(new Vehicle(0, 0, "car"));
        int startingMoney = core.GameData.Money;
        var expansion = new EconomyExpansion();

        expansion.Enable(core);
        expansion.Tick(core, gameTime: 0, canvasWidth: 800, canvasHeight: 600);

        Assert.Equal(startingMoney, core.GameData.Money);
    }

    [Fact]
    public void EconomyExpansion_Disabled_Update_DoesNotPayout()
    {
        var core = new GameCore();
        core.AddAutoMotion(new Vehicle(0, 0, "car"));
        int startingMoney = core.GameData.Money;
        var expansion = new EconomyExpansion();

        expansion.Tick(core, gameTime: 0, canvasWidth: 800, canvasHeight: 600);

        Assert.Equal(startingMoney, core.GameData.Money);
    }

    [Fact]
    public void Expansion_StatusIconAndLabel_ReflectEnabledState()
    {
        var core = new GameCore();
        var expansion = new EconomyExpansion();

        Assert.Equal("⚪", expansion.StatusIcon);
        Assert.Equal("Kapalı", expansion.StatusLabel);

        expansion.Enable(core);

        Assert.Equal("🟢", expansion.StatusIcon);
        Assert.Equal("Açık", expansion.StatusLabel);
    }
}
