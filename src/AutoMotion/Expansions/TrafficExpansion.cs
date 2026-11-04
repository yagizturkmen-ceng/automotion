using System;
using System.Collections.Generic;
using System.Linq;
using AutoMotion.Models;

namespace AutoMotion.Expansions
{
    /// <summary>
    /// Otomatik Trafik Eklentisi - açıkken ekran kenarlarından periyodik olarak
    /// yeni araçlar belirir. Kapatılınca kendi eklediği araçları temizler.
    /// </summary>
    public class TrafficExpansion : Expansion
    {
        private readonly List<Vehicle> spawnedVehicles = new();
        private readonly Random random = new();
        private DateTime lastSpawnAt;

        // Not: eskiden "300 kare" (60fps varsayımıyla ~5sn) olarak sayılıyordu - oyun döngüsü artık
        // 144 FPS'e çıkarıldığından bu kare sayısı gerçek zamanı yanlış temsil ederdi (trafik ~2.4x
        // hızlı türerdi). Gerçek geçen süreye göre çalışarak kare hızından bağımsız hale getirildi.
        private static readonly TimeSpan SpawnInterval = TimeSpan.FromSeconds(5);
        private const int MaxTrafficVehicles = 6;
        private static readonly string[] TrafficTypes = { "car", "truck", "bus", "bike", "motorcycle" };

        public TrafficExpansion()
            : base("traffic", "🚦 Otomatik Trafik", "Ekran kenarlarından periyodik olarak yeni araçlar belirir", "🚦")
        {
        }

        protected override void OnEnable(GameCore gameCore)
        {
            lastSpawnAt = DateTime.Now;
        }

        protected override void OnDisable(GameCore gameCore)
        {
            foreach (var vehicle in spawnedVehicles)
                gameCore.RemoveAutoMotion(vehicle);
            spawnedVehicles.Clear();
        }

        protected override void OnUpdate(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight)
        {
            spawnedVehicles.RemoveAll(v => v.IsDestroyed || !gameCore.AutoMotions.Contains(v));

            if (spawnedVehicles.Count >= MaxTrafficVehicles) return;
            var now = DateTime.Now;
            if (now - lastSpawnAt < SpawnInterval) return;

            lastSpawnAt = now;
            SpawnTrafficVehicle(gameCore, canvasWidth, canvasHeight);
        }

        private void SpawnTrafficVehicle(GameCore gameCore, double canvasWidth, double canvasHeight)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            var type = TrafficTypes[random.Next(TrafficTypes.Length)];

            double x, y;
            switch (random.Next(4))
            {
                case 0: x = 0; y = random.NextDouble() * canvasHeight; break;              // sol kenar
                case 1: x = canvasWidth; y = random.NextDouble() * canvasHeight; break;     // sağ kenar
                case 2: x = random.NextDouble() * canvasWidth; y = 0; break;                // üst kenar
                default: x = random.NextDouble() * canvasWidth; y = canvasHeight; break;    // alt kenar
            }

            var vehicle = new Vehicle(x, y, type);
            gameCore.AddAutoMotion(vehicle);
            spawnedVehicles.Add(vehicle);
        }
    }
}
