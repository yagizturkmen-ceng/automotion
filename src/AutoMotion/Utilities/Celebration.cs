using System;
using System.Collections.Generic;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Başarım kazanıldığında sahneye saçılan kutlama parçacıklarının üretimi
    /// (kalan işler 3.4 / C-1).
    ///
    /// Neden ayrı ve saf: "patlama" birkaç sayıdan ibaret (kaç parçacık, hangi yöne, ne hızla) ve
    /// bu sayılar yanlış olduğunda sonuç ya görünmez ya da ekranı basan bir gürültü olur. Render
    /// döngüsünün içine gömülü olsaydı ancak başarım kazanarak denenebilirdi.
    ///
    /// Tasarım kararı: masaüstü uygulaması olduğu için kutlama KISA ve YEREL - ekranı kaplayan bir
    /// gösteri değil, aracın çevresinde yukarı doğru savrulup sönen bir avuç kıvılcım.
    /// </summary>
    public static class Celebration
    {
        /// <summary>Tek bir kutlamada üretilecek parçacık sayısı.</summary>
        public const int BurstSize = 14;

        /// <summary>Kutlama kıvılcımlarının rengi - başarım/altın çağrışımı.</summary>
        public static readonly string[] Palette = { "#FBCA1F", "#FFE66D", "#FFFFFF", "#FFA726" };

        public readonly record struct Spark(double VelocityX, double VelocityY, double Size, string Color);

        /// <summary>
        /// Yukarı doğru açılan bir yelpaze üretir: parçacıklar yanlara savrulurken hepsi
        /// yukarı çıkar (aşağı düşen bir kutlama "kutlama" gibi durmaz).
        /// </summary>
        public static IReadOnlyList<Spark> BuildBurst(Random random, int count = BurstSize)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count <= 0) return Array.Empty<Spark>();

            var sparks = new List<Spark>(count);
            for (int i = 0; i < count; i++)
            {
                // Yelpaze: -60° ile +60° arası, yukarı (negatif Y) merkezli.
                double spread = (i + random.NextDouble()) / count;          // 0..1 düzgün dağılım
                double angle = -Math.PI / 2 + (spread - 0.5) * (2 * Math.PI / 3);
                double speed = 1.6 + random.NextDouble() * 2.2;

                sparks.Add(new Spark(
                    Math.Cos(angle) * speed,
                    Math.Sin(angle) * speed,
                    2.5 + random.NextDouble() * 2.5,
                    Palette[random.Next(Palette.Length)]));
            }

            return sparks;
        }
    }
}
