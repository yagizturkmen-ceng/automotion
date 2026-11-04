using AutoMotion.Models;

namespace AutoMotion.Tests;

public class GameCoreTests
{
    [Fact]
    public void SaveLoad_RoundTrip_PreservesDirtAndFuelMultiplier()
    {
        // Regresyon testi: bu iki alan önceden GameSaveData'da hiç yoktu, kayıt/yükleme sonrası
        // sessizce varsayılan 1.0x'e dönüyordu (bkz. Models/TouchArea.cs GameSaveData).
        var core = new GameCore { DirtMultiplier = 2.5, FuelMultiplier = 0.5 };

        var loaded = new GameCore();
        loaded.Load(core.Save());

        Assert.Equal(2.5, loaded.DirtMultiplier);
        Assert.Equal(0.5, loaded.FuelMultiplier);
    }

    [Fact]
    public void AddAutoMotion_TracksTotalVehiclesAndTypeCounts()
    {
        var core = new GameCore();
        core.AddAutoMotion(new Vehicle(0, 0, "car"));
        core.AddAutoMotion(new Vehicle(0, 0, "car"));
        core.AddAutoMotion(new Vehicle(0, 0, "truck"));

        Assert.Equal(3, core.AutoMotions.Count);
        Assert.Equal(3, core.GameData.TotalVehicles);
        Assert.Equal(2, core.GameData.VehicleTypeCounts["car"]);
        Assert.Equal(1, core.GameData.VehicleTypeCounts["truck"]);
    }

    [Fact]
    public void AddMoney_TracksTotalEarnings_OnlyForPositiveAmounts()
    {
        var core = new GameCore();
        int startingMoney = core.GameData.Money;

        core.AddMoney(500);
        core.AddMoney(-200); // harcama - kazanç sayılmamalı

        Assert.Equal(startingMoney + 300, core.GameData.Money);
        Assert.Equal(500, core.GameData.TotalEarnings);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesVehiclePositionTypeAndMoney()
    {
        var core = new GameCore();
        core.AddMoney(250);
        core.AddAutoMotion(new Vehicle(123, 456, "bus"));

        var loaded = new GameCore();
        loaded.Load(core.Save());

        var vehicle = Assert.Single(loaded.AutoMotions);
        Assert.Equal("bus", vehicle.Type);
        Assert.Equal(123, vehicle.X, precision: 3);
        Assert.Equal(456, vehicle.Y, precision: 3);
        Assert.Equal(core.GameData.Money, loaded.GameData.Money);
    }
}
