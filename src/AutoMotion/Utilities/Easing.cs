using System;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Sahne (oyun döngüsü) tarafındaki animasyonlar için küçük, tahsissiz (allocation-free) bir
    /// yumuşatma/geçiş yardımcısı.
    ///
    /// Neden WPF'in kendi Storyboard/DoubleAnimation'ı kullanılmıyor: onlar bir DependencyProperty'ye
    /// bağlanır ve WPF'in kendi zamanlayıcısıyla ilerler. Sahnedeki araçlar ise her karede elle
    /// çizilen (RenderCanvas.Children yeniden kurulan) prosedürel nesneler - bir DependencyProperty'ye
    /// sahip değiller. Bu yüzden sahne animasyonları saf sayısal durum olarak Vehicle üzerinde
    /// tutulur ve burada tanımlı fonksiyonlarla ilerletilir.
    ///
    /// TÜM fonksiyonlar kare hızından bağımsızdır: 60 FPS'te ve 144 FPS'te aynı gerçek-zaman
    /// sonucunu verir (bkz. Damp/Approach - "kare başına k ile çarp" davranışının dt'ye göre
    /// doğru üstel karşılığı). Kod tabanının geri kalanı da bu kuralı izliyor (bkz.
    /// Vehicle.Damp/LerpK, GameCore.DeltaFrames).
    /// </summary>
    public static class Easing
    {
        /// <summary>Kare hızından bağımsız üstel sönümleme: "60 FPS'te kare başına perFrameFactor ile
        /// çarp" davranışını geçen gerçek süreye (dt kare) göre birebir aynı sonucu verecek şekilde uygular.</summary>
        public static double Damp(double perFrameFactor, double dt) => Math.Pow(perFrameFactor, dt);

        /// <summary>Kare hızından bağımsız doğrusal yaklaşma katsayısı.</summary>
        public static double LerpK(double perFrameK, double dt) => 1.0 - Math.Pow(1.0 - perFrameK, dt);

        /// <summary>current değerini target'a doğru, kare hızından bağımsız şekilde yaklaştırır.</summary>
        public static double Approach(double current, double target, double perFrameK, double dt)
            => current + (target - current) * LerpK(perFrameK, dt);

        // ------------------------------------------------------------------ eğriler (0..1 -> 0..1)

        public static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);

        public static double OutCubic(double t)
        {
            t = Clamp01(t);
            double inv = 1 - t;
            return 1 - inv * inv * inv;
        }

        public static double InCubic(double t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        public static double InOutCubic(double t)
        {
            t = Clamp01(t);
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        public static double OutQuad(double t)
        {
            t = Clamp01(t);
            return 1 - (1 - t) * (1 - t);
        }

        /// <summary>Hedefi hafifçe aşıp geri oturan "yaylanma" - doğuş/pop animasyonları için
        /// (bir nesnenin canlı şekilde belirmesi).</summary>
        public static double OutBack(double t, double overshoot = 1.70158)
        {
            t = Clamp01(t);
            double c3 = overshoot + 1;
            double inv = t - 1;
            return 1 + c3 * inv * inv * inv + overshoot * inv * inv;
        }

        /// <summary>Sönümlenen yay salınımı - çarpma/iniş tepkileri için.</summary>
        public static double OutElastic(double t)
        {
            t = Clamp01(t);
            if (t <= 0) return 0;
            if (t >= 1) return 1;

            const double period = 0.3;
            double s = period / 4;
            return Math.Pow(2, -10 * t) * Math.Sin((t - s) * (2 * Math.PI) / period) + 1;
        }

        /// <summary>0'da başlayıp ortada 1'e çıkıp yine 0'a dönen tek atımlık zarf - kısa parıltı/
        /// vurgu efektleri için (ör. lamba yanıp sönmesi, rozet parlaması).</summary>
        public static double Pulse(double t)
        {
            t = Clamp01(t);
            return Math.Sin(t * Math.PI);
        }

        // ------------------------------------------------------------------ yay-sönümleyici

        /// <summary>
        /// Bir yay-sönümleyici (spring-damper) sistemini bir kare ilerletir; süspansiyon, sarkma,
        /// yaylanan panel gibi "gerçek kütle" hissi gereken yerler için.
        ///
        /// stiffness/damping değerleri 60 FPS karesi cinsindendir ve dt ile ölçeklenir. Büyük dt
        /// (ör. uygulama bir an takıldıysa) sayısal olarak patlamasın diye adım alt-adımlara bölünür.
        /// </summary>
        public static void Spring(ref double value, ref double velocity, double target,
                                  double stiffness, double damping, double dt)
        {
            if (dt <= 0) return;

            // Kararlılık için: tek bir adım 1 kareden büyükse böl (aksi halde sert yaylar ıraksar).
            int steps = (int)Math.Ceiling(dt);
            if (steps > 8) steps = 8;
            double stepDt = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                velocity += (target - value) * stiffness * stepDt;
                velocity *= Damp(1.0 - damping, stepDt);
                value += velocity * stepDt;
            }
        }
    }
}
