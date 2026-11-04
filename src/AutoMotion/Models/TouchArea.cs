using System;
using System.Windows;

namespace AutoMotion.Models
{
    /// <summary>
    /// Etkileşim bölgesi - VPet Simulator'dan
    /// </summary>
    public class TouchArea
    {
        public Point Location { get; set; }
        public Size Size { get; set; }
        public Func<bool> DoAction { get; set; }
        public bool IsPress { get; set; } // false = immediate, true = long press

        public TouchArea(Point location, Size size, Func<bool> doAction, bool isPress = false)
        {
            Location = location;
            Size = size;
            DoAction = doAction;
            IsPress = isPress;
        }

        public bool Touch(Point point)
        {
            double inx = point.X - Location.X;
            double iny = point.Y - Location.Y;

            return inx >= 0 && inx <= Size.Width &&
                   iny >= 0 && iny <= Size.Height;
        }
    }

    public class GameSaveData
    {
        /// <summary>Kayıt dosyası şema sürümü. Steam'de oyun güncellendiğinde eski kayıtların
        /// sessizce yanlış yüklenmesini (JsonSerializer eşleşmeyen alanı hata vermeden varsayılana
        /// düşürür) engellemek için sürüm damgası taşınır - yükleme sırasında
        /// SaveManager.MigrateIfNeeded gerekli dönüşümleri uygular.
        /// 0 = sürümleme öncesi (eski) kayıtlar, 1 = ilk sürümlü şema.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }

        public int GameTime { get; set; }
        public GameData GameData { get; set; }
        public List<AutoMotionData> Vehicles { get; set; }
        public List<ItemData> Items { get; set; }
        public List<ObstacleData> Obstacles { get; set; } = new();
        public DateTime Timestamp { get; set; }

        // Ayarlar panelindeki kirlilik/yakıt tüketimi çarpanları önceden hiç kaydedilmiyordu,
        // oturum kapanıp açıldığında sessizce varsayılan 1.0x'e dönüyordu.
        public double DirtMultiplier { get; set; } = 1.0;
        public double FuelMultiplier { get; set; } = 1.0;
    }
}
