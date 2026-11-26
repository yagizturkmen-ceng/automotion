using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace AutoMotion
{
    // Uzun süreli çalışma sağlık kaydı (Kalan İşler 2.4 - 24 saatlik dayanıklılık koşusu).
    public partial class MainWindow
    {
        /// <summary>Sağlık satırları arasındaki süre. 30 dakika: 24 saatte ~48 satır, session.log'un
        /// 256 KB sınırının çok altında kalır.</summary>
        internal static readonly TimeSpan HealthLogInterval = TimeSpan.FromMinutes(30);

        private DispatcherTimer healthTimer;
        private readonly Stopwatch uptime = Stopwatch.StartNew();
        private long peakWorkingSetMb;
        private long startWorkingSetMb;

        /// <summary>
        /// 24 saatlik dayanıklılık koşusu artık "uygulamayı açık bırak, sonra session.log'a bak"
        /// kadar basit: açılışta ve her 30 dakikada bir bellek, GC yığını, handle/iş parçacığı sayısı,
        /// araç ve parçacık sayısı tek satır olarak yazılır; kapanışta başlangıca göre özet düşülür.
        /// Sızıntı varsa (ör. handle sayısının saat saat tırmanması) satırlardan doğrudan görülür.
        /// Ölçüm yalnızca birkaç sayaç okur - oyun döngüsüne yük bindirmez.
        /// </summary>
        private void StartHealthLog()
        {
            var first = SampleHealth();
            startWorkingSetMb = first.WorkingSetMb;
            LogSessionEvent("Açılış · " + first.Format(uptime.Elapsed));

            healthTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = HealthLogInterval };
            healthTimer.Tick += (s, e) => LogSessionEvent("Sağlık · " + SampleHealth().Format(uptime.Elapsed));
            healthTimer.Start();
        }

        private void StopHealthLog()
        {
            if (healthTimer == null) return;
            healthTimer.Stop();
            healthTimer = null;

            var last = SampleHealth();
            LogSessionEvent($"Kapanış özeti · çalışma {FormatUptime(uptime.Elapsed)}, bellek {startWorkingSetMb} → {last.WorkingSetMb} MB (en yüksek {peakWorkingSetMb} MB), handle {last.Handles}");
        }

        private HealthSample SampleHealth()
        {
            var sample = new HealthSample();
            try
            {
                using var process = Process.GetCurrentProcess();
                sample.WorkingSetMb = process.WorkingSet64 / (1024 * 1024);
                sample.PrivateMb = process.PrivateMemorySize64 / (1024 * 1024);
                sample.Handles = process.HandleCount;
                sample.Threads = process.Threads.Count;
            }
            catch { /* sayaç okunamazsa satır yine yazılır, eksik alanlar 0 kalır */ }

            sample.GcHeapMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
            sample.Vehicles = gameCore?.AutoMotions.Count ?? 0;
            sample.Particles = particles.Count;

            if (sample.WorkingSetMb > peakWorkingSetMb) peakWorkingSetMb = sample.WorkingSetMb;
            return sample;
        }

        internal static string FormatUptime(TimeSpan t) => $"{(int)t.TotalHours}sa {t.Minutes:D2}dk";

        internal struct HealthSample
        {
            public long WorkingSetMb, PrivateMb, GcHeapMb;
            public int Handles, Threads, Vehicles, Particles;

            public string Format(TimeSpan uptime) =>
                $"süre {FormatUptime(uptime)} | bellek {WorkingSetMb} MB (özel {PrivateMb}, GC {GcHeapMb}) | " +
                $"handle {Handles} | thread {Threads} | araç {Vehicles} | parçacık {Particles}";
        }
    }
}
