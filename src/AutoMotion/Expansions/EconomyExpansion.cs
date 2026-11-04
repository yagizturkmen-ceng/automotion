using System;
using AutoMotion.Models;

namespace AutoMotion.Expansions
{
    /// <summary>
    /// Ekonomi Eklentisi - açıkken her aktif araç, kat ettiği mesafeye bağlı
    /// olarak periyodik pasif gelir üretir (REQUIREMENTS.md F-16/F-22).
    /// </summary>
    public class EconomyExpansion : Expansion
    {
        private DateTime lastPayoutAt;
        // Not: eskiden "600 kare" (60fps varsayımıyla ~10sn) olarak sayılıyordu - oyun döngüsü artık
        // 144 FPS'e çıkarıldığından kare sayımı gerçek zamanı yanlış temsil ederdi (gelir ~2.4x hızlı
        // birikirdi). Gerçek geçen süreye (DateTime) göre çalışarak kare hızından bağımsız hale getirildi.
        private static readonly TimeSpan PayoutInterval = TimeSpan.FromSeconds(10);
        private const int PayoutPerVehicle = 15;

        public EconomyExpansion()
            : base("economy", "💰 Pasif Ekonomi", "Her aktif araç periyodik olarak küçük bir gelir kazandırır", "💰")
        {
        }

        protected override void OnEnable(GameCore gameCore)
        {
            lastPayoutAt = DateTime.Now;
        }

        protected override void OnUpdate(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight)
        {
            var now = DateTime.Now;
            if (now - lastPayoutAt < PayoutInterval) return;
            lastPayoutAt = now;

            // Kilitli araçlar "serviste değil" sayılır, filo gelirine katkı vermez.
            int activeFleetSize = 0;
            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (!vehicle.IsLocked) activeFleetSize++;
            }
            if (activeFleetSize == 0) return;

            // Otomatik Hareket açıkken filo gerçekten "çalışıyor" sayılır ve tam kazanır;
            // kapalıyken araçlar park halinde kabul edilip düşük oranda (bekleme/kira) geliri alır.
            double workingMultiplier = gameCore.AutoMoveEnabled ? 1.0 : 0.4;
            int payout = (int)System.Math.Round(activeFleetSize * PayoutPerVehicle * workingMultiplier);
            if (payout > 0) gameCore.AddMoney(payout);
        }
    }
}
