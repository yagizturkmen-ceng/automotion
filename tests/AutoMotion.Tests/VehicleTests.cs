using AutoMotion.Models;

namespace AutoMotion.Tests;

public class VehicleTests
{
    [Fact]
    public void NewVehicle_StartsWithFullHPAndSeventyPercentFuel()
    {
        var vehicle = new Vehicle(0, 0, "car");

        Assert.Equal(vehicle.MaxHP, vehicle.HP);
        Assert.Equal(vehicle.MaxFuel * 0.7, vehicle.Fuel, precision: 3);
    }

    [Fact]
    public void Update_WithFloorAt_SettlesVehicleOnPerMonitorFloor_NotGlobalCanvasHeight()
    {
        // Regresyon testi: çoklu-monitör modunda her ekranın kendi taban çizgisi kullanılmalı
        // (bkz. Vehicle.Update floorAt parametresi, MainWindow.FloorYForX). floorAt verildiğinde
        // canvasHeight (800) yerine bu değerin kullanıldığını doğrular. Y=0'dan başlar (ekranın
        // tepe sınırı kırpmasını tetiklememek için) - sekme fiziği sönümlenip yerleşmesi için
        // bol kare payı ve gevşek tolerans bırakılır.
        var vehicle = new Vehicle(100, 0, "car");
        // Vehicle constructor'ı AI/autoMoveEnabled'dan tamamen bağımsız olarak HER ZAMAN rastgele bir
        // başlangıç hızı verir (bkz. Vehicle(x,y,type) - VelocityX/Y = (Random-0.5)*4, "doğuşta plop"
        // efekti için). 600 karelik bu testte o rastgele yatay hız zaman zaman aracı bir duvara kadar
        // sürükleyip tırmandırıyordu (EdgeSide != 0), bu da testi flaky yapıyordu - saf yerçekimi/zemin
        // fiziğini izole etmek için başlangıç hızı burada bilerek sıfırlanır.
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;
        const double customFloorY = 300; // canvasHeight'tan (800) kasıtlı olarak farklı

        for (int i = 0; i < 600; i++)
        {
            // autoMoveEnabled: false - bu test yerçekimi/sekme/zemin fiziğini doğruluyor, gezinme
            // (wandering) AI'sini değil.
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false, floorAt: _ => customFloorY);
        }

