using System;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Hasarın kaportada kaç iz olarak görüneceği.
    ///
    /// Ayrı ve saf: "kaç iz çizilecek" kararı render döngüsünün içine gömülseydi, eşiklerin doğru
    /// çalıştığı (sağlam araçta hiç iz olmaması, hurdada dolu olması, aradakinin kademeli artması)
    /// ancak gözle kontrol edilebilirdi.
    /// </summary>
    public static class VehicleDamage
    {
        /// <summary>Bu orandaki hasarın altında kaporta temiz görünür - her küçük çizik iz
        /// bırakırsa araçlar sürekli hasarlı görünür.</summary>
        public const double VisibleThreshold = 0.35;

        /// <summary>En fazla iz sayısı (kaporta tamamen dağılmış hâli).</summary>
        public const int MaxMarks = 5;

        /// <summary>Kalan can oranından (1 = sapasağlam, 0 = hurda) iz sayısı.</summary>
        public static int MarkCount(double healthRatio)
        {
            if (double.IsNaN(healthRatio)) return 0;

            double damage = 1.0 - Math.Clamp(healthRatio, 0, 1);
            if (damage <= VisibleThreshold) return 0;

            // Eşiğin üstünde kalan hasar, iz sayısına doğrusal olarak dağıtılır.
            double t = (damage - VisibleThreshold) / (1.0 - VisibleThreshold);
            return Math.Clamp((int)Math.Ceiling(t * MaxMarks), 1, MaxMarks);
        }
    }

    /// <summary>
    /// Günün saatinden "gecelik" katsayısı (0 = tam gündüz, 1 = tam gece).
    ///
    /// NEDEN AYRI BİR SINIF: bu hesap MainWindow'un içinde gömülüydü ve test edilemiyordu; oysa
    /// gündüz↔gece geçişinin akıcılığı tam olarak burada belirleniyor. Saf bir fonksiyon olarak
    /// ayrılınca hem sınır saatleri hem de "hiçbir yerde sıçrama yok" özelliği doğrulanabiliyor.
    ///
    /// Zaman çizelgesi:
    ///   00:00-06:00  gece (1.0)
    ///   06:00-07:00  şafak - karanlık düzgün şekilde çekilir (1 → 0)
    ///   07:00-19:00  gündüz (0.0)
    ///   19:00-20:00  alacakaranlık - karanlık düzgün şekilde iner (0 → 1)
    ///   20:00-24:00  gece (1.0)
    /// </summary>
    public static class DayNight
    {
        public const double DuskStart = 19.0;
        public const double NightStart = 20.0;
        public const double DawnStart = 6.0;
        public const double DayStart = 7.0;

        /// <summary>Verilen saat için (0-24 arası, kesirli) gecelik katsayısı.</summary>
        public static double NightFactorAt(double hourOfDay)
        {
            double t = hourOfDay % 24.0;
            if (t < 0) t += 24.0;

            if (t >= NightStart || t < DawnStart) return 1.0;
            if (t >= DuskStart) return t - DuskStart;          // 19→20 arası 0'dan 1'e
            if (t < DayStart) return 1.0 - (t - DawnStart);    // 6→7 arası 1'den 0'a
            return 0.0;
        }

        /// <summary>Farların/gece katmanının çizilmeye değecek kadar karanlık olup olmadığı.
        /// Eşik sıfırdan biraz büyük: neredeyse görünmez bir karanlıkta far konisi çizmek
        /// bedava değil (bkz. render profili).</summary>
        public static bool IsDarkEnough(double nightFactor) => nightFactor > 0.02;
    }
}
