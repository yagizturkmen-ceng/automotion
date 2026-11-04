using System;
using System.Collections.Generic;

namespace AutoMotion.Models
{
    /// <summary>
    /// Uygulama içi (global klavye kancası KULLANILMADAN - gizlilik nedeniyle, bkz.
    /// MainWindow.HandleWindowKeyDown "Tuş Vuruşu Sismik Etkisi" ile aynı prensip) yazma hızını
    /// ölçüp bir "yazı yarışı" olayını yönetir: oyuncunun aracı gerçek WPM'ine göre ilerler,
    /// rakip araç sabit bir tempoda (RaceDuration içinde tam pisti biter) ilerler.
    /// </summary>
    public class WpmRace
    {
        private readonly Queue<DateTime> keystrokeTimes = new();
        private DateTime lastTickAt;

        public static readonly TimeSpan RaceDuration = TimeSpan.FromSeconds(20);
        /// <summary>Bu WPM'de sürdürülen yazma hızı tam pisti RaceDuration içinde bitirir.</summary>
        public const double TargetWpm = 45.0;

        public bool IsActive { get; private set; }
        public double PlayerProgress { get; private set; }
        public double RivalProgress { get; private set; }

        public void RegisterKeystroke()
        {
            if (!IsActive) return;
            var now = DateTime.Now;
            keystrokeTimes.Enqueue(now);
            while (keystrokeTimes.Count > 0 && (now - keystrokeTimes.Peek()).TotalSeconds > 8)
                keystrokeTimes.Dequeue();
        }

        /// <summary>Son ~8 saniyedeki tuş vuruşlarından anlık WPM tahmini (ortalama kelime = 5 karakter).</summary>
        public double CurrentWpm
        {
            get
            {
                if (keystrokeTimes.Count < 3) return 0;
                double windowSeconds = Math.Max(1.0, (DateTime.Now - keystrokeTimes.Peek()).TotalSeconds);
                double words = keystrokeTimes.Count / 5.0;
                return words / (windowSeconds / 60.0);
            }
        }

        public void Start()
        {
            keystrokeTimes.Clear();
            PlayerProgress = 0;
            RivalProgress = 0;
            IsActive = true;
            lastTickAt = DateTime.Now;
        }

        public void Cancel() => IsActive = false;

        /// <summary>Her oyun karesinde çağrılır; yarış bu çağrıda bittiyse (biri bitiş çizgisine
        /// ulaştıysa) bir kez true döner ve yarışı kapatır. <paramref name="didPlayerWin"/>,
        /// bitişte oyuncunun rakipten önde olup olmadığını verir.</summary>
        public bool Tick(out bool didPlayerWin)
        {
            didPlayerWin = false;
            if (!IsActive) return false;

            var now = DateTime.Now;
            double dtSeconds = Math.Max(0, (now - lastTickAt).TotalSeconds);
            lastTickAt = now;

            RivalProgress += dtSeconds / RaceDuration.TotalSeconds;
            PlayerProgress += (CurrentWpm / TargetWpm) * (dtSeconds / RaceDuration.TotalSeconds);

            if (PlayerProgress >= 1.0 || RivalProgress >= 1.0)
            {
                didPlayerWin = PlayerProgress >= RivalProgress;
                IsActive = false;
                return true;
            }
            return false;
        }
    }
}