        double expectedRestY = customFloorY - vehicle.Height / 2.0;
        Assert.InRange(vehicle.Y, expectedRestY - 5.0, expectedRestY + 5.0);
    }

    [Fact]
    public void Update_MovingVehicle_AccumulatesTireWear()
    {
        var vehicle = new Vehicle(100, 100, "car") { VelocityX = 5, Fuel = 100, MaxFuel = 100 };

        vehicle.Update(0, new List<Vehicle>(), new List<Obstacle>(), canvasWidth: 1200, canvasHeight: 800);

        Assert.True(vehicle.TireWear > 0);
    }

    [Fact]
    public void Update_WithLearningAIAndTarget_MovesVehicleCloserToTarget()
    {
        var vehicle = new Vehicle(100, 400, "car")
        {
            UseLearningAI = true,
            TargetX = 900,
            TargetY = 400,
            VelocityX = 0,
            VelocityY = 0,
            IsFlying = true // yerçekimi/zemin fiziğini devre dışı bırakıp saf navigasyonu izole et
        };
        double initialDistance = Math.Abs(vehicle.TargetX - vehicle.X);

        for (int i = 0; i < 4000; i++)
        {
            // autoMoveEnabled: true - hedef takibi (Q-learning dahil) UpdateAI içinden çalışır ve
            // UpdateAI yalnızca autoMoveEnabled açıkken (ve düşük hızdayken) çağrılır (bkz. Vehicle.Update).
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 2000, canvasHeight: 800, autoMoveEnabled: true);
        }

        double finalDistance = Math.Sqrt(Math.Pow(vehicle.TargetX - vehicle.X, 2) + Math.Pow(vehicle.TargetY - vehicle.Y, 2));
        Assert.True(finalDistance < initialDistance * 0.7,
            $"Beklenen: hedefe belirgin şekilde yaklaşmış olmalı. Başlangıç mesafesi={initialDistance}, son mesafe={finalDistance}");
    }

    [Fact]
    public void Update_ChasingAnotherVehicleAtIdenticalPosition_DoesNotProduceNaN()
    {
        // Regresyon testi: UpdateAI'nin "chasing"/"fleeing" dallarında hedefe olan mesafe (dist)
        // sıfır çıkarsa (iki araç tam aynı noktadaysa) eskiden (dx/dist) 0/0 = NaN üretiyordu. NaN
        // bir kez X/Y/Velocity'ye sızınca araç köşede sonsuza kadar donmuş/bozuk kalıyordu - kenar
        // geçiş karşılaştırmaları (X <= ...) NaN ile her zaman false döndüğü için "köşe sıkışma
        // güvenlik ağı" bile devreye giremiyordu.
        var vehicle = new Vehicle(500, 500, "car") { State = "chasing", VelocityX = 0.1, VelocityY = 0.1 };
        var other = new Vehicle(500, 500, "car"); // tam aynı konumda - dist == 0
        var others = new List<Vehicle> { vehicle, other };

        for (int i = 0; i < 30; i++)
        {
            vehicle.Update(i, others, new List<Obstacle>(), canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: true);
        }

        Assert.False(double.IsNaN(vehicle.X));
        Assert.False(double.IsNaN(vehicle.Y));
        Assert.False(double.IsNaN(vehicle.VelocityX));
        Assert.False(double.IsNaN(vehicle.VelocityY));
    }

    [Fact]
    public void Update_WithNaNPosition_RecoversToValidFiniteState()
    {
        // Herhangi bir sebeple (beklenmedik bir bölme hatası vb.) X/Y/Velocity NaN'a düşerse,
        // Update() bir sonraki karede aracı ekranın ortasına, sıfır hızla geri getirmeli - aksi
        // halde araç kalıcı olarak "donmuş/titriyor" görünümde kalırdı (bkz. yukarıdaki test).
        var vehicle = new Vehicle(100, 100, "car");
        vehicle.X = double.NaN;
        vehicle.VelocityY = double.PositiveInfinity;

        vehicle.Update(0, new List<Vehicle>(), new List<Obstacle>(), canvasWidth: 1200, canvasHeight: 800);

        Assert.False(double.IsNaN(vehicle.X));
        Assert.False(double.IsNaN(vehicle.Y));
        Assert.False(double.IsInfinity(vehicle.VelocityY));
        Assert.Equal(600, vehicle.X, precision: 1); // canvasWidth/2
    }

    [Fact]
    public void ResetEdgeStateAfterDrag_FromCeiling_ResetsEdgeSideAndAnimatesAngleBackToZero()
    {
        // Regresyon testi: araç tavana tırmanmışken (EdgeSide=2, WallAngleSmoothed ~180°) sürüklenip
        // bırakılınca eskiden yalnızca "EdgeSide = 0" atanıyordu - bu, Update() içindeki normal kenar
        // geçiş algılamasının (edgeSideBeforeTransition karşılaştırması) fark edemediği dıştan bir
        // atama olduğundan WallAngleSmoothed hiç güncellenmiyordu; araç zeminde kalıcı olarak ters
        // (180°) görünüyordu. ResetEdgeStateAfterDrag artık açıyı da 0°'ye doğru akıcı geçişle döndürür.
        var vehicle = new Vehicle(100, 100, "car") { EdgeSide = 2, WallAngleSmoothed = 180 };

        vehicle.ResetEdgeStateAfterDrag();

        Assert.Equal(0, vehicle.EdgeSide);

        // Birkaç kare fizik ilerletilince açı 180'den uzaklaşıp 0'a doğru yakınsamalı (donmuş kalmamalı).
        for (int i = 0; i < 60; i++)
        {
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(), canvasWidth: 1200, canvasHeight: 800);
        }
        Assert.True(Math.Abs(vehicle.WallAngleSmoothed) < 170,
            $"Beklenen: açı 180°'den uzaklaşmış olmalı, ama hâlâ {vehicle.WallAngleSmoothed}");
    }

    [Fact]
    public void Update_WithoutFloorAt_FallsBackToCanvasHeight()
    {
        var vehicle = new Vehicle(100, 0, "car");
        // bkz. yukarıdaki testteki not: constructor'ın rastgele başlangıç hızını sıfırla, saf
        // yerçekimi/zemin fiziğini izole et.
        vehicle.VelocityX = 0;
        vehicle.VelocityY = 0;

        for (int i = 0; i < 600; i++)
        {
            vehicle.Update(i, new List<Vehicle>(), new List<Obstacle>(),
                canvasWidth: 1200, canvasHeight: 800, autoMoveEnabled: false);
        }

        double expectedRestY = 800 - vehicle.Height / 2.0;
        Assert.InRange(vehicle.Y, expectedRestY - 5.0, expectedRestY + 5.0);
    }
}
