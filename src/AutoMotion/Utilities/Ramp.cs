using System;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Bir değeri hedefine kare hızından BAĞIMSIZ bir hızla yaklaştırır.
    ///
    /// Neden ayrı ve saf: hava durumu gibi "açık/kapalı" bayrakların ani sıçraması yerine
    /// yoğunluğun rampayla değişmesi isteniyor (bkz. kalan işler 3.4 / B-2). Bu hesap
    /// render döngüsünün içine gömülseydi test edilemezdi; oysa asıl kırılgan kısım burası:
    /// hedefi aşmak, 144 FPS'te 60 FPS'tekinden farklı hızlanmak, hiç ulaşamamak.
    /// </summary>
    public static class Ramp
    {
        /// <summary>
        /// <paramref name="current"/> değerini <paramref name="target"/>'a doğru taşır.
        /// </summary>
        /// <param name="perFrame">60 FPS'te KARE BAŞINA ilerleme miktarı.</param>
        /// <param name="deltaFrames">Bu karenin 60 FPS'e göre süresi (bkz. GameCore.DeltaFrames);
        /// 144 FPS'te ~0.42, 30 FPS'te ~2.0. Böylece rampa gerçek zamana sabitlenir.</param>
        public static double Approach(double current, double target, double perFrame, double deltaFrames)
        {
            if (perFrame <= 0 || double.IsNaN(perFrame)) return target;

            double step = perFrame * Math.Max(0, deltaFrames);
            if (double.IsNaN(step) || double.IsInfinity(step)) return target;

            // Hedefi AŞMAZ: adım kalan mesafeden büyükse doğrudan hedefe oturur.
            double distance = target - current;
            if (Math.Abs(distance) <= step) return target;

            return current + Math.Sign(distance) * step;
        }
    }
}
