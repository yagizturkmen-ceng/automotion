using System;
using System.Linq;
using AutoMotion.Models;

namespace AutoMotion.Expansions
{
    /// <summary>
    /// Görev/Kargo Eklentisi - eskiden doğrudan MainWindow.xaml.cs içine gömülü olan kargo
    /// toplama/teslimat döngüsünü (StartCargoBtn_Click/UpdateCargoMissions) ayrı bir Expansion'a
    /// taşır (REQUIREMENTS.md bölüm 2.4/F-22 önerisi). Ses/toast gibi UI yan etkileri burada
    /// DEĞİL, MainWindow'un abone olduğu event'lerle tetiklenir - Expansion katmanı sadece
    /// oyun durumunu (obstacles, HasCargo, ödül) yönetir.
    /// </summary>
    public class MissionExpansion : Expansion
    {
        public event Action PackageCollected;
        public event Action<int, int> DeliveryCompleted; // (reward, yeni seri)

        public MissionExpansion()
            : base("mission", "📦 Kargo Görevleri", "Araçlar rastgele beliren paketleri toplayıp teslimat noktasına taşır", "📦")
        {
        }

        protected override void OnEnable(GameCore gameCore)
        {
            // Etkinleştirildiğinde hemen yeni bir görev başlat.
        }

        protected override void OnDisable(GameCore gameCore)
        {
            var oldCargo = gameCore.Obstacles.Where(o => o.Type == "cargo_package" || o.Type == "delivery_point").ToList();
            foreach (var old in oldCargo) gameCore.Obstacles.Remove(old);

            foreach (var vehicle in gameCore.AutoMotions)
            {
                vehicle.HasCargo = false;
                vehicle.TargetX = -1;
                vehicle.TargetY = -1;
            }
        }

        /// <summary>Yeni bir kargo görevi (rastgele paket + teslimat noktası) başlatır. Hem dışarıdan
        /// (kullanıcı butonu) hem de bir teslimat tamamlandığında otomatik olarak çağrılır.</summary>
        public void StartNewMission(GameCore gameCore, double canvasWidth, double canvasHeight)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            var oldCargo = gameCore.Obstacles.Where(o => o.Type == "cargo_package" || o.Type == "delivery_point").ToList();
            foreach (var old in oldCargo) gameCore.Obstacles.Remove(old);

            double px = Random.Shared.Next(60, (int)canvasWidth - 60);
            double py = Random.Shared.Next(60, (int)canvasHeight - 60);
            double dx = Random.Shared.Next(60, (int)canvasWidth - 60);
            double dy = Random.Shared.Next(60, (int)canvasHeight - 60);

            gameCore.Obstacles.Add(new Obstacle(px, py, "cargo_package"));
            gameCore.Obstacles.Add(new Obstacle(dx, dy, "delivery_point"));

            foreach (var vehicle in gameCore.AutoMotions)
            {
                vehicle.HasCargo = false;
                vehicle.TargetX = px;
                vehicle.TargetY = py;
            }
        }

        protected override void OnUpdate(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight)
        {
            var package = gameCore.Obstacles.FirstOrDefault(o => o.Type == "cargo_package");
            var delivery = gameCore.Obstacles.FirstOrDefault(o => o.Type == "delivery_point");

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (!vehicle.HasCargo && package != null)
                {
                    vehicle.TargetX = package.X;
                    vehicle.TargetY = package.Y;

                    double dx = package.X - vehicle.X;
                    double dy = package.Y - vehicle.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 40)
                    {
                        vehicle.HasCargo = true;
                        gameCore.Obstacles.Remove(package);
                        PackageCollected?.Invoke();

                        if (delivery != null)
                        {
                            vehicle.TargetX = delivery.X;
                            vehicle.TargetY = delivery.Y;
                        }
                    }
                }
                else if (vehicle.HasCargo && delivery != null)
                {
                    vehicle.TargetX = delivery.X;
                    vehicle.TargetY = delivery.Y;

                    double dx = delivery.X - vehicle.X;
                    double dy = delivery.Y - vehicle.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 40)
                    {
                        vehicle.HasCargo = false;
                        vehicle.TargetX = -1;
                        vehicle.TargetY = -1;

                        // Zorluk/ödül ilerlemesi: art arda tamamlanan her teslimatta ödül artar
                        // (600$ ile sınırlı), tek seferlik sabit ödül yerine "iş serisi" hissi verir.
                        gameCore.GameData.CargoStreak++;
                        int reward = Math.Min(150 + gameCore.GameData.CargoStreak * 30, 600);
                        gameCore.AddMoney(reward);
                        gameCore.Obstacles.Remove(delivery);
                        DeliveryCompleted?.Invoke(reward, gameCore.GameData.CargoStreak);

                        StartNewMission(gameCore, canvasWidth, canvasHeight);
                    }
                }
                else if (!vehicle.HasCargo && package == null && delivery == null)
                {
                    vehicle.TargetX = -1;
                    vehicle.TargetY = -1;
                }
            }
        }
    }
}
