using AutoMotion.Models;

namespace AutoMotion.Tests;

public class ItemTests
{
    [Fact]
    public void Fuel_Use_FillsUpToMaxFuel_AndReturnsFalseWhenAlreadyFull()
    {
        var vehicle = new Vehicle(0, 0, "car") { Fuel = 10, MaxFuel = 100 };
        var fuel = new Fuel("regular"); // 50 birim

        bool used = fuel.Use(vehicle);

        Assert.True(used);
        Assert.Equal(60, vehicle.Fuel);

        vehicle.Fuel = vehicle.MaxFuel;
        Assert.False(fuel.Use(vehicle));
    }

    [Fact]
    public void Fuel_Use_ClampsAtMaxFuel_WhenAmountWouldOverflow()
    {
        var vehicle = new Vehicle(0, 0, "car") { Fuel = 90, MaxFuel = 100 };
        var fuel = new Fuel("turbo"); // 150 birim

        fuel.Use(vehicle);

        Assert.Equal(100, vehicle.Fuel);
    }

    [Fact]
    public void RepairKit_Use_HealsUpToMaxHP_AndReturnsFalseWhenAlreadyFull()
    {
        var vehicle = new Vehicle(0, 0, "car") { HP = 50, MaxHP = 100 };
        var kit = new RepairKit("standard"); // 30 HP

        Assert.True(kit.Use(vehicle));
        Assert.Equal(80, vehicle.HP);

        vehicle.HP = vehicle.MaxHP;
        Assert.False(kit.Use(vehicle));
    }

    [Fact]
    public void RepairKit_Use_ResetsTireWear_EvenWhenHPAlreadyFull()
    {
        var vehicle = new Vehicle(0, 0, "car") { TireWear = 40 };
        vehicle.HP = vehicle.MaxHP;
        var kit = new RepairKit("standard");

        bool used = kit.Use(vehicle);

        Assert.True(used);
        Assert.Equal(0, vehicle.TireWear);
    }

    [Fact]
    public void RepairKit_Use_ReturnsFalse_WhenHPFullAndNoTireWear()
    {
        var vehicle = new Vehicle(0, 0, "car") { TireWear = 0 };
        vehicle.HP = vehicle.MaxHP;
        var kit = new RepairKit("standard");

        Assert.False(kit.Use(vehicle));
    }

    [Fact]
    public void Upgrade_Use_AppliesEffectOnce_AndRejectsDuplicate()
    {
        var vehicle = new Vehicle(0, 0, "car");
        double originalMaxSpeed = vehicle.MaxSpeed;
        var upgrade = new Upgrade("speed");

        bool firstUse = upgrade.Use(vehicle);
        bool secondUse = upgrade.Use(vehicle);

        Assert.True(firstUse);
        Assert.False(secondUse);
        Assert.Contains("speed", vehicle.Upgrades);
        Assert.Equal(originalMaxSpeed * 1.2, vehicle.MaxSpeed, precision: 3);
    }

    [Fact]
    public void Upgrade_Efficiency_ReducesFuelConsumption()
    {
        var vehicle = new Vehicle(0, 0, "car");
        double originalConsumption = vehicle.FuelConsumption;
        var upgrade = new Upgrade("efficiency");

        upgrade.Use(vehicle);

        Assert.Equal(originalConsumption * 0.75, vehicle.FuelConsumption, precision: 3);
    }

    [Fact]
    public void Upgrade_Durability_IncreasesMaxHP()
    {
        var vehicle = new Vehicle(0, 0, "car");
        int originalMaxHP = vehicle.MaxHP;
        var upgrade = new Upgrade("durability");

        upgrade.Use(vehicle);

        Assert.Equal((int)(originalMaxHP * 1.3), vehicle.MaxHP);
    }

    [Fact]
    public void Decoration_Use_AddsOnce_AndRejectsDuplicate()
    {
        var vehicle = new Vehicle(0, 0, "car");
        var decoration = new Decoration("sticker");

        bool firstUse = decoration.Use(vehicle);
        bool secondUse = decoration.Use(vehicle);

        Assert.True(firstUse);
        Assert.False(secondUse);
        Assert.Contains("sticker", vehicle.Decorations);
    }

    [Theory]
    [InlineData("fuel", "regular", typeof(Fuel))]
    [InlineData("repair", "advanced", typeof(RepairKit))]
    [InlineData("upgrade", "acceleration", typeof(Upgrade))]
    [InlineData("decoration", "spoiler", typeof(Decoration))]
    public void ItemFactory_CreateItem_ReturnsCorrectType(string itemType, string subType, Type expectedType)
    {
        var item = ItemFactory.CreateItem(itemType, subType);

        Assert.NotNull(item);
        Assert.IsType(expectedType, item);
    }

    [Fact]
    public void ItemFactory_CreateItem_ReturnsNull_ForUnknownType()
    {
        Assert.Null(ItemFactory.CreateItem("nonexistent"));
    }

    [Fact]
    public void Fuel_Serialize_RoundTripsSubType()
    {
        var fuel = new Fuel("premium");

        var data = fuel.Serialize();

        Assert.Equal("fuel", data.ItemType);
        Assert.Equal("premium", data.SubType);
    }
}
