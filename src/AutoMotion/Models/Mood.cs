namespace AutoMotion.Models
{
    /// <summary>Aracın o anki hâli - görsel geri bildirimi belirler.</summary>
    public enum MoodState
    {
        /// <summary>Sorun yok.</summary>
        Normal,

        /// <summary>Mutsuz (uzun süre ilgilenilmemiş).</summary>
        Sad,

        /// <summary>Yakıtı bitmek üzere.</summary>
        LowFuel,

        /// <summary>Ağır hasarlı.</summary>
        Hurt
    }

    /// <summary>
    /// Aracın durumunu tek bir değere indirger (kalan işler 3.4 / A-6).
    ///
    /// Ayrı ve saf: eşikler render kodunun içine gömülüydü ve yalnızca fare aracın üzerine
    /// getirildiğinde okunuyordu. Durum DEĞİŞİMİNİ yakalayabilmek için önce durumun kendisi
    /// adlandırılabilir olmalıydı; ayrıca eşiklerin doğruluğu artık gözle değil testle kontrol
    /// ediliyor.
    ///
    /// Öncelik sırası bilinçli: yakıtsızlık ve hasar kullanıcının HEMEN müdahale edebileceği
    /// durumlar, mutsuzluk ise zamanla oluşan bir birikim. Aynı anda birden fazlası doğruysa
    /// en acili gösterilir.
    /// </summary>
    public static class Mood
    {
        public const double SadHappinessBelow = 25;
        public const double LowFuelRatio = 0.15;
        public const double HurtHealthRatio = 0.25;

        public static MoodState Classify(double happiness, double fuel, double maxFuel, double hp, double maxHp)
        {
            if (maxFuel > 0 && fuel < maxFuel * LowFuelRatio) return MoodState.LowFuel;
            if (maxHp > 0 && hp < maxHp * HurtHealthRatio) return MoodState.Hurt;
            if (happiness < SadHappinessBelow) return MoodState.Sad;
            return MoodState.Normal;
        }

        /// <summary>Durumu temsil eden rozet. Normal'de rozet yok.</summary>
        public static string Emoji(MoodState state) => state switch
        {
            MoodState.Sad => "😢",
            MoodState.LowFuel => "😰",
            MoodState.Hurt => "🤕",
            _ => null
        };
    }
}
