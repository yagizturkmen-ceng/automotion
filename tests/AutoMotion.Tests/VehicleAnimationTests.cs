using System;
using System.Collections.Generic;
using AutoMotion.Models;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Görsel canlılık animasyonlarının (rölanti titreşimi, süspansiyon, doğuş, sinyal lambası)
/// testleri.
///
/// Bu animasyonlar SADECE render katmanını etkiler - buradaki testlerin ortak sözleşmesi, hiçbirinin
/// fizik durumunu (konum/hız) değiştirmemesidir. Bir animasyonun yanlış ayarlanması oyunun
/// oynanışını asla bozmamalı.
/// </summary>
public class VehicleAnimationTests
{
    private static void Settle(Vehicle v, int frames, double floorY = 400)
    {
        for (int i = 0; i < frames; i++)
        {
            v.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => floorY);
        }
    }

    // ------------------------------------------------------------------ doğuş (spawn)

    [Fact]
    public void NewVehicle_StartsSpawnAnimationAtZero()
    {
        var vehicle = new Vehicle(100, 100, "car");

        Assert.Equal(0, vehicle.SpawnProgress);
    }

    [Fact]
    public void SpawnProgress_ReachesOne_AndStopsThere()
    {
        var vehicle = new Vehicle(100, 100, "car");

        Settle(vehicle, 60);

        Assert.Equal(1.0, vehicle.SpawnProgress);
    }

    [Fact]
    public void SkipSpawnAnimation_CompletesImmediately()
    {
        var vehicle = new Vehicle(100, 100, "car");

        vehicle.SkipSpawnAnimation();

        Assert.Equal(1.0, vehicle.SpawnProgress);
    }

    // ------------------------------------------------------------------ rölanti titreşimi

    [Fact]
    public void IdleShake_IsZeroForBike_WhichHasNoEngine()
    {
        var vehicle = new Vehicle(100, 100, "bike");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;

        Settle(vehicle, 240);

        Assert.False(vehicle.HasEngine);
        Assert.Equal(0, vehicle.IdleShakeOffset);
    }

    [Fact]
    public void IdleShake_BecomesNonZero_WhenEnginedVehicleRestsOnGround()
    {
        var vehicle = new Vehicle(600, 100, "car");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;

        // Zemine yerleşip rölanti yoğunluğunun yükselmesi için bol kare payı.
        // Titreşim bir sinüs olduğundan tek bir karede sıfırdan geçebilir - bu yüzden bir
        // pencere boyunca gözlenen EN BÜYÜK genlik kontrol edilir.
        Settle(vehicle, 300);

        double maxAbs = 0;
        for (int i = 0; i < 120; i++)
        {
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
            maxAbs = Math.Max(maxAbs, Math.Abs(vehicle.IdleShakeOffset));
        }

        Assert.True(vehicle.HasEngine);
        Assert.True(maxAbs > 0.05, $"Rölanti titreşimi beklenirken genlik {maxAbs:F4} kaldı.");
    }

    [Fact]
    public void IdleShake_IsZero_WhenOutOfFuel()
    {
        var vehicle = new Vehicle(600, 100, "car");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;
        vehicle.Fuel = 0; // motor çalışmıyor - titreşim de olmamalı

        Settle(vehicle, 400);

        Assert.Equal(0, vehicle.IdleShakeOffset, precision: 6);
    }

    [Fact]
    public void IdleShake_DoesNotMoveVehiclePhysicsPosition()
    {
        var vehicle = new Vehicle(600, 100, "truck");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;

        Settle(vehicle, 400);
        double restingY = vehicle.Y;

        Settle(vehicle, 120);

        // Titreşim yalnızca görsel bir ofset; fizik konumu sabit kalmalı.
        Assert.Equal(restingY, vehicle.Y, precision: 6);
    }

    // ------------------------------------------------------------------ süspansiyon

    [Fact]
    public void Suspension_CompressesOnLandingImpact_ThenSettlesBack()
    {
        var vehicle = new Vehicle(600, 50, "car");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;

        // Yüksekten düşüş: zemine çarpınca süspansiyon sıkışmalı.
        double maxOffset = 0;
        for (int i = 0; i < 120; i++)
        {
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
            maxOffset = Math.Max(maxOffset, Math.Abs(vehicle.SuspensionOffset));
        }

        Assert.True(maxOffset > 0.1, $"İnişte süspansiyon tepkisi beklenirken en fazla {maxOffset:F4} görüldü.");

        // Yay sönümlendikten sonra dinlenme konumuna (0'a yakın) dönmeli.
        Settle(vehicle, 400);
        Assert.True(Math.Abs(vehicle.SuspensionOffset) < 0.5,
            $"Süspansiyon dinlenmeye dönmeliydi, {vehicle.SuspensionOffset:F4} kaldı.");
    }

    [Fact]
    public void Suspension_StaysBounded_UnderRepeatedHardImpacts()
    {
        var vehicle = new Vehicle(600, 50, "truck");

        // Sürekli yere çarptır - yay sayısal olarak ıraksamamalı.
        for (int i = 0; i < 900; i++)
        {
            if (i % 30 == 0) vehicle.VelocityY = 18; // sert darbe
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);

            Assert.False(double.IsNaN(vehicle.SuspensionOffset));
            Assert.True(Math.Abs(vehicle.SuspensionOffset) <= vehicle.Height * 0.35 + 0.001);
        }
    }

    // ------------------------------------------------------------------ sinyal lambası

    [Fact]
    public void TriggerSignal_SetsDirection_AndClearsAfterHoldPeriod()
    {
        var vehicle = new Vehicle(600, 100, "car");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;
        Settle(vehicle, 200);

        vehicle.TriggerSignal(1);
        Settle(vehicle, 1);
        Assert.Equal(1, vehicle.SignalDirection);

        // Tutma süresi (~55 kare) dolduktan sonra sinyal kendiliğinden kapanmalı.
        Settle(vehicle, 120);
        Assert.Equal(0, vehicle.SignalDirection);
        Assert.False(vehicle.SignalLampOn);
    }

    [Fact]
    public void SignalLamp_Blinks_WhileActive()
    {
        var vehicle = new Vehicle(600, 100, "car");
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;
        Settle(vehicle, 200);

        vehicle.TriggerSignal(-1);

        bool sawOn = false, sawOff = false;
        for (int i = 0; i < 55; i++)
        {
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
            if (vehicle.SignalLampOn) sawOn = true; else sawOff = true;
        }

        Assert.True(sawOn && sawOff, "Sinyal lambası yanıp sönmeliydi (hem yanık hem sönük görülmeli).");
    }

    [Fact]
    public void TriggerSignal_WithZero_IsIgnored()
    {
        var vehicle = new Vehicle(600, 100, "car");

        vehicle.TriggerSignal(0);

        Assert.Equal(0, vehicle.SignalDirection);
    }

    // ------------------------------------------------------------------ hasar dumanı eşiği

    [Fact]
    public void IsSmoking_OnlyWhenSeverelyDamaged()
    {
        var vehicle = new Vehicle(100, 100, "car");

        Assert.False(vehicle.IsSmoking); // tam sağlıkta duman yok

        vehicle.HP = (int)(vehicle.MaxHP * 0.30);
        Assert.True(vehicle.IsSmoking);

        vehicle.HP = 0;
        Assert.False(vehicle.IsSmoking); // yok edilmiş araç için duman değil, çekici animasyonu var
    }

    // ------------------------------------------------------------------ kenar/gövde açısı (regresyon)

    /// <summary>
    /// REGRESYON: "Köşe Sıkışma Güvenlik Ağı" bir aracı duvardan zemine indirdiğinde gövde açısı da
    /// zemine (0°) dönmeli.
    ///
    /// Uygulama çalıştırılarak gözlendi: otomatik hareket kapalıyken duvarda hareketsiz kalan
    /// araçlar 8 sn sonra güvenlik ağıyla zemine indiriliyor, ama doğrudan "EdgeSide = 0" atandığı
    /// için gövde açısı duvardaki değerinde (±90°) / tavandaki değerinde (180°) DONUYORDU - otomobil
    /// zeminde baş aşağı, motosiklet yan yatmış duruyordu.
    /// </summary>
    [Fact]
    public void CornerStuckSafetyNet_AlsoResetsBodyAngleToGround()
    {
        // Aracı sol duvara yapıştır: sol kenara doğru sürükleyip duvara tırmanmasını sağla.
        var vehicle = new Vehicle(40, 380, "car");
        for (int i = 0; i < 400; i++)
        {
            vehicle.VelocityX = -3; // duvara doğru bastır
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
        }

        Assert.NotEqual(0, vehicle.EdgeSide); // gerçekten bir kenara tırmanmış olmalı
        Assert.True(Math.Abs(vehicle.WallAngleSmoothed) > 30,
            $"Duvarda gövde açısı beklenirken {vehicle.WallAngleSmoothed:F1}° bulundu.");

        // Şimdi hareketsiz bırak: güvenlik ağının devreye girmesi için 8+ saniye (60 FPS -> 480+ kare)
        for (int i = 0; i < 900; i++)
        {
            vehicle.VelocityX = 0;
            vehicle.VelocityY = 0;
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
        }

        Assert.Equal(0, vehicle.EdgeSide);
        Assert.True(Math.Abs(vehicle.WallAngleSmoothed) < 5,
            $"Zemine indikten sonra gövde açısı 0°'ye dönmeliydi, {vehicle.WallAngleSmoothed:F1}° kaldı (araç ters/yan görünür).");
    }

    /// <summary>REGRESYON: NaN kurtarma da gövde açısını sıfırlamalı - aksi halde kurtarılan araç
    /// ekranın ortasında ters/yan belirir.</summary>
    [Fact]
    public void NaNRecovery_AlsoResetsBodyAngleToGround()
    {
        var vehicle = new Vehicle(40, 380, "car");
        for (int i = 0; i < 400; i++)
        {
            vehicle.VelocityX = -3;
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);
        }
        Assert.True(Math.Abs(vehicle.WallAngleSmoothed) > 30);

        // Konumu boz -> bir sonraki tikte NaN kurtarma devreye girer.
        vehicle.X = double.NaN;
        vehicle.Update(0, new List<Vehicle>(), new List<Obstacle>(),
            canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);

        Assert.False(double.IsNaN(vehicle.X));
        Assert.Equal(0, vehicle.EdgeSide);

        // Açı geçişinin tamamlanması için birkaç kare daha
        for (int i = 0; i < 200; i++)
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => 400);

        Assert.True(Math.Abs(vehicle.WallAngleSmoothed) < 5,
            $"NaN kurtarmadan sonra gövde açısı 0°'ye dönmeliydi, {vehicle.WallAngleSmoothed:F1}° kaldı.");
    }

    // ------------------------------------------------------------------ Easing yardımcıları

    [Fact]
    public void Easing_OutBack_OvershootsBeforeSettling()
    {
        // Doğuş animasyonundaki "pop" hissi bu aşmadan gelir.
        double peak = 0;
        for (double t = 0; t <= 1.0; t += 0.01)
            peak = Math.Max(peak, Easing.OutBack(t));

        Assert.True(peak > 1.0, "OutBack hedefi aşmalıydı.");
        Assert.Equal(1.0, Easing.OutBack(1.0), precision: 6);
        Assert.Equal(0.0, Easing.OutBack(0.0), precision: 6);
    }

    [Fact]
    public void Easing_Damp_IsFrameRateIndependent()
    {
        // 60 FPS'te 2 kare ile 120 FPS'te 4 yarım-kare aynı sonucu vermeli.
        double a = 1.0 * Easing.Damp(0.9, 2.0);

        double b = 1.0;
        for (int i = 0; i < 4; i++) b *= Easing.Damp(0.9, 0.5);

        Assert.Equal(a, b, precision: 9);
    }

    [Fact]
    public void Easing_Spring_ConvergesToTarget()
    {
        double value = 10, velocity = 0;

        for (int i = 0; i < 2000; i++)
            Easing.Spring(ref value, ref velocity, target: 0, stiffness: 0.22, damping: 0.14, dt: 1.0);

        Assert.True(Math.Abs(value) < 0.01, $"Yay hedefe yakınsamalıydı, {value:F4} kaldı.");
    }

    [Fact]
    public void Easing_Spring_StaysStable_WithLargeDeltaTime()
    {
        // Uygulama bir an takılırsa (büyük dt) yay sayısal olarak patlamamalı.
        double value = 5, velocity = 0;

        for (int i = 0; i < 200; i++)
            Easing.Spring(ref value, ref velocity, target: 0, stiffness: 0.22, damping: 0.14, dt: 12.0);

        Assert.False(double.IsNaN(value));
        Assert.False(double.IsInfinity(value));
        Assert.True(Math.Abs(value) < 100);
    }

    [Fact]
    public void Easing_Clamp01_ClampsOutOfRangeInput()
    {
        Assert.Equal(0, Easing.Clamp01(-5));
        Assert.Equal(1, Easing.Clamp01(5));
        Assert.Equal(0.5, Easing.Clamp01(0.5));
    }
}
