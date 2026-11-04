using System.Collections.Generic;
using AutoMotion.Models;

namespace AutoMotion.Tests;

/// <summary>
/// Araç durum geri bildirimi (kalan işler 3.4 / A-6).
///
/// Rozet eskiden yalnızca fare aracın üzerindeyken görünüyordu: araç yakıtsız kaldığı AN fark
/// edilmiyordu. Artık durum değiştiğinde kısa süre kendiliğinden beliriyor. Testler hem eşikleri
/// hem de "yalnızca kötüye giderken çıkar, düzelince sessizce gider" kuralını sabitliyor.
/// </summary>
public class MoodTests
{
    private static readonly List<Vehicle> NoOthers = new();
    private static readonly List<Obstacle> NoObstacles = new();

    // ---------------------------------------------------------------- sınıflandırma

    [Fact]
    public void HealthyVehicle_HasNoMood()
    {
        Assert.Equal(MoodState.Normal, Mood.Classify(happiness: 80, fuel: 90, maxFuel: 100, hp: 100, maxHp: 100));
    }

    [Fact]
    public void LowFuel_IsDetected()
    {
        Assert.Equal(MoodState.LowFuel, Mood.Classify(80, fuel: 10, maxFuel: 100, hp: 100, maxHp: 100));
    }

    [Fact]
    public void HeavyDamage_IsDetected()
    {
        Assert.Equal(MoodState.Hurt, Mood.Classify(80, 90, 100, hp: 20, maxHp: 100));
    }

    [Fact]
    public void LowHappiness_IsDetected()
    {
        Assert.Equal(MoodState.Sad, Mood.Classify(happiness: 10, fuel: 90, maxFuel: 100, hp: 100, maxHp: 100));
    }

    /// <summary>Aynı anda birden fazla sorun varsa kullanıcının HEMEN müdahale edebileceği
    /// (yakıt) öne çıkmalı - mutsuzluk zamanla oluşan bir birikim.</summary>
    [Fact]
    public void MostUrgentProblem_Wins()
    {
        Assert.Equal(MoodState.LowFuel, Mood.Classify(happiness: 5, fuel: 5, maxFuel: 100, hp: 10, maxHp: 100));
        Assert.Equal(MoodState.Hurt, Mood.Classify(happiness: 5, fuel: 90, maxFuel: 100, hp: 10, maxHp: 100));
    }

    [Fact]
    public void EveryProblemState_HasABadge()
    {
        Assert.NotNull(Mood.Emoji(MoodState.Sad));
        Assert.NotNull(Mood.Emoji(MoodState.LowFuel));
        Assert.NotNull(Mood.Emoji(MoodState.Hurt));
        Assert.Null(Mood.Emoji(MoodState.Normal)); // normalde rozet yok
    }

    [Fact]
    public void ZeroCapacities_DoNotFalselyTriggerAMood()
    {
        // Bölme/oran hesapları sıfır kapasitede yanlış alarm vermemeli.
        Assert.Equal(MoodState.Normal, Mood.Classify(80, fuel: 0, maxFuel: 0, hp: 0, maxHp: 0));
    }

    // ---------------------------------------------------------------- geçiş davranışı

    private static Vehicle Step(Vehicle v, int frames)
    {
        for (int i = 0; i < frames; i++) v.Update(i, NoOthers, NoObstacles, 1200, 800);
        return v;
    }

    [Fact]
    public void EnteringABadState_ShowsTheBadge()
    {
        var v = new Vehicle(600, 700, "car");
        Step(v, 5);

        v.Fuel = 1; // yakıt bitti
        Step(v, 2);

        Assert.Equal(MoodState.LowFuel, v.CurrentMood);
        Assert.True(v.MoodPulse > 0.5, $"durum değişince rozet belirmeli, MoodPulse={v.MoodPulse:0.##}");
    }

    /// <summary>Rozet kalıcı olmamalı - birkaç saniye sonra solmalı.</summary>
    [Fact]
    public void Badge_FadesAwayOnItsOwn()
    {
        var v = new Vehicle(600, 700, "car");
        Step(v, 5);
        v.Fuel = 1;
        Step(v, 2);

        Step(v, 200); // ~3.3 saniye

        Assert.Equal(0, v.MoodPulse);
        Assert.Equal(MoodState.LowFuel, v.CurrentMood); // durum sürüyor ama rozet solmuş
    }

    /// <summary>
    /// Rozet, araç SÜRÜKLENİRKEN de solmalı. Durum güncellemeleri bir zamanlar sürükleme dalının
    /// `else` tarafındaydı: kullanıcı aracı tutunca rozet donup ekranda asılı kalıyordu.
    /// </summary>
    [Fact]
    public void Badge_KeepsFading_WhileTheVehicleIsDragged()
    {
        var v = new Vehicle(600, 700, "car");
        Step(v, 5);
        v.Fuel = 1;
        Step(v, 2);
        Assert.True(v.MoodPulse > 0.5);

        v.IsDragged = true;
        Step(v, 200);

        Assert.Equal(0, v.MoodPulse);
    }

    /// <summary>Düzelme sessiz olmalı: araç iyileştiğinde ekranda rozet patlaması olmamalı.</summary>
    [Fact]
    public void RecoveringToNormal_DoesNotShowABadge()
    {
        var v = new Vehicle(600, 700, "car");
        Step(v, 5);
        v.Fuel = 1;
        Step(v, 2);
        Step(v, 200); // rozet solsun

        v.Fuel = v.MaxFuel; // yakıt dolduruldu
        Step(v, 2);

        Assert.Equal(MoodState.Normal, v.CurrentMood);
        Assert.Equal(0, v.MoodPulse);
    }
}
