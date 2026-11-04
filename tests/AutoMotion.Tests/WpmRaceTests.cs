using AutoMotion.Models;

namespace AutoMotion.Tests;

public class WpmRaceTests
{
    [Fact]
    public void NewRace_IsNotActive()
    {
        var race = new WpmRace();
        Assert.False(race.IsActive);
    }

    [Fact]
    public void Start_ActivatesRace_AndResetsProgress()
    {
        var race = new WpmRace();

        race.Start();

        Assert.True(race.IsActive);
        Assert.Equal(0, race.PlayerProgress);
        Assert.Equal(0, race.RivalProgress);
    }

    [Fact]
    public void Cancel_DeactivatesRace()
    {
        var race = new WpmRace();
        race.Start();

        race.Cancel();

        Assert.False(race.IsActive);
    }

    [Fact]
    public void RegisterKeystroke_BeforeStart_IsIgnored()
    {
        var race = new WpmRace();

        race.RegisterKeystroke();

        Assert.Equal(0, race.CurrentWpm);
    }

    [Fact]
    public void Tick_WhenNotActive_ReturnsFalse()
    {
        var race = new WpmRace();

        bool completed = race.Tick(out bool didPlayerWin);

        Assert.False(completed);
        Assert.False(didPlayerWin);
    }

    [Fact]
    public void Tick_RivalProgressAdvancesOverTime_EvenWithoutKeystrokes()
    {
        var race = new WpmRace();
        race.Start();

        System.Threading.Thread.Sleep(50);
        race.Tick(out _);

        Assert.True(race.RivalProgress > 0);
        Assert.Equal(0, race.PlayerProgress); // hiç yazılmadıysa oyuncu ilerlemez
    }

    [Fact]
    public void RegisterKeystroke_WhileActive_IncreasesCurrentWpm()
    {
        var race = new WpmRace();
        race.Start();

        for (int i = 0; i < 10; i++) race.RegisterKeystroke();

        Assert.True(race.CurrentWpm > 0);
    }
}
