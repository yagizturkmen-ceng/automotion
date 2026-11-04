using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AutoMotion.Views;

namespace AutoMotion;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // Beklenmeyen bir hata (ör. bir görselin yüklenememesi) artık tüm uygulamayı
        // kapatmasın; hatayı bir log dosyasına yazıp kullanıcı deneyimini sürdürelim.
        // Ancak "sessizce yut" değil: tekrarlayan hatalar oran sınırlanır, log döndürülür ve
        // hata bir döngüye girerse kullanıcıya görünür bir uyarı çıkar (bkz. LogError/TripCircuitBreaker).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    // ------------------------------------------------------------------ tek örnek (single instance)

    private const string SingleInstanceMutexName = @"Local\AutoMotion.SingleInstance.Mutex";
    private const string ActivateExistingEventName = @"Local\AutoMotion.SingleInstance.Activate";

    private Mutex singleInstanceMutex;
    private EventWaitHandle activateEvent;

    /// <summary>
    /// App.xaml'deki StartupUri kaldırıldı - artık başlangıçta doğrudan MainWindow yerine önce
    /// logo + sonsuzluk sembolü dolum animasyonlu bir açılış ekranı (SplashWindow) gösterilir.
    /// Animasyon bitince asıl MainWindow oluşturulup gösterilir ve splash kapatılır - araya her
    /// zaman en az bir pencere açıkken geçildiği için (splash açıkken MainWindow henüz yok, ama
    /// MainWindow gösterildikten SONRA splash kapanıyor) varsayılan ShutdownMode.OnLastWindowClose
    /// erken bir kapanmayı tetiklemez.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!ClaimSingleInstance())
        {
            // Zaten çalışan bir örnek var: onu öne getir ve bu örnekten sessizce çık.
            SignalExistingInstance();
            Shutdown();
            return;
        }

        // Dil, HERHANGİ bir pencere oluşturulmadan önce yüklenmeli: açılış ekranı da çeviri
        // bağlamaları kullanıyor ve bağlamalar ilk okundukları anda sözlüğü hazır bulmalı.
        Services.Loc.Initialize(MainWindow_LanguagePreference());

        // Steam: başarısızlık NORMAL bir durumdur (istemci kapalı, oyun Steam dışından açıldı,
        // native DLL yok). Böyle bir durumda boş köprüye düşülür ve oyun birebir aynı çalışır.
        Services.SteamService.Initialize();

        var splash = new SplashWindow();
        splash.LoadingCompleted += (s, args) =>
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            splash.Close();
        };
        splash.Show();
    }

    /// <summary>Kaydedilmiş dil tercihi (yoksa null - o durumda işletim sisteminin dili kullanılır).</summary>
    private static string MainWindow_LanguagePreference() => AutoMotion.MainWindow.LoadLanguagePreference();

    /// <summary>
    /// Tek örnek kilidini almaya çalışır. Kilit alınabildiyse (ilk örnek) true döner ve ikinci bir
    /// örneğin "beni öne getir" sinyalini dinleyen arka plan iş parçacığı başlatılır.
    ///
    /// Eskiden burada Process.GetProcessesByName(...).Kill() ile diğer AutoMotion süreçleri ZORLA
    /// öldürülüyordu. Bu bir geliştirme kolaylığı hack'iydi (eski exe dosyayı kilitleyip yeni
    /// derlemenin üzerine yazılmasını engelliyordu) ve üretime kalmıştı. Üç ciddi sorunu vardı:
    /// (a) Kill() kaydetmeden öldürdüğü için çalışan örneğin ilerlemesi kayboluyordu - üstelik o an
    /// bir otomatik kayıt yazılıyorsa kayıt dosyası da yarım kalıp bozuluyordu;
    /// (b) kullanıcının makinesindeki "AutoMotion" adlı BAŞKA bir süreci de öldürebiliyordu;
    /// (c) oyunu yeniden başlatan/doğrulayan bir başlatıcıda iki süreç kısa süre örtüştüğünde
    /// yenisi eskisini infaz ediyordu. Artık kimse öldürülmüyor.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        try
        {
            singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                singleInstanceMutex.Dispose();
                singleInstanceMutex = null;
                return false;
            }

            activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateExistingEventName);
            var listener = new Thread(ActivationListenerLoop)
            {
                IsBackground = true,
                Name = "AutoMotion.SingleInstanceListener"
            };
            listener.Start();
            return true;
        }
        catch (Exception ex)
        {
            // Tek örnek kontrolü kritik değil - başarısız olursa uygulama yine de açılmalı.
            LogError(ex);
            return true;
        }
    }

    private void ActivationListenerLoop()
    {
        while (true)
        {
            try
            {
                activateEvent.WaitOne();
                Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
            }
            catch
            {
                return; // kapanış sırasında handle atıldıysa iş parçacığı sonlanır
            }
        }
    }

    private void ActivateMainWindow()
    {
        var window = MainWindow;
        if (window == null) return;

        try
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Show();
            window.Activate();
        }
        catch { /* pencere kapanıyor olabilir */ }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateExistingEventName, out var handle))
            {
                using (handle) handle.Set();
            }
        }
        catch { /* mevcut örnek kapanıyor olabilir - yeni örnek yine de sessizce çıkmalı */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Steam kapanışta bekleyen istatistikleri sunucuya işler (bkz. SteamworksBridge.Shutdown).
        Services.SteamService.Shutdown();

        try
        {
            activateEvent?.Dispose();
            singleInstanceMutex?.ReleaseMutex();
            singleInstanceMutex?.Dispose();
        }
        catch { }

        base.OnExit(e);
    }

    // ------------------------------------------------------------------ hata yönetimi

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogError(ex);
    }

    // Oyun döngüsü CompositionTarget.Rendering'e bağlı olduğundan saniyede 60-144 kez çalışır.
    // Render yolunda KALICI bir hata oluşursa (ör. bozuk bir özel araç PNG'si) eskiden her karede
    // tam bir yığın izi log dosyasına ekleniyordu: dakikalar içinde yüzlerce MB, saatler içinde
    // dolu bir disk - üstelik kullanıcıya hiçbir uyarı gitmeden. Aşağıdaki üç mekanizma bunu keser:
    // 1) aynı hata imzası için oran sınırlama, 2) log dosyası boyut tavanı + döndürme,
    // 3) hata gerçekten döngüye girerse tek seferlik görünür uyarı (devre kesici).
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan SameErrorCooldown = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<string, (DateTime lastLogged, int suppressed)> errorRates = new();
    private static readonly object logLock = new();
    private static int errorBurstCount;
    private static DateTime errorBurstWindowStart = DateTime.UtcNow;
    private static bool circuitBreakerTripped;

    private static void LogError(Exception ex)
    {
        if (ex == null) return;

        try
        {
            string signature = ex.GetType().FullName + "|" + ex.StackTrace?.Split('\n')[0]?.Trim();

            lock (logLock)
            {
                var now = DateTime.UtcNow;

                // --- devre kesici: kısa pencerede çok fazla hata = döngüye girmiş demektir
                if ((now - errorBurstWindowStart) > TimeSpan.FromSeconds(5))
                {
                    errorBurstWindowStart = now;
                    errorBurstCount = 0;
                }
                errorBurstCount++;
                bool shouldWarnUser = errorBurstCount > 40 && !circuitBreakerTripped;
                if (shouldWarnUser) circuitBreakerTripped = true;

                // --- oran sınırlama: aynı hata 10 saniyede bir kez yazılır, arası sayılır
                if (errorRates.TryGetValue(signature, out var state)
                    && (now - state.lastLogged) < SameErrorCooldown)
                {
                    errorRates[signature] = (state.lastLogged, state.suppressed + 1);
                    if (shouldWarnUser) WarnUser(ex);
                    return;
                }

                int suppressed = errorRates.TryGetValue(signature, out var prev) ? prev.suppressed : 0;
                errorRates[signature] = (now, 0);

                WriteLogEntry(ex, suppressed);

                if (shouldWarnUser) WarnUser(ex);
            }
        }
        catch
        {
            // Log yazılamıyorsa dahi uygulama çökmemeli
        }
    }

    private static void WriteLogEntry(Exception ex, int suppressedCount)
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion");
        Directory.CreateDirectory(logDir);
        string logPath = Path.Combine(logDir, "error.log");

        RotateLogIfTooLarge(logPath);

        string suffix = suppressedCount > 0
            ? $" (önceki {suppressedCount} tekrar bastırıldı)"
            : "";
        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{suffix} {ex}\n\n");
    }

    private static void RotateLogIfTooLarge(string logPath)
    {
        try
        {
            var info = new FileInfo(logPath);
            if (!info.Exists || info.Length < MaxLogBytes) return;

            string archived = logPath + ".1";
            if (File.Exists(archived)) File.Delete(archived);
            File.Move(logPath, archived);
        }
        catch { /* döndürme başarısız olsa da yazmayı denemeye devam et */ }
    }

    /// <summary>Hata döngüye girdiğinde kullanıcıya TEK sefer görünür uyarı verir - sessizce donmuş
    /// gibi görünen bir uygulama yerine ne olduğunu ve loga nereden bakacağını söyler.</summary>
    private static void WarnUser(Exception ex)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion");

            Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    "AutoMotion tekrarlayan bir hatayla karşılaştı ve bazı görsel efektler düzgün " +
                    "çalışmıyor olabilir.\n\n" +
                    $"Hata: {ex.GetType().Name} - {ex.Message}\n\n" +
                    $"Ayrıntılar: {Path.Combine(logDir, "error.log")}\n\n" +
                    "Uygulamayı yeniden başlatmanız önerilir.",
                    "AutoMotion - Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }));
        }
        catch { }
    }
}
