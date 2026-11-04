using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AutoMotion.Models
{
    /// <summary>
    /// AutoMotion'un merkezi oyun sistemi (VPet Simulator'dan esinlenmiş)
    /// </summary>
    public class GameCore
    {
        public IController Controller { get; set; }
        public List<TouchArea> TouchEvents { get; private set; } = new();
        public ObservableCollection<Vehicle> AutoMotions { get; private set; } = new();
        public List<Item> Items { get; private set; } = new();
        public ObservableCollection<Obstacle> Obstacles { get; private set; } = new();

        public int GameTime { get; private set; }
        public bool IsPaused { get; set; }
        public string CurrentScene { get; set; } = "main";

        /// <summary>Kapatıldığında araçlar kendi kendine gezinmeyi (wandering AI) durdurur;
        /// sadece manuel sürükleme, fizik (yerçekimi/sekme) ve rota takibi çalışmaya devam eder.</summary>
        public bool AutoMoveEnabled { get; set; } = true;

        /// <summary>Ayarlar panelindeki kaydırıcılarla kontrol edilir - kirlenme birikim hızını
        /// ve yakıt tüketim hızını genel olarak ölçekler (1.0 = varsayılan).</summary>
        public double DirtMultiplier { get; set; } = 1.0;
        public double FuelMultiplier { get; set; } = 1.0;

        public GameData GameData { get; set; } = new();

        public event EventHandler<Vehicle> VehicleAdded;
        public event EventHandler<Vehicle> VehicleRemoved;
        public event EventHandler<Item> ItemAdded;
        public event EventHandler<Achievement> AchievementUnlocked;

        public static readonly List<Achievement> AchievementCatalog = new()
        {
            new Achievement("first_vehicle", "achv.first_vehicle", d => d.TotalVehicles >= 1),
            new Achievement("fleet_5", "achv.fleet_5", d => d.TotalVehicles >= 5),
            new Achievement("fleet_15", "achv.fleet_15", d => d.TotalVehicles >= 15),
            new Achievement("earn_1000", "achv.earn_1000", d => d.TotalEarnings >= 1000),
            new Achievement("earn_10000", "achv.earn_10000", d => d.TotalEarnings >= 10000),
            new Achievement("mileage_100", "achv.mileage_100", d => d.TotalMileage >= 100),
            new Achievement("mileage_1000", "achv.mileage_1000", d => d.TotalMileage >= 1000),
            new Achievement("collector", "achv.collector", d => d.VehicleTypeCounts.Count(kv => kv.Value > 0) >= 3),
            new Achievement("first_fine", "achv.first_fine", d => d.PoliceFinesReceived >= 1),
            new Achievement("car_meet", "achv.car_meet", d => d.CarMeetsAttended >= 1),
            new Achievement("clean_freak", "achv.clean_freak", d => d.VehiclesWashedClean >= 1),
            new Achievement("master_mechanic", "achv.master_mechanic", d => d.RepairMinigameSuccessCount >= 5),
        };

        public GameCore()
        {
            GameTime = 0;
        }

        public void AddMoney(int amount)
        {
            GameData.Money += amount;
            if (amount > 0) GameData.TotalEarnings += amount;
        }

        private void CheckAchievements()
        {
            foreach (var achievement in AchievementCatalog)
            {
                if (!GameData.UnlockedAchievements.Contains(achievement.Id) && achievement.IsUnlocked(GameData))
                {
                    GameData.UnlockedAchievements.Add(achievement.Id);
                    AchievementUnlocked?.Invoke(this, achievement);
                }
            }
        }

        public void AddTouchEvent(TouchArea touchArea)
        {
            TouchEvents.Add(touchArea);
        }

        public void RemoveTouchEvent(TouchArea touchArea)
        {
            TouchEvents.Remove(touchArea);
        }

        public void ClearTouchEvents()
        {
            TouchEvents.Clear();
        }

        public bool HandleTouch(double x, double y)
        {
            foreach (var touchEvent in TouchEvents)
            {
                if (touchEvent.Touch(new System.Windows.Point(x, y)))
                {
                    return touchEvent.DoAction?.Invoke() ?? false;
                }
            }
            return false;
        }

        public void AddAutoMotion(Vehicle vehicle)
        {
            AutoMotions.Add(vehicle);
            GameData.TotalVehicles++;
            GameData.VehicleTypeCounts.TryGetValue(vehicle.Type, out int count);
            GameData.VehicleTypeCounts[vehicle.Type] = count + 1;
            VehicleAdded?.Invoke(this, vehicle);
        }

        public void RemoveAutoMotion(Vehicle vehicle)
        {
            AutoMotions.Remove(vehicle);
            VehicleRemoved?.Invoke(this, vehicle);
        }

        public void AddItem(Item item)
        {
            Items.Add(item);
            ItemAdded?.Invoke(this, item);
        }

        public bool UseItem(int itemIndex, Vehicle targetVehicle)
        {
            if (itemIndex >= 0 && itemIndex < Items.Count)
            {
                var item = Items[itemIndex];
                if (item.Use(targetVehicle))
                {
                    Items.RemoveAt(itemIndex);
                    return true;
                }
            }
            return false;
        }

        private DateTime lastUpdateAt = DateTime.MinValue;
        private double gameTimeAccumulator;

        /// <summary>
        /// Son karede geçen gerçek süre - 60 FPS'lik bir kare "1.0" kabul edilerek ölçeklenir.
        /// Tüm fizik sabitleri (yerçekimi 0.4, sürtünme 0.98, ivmeler, sayaçlar...) tarihsel olarak
        /// "60 FPS'te kare başına" ayarlandığı için, oyun döngüsü 144 FPS'e çıkarıldığında bunlar
        /// saniyede 2.4 kat daha sık uygulanıyordu: yerçekimi saniyede ~5.8 kat daha fazla yol
        /// aldırıyor, sürtünme hızı 2.4 kat çabuk yiyordu - araçlar zemine çakılıp orada kalıyordu.
        /// Bu ölçek sayesinde hız birimleri "60 FPS karesi başına piksel" olarak KORUNUR (dolayısıyla
        /// MaxSpeed/Clamp gibi mevcut tüm ayarlar geçerli kalır) ama gerçek zamanlı davranış kare
        /// hızından bağımsız hale gelir.
        /// </summary>
        public double DeltaFrames { get; private set; } = 1.0;

        /// <summary>Kare hızından bağımsız, saniye cinsinden biriken oyun zamanı (salınım/animasyon fazları için).</summary>
        public double ElapsedSeconds { get; private set; }

        public void Update(double canvasWidth = 1200, double canvasHeight = 800, double activeWinTop = -1, double activeWinLeft = -1, double activeWinRight = -1, Func<double, double> floorAt = null)
        {
            var now = DateTime.UtcNow;
            double dt = lastUpdateAt == DateTime.MinValue ? 1.0 : (now - lastUpdateAt).TotalSeconds * 60.0;
            lastUpdateAt = now;
            // Ani takılmalarda (GC duraklaması, pencere sürükleme, uyku sonrası) devasa bir dt ile
            // araçların ekranın dışına ışınlanmasını önlemek için makul bir aralığa kırpılır.
            DeltaFrames = Math.Clamp(dt, 0.2, 3.0);

            if (IsPaused) return;

            ElapsedSeconds += DeltaFrames / 60.0;

            // GameTime artık "gerçek kare" değil, "60 FPS eşdeğeri kare" sayar. Böylece ona bağlı
            // TÜM mevcut eşikler (buluşma için 36000 = ~10 dk, pil uyarısı için 1800 = ~30 sn vb.)
            // tek satırlık bu değişiklikle yeniden doğru gerçek süreye karşılık gelir - 144 FPS'te
            // eskiden hepsi 2.4 kat erken tetikleniyordu.
            gameTimeAccumulator += DeltaFrames;
            int wholeFrames = (int)gameTimeAccumulator;
            if (wholeFrames > 0)
            {
                gameTimeAccumulator -= wholeFrames;
                GameTime += wholeFrames;
                GameData.PlayTime += wholeFrames;
            }

            // Engelleri güncelle
            for (int i = Obstacles.Count - 1; i >= 0; i--)
            {
                Obstacles[i].Update();
            }

            // Araçları güncelle
            for (int i = AutoMotions.Count - 1; i >= 0; i--)
            {
                double mileageBefore = AutoMotions[i].Mileage;
                AutoMotions[i].Update(GameTime, AutoMotions.ToList(), Obstacles.ToList(), canvasWidth, canvasHeight, activeWinTop, activeWinLeft, activeWinRight, AutoMoveEnabled, DeltaFrames, ElapsedSeconds, DirtMultiplier, FuelMultiplier, floorAt);
                GameData.TotalMileage += AutoMotions[i].Mileage - mileageBefore;

                if (AutoMotions[i].IsDestroyed)
                {
                    // Bozulan Araç Çekicisi: anında silmek yerine kısa bir "çekiliyor" süresi tanınır
                    var wreck = AutoMotions[i];
                    if (wreck.TowCountdown < 0)
                    {
                        wreck.TowCountdown = Vehicle.TowDurationFrames;
                    }
                    else if (wreck.TowCountdown <= 0)
                    {
                        RemoveAutoMotion(wreck);
                    }
                    else
                    {
                        wreck.TowCountdown -= DeltaFrames;
                    }
                }
            }

            // Mesafe kilometre taşı (achievement) kontrolü - her 100 km'de bir
            int milestone = (int)(GameData.TotalMileage / 100) * 100;
            if (milestone > GameData.LastMileageMilestone && milestone > 0 && AutoMotions.Count > 0)
            {
                GameData.LastMileageMilestone = milestone;
            }

            CheckAchievements();
        }

        public GameSaveData Save()
        {
            return new GameSaveData
            {
                GameTime = GameTime,
                GameData = GameData,
                Vehicles = AutoMotions.Select(v => v.Serialize()).ToList(),
                Items = Items.Select(i => i.Serialize()).ToList(),
                // Yerleştirilen trafik ışıkları/istasyonlar/koniler/polis/kargo objeleri önceden
                // hiç kaydedilmiyordu - kayıt yüklenince sessizce kayboluyorlardı.
                Obstacles = Obstacles.Select(o => o.Serialize()).ToList(),
                Timestamp = DateTime.Now,
                DirtMultiplier = DirtMultiplier,
                FuelMultiplier = FuelMultiplier
            };
        }

        public bool Load(GameSaveData saveData)
        {
            if (saveData == null) return false;

            GameTime = saveData.GameTime;
            GameData = saveData.GameData;
            DirtMultiplier = saveData.DirtMultiplier;
            FuelMultiplier = saveData.FuelMultiplier;

            AutoMotions.Clear();
            foreach (var vehicleData in saveData.Vehicles ?? new List<AutoMotionData>())
            {
                var vehicle = new Vehicle(vehicleData.X, vehicleData.Y, vehicleData.Type);
                vehicle.Deserialize(vehicleData);

                // Kayıttan dönen araçlar "yeni doğmuş" gibi görünmemeli: oyun devam eden bir duruma
                // geri dönüyor, filonun tamamının aynı anda ölçeklenerek belirmesi kafa karıştırıcı
                // olurdu. Doğuş animasyonu yalnızca gerçekten YENİ eklenen araçlarda oynar.
                vehicle.SkipSpawnAnimation();
                AddAutoMotion(vehicle);
            }

            Items.Clear();
            foreach (var itemData in saveData.Items ?? new List<ItemData>())
            {
                var item = ItemFactory.CreateItem(itemData.ItemType, itemData.SubType);
                item.Deserialize(itemData);
                AddItem(item);
            }

            Obstacles.Clear();
            foreach (var obstacleData in saveData.Obstacles ?? new List<ObstacleData>())
            {
                var obstacle = new Obstacle(obstacleData.X, obstacleData.Y, obstacleData.Type);
                obstacle.Deserialize(obstacleData);
                Obstacles.Add(obstacle);
            }

            // "Özlem" mekaniği (VPet'in imza özelliği): uygulama kapalıyken de gerçek zaman işlemiş
            // gibi davranır - kayıt ne kadar süre önce alınmışsa araçlar o kadar yakıt/mutluluk
            // kaybetmiş olarak karşılanır (uzun süre bakılmayan bir evcil hayvan gibi). Aşırı cezayı
            // önlemek için 24 saatte doyuma ulaşır (tavan), her araç en az %15 yakıt/mutlulukla kalır.
            LastAbsenceHours = 0;
            if (saveData.Timestamp != default)
            {
                double hoursAway = Math.Clamp((DateTime.Now - saveData.Timestamp).TotalHours, 0, 24);
                LastAbsenceHours = hoursAway;
                if (hoursAway > 0.1)
                {
                    double fuelPenalty = hoursAway * 1.8;
                    double happinessPenalty = hoursAway * 1.2;
                    foreach (var vehicle in AutoMotions)
                    {
                        vehicle.Fuel = Math.Max(vehicle.MaxFuel * 0.15, vehicle.Fuel - fuelPenalty);
                        vehicle.Happiness = Math.Max(15, vehicle.Happiness - happinessPenalty);
                    }
                }
            }

            return true;
        }

        /// <summary>Son yüklemede kaydın ne kadar süre önce alındığı (saat) - UI'da "beni özledin mi"
        /// tarzı bir bildirim göstermek için (bkz. MainWindow.Window_Loaded).</summary>
        public double LastAbsenceHours { get; private set; }

        public void Reset()
        {
            GameTime = 0;
            AutoMotions.Clear();
            Items.Clear();
            Obstacles.Clear();
            GameData = new();
        }
    }

    public class GameData
    {
        public int Money { get; set; } = 1000;
        public int TotalVehicles { get; set; }
        public int PlayTime { get; set; }
        public double TotalMileage { get; set; }
        public int LastMileageMilestone { get; set; }
        public DateTime? LastSave { get; set; }

        // Başarım / istatistik sistemi
        public int TotalEarnings { get; set; }
        public Dictionary<string, int> VehicleTypeCounts { get; set; } = new();
        public List<string> UnlockedAchievements { get; set; } = new();

        // Yeni mekanikler için istatistikler (başarımlar ve kalıcılık için)
        public int PoliceFinesReceived { get; set; }
        public int CarMeetsAttended { get; set; }
        public int VehiclesWashedClean { get; set; }
        public int RepairMinigameSuccessCount { get; set; }

        /// <summary>Kargo teslimat serisi - art arda başarılı teslimatlarda ödül kademeli artar.
        /// Önceden MainWindow'da geçici bir alandı, uygulama kapanınca sıfırlanıyordu.</summary>
        public int CargoStreak { get; set; }
    }

    /// <summary>Başarım tanımı: benzersiz kimlik, görünen ad ve kilidinin ne zaman açılacağını belirleyen koşul.</summary>
    public class Achievement
    {
        public string Id { get; }

        /// <summary>Çeviri anahtarı (ör. "achv.first_vehicle"). Sabit metin DEĞİL: başarım adları
        /// arayüzde göründüğü için dile göre çözümlenmeli - bkz. <see cref="Label"/>.</summary>
        public string LabelKey { get; }

        /// <summary>Geçerli dildeki başarım adı. Her okunduğunda çözümlenir, böylece kullanıcı dili
        /// değiştirdiğinde başarım listesi de anında güncellenir.</summary>
        public string Label => Services.Loc.T(LabelKey);

        public Func<GameData, bool> IsUnlocked { get; }

        public Achievement(string id, string labelKey, Func<GameData, bool> isUnlocked)
        {
            Id = id;
            LabelKey = labelKey;
            IsUnlocked = isUnlocked;
        }
    }

    public interface IController
    {
        void MoveToSide();
        void UpdateSettings();
    }
}
