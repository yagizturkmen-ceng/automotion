using AutoMotion.Models;

namespace AutoMotion.Tests;

public class ObstacleTests
{
    [Fact]
    public void TrafficLight_StartsRed_AndCyclesToGreenThenYellowThenRed()
    {
        var light = new Obstacle(0, 0, "traffic_light");
        Assert.Equal("red", light.State);
        Assert.Equal(180, light.Timer);

        for (int i = 0; i < 180; i++) light.Update();
        Assert.Equal("green", light.State);
        Assert.Equal(240, light.Timer);

        for (int i = 0; i < 240; i++) light.Update();
        Assert.Equal("yellow", light.State);
        Assert.Equal(60, light.Timer);

        for (int i = 0; i < 60; i++) light.Update();
        Assert.Equal("red", light.State);
        Assert.Equal(180, light.Timer);
    }

    [Fact]
    public void NonTrafficLight_Update_DoesNothing()
    {
        var package = new Obstacle(0, 0, "cargo_package");
        Assert.Equal("idle", package.State);

        package.Update();

        Assert.Equal("idle", package.State);
    }

    [Theory]
    [InlineData("cargo_package", "idle", "📦")]
    [InlineData("delivery_point", "", "🏁")]
    [InlineData("station", "idle", "⛽")]
    [InlineData("cone", "idle", "🚧")]
    [InlineData("police", "red", "🚓")]
    public void Constructor_InitializesExpectedStateAndEmoji(string type, string expectedState, string expectedEmoji)
    {
        var obstacle = new Obstacle(0, 0, type);

        Assert.Equal(expectedState, obstacle.State);
        Assert.Equal(expectedEmoji, obstacle.Emoji);
    }

    [Fact]
    public void IsPointInside_TrueWithinRadius_FalseOutside()
    {
        var obstacle = new Obstacle(100, 100, "cone"); // Width=Height=22

        Assert.True(obstacle.IsPointInside(105, 105));
        Assert.False(obstacle.IsPointInside(500, 500));
    }

    [Fact]
    public void Serialize_Deserialize_RoundTripsStateAndTimer()
    {
        var original = new Obstacle(10, 20, "traffic_light");
        original.Update(); // Timer'ı varsayılandan değiştir (179)

        var data = original.Serialize();

        var restored = new Obstacle(10, 20, "traffic_light");
        restored.Deserialize(data);

        Assert.Equal(original.State, restored.State);
        Assert.Equal(original.Timer, restored.Timer);
        Assert.Equal(10, data.X);
        Assert.Equal(20, data.Y);
        Assert.Equal("traffic_light", data.Type);
    }
}
