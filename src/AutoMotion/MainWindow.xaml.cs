using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoMotion.Expansions;
using AutoMotion.Models;
using AutoMotion.Services;
using AutoMotion.Views;
using AutoMotion.Utilities;
using Microsoft.Win32;

namespace AutoMotion
{
    public partial class MainWindow : Window
    {
        private GameCore gameCore;

        /// <summary>Tüm kullanıcı tercihlerinin tek kaynağı (%AppData%\AutoMotion\settings.json).
        /// Eskiden dil, panel düzeni, onboarding işareti ve çarpanlar ayrı ayrı dosyalarda/kayıt
        /// dosyasının içinde tutuluyordu - bkz. Services/SettingsService.cs.</summary>
        private readonly SettingsService settings = SettingsService.Instance;

        private SaveManager saveManager;
        private SoundEngine soundEngine;
        private ThemeEngine themeEngine;
        private ExpansionManager expansionManager;
        private MissionExpansion missionExpansion;

        private Vehicle draggedVehicle;
        private Point dragOffset;
        private Vehicle selectedVehicle;
        private Point lastMousePos;

        // Sürüş Modu (1:1 kontrol): drivenVehicle != null iken ok tuşları/WASD ile doğrudan
        // kontrol edilir, AI durur. pressedDriveKeys her tikte okunup ApplyDriveInput'a aktarılır.
        private Vehicle drivenVehicle;
        private readonly HashSet<Key> pressedDriveKeys = new();
        private double smoothedThrottle;
        private double smoothedSteerInput;

        // Sürüş modunda periyodik motor sesi için sayaç (her karede çalmasın diye)
        private double engineSoundCooldown = 0;

        // Gece modu: null=otomatik (saate göre), true=zorla gece, false=zorla gündüz
        private bool? forcedNightMode = null;

        // İşlemci (CPU) Takometresi - sistem yükü arttıkça hafif "hararet" görsel efekti/uyarısı
        private System.Diagnostics.PerformanceCounter cpuCounter;
        private double currentCpuLoad = 0;
        private int hardwareCheckCooldown = 0;
        private int lastHeatWarningTick = -100000;
        private int lastBatteryWarningTick = -100000;

        // Müzik Süspansiyon Senkronizasyonu: sistem çıkış sesi (WASAPI loopback) pasif olarak
        // dinlenir - ses KAYDEDİLMEZ/gönderilmez, sadece anlık seviye okunup araçları zıplatmak
        // için kullanılır. Kayıt başlatılamazsa (izin/donanım yoksa) özellik sessizce kapalı kalır.
        private NAudio.Wave.WasapiLoopbackCapture audioCapture;
        private volatile float currentAudioLevel = 0f;
        private int musicBounceCooldown = 0;
        private bool musicSyncEnabled = true; // genel ses kapatmadan bağımsız, kendi anahtarı

        // Efekt Yoğunluğu: çok araçlı sahnelerde performans için lastik izi/çamur/parçacık/silecek/
        // neon yansıma gibi ek görsel efektleri azaltır (Düşük modda) veya tam açar (Normal).
        private bool lowEffectsMode = false;

        // Kirlenme Efekti: kapatıldığında araç gövdesindeki Dirtiness'e bağlı çamur lekesi ÇİZİLMEZ
        // (Dirtiness değeri kendisi birikmeye devam eder - sadece görsel gösterim kapanır), böylece
        // kullanıcı isterse temiz görünümü koruyabilir.
        private bool dirtEffectEnabled = false;

        // Trafik sıkışıklığı: kenarda uzun süre bekleyen araçlar korna çalar
        private readonly Dictionary<Vehicle, double> jamStuckTicks = new();

        // İmleç Trafik Değneği: basılı tutulunca imleç "Dur" tabelasına döner
        private bool stopSignActive = false;

        // Detaylı Yıkama: "W" basılıyken fare sürüklenip araca değince kir azalır (sünger hareketi)
        private bool washModeActive = false;

        // Fiziksel Rampa Çizimi: orta fare tuşuyla çizilen çizgiler geçici zemin gibi davranır
        private readonly List<(Point A, Point B, double Life)> physicsRamps = new();
        private bool isDrawingRamp = false;
        private Point lastRampPoint;

        // Manuel Doldurma: benzin bidonunu farenin ucunda taşıyıp bir araca tıklayarak doldurma
        private bool fuelCanActive = false;

        // Araç Okşama: fare aracın üzerinde ileri-geri sürtülünce tepki verir
        private readonly Dictionary<Vehicle, (double lastX, int direction, int flips, int windowTicks)> pettingState = new();

        // Araba Buluşması: belirli aralıklarla tüm araçlar ekranın ortasında toplanır
        private int nextCarMeetTick = 36000; // ilk buluşma ~10 dk sonra (60fps)
        private bool carMeetActive = false;
        private double carMeetTicksLeft = 0;

        // Ekranlar Arası Atlama Rampası: çoklu-monitör modunda ekran sınırlarını geçen araçlar
        // küçük bir "rampadan uçma" hissi alır (yukarı zıplama + kıvılcım)
        private readonly List<double> monitorBoundaryX = new();
        // Çoklu-monitör modunda her ekranın kendi (Left, Right, FloorY) - farklı çözünürlük/konumdaki
        // ikincil ekranlarda tek bir global zemin (pencere yüksekliği) yanlış olurdu (bkz. FloorYForX).
        private readonly List<(double Left, double Right, double FloorY)> monitorFloors = new();
        private readonly Dictionary<Vehicle, double> lastVehicleX = new();

        // Performans: 144 FPS'te her karede/her damla-tanede yeniden allocate edilmek yerine,
        // sabit renkli fırçalar bir kere oluşturulup Freeze() ile donduruluyor - WPF donmuş
        // (frozen) fırçaları thread-safe ve değişmez kabul ettiğinden hem GC baskısını azaltır
        // hem de invalidation takibini atlar. Görsel sonuç birebir aynı, sadece maliyeti düşük.
        private static readonly Brush RainDropBrush = CreateFrozenBrush(Color.FromArgb(90, 160, 220, 255));
        private static readonly Brush SnowFlakeBrush = CreateFrozenBrush(Color.FromArgb(210, 255, 255, 255));
        private static Brush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static readonly Brush HeadlightConeBrush = CreateFrozenRadialBrush(
            Color.FromArgb(85, 255, 255, 220), Color.FromArgb(0, 255, 255, 220));
        private static readonly Brush BrakeLightBrush = CreateFrozenRadialBrush(
            Color.FromArgb(230, 255, 40, 40), Color.FromArgb(0, 255, 40, 40));
        /// <summary>Geri vites lambası - gerçek araçlardaki gibi beyaza yakın soğuk bir ton.</summary>
        private static readonly Brush ReverseLightBrush = CreateFrozenRadialBrush(
            Color.FromArgb(225, 255, 255, 245), Color.FromArgb(0, 255, 255, 245));
        /// <summary>Dönüş sinyali (flaşör) - kehribar/amber.</summary>
        private static readonly Brush SignalLightBrush = CreateFrozenRadialBrush(
            Color.FromArgb(240, 255, 176, 32), Color.FromArgb(0, 255, 176, 32));
        /// <summary>Ağır hasarlı motordan çıkan koyu duman.</summary>
        private static readonly Brush DamageSmokeBrush = CreateFrozenRadialBrush(
            Color.FromArgb(150, 60, 60, 65), Color.FromArgb(0, 60, 60, 65));
        private static Brush CreateFrozenRadialBrush(Color inner, Color outer)
        {
            var b = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection { new GradientStop(inner, 0), new GradientStop(outer, 1) }
            };
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Parçacık parıltısı için renk bazlı önbelleklenmiş radyal fırça.
        ///
        /// Eskiden her parçacık için ayrı bir <c>DropShadowEffect</c> oluşturuluyordu. Bir efekt,
        /// WPF'te eleman başına AYRI bir GPU bulanıklık geçişi demektir - 500 parçacıkta karede 500
        /// shader geçişi, 144 Hz'de sürdürülemez bir yük. Merkezi opak, kenarı saydam bir radyal
        /// gradyanla neredeyse aynı görsel sonuç, shader geçişi olmadan elde ediliyor. Fırçalar
        /// dondurulmuş (frozen) ve renk anahtarına göre yeniden kullanılıyor.
        /// </summary>
        private readonly Dictionary<int, Brush> particleGlowBrushCache = new();
        private Brush GetParticleGlowBrush(Color c)
        {
            int key = (c.R << 16) | (c.G << 8) | c.B;
            if (particleGlowBrushCache.TryGetValue(key, out var cached)) return cached;

            var gradient = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(255, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(190, c.R, c.G, c.B), 0.35),
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
                }
            };
            gradient.Freeze();

            // ÖLÇÜLEN DARBOĞAZ: gradyanı fırça olarak bırakmak, her parçacığın her karede yeniden
            // RASTERLEŞTİRİLMESİ demekti. Profil: aynı sahnede 87 parçacık süreç CPU'sunun 52
            // puanını yiyordu (parçacıklar açık %95.1, kapalı %43.2 - çekirdeğin yüzdesi).
            // Gradyan bir kez küçük bir bitmap'e çizilip ImageBrush olarak kullanılıyor: aynı
            // görüntü, ama kare başına iş "gradyan hesapla" yerine "hazır görüntüyü ölçekleyip bas".
            var brush = BakeBrushToBitmap(gradient, 64);
            particleGlowBrushCache[key] = brush;
            return brush;
        }

        /// <summary>Bir fırçayı verilen çözünürlükte kare bir bitmap'e çizip dondurulmuş bir
        /// <see cref="ImageBrush"/> olarak döndürür. Parçacıklar ekranda küçük çizildiğinden 64x64
        /// yeterli; ölçekleme sırasında yumuşak kalması için doğrusal örnekleme kullanılır.</summary>
        private static Brush BakeBrushToBitmap(Brush source, int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(source, null, new Rect(0, 0, size, size));

            var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();

            var brush = new ImageBrush(target) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
            brush.Freeze();
            return brush;
        }

        // Araca özgü neon rengine göre değişen alt-parlama fırçası: renk sabit olduğu sürece her
        // karede yeniden oluşturmak yerine renk anahtarına göre bir kez üretilip yeniden kullanılır.
        private readonly Dictionary<(string color, bool bright), Brush> neonGlowBrushCache = new();
        private Brush GetNeonGlowBrush(string neonColorHex, bool hasLightsDecoration)
        {
            var key = (neonColorHex, hasLightsDecoration);
            if (neonGlowBrushCache.TryGetValue(key, out var cached)) return cached;

            var neonColor = (Color)ColorConverter.ConvertFromString(neonColorHex);
            var brush = CreateFrozenRadialBrush(
                Color.FromArgb(120, neonColor.R, neonColor.G, neonColor.B),
                Color.FromArgb(0, neonColor.R, neonColor.G, neonColor.B));
            neonGlowBrushCache[key] = brush;
            return brush;
        }

        private int lastAchievementUnlockedCount = -1;
        public class AchievementRow
        {
            public string Label { get; set; }
            public bool Unlocked { get; set; }
        }

        private bool IsNightActive()
        {
            if (forcedNightMode.HasValue) return forcedNightMode.Value;

            // Eskiden burası sert bir eşikti (saat >= 20): karanlık 19:00'dan itibaren yumuşakça
            // inerken FARLAR ve gece katmanı 20:00'de bir anda yanıyordu. Artık ikisi de aynı
            // sürekli katsayıdan besleniyor (bkz. DayNight ve GetNightFactor kullanımları).
            return DayNight.IsDarkEnough(GetNightFactor());
        }

        /// <summary>Gündüz/gece arasında ANİ geçiş yerine akıcı bir alacakaranlık/şafak geçişi
        /// için 0 (tam gündüz) ile 1 (tam gece) arasında yumuşak bir katsayı döndürür. 19:00-20:00
        /// arası alacakaranlık (karanlığa doğru artar), 06:00-07:00 arası şafak (aydınlığa döner).</summary>
        private double GetNightFactor()
        {
            if (forcedNightMode.HasValue) return forcedNightMode.Value ? 1.0 : 0.0;

            var now = DateTime.Now;
            return DayNight.NightFactorAt(now.Hour + now.Minute / 60.0);
        }
        private int currentTheme = 0;
        private bool panelsVisible = true;
        private bool dockExpanded = true;
        private bool allUIHidden = false;

        private Border draggedPanel;
        private Point panelDragOffset;
        private readonly HashSet<Border> pinnedPanels = new();

        // Panel Ayırma (Detach): konsolide "☰ Paneller" sekmeli penceresindeki her sekme, ↗️
        // butonuyla kendi bağımsız/sürüklenebilir/sabitlenebilir mini penceresine "ayrılabilir" -
        // Sabitle (📌) butonu ancak panel bu şekilde ayrılıp Canvas'a (PanelsContainer) geri
        // döndüğünde bir işe yarar (sekme içindeyken sürüklenecek bağımsız bir konumu yok).
        private readonly Dictionary<Border, TabItem> panelHomeTabs = new();
        private readonly Dictionary<Border, double> panelOriginalWidths = new();
        private readonly HashSet<Border> detachedPanels = new();
        private int detachCascadeIndex = 0;

        // Parçacık Efekt Sistemi (Neon Trail)
        private readonly List<VehicleParticle> particles = new();
        private readonly List<SkidMark> skidMarks = new();

        // Gerçek marka görselleri önbelleği (Araç Listesi entegrasyonu) - her kare yeniden yüklenmesin diye
        private readonly Dictionary<string, BitmapImage> vehicleImageCache = new();

        private readonly HashSet<string> failedImagePaths = new();

        /// <summary>
        /// Görseli yükler; başarısız olursa (eksik dosya, bozuk kaynak vb.) uygulamayı
        /// çökertmek yerine null döner ve aynı yolu bir daha denemez (fallback çizime düşülür).
        /// </summary>
        private BitmapImage GetVehicleBitmap(string imagePath)
        {
            if (vehicleImageCache.TryGetValue(imagePath, out var cached))
                return cached;
            if (failedImagePaths.Contains(imagePath))
                return null;

            try
            {
                // Mutlak dosya yolu (kullanıcının kendi eklediği araç görseli, ör. "C:\...") olduğu
                // gibi kullanılır; proje içi göreli yol ("/Assets/Vehicles/...") ise artık gömülü
                // WPF Resource DEĞİL - exe'nin yanına kopyalanan gerçek bir Content dosyası (bkz.
                // AutoMotion.csproj). Bu sayede kullanıcı PNG'yi Photoshop'ta güncelleyip uygulamayı
                // yeniden başlattığında, tam bir yeniden derleme gerekmeden en güncel görsel yüklenir.
                string fullPath = System.IO.Path.IsPathFullyQualified(imagePath)
                    ? imagePath
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath.TrimStart('/', '\\').Replace('/', System.IO.Path.DirectorySeparatorChar));

                if (!System.IO.File.Exists(fullPath))
                    throw new System.IO.FileNotFoundException("Araç görseli bulunamadı: " + fullPath);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                bmp.Freeze();
                vehicleImageCache[imagePath] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                failedImagePaths.Add(imagePath);
                try
                {
                    string logDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion");
                    System.IO.Directory.CreateDirectory(logDir);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "error.log"),
                        $"[{DateTime.Now}] Görsel yüklenemedi: {imagePath}\n{ex}\n\n");
                }
                catch { }
                return null;
            }
        }

        // Kullanıcının içe aktardığı özel araç görselleri (marka anahtarı -> VehicleVisual)
        private readonly Dictionary<string, VehicleVisual> customVisuals = new();

        private VehicleVisual GetVisualForVehicle(Vehicle vehicle)
        {
            if (!string.IsNullOrEmpty(vehicle.Brand) && customVisuals.TryGetValue(vehicle.Brand, out var custom))
                return custom;
            return VehicleVisuals.Get(vehicle.Type, vehicle.Brand);
        }

        public class VehicleParticle
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double VelocityX { get; set; }
            public double VelocityY { get; set; }
            public double Size { get; set; }
            public double Opacity { get; set; }
            public Color Color { get; set; }
        }

        /// <summary>Zeminde yavaşça silinen lastik izi noktası (sert fren/çarpışma sırasında bırakılır).</summary>
        public class SkidMark
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Angle { get; set; }
            public double Width { get; set; }
            public double Opacity { get; set; } = 0.55;
        }

        /// <summary>Hızlı giden araçların egzozundan çıkan, yükselip dağılan hafif duman parçacığı.</summary>
        public class ExhaustPuff
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double VelX { get; set; }
            public double VelY { get; set; }
            public double Size { get; set; }
            public double Opacity { get; set; } = 0.35;
        }
        private readonly List<ExhaustPuff> exhaustPuffs = new();

        /// <summary>Ağır hasarlı araçların motorundan çıkan koyu duman (egzoz dumanıyla aynı veri
        /// yapısını kullanır ama farklı renk/fizik ile çizilir - bkz. RenderGame).</summary>
        private readonly List<ExhaustPuff> damageSmoke = new();

        // Parçacık Üst Sınırları (performans güvenlik ağı)
        //
        // Her efekt listesi eskiden SINIRSIZDI: her araç her karede bir parçacık üretiyor, ömrü ~25
        // kare olduğundan araç başına ~25 kalıcı parçacık birikiyordu. 20 araçta bu ~500 parçacık
        // demek ve her biri ayrı bir Ellipse + DropShadowEffect (GPU'da eleman başına ayrı bulanıklık
        // geçişi) olarak çiziliyordu - 144 Hz'de sürdürülebilir değil. Sınırlar aşıldığında EN ESKİ
        // parçacıklar düşürülür (görsel olarak zaten en soluk olanlar).
        private const int MaxParticles = 220;
        private const int MaxExhaustPuffs = 120;
        private const int MaxDamageSmoke = 90;
        private const int MaxSkidMarks = 160;

        /// <summary>Bir efekt listesini üst sınıra kırpar - baştan (en eski) siler.</summary>
        private static void TrimEffectList<T>(List<T> list, int max)
        {
            if (list.Count <= max) return;
            list.RemoveRange(0, list.Count - max);
        }

        /// <summary>Tüm parçacık listelerini kendi üst sınırlarına kırpar. Her karede render'ın
        /// başında çağrılır.</summary>
        private void TrimAllEffectLists()
        {
            TrimEffectList(particles, MaxParticles);
            TrimEffectList(exhaustPuffs, MaxExhaustPuffs);
            TrimEffectList(damageSmoke, MaxDamageSmoke);
            TrimEffectList(skidMarks, MaxSkidMarks);
        }

        /// <summary>Sürüş modunda yüksek hızda beliren, kısa süreliğine geriye doğru kayan çizgi -
        /// "hız hissi" (sense of speed) için, gerçek araç oyunlarındaki hız çizgilerinin sadeleştirilmiş hali.</summary>
        public class SpeedLine
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double VelX { get; set; }
            public double VelY { get; set; }
            public double Length { get; set; }
            public double Opacity { get; set; } = 0.5;
        }
        private readonly List<SpeedLine> speedLines = new();

        /// <summary>İki araç zeminde yan yana/yakın gelince rastgele bir ihtimalle "selamlaşır"
        /// (korna + kısa 👋 balonu) - Shimeji tarzı çoklu maskotların birbirleriyle etkileşimine
        /// benzer küçük bir canlılık hissi katar.</summary>
        public class GreetingPop
        {
            public double X { get; set; }
            public double Y { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public const double DurationSeconds = 1.1;
        }
        private readonly List<GreetingPop> greetingPops = new();
        private readonly Dictionary<Vehicle, DateTime> lastGreetTime = new();
        private DateTime lastGreetScan = DateTime.MinValue;

        // Dinamik Hava Durumu (Yağmur)
        private bool isRaining = false;
        private readonly List<RainDrop> rainDrops = new();
        /// <summary>Bir yağmur damlası zemine ulaştığında kısa süreliğine genişleyip solan halka -
        /// yağmurun "yüzeye çarpma" hissini güçlendirir (öncesinde damlalar sessizce kayboluyordu).</summary>
        private readonly List<RainSplash> rainSplashes = new();

        // Dinamik Hava Durumu (Kar) - yağmura simetrik "Kış Teması" ağırlıksız/yavaş düşen kar taneleri
        private bool isSnowing = false;
        private readonly List<SnowFlake> snowFlakes = new();

        // Yağmur/kar doğuş oranını kare hızından bağımsız tutan kesirli birikimler
        private double rainSpawnAccumulator;
        private double snowSpawnAccumulator;

        // Engel Sürükleme Sistem özellikleri
        private Obstacle draggedObstacle = null;
        private Point obstacleDragOffset;

        // Gelişmiş Seçim ve Rota (Pathing) Özellikleri
        private readonly List<Vehicle> selectedVehicles = new();
        private bool isDragSelecting = false;
        private Point dragSelectionStart;
        private Rectangle selectionBox = null;

        public class RainDrop
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Speed { get; set; }
            public double Length { get; set; }
        }

        public class RainSplash
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Age { get; set; }
        }

        public class SnowFlake
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Speed { get; set; }
            public double Size { get; set; }
            public double Drift { get; set; }
            public double DriftPhase { get; set; }
        }

        // Win32 API pencereleri okumak için (Window Clambering)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private System.Windows.Forms.NotifyIcon trayIcon;

        /// <summary>
        /// Pencereyi (ve dolayısıyla araçların gezineceği alanı) ekranın tamamına yayar - sadece
        /// ALT kenar görev çubuğunun üstünde durur, sol/sağ/üst kenarlar tam ekran sınırına kadar
        /// gider (WorkArea kullanmanın aksine, görev çubuğu sadece alt kenarı kısıtlar).
        /// </summary>
        private void ApplyFullScreenBoundsTaskbarOnBottom()
        {
            var workArea = SystemParameters.WorkArea;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = workArea.Bottom; // görev çubuğunun üst kenarı
        }

        // Ana pencere Topmost=true olduğu için normal ToolTip popup'ları bazen bu pencerenin
        // ARKASINDA açılıp görünmez oluyordu (WPF'in bilinen bir Topmost+ToolTip sorunu) - bu
        // yüzden butonların üstüne gelince "ne olduğu" hiç görünmüyordu. Her ToolTip açıldığında
        // kendi native penceresini de zorla topmost yaparak çözüyoruz.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        static MainWindow()
        {
            EventManager.RegisterClassHandler(typeof(ToolTip), FrameworkElement.LoadedEvent, new RoutedEventHandler(ToolTip_ForceTopmost));
        }

        private static void ToolTip_ForceTopmost(object sender, RoutedEventArgs e)
        {
            if (sender is ToolTip tooltip && PresentationSource.FromVisual(tooltip) is System.Windows.Interop.HwndSource hwndSource)
            {
                SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Masaüstünün tamamını kapla - sadece alt kenar görev çubuğunun üstünde durur,
            // sol/sağ/üst kenarlar ekranın gerçek sınırına kadar uzanır.
            ApplyFullScreenBoundsTaskbarOnBottom();

            InitializeTrayIcon();

            // Görev çubuğu ikonu için pencere handle'ı henüz yok - asıl uygulama (WM_SETICON)
            // Window_Loaded içindeki ApplyTaskbarIconWorkaround() ile yapılıyor (bkz. aşağıda),
            // ancak SourceInitialized'da da tekrar deniyoruz: bazı ortamlarda Loaded olayı
            // görev çubuğu düğmesi zaten oluşturulduktan biraz sonra tetikleniyor, bu erken
            // deneme ilk anda doğru ikonun görünme ihtimalini artırıyor.
            SourceInitialized += (s, e) =>
            {
                ApplyTaskbarIconWorkaround();
                mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            };

            // Güvenlik önlemi: tıklama-geçirgenliği tespitinde ileride başka bir mantık hatası
            // çıkarsa bile kullanıcı asla kalıcı olarak kilitlenmesin diye, pencere odağı her
            // kazandığında (görev çubuğu ikonuna tıklama, Alt+Tab) tıklama-geçirgenlik ANINDA
            // zorla kapatılır - odaklı bir pencerenin tıklamayı yutmaması hiçbir zaman doğru olamaz.
            Activated += (s, e) => SetClickThrough(false);
        }

        // ---- Tam Tıklama-Geçirgenliği (Click-Through) ----
        // Kullanıcı isteği: arka plan tamamen şeffaf (Tam Şeffaf/Şeffaf tema) olduğunda, üstünde
        // hiçbir araç/panel olmayan boş alanlara tıklanınca fare tıklaması bu pencereyi DEĞİL,
        // arkadaki gerçek masaüstü/uygulamayı almalı (VPet ve benzeri masaüstü dostlarının
        // standart davranışı). WPF'in kendi AllowsTransparency+Background=Transparent özelliği
        // sadece GÖRSEL olarak şeffaflık sağlar, fare olaylarını yine de bu pencere yutar - gerçek
        // tıklama geçirgenliği için Win32 WS_EX_TRANSPARENT bayrağı gerekir. Bu bayrak set/kaldır
        // edilirken pencere ya TAMAMEN tıklama-geçirgen ya da TAMAMEN normal olur; bu yüzden fare
        // imlecinin bir araç/panel üzerinde olup olmadığını her karede kontrol edip bayrağı buna
        // göre anlık olarak açıp kapatıyoruz (araç/panel üzerindeyken normal, boşluktayken geçirgen).
        private IntPtr mainHwnd = IntPtr.Zero;
        private bool clickThroughActive = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void SetClickThrough(bool enable)
        {
            if (mainHwnd == IntPtr.Zero || enable == clickThroughActive) return;
            int exStyle = GetWindowLong(mainHwnd, GWL_EXSTYLE);
            exStyle = enable ? (exStyle | WS_EX_TRANSPARENT) : (exStyle & ~WS_EX_TRANSPARENT);
            SetWindowLong(mainHwnd, GWL_EXSTYLE, exStyle);
            clickThroughActive = enable;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Win32Point lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Win32Point { public int X; public int Y; }

        /// <summary>Sadece tamamen şeffaf temalarda (Şeffaf/Tam Şeffaf) çalışır - renkli/koyu bir
        /// arka plan teması seçiliyken tıklama geçirgenliği anlamsız olurdu (görünen renkli katmana
        /// "görünmez" tıklamak kafa karıştırıcı olurdu), o yüzden o durumda her zaman normal davranır.</summary>
        private Win32Point lastClickThroughCursor = new() { X = int.MinValue, Y = int.MinValue };
        private DateTime lastClickThroughEval = DateTime.MinValue;
        private double cachedDpiScaleX, cachedDpiScaleY;

        /// <summary>Pencere başka DPI'lı bir monitöre taşındığında önbelleklenmiş ölçek geçersizleşir.</summary>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            cachedDpiScaleX = 0; // bir sonraki değerlendirmede yeniden okunur
        }

        private void UpdateClickThrough()
        {
            bool themeIsFullyTransparent = currentTheme == 0 || currentTheme == 6;
            if (!themeIsFullyTransparent) { SetClickThrough(false); return; }
            if (mainHwnd == IntPtr.Zero) return;

            // Herhangi bir fare tuşu basılıyken (sürükleme, çoklu seçim kutusu, rampa çizimi vb.
            // devam eden bir etkileşim) bayrağı DEĞİŞTİRME - aksi halde etkileşim ortasında pencere
            // aniden tıklama-geçirgen olup fare olaylarını kaybedebilir.
            if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed)
                return;

            // KRİTİK HATA (önceki sürümde): WPF'in Mouse.GetPosition() metodu OS'a canlı sormaz -
            // InputManager'ın EN SON ALDIĞI fare olayını önbellekten döndürür. Pencere tıklama-geçirgen
            // (WS_EX_TRANSPARENT) hale gelir gelmez bu pencereye artık HİÇ fare olayı gelmiyor, yani
            // konum tamamen DONUYOR. Donan nokta genelde "boşluk" olduğundan (zaten o yüzden geçirgen
            // olmuştu), kod bir daha asla "araç/panel üstündeyim" sonucuna varamıyor ve pencere
            // SONSUZA DEK tıklama-geçirgen kalıp kilitleniyordu (görev çubuğu ikonu, kapat butonu,
            // HİÇBİR ŞEY tıklanamıyordu). Çözüm: ham Win32 GetCursorPos ile HER ZAMAN canlı sorgu -
            // pencere fare olayı alsın almasın çalışır. ScreenToClient fiziksel piksel döndürdüğü için
            // DPI ölçeğine bölünerek WPF'in mantıksal birimine çevrilir (ör. %125 ölçeklemede /1.25).
            if (!GetCursorPos(out var screenPt)) return;

            // İmleç kıpırdamadıysa sonuç da değişemez: aşağıdaki iş (DPI hesabı, filo taraması,
            // panel dikdörtgenleri) her karede tekrarlanıyordu. Yine de tamamen imlece bağlamıyoruz -
            // panel taşınıp boyutlandığında imleç sabitken de durum değişebilir, o yüzden kısa bir
            // aralıkla (150 ms) her hâlükârda tazeleniyor.
            bool cursorMoved = screenPt.X != lastClickThroughCursor.X || screenPt.Y != lastClickThroughCursor.Y;
            if (!cursorMoved && (DateTime.UtcNow - lastClickThroughEval).TotalMilliseconds < 150) return;

            lastClickThroughCursor = screenPt;
            lastClickThroughEval = DateTime.UtcNow;

            var clientPt = screenPt;
            if (!ScreenToClient(mainHwnd, ref clientPt)) return;

            // DPI ölçeği pencere taşınmadıkça değişmez; her karede sormak yerine önbelleklenir
            // (monitörler arası taşımada OnDpiChanged tazeliyor).
            if (cachedDpiScaleX <= 0)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                cachedDpiScaleX = dpi.DpiScaleX;
                cachedDpiScaleY = dpi.DpiScaleY;
            }

            Point pt = new Point(clientPt.X / cachedDpiScaleX, clientPt.Y / cachedDpiScaleY);

            bool overInteractive = IsPointOverVehicleOrObstacle(pt) || IsPointOverVisiblePanel(pt);
            SetClickThrough(!overInteractive);
        }

        private bool IsPointOverVehicleOrObstacle(Point pt)
        {
            const double margin = 6;
            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (pt.X >= vehicle.X - vehicle.Width / 2 - margin && pt.X <= vehicle.X + vehicle.Width / 2 + margin &&
                    pt.Y >= vehicle.Y - vehicle.Height / 2 - margin && pt.Y <= vehicle.Y + vehicle.Height / 2 + margin)
                    return true;
            }
            foreach (var obs in gameCore.Obstacles)
            {
                if (pt.X >= obs.X - obs.Width / 2 - margin && pt.X <= obs.X + obs.Width / 2 + margin &&
                    pt.Y >= obs.Y - obs.Height / 2 - margin && pt.Y <= obs.Y + obs.Height / 2 + margin)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// İmlecin altında GERÇEKTEN bir arayüz öğesi var mı?
        ///
        /// Eskiden bu, her yüzeyin dikdörtgeni hesaplanıp (<c>PointToScreen</c> + ActualWidth/Height)
        /// nokta o dikdörtgende mi diye bakılarak çözülüyordu. ÖLÇÜLEN HATA: konsolide panelin
        /// İÇERİĞİ, panelin kendi kutusunu taşıyor - MasterPanel ekranda y=698'de bitiyor ama
        /// içindeki sekme kontrolü y=852'ye kadar çizilmeye devam ediyor. Dikdörtgen hesabı bu
        /// taşan bölgeyi "boş masaüstü" sayıp pencereyi orada tıklama-geçirgen yapıyordu: panelin
        /// görünen alt ~150 pikseli (BOYUTLANDIRMA TUTAMAKLARI dahil) hiçbir fare olayı almıyordu.
        /// Panel köşesinden sürükleyerek boyutlandırmanın "çalışmaması" bundandı.
        /// (<c>VisualTreeHelper.GetDescendantBounds</c> de çözmüyor: düzen kırpması yüzünden panelin
        /// kendi boyutunu döndürüyor.)
        ///
        /// Çözüm: dikdörtgen matematiği yerine WPF'in KENDİ hit-test'i. "Bu noktada çizili bir öğe
        /// var mı, varsa etkileşimli bir yüzeye mi ait?" sorusunu doğrudan soruyor; taşma, dönüşüm,
        /// ölçek ve düzensiz şekiller kendiliğinden doğru ele alınıyor.
        /// </summary>
        private bool IsPointOverVisiblePanel(Point pt)
        {
            foreach (var surface in InteractiveSurfaces())
            {
                if (surface == null || surface.Visibility != Visibility.Visible) continue;

                var r = RenderedRectOf(surface);
                if (r.IsEmpty) continue;

                // Kenar bandı KADAR dışarı taşırılır: kenardan boyutlandırma (bkz. DetectEdgeAt)
                // kenara dışarıdan yaklaşırken de çalışmalı. Taşırma olmadan pencere tam kenar
                // pikselinde tıklama-geçirgen kalıyordu - yani tutulmak istenen yer ölü oluyordu.
                // (Rect.Contains sağ/alt kenarda dışlayıcıdır, o sınır durumunu da bu kapsıyor.)
                r.Inflate(EdgeGrabThickness, EdgeGrabThickness);
                if (r.Contains(pt)) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // "GERÇEKTEN ÇİZİLEN ALAN" HESABI
        //
        // Bir panelin ekranda kapladığı yer, panelin DÜZEN KUTUSUNDAN büyük olabilir: konsolide
        // "Paneller" penceresinde içerik kutuyu ~150 piksel taşıyor (ölçüldü: panel kutusu y=698'de
        // bitiyor, içindeki sekme kontrolü y=852'ye kadar çiziliyor). WPF taşan kısmı ÇİZER ama
        // ne hit-test'e alır ne de GetDescendantBounds'a yansıtır (düzen kırpması).
        //
        // Sonuç, iki somut hataydı: (a) panelin görünen alt bölgesi "boş masaüstü" sayılıp pencere
        // orada tıklama-geçirgen yapılıyordu, (b) o bölgedeki boyutlandırma tutamağı/kenar hiçbir
        // fare olayı almıyordu. İkisi de aşağıdaki birleşim hesabıyla çözülüyor: görsel ağaç
        // gezilip TÜM alt öğelerin çizim sınırları birleştiriliyor.
        //
        // Maliyet: ağaç gezintisi her karede yapılamaz, o yüzden panel başına önbelleklenip
        // yalnızca boyut/ölçek değiştiğinde veya kısa bir süre geçtiğinde yenileniyor.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private readonly Dictionary<FrameworkElement, (Rect Rect, DateTime At)> renderedRectCache = new();

        private static readonly TimeSpan RenderedRectTtl = TimeSpan.FromMilliseconds(400);

        private Rect RenderedRectOf(FrameworkElement element)
        {
            if (renderedRectCache.TryGetValue(element, out var cached) && DateTime.UtcNow - cached.At < RenderedRectTtl)
                return cached.Rect;

            var rect = ComputeRenderedRect(element);
            renderedRectCache[element] = (rect, DateTime.UtcNow);
            return rect;
        }

        /// <summary>Öğenin ve tüm alt öğelerinin çizim sınırlarının birleşimi - PENCERE koordinatında.</summary>
        private Rect ComputeRenderedRect(FrameworkElement element)
        {
            try
            {
                var local = UnionDescendantBounds(element, 0);
                if (local.IsEmpty) return Rect.Empty;

                var topLeft = PointFromScreen(element.PointToScreen(local.TopLeft));
                var bottomRight = PointFromScreen(element.PointToScreen(local.BottomRight));
                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return Rect.Empty; // düzen henüz hazır değil
            }
        }

        /// <summary>Görsel ağacı gezip sınırları birleştirir (öğenin KENDİ koordinat uzayında).
        /// Derinlik sınırı, patolojik derin ağaçlarda maliyeti sabit tutmak için.</summary>
        private static Rect UnionDescendantBounds(Visual visual, int depth)
        {
            var bounds = VisualTreeHelper.GetDescendantBounds(visual);
            if (visual is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                bounds.Union(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));

            if (depth >= 12) return bounds;

            int count = VisualTreeHelper.GetChildrenCount(visual);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(visual, i) is not Visual child) continue;
                if (child is UIElement ui && ui.Visibility != Visibility.Visible) continue;

                var childBounds = UnionDescendantBounds(child, depth + 1);
                if (childBounds.IsEmpty) continue;

                // Çocuğun kutusunu ebeveynin uzayına taşı (offset + dönüşümler dahil).
                var t = child.TransformToAncestor(visual);
                bounds.Union(t.TransformBounds(childBounds));
            }

            return bounds;
        }

        /// <summary>
        /// Fare olaylarını ALMASI gereken tüm görünür yüzeyler.
        ///
        /// KRİTİK: "Panelden Ayır" ile bağımsız pencereye çıkarılan paneller de buraya DAHİL olmalı.
        /// Önceden yalnızca sabit bir liste (MasterPanel, modallar, dock) kontrol ediliyordu; ayrılan
        /// bir panelin üzerine gelindiğinde kod burayı "boş masaüstü" sanıp pencereyi tıklama-geçirgen
        /// (WS_EX_TRANSPARENT) yapıyordu. Sonuç: ayrılan panel HİÇ fare olayı almıyor, üzerindeki
        /// butonlar tıklanamıyordu - panel görünüyor ama tamamen ölü duruyordu.
        /// </summary>
        private IEnumerable<FrameworkElement> InteractiveSurfaces()
        {
            yield return MasterPanel;
            yield return DockWrapper;
            yield return SaveListModal;
            yield return SpawnModal;
            yield return HelpModal;
            yield return CustomDialogOverlay;
            yield return ToastPanel;

            // Ayrılmış paneller (bağımsız, sürüklenebilir mini pencereler)
            foreach (var panel in detachedPanels)
                yield return panel;
        }

        /// <summary>Kaydedilmiş dil tercihi; yoksa null (o durumda işletim sisteminin dili kullanılır).
        /// Pencere açılmadan ÖNCE (App.OnStartup) çağrıldığı için örnek alanına değil, birleşik ayar
        /// servisinin tekiline bakar.</summary>
        internal static string LoadLanguagePreference()
        {
            string code = SettingsService.Instance.Current.Language;
            return Loc.IsSupported(code) ? code : null;
        }

        private void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (soundEngine == null) return;
            soundEngine.MasterVolume = (float)e.NewValue;
            if (MasterVolumeTxt != null) MasterVolumeTxt.Text = $"{(int)Math.Round(e.NewValue * 100)}%";

            settings.Current.MasterVolume = e.NewValue;
            settings.SaveDeferred();
        }

        private void SfxVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (soundEngine == null) return;
            soundEngine.SfxVolume = (float)e.NewValue;
            if (SfxVolumeTxt != null) SfxVolumeTxt.Text = $"{(int)Math.Round(e.NewValue * 100)}%";

            settings.Current.SfxVolume = e.NewValue;
            settings.SaveDeferred();
        }

        /// <summary>Dil ile ilgili tüm kontrolleri (Ayarlar'daki liste + Kısayollar'daki geçiş
        /// butonu) geçerli dile göre tazeler.</summary>
        private void SyncLanguageControls()
        {
            if (LanguageCombo != null)
            {
                LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
                foreach (var obj in LanguageCombo.Items)
                {
                    if (obj is ComboBoxItem item && (string)item.Tag == Loc.Instance.Language)
                    {
                        LanguageCombo.SelectedItem = item;
                        break;
                    }
                }
                LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
            }

            RefreshLanguageToggleButton();
        }

        /// <summary>Ayarlar panelindeki dil listesini doldurur ve geçerli dili seçili yapar.
        /// Window_Loaded'dan çağrılır.</summary>
        private void InitializeLanguageCombo()
        {
            if (LanguageCombo == null) return;

            LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
            LanguageCombo.Items.Clear();
            foreach (var (code, displayName) in Loc.SupportedLanguages)
            {
                var item = new ComboBoxItem { Content = displayName, Tag = code };
                LanguageCombo.Items.Add(item);
                if (code == Loc.Instance.Language) LanguageCombo.SelectedItem = item;
            }
            LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
        }

        /// <summary>Kısayollar penceresindeki dil geçiş butonu: mevcut dil dışındaki İLK dile geçer
        /// (bugün iki dil olduğu için pratikte TR ↔ EN). Buton üzerinde her zaman GEÇİLECEK dilin adı
        /// yazar, böylece ne olacağı tıklamadan önce anlaşılır.</summary>
        private void HelpLanguageToggle_Click(object sender, RoutedEventArgs e)
        {
            var next = NextLanguage();
            if (next != null) SetLanguage(next);
            PlayUiClick();
        }

        private static string NextLanguage()
        {
            var languages = Loc.SupportedLanguages;
            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i].Code == Loc.Instance.Language)
                    return languages[(i + 1) % languages.Count].Code;
            }
            return languages.Count > 0 ? languages[0].Code : null;
        }

        /// <summary>Dil butonunun etiketini geçilecek dile göre günceller (XAML bağlaması değil,
        /// çünkü gösterilen metin geçerli dile DEĞİL, hedef dile bağlı).</summary>
        private void RefreshLanguageToggleButton()
        {
            if (HelpLanguageToggleBtn == null) return;

            string next = NextLanguage();
            var match = Loc.SupportedLanguages.FirstOrDefault(l => l.Code == next);
            HelpLanguageToggleBtn.Content = "🌐 " + (match.DisplayName ?? next);
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo?.SelectedItem is ComboBoxItem item && item.Tag is string code
                && code != Loc.Instance.Language)
            {
                SetLanguage(code);
            }
        }

        /// <summary>Bir aracın bildirimlerde gösterilecek adı: kullanıcı isim verdiyse o isim,
        /// vermediyse tip emojisi. Aynı üçlü ifade bildirim metinlerinde defalarca tekrarlanıyordu.</summary>
        private static string VehicleDisplayName(Vehicle vehicle)
        {
            if (vehicle == null) return "";
            return string.IsNullOrEmpty(vehicle.Name) ? vehicle.Emoji : vehicle.Name;
        }

        private void SetLanguage(string code)
        {
            Loc.Instance.SetLanguage(code);

            // Tercih yazılamazsa (LastError dolar) yalnızca bu oturum için geçerli olur - dil
            // değişiminin kendisi engellenmez.
            settings.Current.Language = code;
            settings.Save();

            // Kod içinde üretilen (XAML bağlaması olmayan) metinleri de tazele. Başarım listesi
            // normalde yalnızca kazanılan sayısı DEĞİŞTİĞİNDE yeniden kurulur; dil değişimi bu
            // sayıyı değiştirmediğinden liste eski dilde kalırdı - sayacı sıfırlayıp zorla tazeliyoruz.
            lastAchievementUnlockedCount = -1;
            UpdateUI();

            // Dil kutusu ve geçiş butonu yeni durumu yansıtsın.
            SyncLanguageControls();
            ShowToast(Loc.T("toast.language_changed"));
        }

        private const string StartWithWindowsRunKeyName = "AutoMotion";

        private static bool IsStartWithWindowsEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue(StartWithWindowsRunKeyName) != null;
            }
            catch { return false; }
        }

        private static void SetStartWithWindows(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (enabled)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(StartWithWindowsRunKeyName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(StartWithWindowsRunKeyName, false);
                }
            }
            catch { /* yetersiz izin vb. - sessizce yoksay, kritik değil */ }
        }

        /// <summary>
        /// Sistem tepsisi simgesi: pencere gizlendiğinde uygulama arka planda çalışmaya
        /// devam eder, tepsi simgesine sağ tıklayarak hızlı erişim sağlanır.
        /// </summary>
        private void InitializeTrayIcon()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var showHideItem = menu.Items.Add("", null, (s, e) => ToggleWindowVisibility());
            var addVehicleItem = menu.Items.Add("", null, (s, e) => { ShowWindowFromTray(); AddVehicleBtn_Click(null, null); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Dil seçimi: WinForms tepsi menüsü XAML bağlaması kullanamadığından metinler
            // LanguageChanged olayında elle tazelenir (bkz. RefreshTrayMenuTexts).
            var languageItem = new System.Windows.Forms.ToolStripMenuItem();
            foreach (var (code, displayName) in Loc.SupportedLanguages)
            {
                var entry = new System.Windows.Forms.ToolStripMenuItem(displayName) { Tag = code };
                entry.Click += (s, e) => SetLanguage(code);
                languageItem.DropDownItems.Add(entry);
            }
            menu.Items.Add(languageItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Windows ile Başlat: çoğu masaüstü pet/widget uygulamasında (VPet, Rainmeter) beklenen
            // standart bir özellik - HKCU\...\Run altına kendi exe yolunu yazar/siler.
            var startWithWindowsItem = new System.Windows.Forms.ToolStripMenuItem
            {
                CheckOnClick = true,
                Checked = IsStartWithWindowsEnabled()
            };
            startWithWindowsItem.CheckedChanged += (s, e) => SetStartWithWindows(startWithWindowsItem.Checked);
            menu.Items.Add(startWithWindowsItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exitItem = menu.Items.Add("", null, (s, e) => ExitApplication());

            // Tepsi menüsü metinleri dil değişiminde tazelenir.
            void RefreshTrayMenuTexts()
            {
                showHideItem.Text = Loc.T("tray.show_hide");
                addVehicleItem.Text = Loc.T("tray.add_vehicle");
                languageItem.Text = Loc.T("tray.language");
                startWithWindowsItem.Text = Loc.T("tray.start_with_windows");
                exitItem.Text = Loc.T("tray.exit");

                foreach (System.Windows.Forms.ToolStripMenuItem entry in languageItem.DropDownItems)
                    entry.Checked = (string)entry.Tag == Loc.Instance.Language;
            }

            RefreshTrayMenuTexts();
            Loc.Instance.LanguageChanged += (s, e) => RefreshTrayMenuTexts();

            System.Drawing.Icon customIcon = null;
            try
            {
                var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo.ico"));
                if (sri != null)
                {
                    using (var stream = sri.Stream)
                    {
                        // Sistem tepsisi ikonu her zaman küçük (16x16/32x32) gösterildiğinden boyut
                        // açıkça istenir - aksi halde belirsiz varsayılan boyut seçimi bulanıklaşmaya
                        // sebep olabiliyordu.
                        customIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
                    }
                }
            }
            catch
            {
                customIcon = System.Drawing.SystemIcons.Application;
            }

            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = customIcon ?? System.Drawing.SystemIcons.Application,
                Text = "AutoMotion",
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += (s, e) => ToggleWindowVisibility();
        }

        private void ToggleWindowVisibility()
        {
            if (IsVisible) Hide();
            else ShowWindowFromTray();
        }

        /// <summary>
        /// Başarım kazanıldığında aracın çevresinde kısa bir kıvılcım patlaması (3.4 / C-1).
        ///
        /// Mevcut parçacık sistemi kullanılıyor - yeni bir çizim yolu açmıyor, dolayısıyla
        /// önbelleklenmiş bitmap parıltısından ve sahne havuzlarından kendiliğinden yararlanıyor.
        /// Araç yoksa (filo boş) kutlama ekranın üst ortasında belirir; bildirimi görsel olarak
        /// destekleyen kısa bir vurgu, ekranı kaplayan bir gösteri değil.
        /// </summary>
        private void PlayAchievementCelebration(Vehicle target)
        {
            if (lowEffectsMode) return;

            double x = target?.X ?? RenderCanvas.ActualWidth / 2;
            double y = target != null ? target.Y - target.Height / 2 : 120;
            if (double.IsNaN(x) || x <= 0) return;

            foreach (var spark in Celebration.BuildBurst(Random.Shared))
            {
                particles.Add(new VehicleParticle
                {
                    X = x,
                    Y = y,
                    VelocityX = spark.VelocityX,
                    VelocityY = spark.VelocityY,
                    Size = spark.Size,
                    Opacity = 1.0,
                    Color = (Color)ColorConverter.ConvertFromString(spark.Color)
                });
            }

            // Sahne boşken render döngüsü askıya alınmış olabilir - kutlama görünmeden kaybolmasın.
            StartRenderLoop();
        }

        private void ShowWindowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;

        // Pencere kapanana kadar canlı tutulmalı - bkz. ApplyTaskbarIconWorkaround.
        private System.Drawing.Icon windowIcon;
        private System.Drawing.Icon windowIconSmall;

        /// <summary>
        /// AllowsTransparency="True" + WindowStyle="None" kombinasyonundaki WPF pencerelerinde
        /// bilinen bir sorun: XAML'deki Window.Icon özelliği görev çubuğu düğmesine doğru şekilde
        /// yansımıyor, Windows onun yerine varsayılan/genel bir simge gösteriyor.
        ///
        /// Asıl kök sebep (ve önceki denemenin neden işe yaramadığı): ikon nesnesi eskiden `using`
        /// ile hemen bu metodun sonunda dispose ediliyordu - bu, WM_SETICON'a verilen HICON handle'ını
        /// anında geçersiz kılıyordu. WM_SETICON, verilen handle'ı KOPYALAMAZ, sadece referans alır;
        /// pencere kendisi görünürde çalışmaya devam etse de, Görev Çubuğu/Alt+Tab/Görev Yöneticisi
        /// gibi ikonu GECİKMELİ (lazy, ihtiyaç anında) okuyan yerler artık geçersiz bir handle'a
        /// bakıp jenerik/varsayılan ikona düşüyordu - "logoyu koydum ama hâlâ varsayılan görünüyor"
        /// şikayetinin gerçek nedeni buydu. Çözüm: ikonu pencere kapanana kadar canlı tutmak (instance
        /// alanında saklamak) ve ayrıca WM_SETICON'a ek olarak pencere SINIFININ (GCLP_HICON/HICONSM)
        /// ikonunu da ayarlamak - bazı kabuk (shell) yüzeyleri per-pencere yerine sınıf ikonunu okur.
        /// </summary>
        private void ApplyTaskbarIconWorkaround()
        {
            try
            {
                if (windowIcon == null)
                {
                    // ÖNEMLİ: parametresiz `new Icon(stream)` .ico içindeki BOYUTU BELİRSİZ/varsayılan
                    // (genelde ~32x32) bir kareyi seçiyordu ve bu AYNI küçük bitmap hem ICON_SMALL
                    // hem ICON_BIG (görev çubuğu/Alt+Tab'daki büyük simge) için kullanılıyordu - büyük
                    // slotta 32x32 kaynak zorla büyütülünce bulanık/küçük-kalmış görünüyordu ("logo çok
                    // küçük duruyor"). logo.ico içinde 16/32/48/64/128/256 kareleri gömülü olduğundan
                    // (bkz. Register/logo.ico) artık boyut AÇIKÇA istenip GDI+'nin en YAKIN/en BÜYÜK
                    // net kareyi seçmesi sağlanıyor: küçük simge için 16x16, büyük simge/sınıf ikonu
                    // için 256x256.
                    var iconUri = new Uri("pack://application:,,,/Assets/logo.ico");

                    var sriSmall = Application.GetResourceStream(iconUri);
                    if (sriSmall == null) return;
                    using (var streamSmall = sriSmall.Stream)
                        windowIconSmall = new System.Drawing.Icon(streamSmall, new System.Drawing.Size(16, 16));

                    var sriBig = Application.GetResourceStream(iconUri);
                    if (sriBig == null) return;
                    using (var streamBig = sriBig.Stream)
                        windowIcon = new System.Drawing.Icon(streamBig, new System.Drawing.Size(256, 256));
                }

                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, windowIconSmall.Handle);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, windowIcon.Handle);
                SetClassLongPtr(hwnd, GCLP_HICON, windowIcon.Handle);
                SetClassLongPtr(hwnd, GCLP_HICONSM, windowIconSmall.Handle);
            }
            catch { /* ikon kozmetik - başarısız olursa sessizce atlanır */ }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyTaskbarIconWorkaround();

            // Sistemleri başlat
            scene = new RetainedScene(SceneLayer);
            gameCore = new GameCore();
            saveManager = new SaveManager();
            soundEngine = new SoundEngine();
            themeEngine = new ThemeEngine();

            // CPU Takometresi - bazı ortamlarda (kısıtlı izin, WMI kapalı) PerformanceCounter
            // oluşturma hata verebilir; başarısız olursa özellik sessizce devre dışı kalır.
            try
            {
                cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // ilk çağrı her zaman 0 döner, ısındırma amaçlı
            }
            catch { cpuCounter = null; }

            // Müzik Süspansiyon Senkronizasyonu: çıkış sesini pasif dinlemeye başla
            try
            {
                audioCapture = new NAudio.Wave.WasapiLoopbackCapture();
                audioCapture.DataAvailable += (s, args) =>
                {
                    // Loopback formatı genelde 32-bit IEEE float; en yüksek örneği (peak) bul.
                    float peak = 0f;
                    int bytesPerSample = audioCapture.WaveFormat.BitsPerSample / 8;
                    if (bytesPerSample == 4)
                    {
                        for (int i = 0; i + 4 <= args.BytesRecorded; i += 4)
                        {
                            float sample = Math.Abs(BitConverter.ToSingle(args.Buffer, i));
                            if (sample > peak) peak = sample;
                        }
                    }
                    else if (bytesPerSample == 2)
                    {
                        for (int i = 0; i + 2 <= args.BytesRecorded; i += 2)
                        {
                            float sample = Math.Abs(BitConverter.ToInt16(args.Buffer, i) / 32768f);
                            if (sample > peak) peak = sample;
                        }
                    }
                    currentAudioLevel = peak;
                };
                audioCapture.StartRecording();
            }
            catch { audioCapture = null; }

            // Her eklenen araç için duvar çarpışma sesi bağla
            gameCore.VehicleAdded += (s, vehicle) => vehicle.OnWallBounce += () =>
            {
                soundEngine.PlayCrash();
                SpawnSkidMark(vehicle);
            };

            // Başarım kazanılınca bildirim göster
            gameCore.AchievementUnlocked += (s, achievement) =>
            {
                ShowToast(Loc.T("toast.achievement_unlocked", achievement.Label));
                soundEngine.PlaySuccess();

                // Steam'e köprüle. Steam yoksa boş köprü çağrıyı sessizce yutar (bkz. SteamService).
                SteamService.Bridge.UnlockAchievement(achievement.Id);

                // Başarım Ödülü: kazanılan başarım sadece bir bildirim olarak kalmaz, seçili
                // (yoksa rastgele) bir aracın kaportasına kalıcı bir "paint" (parlak kontur) olarak
                // işlenir (önceden "sticker"/parıltı kullanılıyordu, kullanıcı isteğiyle kaldırıldı).
                var targetVehicle = selectedVehicle ?? gameCore.AutoMotions.FirstOrDefault();
                if (targetVehicle != null && !targetVehicle.Decorations.Contains("paint"))
                {
                    targetVehicle.Decorations.Add("paint");
                }

                PlayAchievementCelebration(targetVehicle);
            };

            // Eklenti sistemi - buraya yeni eklentiler kaydedilir (VPet mod sistemi gibi)
            expansionManager = new ExpansionManager();
            expansionManager.Register(new TrafficExpansion());
            expansionManager.Register(new EconomyExpansion());
            missionExpansion = new MissionExpansion();
            missionExpansion.PackageCollected += () => soundEngine.PlaySuccess();
            missionExpansion.DeliveryCompleted += (reward, streak) =>
            {
                soundEngine.PlayCash();
                ShowToast(Loc.T("msg.delivery_complete", reward, streak));
            };
            expansionManager.Register(missionExpansion);
            missionExpansion.Enable(gameCore); // eskiden her zaman aktif olan kargo döngüsünü varsayılan olarak açık tutar
            RefreshExpansionsList();

            // Başlangıç araçlarını oluştur
            InitializeGame();
            VehicleListBox.ItemsSource = gameCore.AutoMotions;

            // Eskiden ayrı ayrı köşelere dağılmış 6 panel, tek bir sekmeli menüde konsolide edilir
            MoveConsolidatedPanelsIntoTabs();

            // Panelleri varsayılan köşelerine yerleştir (sonra serbestçe sürüklenebilir)
            PositionDefaultPanels();
            AttachResizeGrips();  // panellerin sağ-alt köşesine sürüklenebilir boyutlandırma tutamağı

            // Kenardan/köşeden boyutlandırma pencere seviyesinde dinlenir (bkz. DetectEdgeAt).
            PreviewMouseMove += Window_EdgeResizeMouseMove;
            PreviewMouseLeftButtonDown += Window_EdgeResizeMouseDown;
            PreviewMouseLeftButtonUp += Window_EdgeResizeMouseUp;
            LoadPanelLayout(); // varsa önceki oturumdan kaydedilmiş konum/sabitleme/büyüklük durumunu uygula
            ApplyPersistedPreferences();    // tema/ses/efekt/çarpan/otomatik kayıt tercihleri (bkz. SettingsService)
            ApplyPersistedIoTPreferences(); // broker adresi/konu/kullanıcı adı/TLS (parola hariç)

            InitializeLanguageCombo();
            RefreshLanguageToggleButton();
            ShowFirstRunOnboardingIfNeeded();

            // Oyun döngüsü: ekranın gerçek yenileme hızına kilitli (bkz. OnCompositionRendering).
            // 60 Hz ekranda 60 FPS, 144 Hz'e alınmış ekranda otomatik olarak 144 FPS.
            StartRenderLoop();

            // Boşta Askıya Alma gözcüsü: sahne tamamen boşken render döngüsünü tamamen durdurup
            // gerektiğinde geri açar (bkz. UpdateRenderLoopState).
            renderLoopWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            renderLoopWatchdog.Tick += (s, e) => { UpdateRenderLoopState(); PumpSteam(); };
            renderLoopWatchdog.Start();

            // Yeni araç eklendiği anda döngü gözcüyü beklemeden (200 ms gecikmesiz) uyanır.
            gameCore.VehicleAdded += (s, v) => StartRenderLoop();

            // UI güncellemesi
            UpdateUI();

            RenderCanvas.Focus();
        }

        private void InitializeGame()
        {
            // Başlangıçta hiç araç eklenmez - kullanıcı "Araç Ekle" ile kendi seçtiği
            // marka/tipi eklesin diye boş başlar.
            selectedVehicle = null;

            // Başlangıç eşyaları
            gameCore.AddItem(new Fuel("regular"));
            gameCore.AddItem(new RepairKit("standard"));

            RefreshItemsList();
        }

        private DateTime lastUserActivity = DateTime.Now;
        private DateTime lastIdleFrameAt = DateTime.MinValue;

        /// <summary>Boşta Modu: kullanıcı uzun süre (5dk+) fare/klavye ile etkileşime girmediyse ve
        /// sürüş modunda değilse kare hızı ~15 FPS'e düşürülerek CPU/pil tüketimi azaltılır - masaüstü
        /// pet uygulamalarında sektör standardı bir davranıştır (VPet'in gece/boşta performans modu gibi).
        /// Herhangi bir fare/klavye etkinliğinde anında normal hıza döner.
        /// Artık zamanlayıcı aralığı değiştirilmiyor (döngü ekran yenilemesine bağlı); boştayken
        /// aradaki kareler basitçe atlanıyor.</summary>
        private bool ShouldSkipIdleFrame()
        {
            bool idle = drivenVehicle == null && (DateTime.Now - lastUserActivity).TotalMinutes > 5;
            if (!idle) return false;

            if ((DateTime.UtcNow - lastIdleFrameAt).TotalMilliseconds < 66) return true;
            lastIdleFrameAt = DateTime.UtcNow;
            return false;
        }

        /// <summary>
        /// Oyun döngüsü artık DispatcherTimer ile değil, WPF'in kompozisyon (render) olayıyla
        /// sürülüyor: CompositionTarget.Rendering ekranın GERÇEK yenileme hızında, tam da kare
        /// çizilmeden hemen önce tetiklenir.
        ///
        /// Neden değiştirildi: 7 ms'lik bir DispatcherTimer'ı en yüksek öncelikle (Send) çalıştırmak,
        /// ekran 60 Hz'deyken bile saniyede ~144 kez tüm sahneyi yeniden kuruyordu. Monitörün
        /// gösteremeyeceği bu fazladan kareler görünür bir akıcılık sağlamadan bir CPU çekirdeğinin
        /// neredeyse tamamını yakıyordu. Bu olayla döngü kendiliğinden ekranın hızına kilitlenir:
        /// Windows'ta ekran 144 Hz'e alındığında uygulama da otomatik olarak 144 FPS çalışır,
        /// 60 Hz'de ise 60 FPS - ve fizik artık kare hızından bağımsız olduğu için (bkz.
        /// GameCore.DeltaFrames) hareket her iki durumda da birebir aynı hızda görünür.
        /// </summary>
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // RENDER DÖNGÜSÜ ASKIYA ALMA (boşta CPU tüketimi)
        //
        // ÖLÇÜM (1920x1080, 60 Hz, sahnede hiç araç yokken):
        //   • CompositionTarget.Rendering aboneliği AÇIK  → bir çekirdeğin ~%9.9'u
        //   • abonelik KAPALI                              → ~%0.1'i
        //
        // Sebep: Rendering aboneliği WPF'in render thread'ini ekran yenileme hızında sürekli tetikte
        // tutar; pencere AllowsTransparency="True" ve tam ekran olduğundan her tik, 1920x1080'lik
        // per-piksel-alfa katmanlı yüzeyin yeniden blit edilmesi anlamına gelir. Çizilecek hiçbir şey
        // yokken bu tamamen boşa yanan CPU'dur - masaüstünde sürekli açık kalan bir uygulamada pil ve
        // fan açısından kabul edilemez.
        //
        // Bu yüzden sahne GERÇEKTEN boşken (hiç araç/engel/parçacık/hava olayı yok ve kullanıcı bir
        // süredir etkileşimde değil) döngüden tamamen çıkılır. 5 Hz'lik hafif bir gözcü zamanlayıcı
        // uyanma koşullarını izler; araç eklenmesi ise gözcüyü beklemeden anında uyandırır.
        //
        // NOT: Bu, WPF'in KENDİ Storyboard animasyonlarını (toast, panel geçişleri, diyaloglar)
        // etkilemez - onları WPF kendi zamanlayıcısıyla sürer ve gerektiğinde kompozisyonu kendisi
        // uyandırır. Askıya alınan yalnızca oyun döngüsüdür.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private bool renderLoopActive;
        private DispatcherTimer renderLoopWatchdog;

        private void StartRenderLoop()
        {
            if (renderLoopActive) return;
            renderLoopActive = true;
            CompositionTarget.Rendering += OnCompositionRendering;
        }

        private void StopRenderLoop()
        {
            if (!renderLoopActive) return;
            renderLoopActive = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }

        /// <summary>Sahnede canlandırılacak/simüle edilecek hiçbir şey kalmadıysa ve kullanıcı da bir
        /// süredir etkileşimde değilse true. Muhafazakâr davranır: en ufak şüphede döngü açık kalır.</summary>
        private bool CanSuspendRenderLoop()
        {
            // Sahnede herhangi bir dinamik içerik varsa asla askıya alma.
            if (HasAnythingToRender()) return false;
            if (drivenVehicle != null) return false;

            // Devam eden bir fare etkileşimi (sürükleme, seçim kutusu, rampa çizimi) varsa bekle.
            if (Mouse.LeftButton == MouseButtonState.Pressed
                || Mouse.RightButton == MouseButtonState.Pressed
                || Mouse.MiddleButton == MouseButtonState.Pressed) return false;

            // Kullanıcı az önce bir şey yaptıysa (menü açtı, buton bekliyor) hemen uykuya dalma.
            if ((DateTime.Now - lastUserActivity).TotalSeconds < 2) return false;

            return true;
        }

        /// <summary>
        /// Sahnedeki motor sesini her karede günceller.
        ///
        /// Tasarım: filodaki her araç için ayrı bir ton çalmak hem kakofoni yaratır hem CPU yakar.
        /// Bunun yerine SAHNEYİ TEMSİL EDEN tek bir ton çalınır - sürülen araç varsa o, yoksa en
        /// hızlı hareket eden araç. Ses şiddeti o aracın hızına bağlı olduğundan, filo yavaşlayınca
        /// ses kendiliğinden kısılır; hiçbir şey hareket etmiyorsa tamamen susar.
        /// </summary>
        private void UpdateEngineAudio()
        {
            // Sürülen araç her zaman önceliklidir: oyuncu onu kontrol ediyor, sesi de o vermeli.
            if (drivenVehicle != null && !drivenVehicle.IsDestroyed && drivenVehicle.Fuel > 0)
            {
                double speed = Math.Sqrt(drivenVehicle.VelocityX * drivenVehicle.VelocityX
                                       + drivenVehicle.VelocityY * drivenVehicle.VelocityY);
                double gearTop = Math.Max(0.1, drivenVehicle.CurrentGearMaxSpeed);
                double rpm = Math.Clamp(speed / gearTop, 0, 1);

                // Boşta (N) veya dururken bile motor çalışıyor: rölantide taban bir ses kalır.
                soundEngine.UpdateEngineTone(drivenVehicle.Type, rpm, 0.55 + rpm * 0.45);
                return;
            }

            // Sürüş yoksa: en hızlı hareket eden, motoru olan aracı temsilci seç.
            Vehicle loudest = null;
            double loudestSpeed = 0;
            foreach (var v in gameCore.AutoMotions)
            {
                if (v.IsDestroyed || !v.HasEngine || v.Fuel <= 0) continue;
                double speed = Math.Sqrt(v.VelocityX * v.VelocityX + v.VelocityY * v.VelocityY);
                if (speed > loudestSpeed) { loudestSpeed = speed; loudest = v; }
            }

            if (loudest == null)
            {
                soundEngine.StopEngineTone();
                return;
            }

            double ratio = Math.Clamp(loudestSpeed / Math.Max(0.1, loudest.MaxSpeed * 2.5), 0, 1);
            // Arka plandaki filo, sürülen araçtan belirgin daha kısık duyulur.
            soundEngine.UpdateEngineTone(loudest.Type, ratio, ratio * 0.5);
        }

        private DateTime lastSteamStatsPush = DateTime.MinValue;

        /// <summary>
        /// Steam geri çağrılarını işler ve istatistik/durum bilgisini periyodik olarak günceller.
        ///
        /// ÖNEMLİ: bu, oyun döngüsüne DEĞİL 5 Hz'lik gözcü zamanlayıcısına bağlı. Oyun döngüsü sahne
        /// boşken tamamen askıya alınıyor (bkz. UpdateRenderLoopState); geri çağrılar oraya bağlansaydı
        /// masaüstünde hiç araç yokken Steam ile iletişim tamamen kesilirdi (arkadaş listesinde
        /// durum donar, başarımlar sunucuya gitmezdi).
        /// </summary>
        private void PumpSteam()
        {
            if (!SteamService.IsAvailable) return;

            SteamService.Bridge.RunCallbacks();

            // İstatistikler her 200 ms'de değil, ~10 saniyede bir gönderilir - Steam'e gereksiz
            // yük bindirmeden arkadaş listesindeki durumu güncel tutmaya fazlasıyla yeter.
            if ((DateTime.UtcNow - lastSteamStatsPush).TotalSeconds < 10) return;
            lastSteamStatsPush = DateTime.UtcNow;

            var data = gameCore.GameData;
            SteamService.Bridge.SetStat(SteamService.Stats.TotalVehicles, data.TotalVehicles);
            SteamService.Bridge.SetStat(SteamService.Stats.TotalEarnings, data.TotalEarnings);
            SteamService.Bridge.SetStat(SteamService.Stats.TotalDistanceKm, (int)data.TotalMileage);
            SteamService.UpdateRichPresence(gameCore.AutoMotions.Count, data.Money);
        }

        private void UpdateRenderLoopState()
        {
            if (renderLoopActive)
            {
                if (!CanSuspendRenderLoop()) return;

                StopRenderLoop();
                // Askıya alınmadan önce sahneyi bir kez temizle - donmuş bir kare kalmasın.
                // Yalnızca sahne katmanı boşaltılır: seçim kutusu/animasyon gibi sahne dışı öğeler
                // kendi ömürlerini yaşamaya devam eder (bkz. RetainedScene).
                scene.Clear();
                // Motor sesi susturulmalı (döngü durunca UpdateEngineAudio çağrılmaz ve ton son
                // değerinde sonsuza kadar çalmaya devam ederdi) VE ses aygıtı uykuya alınmalı:
                // NAudio'nun çıkış thread'i sessizlik üretmek için bile sürekli çalışıyor.
                soundEngine?.SuspendAudioDevice();
            }
            else
            {
                if (!CanSuspendRenderLoop())
                {
                    StartRenderLoop();
                    return;
                }

                // Döngü uyurken de tıklama-geçirgenliği canlı kalmalı: aksi halde pencere son
                // durumunda donar ve kullanıcı masaüstüne tıklayamaz hale gelebilir. Bu kontrol
                // sadece bir GetCursorPos + birkaç sınır karşılaştırması - 5 Hz'de bedeli yok sayılır.
                UpdateClickThrough();
            }
        }

        private void OnCompositionRendering(object sender, EventArgs e)
        {
            // Aynı kompozisyon karesinde birden fazla tetiklenmeyi ele (WPF bunu yapabiliyor)
            if (e is RenderingEventArgs args)
            {
                if (args.RenderingTime == lastRenderingTime) return;
                lastRenderingTime = args.RenderingTime;
            }

            if (ShouldSkipIdleFrame()) return;
            GameLoop_Tick(sender, e);
        }

        private TimeSpan lastRenderingTime = TimeSpan.MinValue;

        private void GameLoop_Tick(object sender, EventArgs e)
        {
            UpdateGearKnobDragPhysics();
            SyncGearShaftLine();

            double activeWinTop = -1;
            double activeWinLeft = -1;
            double activeWinRight = -1;

            try
            {
                IntPtr fgWnd = GetForegroundWindow();
                IntPtr myWnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (fgWnd != IntPtr.Zero && fgWnd != myWnd)
                {
                    if (GetWindowRect(fgWnd, out RECT rect))
                    {
                        // Win32 ekran koordinatlarını WPF pencere koordinatlarına eşle
                        activeWinTop = rect.Top - Top;
                        activeWinLeft = rect.Left - Left;
                        activeWinRight = rect.Right - Left;
                    }
                }
            }
            catch { }

            // Görev Çubuğu Otoyolu (Odak Modu): kullanıcı tam ekrana yakın bir pencerede
            // "çalışırken" (aktif pencere neredeyse ekranın tamamını kaplıyorsa) araçlar o
            // pencerenin üstüne tırmanmaz, sadece görev çubuğunun hemen üstündeki izole şeritte
            // dolaşır - pencere tırmanma fiziği bu durumda devre dışı bırakılır.
            bool focusMode = activeWinTop >= 0 && activeWinTop <= 4 && activeWinLeft <= 4;
            if (focusMode)
            {
                activeWinTop = -1;
                activeWinLeft = -1;
                activeWinRight = -1;
            }

            if (drivenVehicle != null && drivenVehicle.IsDestroyed)
            {
                StopDriving();
            }
            else if (drivenVehicle != null && drivenVehicle.Fuel <= 0)
            {
                // Yakıt biterken sürüş kontrolü açık kalıyordu ama araç güçsüzce yavaşlıyordu -
                // artık yakıt tükenince sürüş modundan otomatik çıkılıyor, oyuncuya bildirim veriliyor.
                ShowToast(Loc.T("msg.out_of_fuel_drive_exit", VehicleDisplayName(drivenVehicle)));
                StopDriving();
            }
            else if (drivenVehicle != null)
            {
                double vertical = (pressedDriveKeys.Contains(Key.W) || pressedDriveKeys.Contains(Key.Up) ? 1 : 0)
                                 - (pressedDriveKeys.Contains(Key.S) || pressedDriveKeys.Contains(Key.Down) ? 1 : 0);
                double horizontal = (pressedDriveKeys.Contains(Key.D) || pressedDriveKeys.Contains(Key.Right) ? 1 : 0)
                                   - (pressedDriveKeys.Contains(Key.A) || pressedDriveKeys.Contains(Key.Left) ? 1 : 0);

                // Gaz/fren girdisi ham tuş durumuyla (0/1) değil, her karede hedefe doğru yumuşakça
                // kayan bir değerle uygulanır - ani tuş basış/bırakışlarda sarsıntılı değil, akıcı
                // bir hızlanma/yavaşlama hissi verir (gerçek gaz pedalı gibi).
                // Not: genel zemin sürtünmesiyle çakışma düzeltildikten sonra gaz artık gerçekten
                // vitese göre tepki veriyor - girdi yumuşatması da hafifçe sıkılaştırıldı (daha
                // duyarlı ama hâlâ akıcı bir gaz pedalı hissi için).
                // Girdi yumuşatması da kare hızından bağımsız: 60 FPS'te "kare başına %20 yaklaş"
                // davranışı, 144 FPS'te aynı gerçek-zaman tepkisini verecek şekilde ölçeklenir.
                double inputDt = gameCore.DeltaFrames;
                smoothedThrottle += (vertical - smoothedThrottle) * (1 - Math.Pow(1 - 0.2, inputDt));
                smoothedSteerInput += (horizontal - smoothedSteerInput) * (1 - Math.Pow(1 - 0.26, inputDt));

                drivenVehicle.ApplyDriveInput(smoothedThrottle, smoothedSteerInput, inputDt);

                // Direksiyon göstergesi: gerçek bir direksiyon simidi gibi A/D ile döner (±75°)
                SteeringWheelRotate.Angle = smoothedSteerInput * 75.0;

                // Zeminde giden araçlarda S/Down freni olarak davranır (yerdeyken dikey girdi kullanılmıyor)
                bool isGroundVehicle = drivenVehicle.Type != "helicopter" && !drivenVehicle.IsFlying;
                drivenVehicle.IsBraking = isGroundVehicle && vertical < 0 && Math.Abs(drivenVehicle.VelocityX) > 0.5;
                if (isGroundVehicle && vertical < 0)
                {
                    // Yüksek hızda sert fren yapılırsa lastik izi bırakılır (gerçekçi drift/fren hissi).
                    // Islak zeminde tutuş azaldığı için daha düşük hızlarda bile patinaj/kayma başlar.
                    double skidThreshold = drivenVehicle.IsWet ? 2.0 : 3.5;
                    if (Math.Abs(drivenVehicle.VelocityX) > skidThreshold && drivenVehicle.EdgeSide == 0)
                    {
                        SpawnSkidMark(drivenVehicle);
                    }
                    drivenVehicle.VelocityX *= Math.Pow(0.88, gameCore.DeltaFrames);
                }

                // Lastik Patinajı (Wheelspin/Burnout): dururken veya çok düşük hızdayken gaza
                // sonuna kadar basılırsa motor torku lastik tutuşundan fazla gelir - gerçek bir
                // araçtaki gibi tekerlek kayar ve arkada duman/iz bırakır (sert fren izinin
                // simetriği: orada hız yüksekken ani durma, burada hız düşükken ani kalkış).
                if (isGroundVehicle && Math.Abs(vertical) > 0.8 && Math.Abs(drivenVehicle.VelocityX) < 1.2 && drivenVehicle.EdgeSide == 0 && drivenVehicle.Gear != 0)
                {
                    SpawnSkidMark(drivenVehicle);
                }

                // Motor sesi artık sürekli (bkz. UpdateEngineAudio) - burada yalnızca egzoz
                // patlatma (backfire) gibi ANLIK olaylar tetikleniyor.
                bool throttling = horizontal != 0 || (vertical != 0 && !isGroundVehicle);
                if (throttling)
                {
                    engineSoundCooldown -= gameCore.DeltaFrames;
                    if (engineSoundCooldown <= 0)
                    {
                        engineSoundCooldown = 12;

                        // Egzoz Patlatma (Backfire): tüm upgradeları alınmış (maksimum seviye) bir
                        // araç ani gaz verince küçük bir ihtimalle egzozdan alev/pop efekti çıkarır.
                        if (drivenVehicle.Upgrades.Count >= 4 && Random.Shared.NextDouble() < 0.12)
                        {
                            SpawnBackfire(drivenVehicle);
                        }
                    }
                }
                else
                {
                    engineSoundCooldown = 0;
                }

                // Hız göstergesi (HUD): piksel/kare hızını kabaca km/s hissi veren bir değere ölçekler.
                // Geçerli vitesin tepe hızına ne kadar yaklaşıldığına göre renk sarıdan kırmızıya döner (redline hissi).
                double displaySpeed = Math.Sqrt(drivenVehicle.VelocityX * drivenVehicle.VelocityX + drivenVehicle.VelocityY * drivenVehicle.VelocityY);
                SpeedTxt.Text = ((int)Math.Round(displaySpeed * Vehicle.SpeedDisplayScale)).ToString();

                double gearMax = drivenVehicle.CurrentGearMaxSpeed;
                double speedRatio = gearMax > 0 ? Math.Clamp(displaySpeed / gearMax, 0, 1) : 0;
                SpeedTxt.Foreground = speedRatio > 0.85
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x4B))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x6D));

                // Devir (RPM) barı: yeşilden (düşük devir) sarı/kırmızıya (redline) yumuşak geçiş
                RpmBarFill.Width = speedRatio * 128;
                RpmBarFill.Fill = new SolidColorBrush(speedRatio switch
                {
                    < 0.6 => Color.FromRgb(0x39, 0xFF, 0x14),
                    < 0.85 => Color.FromRgb(0xFF, 0xE6, 0x6D),
                    _ => Color.FromRgb(0xFF, 0x4B, 0x4B)
                });

                // Hız Hissi (Sense of Speed): vites tepe hızının %70'ini geçince, sürülen aracın
                // rastgele bir kenarından geriye doğru hızla kayan ince beyaz çizgiler belirir -
                // yüksek hızda sürüyormuş hissini güçlendiren, gerçek yarış oyunlarından tanıdık bir detay.
                if (speedRatio > 0.7 && !lowEffectsMode && Random.Shared.NextDouble() < (speedRatio - 0.7) * 0.6)
                {
                    double travelDirX = drivenVehicle.VelocityX;
                    double travelDirY = drivenVehicle.VelocityY;
                    double travelLen = Math.Sqrt(travelDirX * travelDirX + travelDirY * travelDirY);
                    if (travelLen > 0.01)
                    {
                        double perpX = -travelDirY / travelLen;
                        double perpY = travelDirX / travelLen;
                        double side = (Random.Shared.NextDouble() - 0.5) * drivenVehicle.Height * 1.8;
                        speedLines.Add(new SpeedLine
                        {
                            X = drivenVehicle.X - (travelDirX / travelLen) * drivenVehicle.Width + perpX * side,
                            Y = drivenVehicle.Y - (travelDirY / travelLen) * drivenVehicle.Width + perpY * side,
                            VelX = -travelDirX * 0.6,
                            VelY = -travelDirY * 0.6,
                            Length = 14 + Random.Shared.NextDouble() * 16,
                            Opacity = 0.35 + speedRatio * 0.25
                        });
                    }
                }
            }

            gameCore.Update(RenderCanvas.ActualWidth, RenderCanvas.ActualHeight, activeWinTop, activeWinLeft, activeWinRight,
                multiMonitorMode && monitorFloors.Count > 1 ? FloorYForX : null);
            UpdateStations();
            CheckHardwareStatus();
            UpdateTrafficJams();
            UpdateCarMeetEvent();
            UpdatePoliceStops();
            UpdatePoliceChases();
            UpdateMonitorJumpRamps();
            UpdatePhysicsRamps();
            UpdateMusicSync();

            // İmleç Trafik Değneği aktifken imlece yakın araçlar acil fren yapar
            if (stopSignActive)
            {
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsDragged) continue;
                    double dx = lastMousePos.X - vehicle.X;
                    double dy = lastMousePos.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 120)
                    {
                        vehicle.VelocityX *= 0.75;
                        vehicle.VelocityY *= 0.75;
                    }
                }
            }
            UpdateEngineAudio();
            expansionManager.UpdateAll(gameCore, gameCore.GameTime, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);
            CheckPomodoroCompletion();
            CheckCargoDeliveries();
            UpdateWpmRace();
            PublishIotTelemetryIfDue();
            CheckReminders();
            CheckGreetings();
            UpdateClickThrough();
            RenderGame();

            // Panel metinleri (para, yakıt, HP, başarım listesi...) 144 kez/sn güncellenmesine gerek
            // yok - her karede onlarca TextBlock'a yazmak WPF'te sürekli layout/measure tetikleyip
            // kayda değer CPU yakıyordu. Saniyede ~20 kez güncellemek göz için tamamen yeterli.
            if ((DateTime.UtcNow - lastUiRefresh).TotalMilliseconds >= 50)
            {
                lastUiRefresh = DateTime.UtcNow;
                UpdateUI();
            }

            saveManager.AutoSave(gameCore);
        }


        private DateTime lastUiRefresh = DateTime.MinValue;


        private void UpdateUI()
        {
            VehicleCountTxt.Text = gameCore.AutoMotions.Count.ToString();
            MoneyTxt.Text = $"${gameCore.GameData.Money}";
            AddFuelBtn.Content = Loc.T("ui.buy_fuel_price", CurrentFuelPrice());
            ItemCountTxt.Text = gameCore.Items.Count.ToString();
            TotalMileageTxt.Text = $"{(int)gameCore.GameData.TotalMileage} km";

            miniHud?.UpdateStats(gameCore.AutoMotions.Count, gameCore.GameData.Money, gameCore.GameData.TotalMileage);
            miniHud?.UpdateMiniMap(gameCore.AutoMotions, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);

            int unlockedCount = gameCore.GameData.UnlockedAchievements.Count;
            AchievementsSummaryTxt.Text = Loc.T("ui.achievements_summary", unlockedCount, GameCore.AchievementCatalog.Count);
            // Artık sadece kazanılanlar değil, TÜM başarımlar listeleniyor - henüz kazanılmayanlar
            // 🔒 ile soluk gösterilir, böylece oyuncu "sırada ne var" görebiliyor (VPet'in başarım
            // vitrinine benzer motivasyon hissi).
            if (lastAchievementUnlockedCount != unlockedCount || AchievementsListControl.ItemsSource == null)
            {
                lastAchievementUnlockedCount = unlockedCount;
                AchievementsListControl.ItemsSource = GameCore.AchievementCatalog
                    .Select(a => new AchievementRow
                    {
                        Label = (gameCore.GameData.UnlockedAchievements.Contains(a.Id) ? "✅ " : "🔒 ") + a.Label,
                        Unlocked = gameCore.GameData.UnlockedAchievements.Contains(a.Id)
                    })
                    .ToList();
            }

            if (selectedVehicle != null)
            {
                VehicleTypeTxt.Text = selectedVehicle.Emoji + " " + selectedVehicle.Type.ToUpper();
                VehicleScaleTxt.Text = $"{(int)Math.Round(selectedVehicle.Scale * 100)}%";
                HPTxt.Text = $"{selectedVehicle.HP}/{selectedVehicle.MaxHP}";
                FuelTxt.Text = $"{(int)selectedVehicle.Fuel}/{(int)selectedVehicle.MaxFuel}";
                HPBar.Value = selectedVehicle.HP;
                HPBar.Maximum = selectedVehicle.MaxHP;
                FuelBar.Value = selectedVehicle.Fuel;
                FuelBar.Maximum = selectedVehicle.MaxFuel;
                HappinessBar.Value = selectedVehicle.Happiness;
                TireWearBar.Value = selectedVehicle.TireWear;
                ToggleLearningAIBtn.Opacity = selectedVehicle.UseLearningAI ? 1.0 : 0.4;

                if (VehicleNameBox != null && !VehicleNameBox.IsFocused)
                {
                    VehicleNameBox.TextChanged -= VehicleNameBox_TextChanged;
                    VehicleNameBox.Text = selectedVehicle.Name;
                    VehicleNameBox.TextChanged += VehicleNameBox_TextChanged;
                }

                UpgradesTxt.Text = selectedVehicle.Upgrades.Count > 0
                    ? string.Join(", ", selectedVehicle.Upgrades)
                    : "Yok";

                // Dorse / Ekipman paneli - sadece kamyon ve traktörlerde
                bool isTruck = selectedVehicle.Type == "truck";
                bool isTractor = selectedVehicle.Type == "tractor";
                TrailerPanel.Visibility = (isTruck || isTractor) ? Visibility.Visible : Visibility.Collapsed;
                TrailerFlatbedBtn.Visibility = isTruck ? Visibility.Visible : Visibility.Collapsed;
                TrailerTankerBtn.Visibility = isTruck ? Visibility.Visible : Visibility.Collapsed;
                TrailerEquipmentBtn.Visibility = isTractor ? Visibility.Visible : Visibility.Collapsed;
                UpdateTrailerButtonHighlights();
            }
            else
            {
                if (VehicleNameBox != null) VehicleNameBox.Text = "";
                VehicleTypeTxt.Text = Loc.T("ui.no_selection");
                HPTxt.Text = "0/0";
                FuelTxt.Text = "0/0";
                HPBar.Value = 0;
                FuelBar.Value = 0;
                HappinessBar.Value = 0;
                TireWearBar.Value = 0;
                UpgradesTxt.Text = "Yok";
                TrailerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void AutoSaveIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (saveManager == null) return; // Window_Loaded'dan önce XAML varsayılan seçimi tetikleyebilir
            if (AutoSaveIntervalCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
            {
                saveManager.AutoSaveIntervalSeconds = seconds;
                settings.Current.AutoSaveIntervalSeconds = seconds;
                settings.Save();
                soundEngine?.PlayClick();
            }
        }

        private void RefreshItemsList()
        {
            var itemsList = new ObservableCollection<Item>(gameCore.Items);
            ItemsListControl.ItemsSource = itemsList;
        }

        /// <summary>
        /// Eşya kartındaki "Kullan" butonu: önceden envanterdeki eşyaları kullanmanın hiçbir yolu
        /// yoktu (kartlarda sadece bilgi gösteriliyordu, sağ tık menüsü de her zaman körlemesine
        /// SON eklenen eşyayı kullanıyordu). Artık her kart kendi eşyasını, seçili araca, doğrudan uygulayabiliyor.
        /// </summary>
        private async void UseItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Item item) return;

            if (selectedVehicle == null)
            {
                ShowToast(Loc.T("msg.esyayi_kullanmak_icin_once_bir_arac"));
                return;
            }

            int index = gameCore.Items.IndexOf(item);
            if (index < 0) return;

            // Tamirci Mikro-Oyunu: RepairKit doğrudan uygulanmaz, önce vidaları sıkma mini
            // oyunu başlar - tam tamamlanırsa tam iyileşme, süre dolarsa yarım iyileşme olur.
            if (item is RepairKit repairKit)
            {
                if (selectedVehicle.HP >= selectedVehicle.MaxHP)
                {
                    ShowToast(Loc.T("msg.item_not_applicable", item.Name));
                    return;
                }
                gameCore.Items.RemoveAt(index);
                RefreshItemsList();
                StartRepairMinigame(selectedVehicle, repairKit);
                return;
            }

            var usedOnVehicle = selectedVehicle;
            if (gameCore.UseItem(index, selectedVehicle))
            {
                soundEngine.PlaySuccess();

                // Kart hemen listeden kaybolunca sert bir geçiş oluyordu - kısa bir "başarılı"
                // geri bildirimi gösterip ardından listeyi yeniliyoruz.
                btn.IsEnabled = false;
                btn.Content = Loc.T("ui.item_used");
                btn.Background = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14));
                await System.Threading.Tasks.Task.Delay(350);
                RefreshItemsList();

                // Polis Kovalamacası: hız/hızlanma upgrade'i alan araç bir ihtimalle polisin
                // dikkatini çeker.
                if (item is Upgrade upgrade && (upgrade.UpgradeType == "speed" || upgrade.UpgradeType == "acceleration")
                    && Random.Shared.NextDouble() < 0.5)
                {
                    SpawnPoliceChase(usedOnVehicle);
                }
            }
            else
            {
                ShowToast(Loc.T("msg.item_not_applicable", item.Name));
            }
        }

        /// <summary>
        /// Tamirci Mikro-Oyunu: aracın üzerinde beliren birkaç vida ikonunu süre dolmadan hızlıca
        /// tıklamayı gerektirir. Hepsi sıkılırsa tam iyileşme, süre dolarsa yarım iyileşme uygulanır.
        /// </summary>
        private void StartRepairMinigame(Vehicle vehicle, RepairKit repairKit)
        {
            const int boltCount = 4;
            var boltButtons = new List<Button>();
            // NOT: RenderCanvas her karede tamamen temizlenip yeniden çiziliyor (RenderGame), bu yüzden
            // bu geçici mini-oyun katmanı RenderCanvas'a değil, her karede silinmeyen PanelsContainer'a eklenir.
            var overlayCanvas = new Canvas { Width = RenderCanvas.ActualWidth, Height = RenderCanvas.ActualHeight, IsHitTestVisible = true };
            Canvas.SetLeft(overlayCanvas, 0);
            Canvas.SetTop(overlayCanvas, 0);
            Canvas.SetZIndex(overlayCanvas, 999);
            PanelsContainer.Children.Add(overlayCanvas);

            var rnd = Random.Shared;
            for (int i = 0; i < boltCount; i++)
            {
                double angle = i * (360.0 / boltCount) * Math.PI / 180.0;
                double bx = vehicle.X + Math.Cos(angle) * (vehicle.Width * 0.55);
                double by = vehicle.Y + Math.Sin(angle) * (vehicle.Height * 0.7) - 10;

                var boltBtn = new Button
                {
                    Content = "🔩",
                    FontSize = 16,
                    Width = 28,
                    Height = 28,
                    Style = (Style)FindResource("IconButton")
                };
                Canvas.SetLeft(boltBtn, bx - 14);
                Canvas.SetTop(boltBtn, by - 14);
                overlayCanvas.Children.Add(boltBtn);
                boltButtons.Add(boltBtn);
            }

            ShowToast(Loc.T("msg.tamirci_mikro_oyunu_vidalari_hizlica_sik"));
            PlayUiClick();

            // Vidalar araca göre sabit ekran konumunda çizildiği için, oyun sırasında araç
            // hareket edip vidalardan uzaklaşmasın diye geçici olarak kilitlenir.
            bool wasLocked = vehicle.IsLocked;
            vehicle.IsLocked = true;

            int remaining = boltCount;
            var timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

            void FinishMinigame(bool fullSuccess)
            {
                timeoutTimer.Stop();
                PanelsContainer.Children.Remove(overlayCanvas);
                vehicle.IsLocked = wasLocked;

                int healAmount = fullSuccess ? repairKit.HealAmount : repairKit.HealAmount / 2;
                vehicle.HP = Math.Min(vehicle.MaxHP, vehicle.HP + healAmount);
                if (fullSuccess) gameCore.GameData.RepairMinigameSuccessCount++;
                soundEngine.PlaySuccess();
                ShowToast(Loc.T(fullSuccess ? "msg.repair_full" : "msg.repair_partial"));
            }

            foreach (var boltBtn in boltButtons)
            {
                boltBtn.Click += (s, e) =>
                {
                    overlayCanvas.Children.Remove(boltBtn);
                    remaining--;
                    PlayUiClick();
                    if (remaining <= 0) FinishMinigame(true);
                };
            }

            timeoutTimer.Tick += (s, e) => FinishMinigame(false);
            timeoutTimer.Start();
        }

        /// <summary>
        /// Polis Kovalamacası: hedef aracın yakınına, onu kalıcı olarak takip eden özel bir
        /// polis aracı (🚓) doğurur. Yakalarsa (yeterince yaklaşırsa) para cezası kesip kovalamayı bırakır.
        /// </summary>
        private void SpawnPoliceChase(Vehicle target)
        {
            if (target == null) return;
            ShowToast(Loc.T("msg.polis_hiz_yapan_araci_fark_etti"));

            double spawnX = target.X < RenderCanvas.ActualWidth / 2 ? RenderCanvas.ActualWidth - 20 : 20;
            var police = new Vehicle(spawnX, Math.Max(20, target.Y - 60), "car")
            {
                IsPolice = true,
                ChaseTarget = target,
                Color = "#1565C0",
                NeonColor = "#00A0FF",
                Name = "POLİS",
                Emoji = "🚓",
                Brand = "" // gerçek marka görseli kullanılmasın, emoji ile ayırt edilsin
            };
            police.MaxSpeed *= 1.15; // hedefi gerçekten yakalayabilsin diye biraz daha hızlı
            gameCore.AddAutoMotion(police);
        }

        /// <summary>Polis aracı hedefine yeterince yaklaşınca cezayı kesip filoya geri döner (kovalamaca biter).</summary>
        private void UpdatePoliceChases()
        {
            foreach (var police in gameCore.AutoMotions.Where(v => v.IsPolice && v.ChaseTarget != null).ToList())
            {
                double dx = police.ChaseTarget.X - police.X;
                double dy = police.ChaseTarget.Y - police.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 35)
                {
                    int fine = Math.Min(gameCore.GameData.Money, 60);
                    if (fine > 0) gameCore.AddMoney(-fine);
                    gameCore.GameData.PoliceFinesReceived++;
                    ShowToast(Loc.T("msg.police_speed_fine", fine));
                    soundEngine.PlayCrash();
                    police.ChaseTarget = null;
                    gameCore.RemoveAutoMotion(police);
                }
            }
        }

        private void RefreshExpansionsList()
        {
            ExpansionsListControl.ItemsSource = null;
            ExpansionsListControl.ItemsSource = expansionManager.Expansions;
        }

        private void ExpansionsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Eklentiler artık ayrı bir yüzen panel değil, konsolide "☰ Paneller" menüsünün bir
            // sekmesi - bu buton menüyü açıp doğrudan o sekmeye geçiyor.
            ShowMasterPanelTab(TabExpansions);
            PlayUiClick();
        }

        /// <summary>Konsolide "☰ Paneller" menüsünü (kapalıysa) açar ve istenen sekmeye geçer -
        /// örn. sürüş moduna girince Vites sekmesine, Eklentiler butonuna basınca o sekmeye.</summary>
        private void ShowMasterPanelTab(TabItem tab)
        {
            if (MasterPanel.Visibility != Visibility.Visible)
            {
                panelsVisible = true;
                AnimateVisibility(PanelsContainer, true);
                AnimateVisibility(MasterPanel, true);
            }
            MasterTabControl.SelectedItem = tab;
        }

        private void ExpansionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Expansion expansion) return;

            expansionManager.ToggleExpansion(expansion.Id, gameCore);
            RefreshExpansionsList();
            PlayUiClick();
        }

        // Mouse etkileşimi
        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point posAny = e.GetPosition(RenderCanvas);

            // Fiziksel Rampa Çizimi: orta fare tuşuyla çizime başla
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                isDrawingRamp = true;
                lastRampPoint = posAny;
                RenderCanvas.CaptureMouse();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(RenderCanvas);

                // Manuel Doldurma: bidon elimizdeyken bir araca tıklarsak dolduruyoruz
                if (fuelCanActive)
                {
                    foreach (var vehicle in gameCore.AutoMotions)
                    {
                        if (vehicle.IsPointInside(pos.X, pos.Y))
                        {
                            vehicle.Fuel = Math.Min(vehicle.MaxFuel, vehicle.Fuel + 50);
                            ShowToast(Loc.T("msg.manual_refuel_done", VehicleDisplayName(vehicle)));
                            soundEngine.PlaySuccess();
                            break;
                        }
                    }
                    CancelFuelCan();
                    return;
                }

                // Shift tuşu basılıysa ve seçili araç(lar) varsa ROTA/GRUP HEDEFİ ata
                if (Keyboard.Modifiers == ModifierKeys.Shift && (selectedVehicle != null || selectedVehicles.Count > 0))
                {
                    var groupTargets = selectedVehicles.Count > 1 ? selectedVehicles : new List<Vehicle> { selectedVehicle };

                    if (groupTargets.Count > 1)
                    {
                        // VIP Konvoy: dağınık gitmek yerine en "lüks" (en ağır/büyük) araç öne
                        // geçer, geri kalanlar onun arkasında V-formasyonu alır.
                        var lead = groupTargets.OrderByDescending(v => v.Mass).First();
                        double dirAngle = Math.Atan2(pos.Y - lead.Y, pos.X - lead.X);
                        double behindAngle = dirAngle + Math.PI;

                        lead.CustomPath.Clear();
                        lead.CustomPath.Add(pos);

                        int slot = 0;
                        foreach (var v in groupTargets)
                        {
                            if (v == null || v == lead) continue;
                            slot++;
                            int side = slot % 2 == 0 ? 1 : -1;
                            int rank = (slot + 1) / 2;
                            double spread = rank * 28;
                            double perpAngle = behindAngle + side * (Math.PI / 4);
                            var slotPoint = new Point(
                                pos.X + Math.Cos(behindAngle) * rank * 24 + Math.Cos(perpAngle) * spread,
                                pos.Y + Math.Sin(behindAngle) * rank * 24 + Math.Sin(perpAngle) * spread);

                            v.CustomPath.Clear();
                            v.CustomPath.Add(slotPoint);
                        }
                    }
                    else if (selectedVehicle != null)
                    {
                        selectedVehicle.CustomPath.Clear();
                        selectedVehicle.CustomPath.Add(pos);
                    }

                    soundEngine.PlayDrift();
                    return;
                }

                // Önce araç tıklamalarını tara
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsPointInside(pos.X, pos.Y))
                    {
                        // Trafik Sıkışıklığı: bir kenarda uzun süre birikmiş araca tıklanınca
                        // dağıtılır - dürtülüp zeminin ortasına doğru fırlatılır.
                        if (jamStuckTicks.TryGetValue(vehicle, out double jamTicks) && jamTicks > 240)
                        {
                            jamStuckTicks.Remove(vehicle);
                            double dispersDir = vehicle.EdgeSide == -1 ? 1 : -1;
                            vehicle.VelocityX = dispersDir * (3 + Random.Shared.NextDouble() * 2);
                            vehicle.VelocityY = -2;
                            soundEngine.PlayHorn(vehicle.Type);
                            return;
                        }

                        selectedVehicle = vehicle;
                        draggedVehicle = vehicle;
                        dragOffset = new Point(pos.X - vehicle.X, pos.Y - vehicle.Y);

                        // Sürükleme durumunu başlat
                        vehicle.IsDragged = true;
                        vehicle.DragLastX = vehicle.X;
                        vehicle.DragLastY = vehicle.Y;
                        vehicle.DragManualRotation = 0; // her yeni tutuşta manuel döndürme sıfırdan başlasın
                        vehicle.VelocityX = 0;
                        vehicle.VelocityY = 0;

                        selectedVehicles.Clear(); // Çoklu seçimi sıfırla

                        // Fareyi yakala: yakalama olmadan fare çok hızlı hareket ettirildiğinde
                        // imleç anlık olarak pencere/canvas dışına çıkabiliyor, bu durumda MouseMove
                        // olayları kaçırılıp araç "havada asılı/takılı" kalmış gibi görünüyordu.
                        RenderCanvas.CaptureMouse();

                        RenderCanvas.Cursor = Cursors.Hand;

                        // Liste seçileni de eşle
                        VehicleListBox.SelectedItem = vehicle;
                        return;
                    }
                }

                // Araç tıklanmadıysa engelleri tara
                foreach (var obs in gameCore.Obstacles)
                {
                    if (obs.IsPointInside(pos.X, pos.Y))
                    {
                        draggedObstacle = obs;
                        obstacleDragOffset = new Point(pos.X - obs.X, pos.Y - obs.Y);
                        RenderCanvas.CaptureMouse();
                        RenderCanvas.Cursor = Cursors.SizeAll;
                        PlayUiClick();
                        return;
                    }
                }

                // Boş ekrana tıklanmışsa, Çoklu Seçim çerçevesini (Drag Selection) başlat
                isDragSelecting = true;
                dragSelectionStart = pos;
                selectedVehicles.Clear();
                selectedVehicle = null;

                if (selectionBox != null) RenderCanvas.Children.Remove(selectionBox);
                selectionBox = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                    StrokeThickness = 1.0,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)),
                    Visibility = Visibility.Visible
                };
                Canvas.SetLeft(selectionBox, pos.X);
                Canvas.SetTop(selectionBox, pos.Y);
                RenderCanvas.Children.Add(selectionBox);
            }
        }

        /// <summary>
        /// Fare, sürüklemeden/tıklamadan sadece bir aracın yakınından geçince küçük bir ihtimalle
        /// mutlu tepki (zıplama) versin - global klavye/fare kancası kullanmadan güvenli bir
        /// "etkinliğe tepki" hissi vermek için (Bongo Cat/Shimeji tarzı).
        /// </summary>
        private void CheckHoverReaction(Point pos)
        {
            if (draggedVehicle != null || draggedObstacle != null || isDragSelecting) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                double dx = pos.X - vehicle.X;
                double dy = pos.Y - vehicle.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < Math.Max(vehicle.Width, vehicle.Height) * 0.9)
                {
                    // Önceden sadece fareyle üzerinden geçince (hiçbir tıklama/okşama olmadan) küçük
                    // bir ihtimalle araç zıplayıp sallanıyordu - kullanıcı için rahatsız edici bir
                    // "aşağı yukarı hareket" hissi veriyordu. Artık sadece bilinçli okşama hareketi
                    // (CheckPetting - sağa-sola hızlı sürtme) tepki tetikliyor, salt hover tetiklemiyor.
                    CheckPetting(vehicle, pos.X);
                }
                else
                {
                    pettingState.Remove(vehicle);
                }
            }
        }

        /// <summary>
        /// Araç Okşama: fare aracın üzerindeyken kısa sürede birkaç kez sağa-sola sürtülürse
        /// (yön değiştirme sayısı eşiği aşarsa) araç motorunu bağırtıp zıplar ve kalp gösterir.
        /// </summary>
        private void CheckPetting(Vehicle vehicle, double mouseX)
        {
            pettingState.TryGetValue(vehicle, out var state);

            double deltaX = mouseX - state.lastX;
            if (Math.Abs(deltaX) < 1.5)
            {
                pettingState[vehicle] = (mouseX, state.direction, state.flips, state.windowTicks + 1);
                return;
            }

            int dir = deltaX > 0 ? 1 : -1;
            int flips = state.flips;
            int windowTicks = state.windowTicks + 1;

            if (state.direction != 0 && dir != state.direction)
            {
                flips++;
            }

            if (windowTicks > 40) // ~0.65sn pencere dolunca sıfırla
            {
                flips = 0;
                windowTicks = 0;
            }

            if (flips >= 3)
            {
                vehicle.ReactToClick();
                vehicle.Happiness = Math.Min(100, vehicle.Happiness + 15);
                soundEngine.PlayRev(vehicle.Type);
                ShowPettingHeart(vehicle);
                flips = 0;
                windowTicks = 0;
            }

            pettingState[vehicle] = (mouseX, dir, flips, windowTicks);
        }

        private void ShowPettingHeart(Vehicle vehicle)
        {
            var heart = new TextBlock { Text = "💖", FontSize = 16 };
            Canvas.SetLeft(heart, vehicle.X - 8);
            Canvas.SetTop(heart, vehicle.Y - vehicle.Height / 2 - 26);
            RenderCanvas.Children.Add(heart);

            var floatUp = new DoubleAnimation { From = 0, To = -20, Duration = TimeSpan.FromMilliseconds(700) };
            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(700) };
            fadeOut.Completed += (s, e) => RenderCanvas.Children.Remove(heart);
            var transform = new TranslateTransform();
            heart.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, floatUp);
            heart.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>Bir araç sürüklenirken Ctrl+fare tekerleği ile aracı manuel döndürmeye yarar -
        /// kullanıcı isteği: özellikle bir duvar/tavandan alınıp bırakılacak aracın yönünü bırakmadan
        /// önce elle düzeltebilsin. Ctrl basılı değilse veya sürüklenen bir araç yoksa hiçbir şey yapmaz.</summary>
        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (draggedVehicle == null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            draggedVehicle.DragManualRotation += e.Delta > 0 ? 15 : -15;
            draggedVehicle.Rotation = draggedVehicle.DragManualRotation;
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(RenderCanvas);
            lastMousePos = pos;
            lastUserActivity = DateTime.Now;
            CheckHoverReaction(pos);

            // Fiziksel Rampa Çizimi: orta tuş basılıyken sürüklenen yol kalıcı bir rampa segmenti olur
            if (isDrawingRamp && e.MiddleButton == MouseButtonState.Pressed)
            {
                double segLen = Math.Sqrt(Math.Pow(pos.X - lastRampPoint.X, 2) + Math.Pow(pos.Y - lastRampPoint.Y, 2));
                if (segLen > 8)
                {
                    physicsRamps.Add((lastRampPoint, pos, 600)); // ~10 saniye ömür
                    lastRampPoint = pos;
                }
                return;
            }

            // Detaylı Yıkama: W basılıyken sol tuş basılı sürüklenirse üzerindeki aracın kiri azalır
            if (washModeActive && e.LeftButton == MouseButtonState.Pressed)
            {
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsPointInside(pos.X, pos.Y))
                    {
                        bool wasDirty = vehicle.Dirtiness > 0.05;
                        vehicle.Dirtiness = Math.Max(0, vehicle.Dirtiness - 0.03);
                        if (wasDirty && vehicle.Dirtiness <= 0.001)
                        {
                            gameCore.GameData.VehiclesWashedClean++;
                        }
                        break;
                    }
                }
            }

            // Shift rota çizim modu aktifse noktaları ekle (yalnızca tekil seçimde serbest çizim - grup komutunda tek nokta yeterli)
            if (Keyboard.Modifiers == ModifierKeys.Shift && selectedVehicle != null && selectedVehicles.Count <= 1 && e.LeftButton == MouseButtonState.Pressed)
            {
                if (selectedVehicle.CustomPath.Count > 0)
                {
                    var lastPt = selectedVehicle.CustomPath.Last();
                    double d = Math.Sqrt((lastPt.X - pos.X) * (lastPt.X - pos.X) + (lastPt.Y - pos.Y) * (lastPt.Y - pos.Y));
                    if (d > 12)
                    {
                        selectedVehicle.CustomPath.Add(pos);
                    }
                }
                else
                {
                    selectedVehicle.CustomPath.Add(pos);
                }
                return;
            }

            // Ekran kenarında saklanan araçların üstüne gelindiğinde geri çıkması (Hover Pop-out)
            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsSideHidden)
                {
                    double dist = Math.Sqrt((vehicle.X - pos.X) * (vehicle.X - pos.X) + (vehicle.Y - pos.Y) * (vehicle.Y - pos.Y));
                    if (dist < 55) // Mouse imleci yeterince yakınsa saklanma durumunu boz
                    {
                        vehicle.IsSideHidden = false;
                        if (vehicle.HiddenDirection == "left")
                        {
                            vehicle.X = vehicle.Width + 15;
                            vehicle.VelocityX = 4.0;
                        }
                        else
                        {
                            vehicle.X = RenderCanvas.ActualWidth - vehicle.Width - 15;
                            vehicle.VelocityX = -4.0;
                        }
                        soundEngine.PlaySuccess();
                    }
                }
            }

            if (draggedVehicle != null && e.LeftButton == MouseButtonState.Pressed)
            {
                double targetX = pos.X - dragOffset.X;
                double targetY = pos.Y - dragOffset.Y;

                // Fırlatma hızı hesabı (VPet benzeri fırlatma ivmesi) - ağır araçlar (kamyon/tren)
                // aynı fare hızıyla sürüklendiğinde daha yavaş/ağır fırlar, hafifler (bisiklet/motor)
                // gerçekçi biçimde çok daha kolay fırlatılabiliyor.
                double flingFactor = Math.Clamp(0.75 / Math.Sqrt(draggedVehicle.Mass), 0.3, 1.3);
                draggedVehicle.VelocityX = (targetX - draggedVehicle.X) * flingFactor;
                draggedVehicle.VelocityY = (targetY - draggedVehicle.Y) * flingFactor;

                draggedVehicle.X = targetX;
                draggedVehicle.Y = targetY;
                RenderCanvas.Cursor = Cursors.ScrollAll;
            }
            else if (draggedObstacle != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Engeli sürükle
                draggedObstacle.X = pos.X - obstacleDragOffset.X;
                draggedObstacle.Y = pos.Y - obstacleDragOffset.Y;
                RenderCanvas.Cursor = Cursors.SizeAll;
            }
            else if (isDragSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                // Çoklu seçim kutusu boyutlarını güncelle
                double x = Math.Min(pos.X, dragSelectionStart.X);
                double y = Math.Min(pos.Y, dragSelectionStart.Y);
                double width = Math.Max(1.0, Math.Abs(pos.X - dragSelectionStart.X));
                double height = Math.Max(1.0, Math.Abs(pos.Y - dragSelectionStart.Y));

                Canvas.SetLeft(selectionBox, x);
                Canvas.SetTop(selectionBox, y);
                selectionBox.Width = width;
                selectionBox.Height = height;
            }
        }

        /// <summary>
        /// Fare yakalaması beklenmedik şekilde kaybolursa (ör. Alt-Tab, başka bir pencereye
        /// odaklanma) sürükleme durumunu güvenle sonlandırır - aksi halde araç "sürükleniyormuş"
        /// durumda takılı kalabilirdi.
        /// </summary>
        /// <summary>
        /// İşlemci (CPU) Takometresi + Batarya Uyarısı: sistem yükü/pil durumu periyodik olarak
        /// (saniyede birkaç kez değil, ~1.5 saniyede bir) kontrol edilir. Yüksek CPU yükünde
        /// araçlara geçici bir "hararet" hız/agresiflik bonusu ve görsel uyarı verilir; düşük
        /// pilde (şarjda değilken) araçlar yorgun düşüp yakıtları bitiyormuş gibi davranır.
        /// </summary>
        private void CheckHardwareStatus()
        {
            hardwareCheckCooldown--;
            if (hardwareCheckCooldown > 0) return;
            hardwareCheckCooldown = 90;

            if (cpuCounter != null)
            {
                try { currentCpuLoad = cpuCounter.NextValue(); } catch { currentCpuLoad = 0; }
            }

            if (currentCpuLoad > 85 && gameCore.GameTime - lastHeatWarningTick > 1200)
            {
                lastHeatWarningTick = gameCore.GameTime;
                ShowToast(Loc.T("msg.high_cpu_load", (int)currentCpuLoad));
                var hotVehicle = gameCore.AutoMotions.FirstOrDefault();
                if (hotVehicle != null) SpawnBackfire(hotVehicle);
            }

            try
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool isLowBattery = power.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Online
                                     && power.BatteryLifePercent > 0 && power.BatteryLifePercent < 0.15f;
                if (isLowBattery && gameCore.GameTime - lastBatteryWarningTick > 1800)
                {
                    lastBatteryWarningTick = gameCore.GameTime;
                    ShowToast(Loc.T("msg.low_battery", (int)(power.BatteryLifePercent * 100)));
                }
            }
            catch { }
        }

        /// <summary>
        /// Trafik Sıkışıklığı: bir kenara (EdgeSide) uzun süre neredeyse hareketsiz yığılan araçlar
        /// periyodik olarak korna çalar. Fareyle tıklanınca (bkz. Canvas_MouseDown) dağıtılabilirler.
        /// </summary>
        private void UpdateTrafficJams()
        {
            foreach (var vehicle in gameCore.AutoMotions)
            {
                bool isStuckAtEdge = vehicle.EdgeSide != 0 && !vehicle.IsDragged && !vehicle.IsDriven
                    && Math.Abs(vehicle.VelocityX) < 0.3 && Math.Abs(vehicle.VelocityY) < 0.3;

                if (isStuckAtEdge)
                {
                    jamStuckTicks.TryGetValue(vehicle, out double ticks);
                    double before = ticks;
                    ticks += gameCore.DeltaFrames;
                    jamStuckTicks[vehicle] = ticks;

                    // ~4 sn sonra, ~2.5 sn'de bir korna. Kesirli sayaçla "% 150 == 0" güvenilir
                    // olmadığından, 150'lik eşiğin geçildiği kare yakalanır.
                    if (ticks > 240 && Math.Floor(before / 150) != Math.Floor(ticks / 150))
                    {
                        soundEngine.PlayHorn(vehicle.Type);
                    }
                }
                else
                {
                    jamStuckTicks.Remove(vehicle);
                }
            }
        }

        /// <summary>
        /// Masaüstü Araba Buluşması: belirli aralıklarla tüm araçlar işi bırakıp ekranın ortasına
        /// toplanır, kısa bir süre orada kalıp sonra normal davranışa döner.
        /// </summary>
        private void UpdateCarMeetEvent()
        {
            if (!carMeetActive)
            {
                if (gameCore.GameTime < nextCarMeetTick || gameCore.AutoMotions.Count < 3) return;

                carMeetActive = true;
                carMeetTicksLeft = 420; // ~7 saniye
                double cx = RenderCanvas.ActualWidth / 2;
                double cy = RenderCanvas.ActualHeight - 60;
                int i = 0;
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsDragged || vehicle.IsLocked || vehicle.IsDriven) continue;
                    double angle = i * (360.0 / gameCore.AutoMotions.Count) * Math.PI / 180.0;
                    vehicle.TargetX = cx + Math.Cos(angle) * 90;
                    vehicle.TargetY = cy + Math.Sin(angle) * 22;
                    i++;
                }
                gameCore.GameData.CarMeetsAttended++;
                ShowToast(Loc.T("msg.masaustu_araba_bulusmasi_basladi"));
                soundEngine.PlaySuccess();
            }
            else
            {
                carMeetTicksLeft -= gameCore.DeltaFrames;
                if (carMeetTicksLeft <= 0)
                {
                    carMeetActive = false;
                    nextCarMeetTick = gameCore.GameTime + 36000; // bir sonraki buluşma ~10 dk sonra
                    foreach (var vehicle in gameCore.AutoMotions)
                    {
                        vehicle.TargetX = -1;
                        vehicle.TargetY = -1;
                    }
                    ShowToast(Loc.T("msg.bulusma_sona_erdi_araclar_yollarina_devam"));
                }
            }
        }

        /// <summary>
        /// Ekranlar Arası Atlama Rampası: çoklu-monitör modunda bir ekran sınırını hızla geçen
        /// araçlar sanal bir rampadan uçuyormuş gibi hafifçe zıplar ve kıvılcım bırakır.
        /// </summary>
        private void UpdateMonitorJumpRamps()
        {
            if (monitorBoundaryX.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (!lastVehicleX.TryGetValue(vehicle, out double prevX))
                {
                    lastVehicleX[vehicle] = vehicle.X;
                    continue;
                }

                if (Math.Abs(vehicle.VelocityX) > 1.0)
                {
                    foreach (var boundary in monitorBoundaryX)
                    {
                        bool crossed = (prevX < boundary && vehicle.X >= boundary) || (prevX > boundary && vehicle.X <= boundary);
                        if (crossed)
                        {
                            vehicle.VelocityY -= 3.5;
                            for (int i = 0; i < 4; i++)
                            {
                                particles.Add(new VehicleParticle
                                {
                                    X = boundary,
                                    Y = vehicle.Y + vehicle.Height / 2,
                                    VelocityX = (Random.Shared.NextDouble() - 0.5) * 2,
                                    VelocityY = -Random.Shared.NextDouble() * 2,
                                    Size = Random.Shared.Next(4, 8),
                                    Opacity = 1.0,
                                    Color = Color.FromRgb(0xFF, 0xD7, 0x00)
                                });
                            }
                            soundEngine.PlayDrift();
                            break;
                        }
                    }
                }

                lastVehicleX[vehicle] = vehicle.X;
            }
        }

        /// <summary>
        /// Fiziksel Rampa Çizimi: orta fare tuşuyla çizilen geçici çizgiler katı zemin gibi
        /// davranır - araçlar üzerine düşünce durur/seker, üzerinden geçince zıplayabilir.
        /// Ömrü dolan rampalar yavaşça kaybolur.
        /// </summary>
        private void UpdatePhysicsRamps()
        {
            for (int i = physicsRamps.Count - 1; i >= 0; i--)
            {
                var (a, b, life) = physicsRamps[i];
                life -= gameCore.DeltaFrames;
                if (life <= 0) { physicsRamps.RemoveAt(i); continue; }
                physicsRamps[i] = (a, b, life);
            }

            if (physicsRamps.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.VelocityY < 0) continue; // sadece düşerken/yere basarken

                double footY = vehicle.Y + vehicle.Height / 2;

                foreach (var (a, b, _) in physicsRamps)
                {
                    double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
                    if (vehicle.X < minX || vehicle.X > maxX) continue;
                    if (Math.Abs(b.X - a.X) < 0.01) continue;

                    double t = (vehicle.X - a.X) / (b.X - a.X);
                    double rampY = a.Y + (b.Y - a.Y) * t;

                    if (footY >= rampY - 4 && footY <= rampY + 14)
                    {
                        vehicle.Y = rampY - vehicle.Height / 2;
                        if (vehicle.VelocityY > 2)
                        {
                            vehicle.VelocityY *= -0.4; // rampadan sekme
                        }
                        else
                        {
                            vehicle.VelocityY = 0;
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Müzik Süspansiyon Senkronizasyonu: arka planda çalan müziğin anlık ses seviyesine göre
        /// (bas vuruşu gibi) tüm araçlar hafifçe zıplar - lowrider tarzı ritmik süspansiyon hissi.
        /// Ses tamamen sessizse veya kayıt başlatılamadıysa (audioCapture null) hiçbir şey olmaz.
        /// </summary>
        private void UpdateMusicSync()
        {
            if (audioCapture == null || !musicSyncEnabled) return;

            musicBounceCooldown--;
            if (musicBounceCooldown > 0) return;

            float level = currentAudioLevel;
            if (level < 0.35f) return; // sessiz/normal seviye - sadece belirgin vuruşlarda tepki ver

            musicBounceCooldown = 8; // art arda titremesin diye kısa bir bekleme
            double bounceStrength = Math.Min(1.0, level) * 2.2;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.IsFlying || vehicle.IsDriven) continue;
                if (vehicle.EdgeSide != 0) continue; // sadece zemindeki araçlar
                vehicle.VelocityY -= bounceStrength;
            }
        }

        private void SpawnBackfire(Vehicle vehicle)
        {
            double angleRad = vehicle.Angle + Math.PI; // araç arkası
            double offsetX = Math.Cos(angleRad) * (vehicle.Width / 2);
            double offsetY = Math.Sin(angleRad) * (vehicle.Height / 2);

            for (int i = 0; i < 5; i++)
            {
                particles.Add(new VehicleParticle
                {
                    X = vehicle.X + offsetX,
                    Y = vehicle.Y + offsetY,
                    VelocityX = Math.Cos(angleRad) * (1.5 + Random.Shared.NextDouble()) + (Random.Shared.NextDouble() - 0.5),
                    VelocityY = Math.Sin(angleRad) * (1.5 + Random.Shared.NextDouble()) + (Random.Shared.NextDouble() - 0.5) - 0.5,
                    Size = Random.Shared.Next(6, 12),
                    Opacity = 1.0,
                    Color = i % 2 == 0 ? Color.FromRgb(255, 140, 0) : Color.FromRgb(255, 60, 0)
                });
            }
            soundEngine.PlayCrash();
        }

        private void SpawnSkidMark(Vehicle vehicle)
        {
            if (lowEffectsMode) return;
            skidMarks.Add(new SkidMark
            {
                X = vehicle.X,
                Y = vehicle.Y + vehicle.Height / 2 - 2,
                Angle = vehicle.EdgeSide == 0 ? 0 : Math.PI / 2,
                Width = vehicle.Width * 0.7
            });
        }

        private void RenderCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (draggedVehicle != null)
            {
                draggedVehicle.IsDragged = false;
                draggedVehicle.ResetEdgeStateAfterDrag();
                draggedVehicle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
            draggedObstacle = null;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDrawingRamp && e.MiddleButton == MouseButtonState.Released)
            {
                isDrawingRamp = false;
                RenderCanvas.ReleaseMouseCapture();
                return;
            }

            if (draggedVehicle != null)
            {
                // Sürüklemeyi sonlandır, limitli fırlatma hızı ile serbest bırak
                draggedVehicle.IsDragged = false;
                draggedVehicle.IsFlying = false; // bırakınca yerçekimine tabi olup düşsün

                // Sürükleme öncesi bir duvara/tavana tırmanmış (EdgeSide != 0) olabilir; bu durumda
                // bırakıldığı yeni konumdan bağımsız olarak yerçekimi o eski duvara doğru yanlamasına
                // çekiyor ve araç havada "asılı" kalıyormuş gibi görünüyordu. Bırakınca zemin moduna
                // sıfırlanır - gerçek konumuna göre gerekiyorsa fizik kendi kenar tırmanmasını yeniden
                // tespit eder. ResetEdgeStateAfterDrag ayrıca gövde açısını (WallAngleSmoothed) da
                // zemin yönelimine akıcı geçişle döndürür - aksi halde araç tavandan/duvardan
                // bırakılınca zeminde kalıcı olarak ters/yan görünürdü (bkz. Models/AutoMotion.cs).
                draggedVehicle.ResetEdgeStateAfterDrag();

                draggedVehicle.VelocityX = Math.Clamp(draggedVehicle.VelocityX, -25, 25);
                draggedVehicle.VelocityY = Math.Clamp(draggedVehicle.VelocityY, -25, 25);

                draggedVehicle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
                RenderCanvas.ReleaseMouseCapture();
            }

            if (draggedObstacle != null)
            {
                draggedObstacle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
                RenderCanvas.ReleaseMouseCapture();
            }

            if (isDragSelecting)
            {
                // Çoklu seçim alanını hesapla ve içindeki araçları seç
                double x = Canvas.GetLeft(selectionBox);
                double y = Canvas.GetTop(selectionBox);
                double w = selectionBox.Width;
                double h = selectionBox.Height;
                Rect selectRect = new Rect(x, y, w, h);

                foreach (var vehicle in gameCore.AutoMotions)
                {
                    Rect vehRect = new Rect(vehicle.X - vehicle.Width / 2, vehicle.Y - vehicle.Height / 2, vehicle.Width, vehicle.Height);
                    if (selectRect.IntersectsWith(vehRect))
                    {
                        selectedVehicles.Add(vehicle);
                    }
                }

                if (selectedVehicles.Count > 0)
                {
                    selectedVehicle = selectedVehicles[0];
                    VehicleListBox.SelectedItem = selectedVehicle;
                    soundEngine.PlaySuccess();
                }

                RenderCanvas.Children.Remove(selectionBox);
                selectionBox = null;
                isDragSelecting = false;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
        }

        private void Canvas_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(RenderCanvas);

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsPointInside(pos.X, pos.Y))
                {
                    selectedVehicle = vehicle;
                    ShowVehicleContextMenu(vehicle);
                    return;
                }
            }
        }

        /// <summary>Bir araca sağ tıklayınca hızlı erişim menüsü (kilitle/uçur/dorse/sil/eşya kullan)</summary>
        private void ShowVehicleContextMenu(Vehicle vehicle)
        {
            var menu = new ContextMenu();

            var lockItem = new MenuItem { Header = vehicle.IsLocked ? "🔓 Kilidi Aç" : "🔒 Kilitle" };
            lockItem.Click += (s, e) =>
            {
                vehicle.IsLocked = !vehicle.IsLocked;
                // Kilitlerken artık garaj ızgarasına ışınlanmıyor - araç TAM OLARAK bulunduğu
                // konumda donuyor (bkz. ToggleLockBtn_Click).
                vehicle.DockX = -1;
                vehicle.DockY = -1;
                PlayUiClick();
            };
            menu.Items.Add(lockItem);

            var flyItem = new MenuItem { Header = vehicle.IsFlying ? "🛬 Uçuşu Kapat" : "🛫 Uçuşu Aç" };
            flyItem.Click += (s, e) => { vehicle.IsFlying = !vehicle.IsFlying; if (vehicle.IsFlying) vehicle.IsLocked = false; PlayUiClick(); };
            menu.Items.Add(flyItem);

            if (gameCore.Items.Count > 0)
            {
                var useItem = new MenuItem { Header = "🎒 Eşya Kullan" };
                useItem.Click += (s, e) =>
                {
                    if (gameCore.UseItem(gameCore.Items.Count - 1, vehicle))
                    {
                        soundEngine.PlaySuccess();
                        RefreshItemsList();
                    }
                };
                menu.Items.Add(useItem);
            }

            var trailerMenu = new MenuItem { Header = "🚛 Dorse / Ekipman" };
            foreach (var kv in Vehicle.TrailerCatalog)
            {
                string trailerKey = kv.Key;
                var trailerItem = new MenuItem { Header = $"{kv.Value.emoji} {kv.Value.label}" };
                trailerItem.Click += (s, e) => { vehicle.TrailerType = trailerKey; PlayUiClick(); };
                trailerMenu.Items.Add(trailerItem);
            }
            trailerMenu.Items.Add(new Separator());
            var removeTrailerItem = new MenuItem { Header = "✕ Dorseyi Çıkar" };
            removeTrailerItem.Click += (s, e) => { vehicle.TrailerType = ""; PlayUiClick(); };
            trailerMenu.Items.Add(removeTrailerItem);
            menu.Items.Add(trailerMenu);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Sil" };
            deleteItem.Click += (s, e) =>
            {
                gameCore.RemoveAutoMotion(vehicle);
                if (selectedVehicle == vehicle) selectedVehicle = null;
                PlayUiClick();
            };
            menu.Items.Add(deleteItem);

            RenderCanvas.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private Rect restoreBounds;
        private bool isPseudoMaximized = false;

        // Pencere sürükleme (top bar'dan)
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (e.ClickCount == 2)
                {
                    // NOT: WindowState.Maximized kullanılmıyor - WindowStyle=None pencerelerde bu,
                    // görev çubuğunun ÜZERİNİ de kaplayarak araçların görev çubuğu altına/içine
                    // düşmesine sebep oluyordu. Bunun yerine manuel olarak WorkArea sınırlarına
                    // (görev çubuğu hariç) büyütme/küçültme yapılır.
                    if (!isPseudoMaximized)
                    {
                        restoreBounds = new Rect(Left, Top, Width, Height);
                        ApplyFullScreenBoundsTaskbarOnBottom();
                        isPseudoMaximized = true;
                    }
                    else
                    {
                        Left = restoreBounds.Left;
                        Top = restoreBounds.Top;
                        Width = restoreBounds.Width;
                        Height = restoreBounds.Height;
                        isPseudoMaximized = false;
                    }
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            // Görev çubuğuna küçültmek yerine tepsiye gizle - uygulama arka planda çalışmaya devam eder
            Hide();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private bool isExiting;

        /// <summary>
        /// Uygulamayı KESİN olarak kapatır. Eskiden tepsideki "Çıkış" sadece Close() çağırıyordu -
        /// pencere Hide() ile gizliyken (tepsiye küçültülmüşken) bazı ortamlarda Closed olayı
        /// zincirinin bir yerinde (ör. saveManager.Save/soundEngine.Dispose sırasında) atılan bir
        /// istisna App.xaml.cs'teki genel DispatcherUnhandledException tarafından sessizce
        /// yutulabiliyor, bu da kapanmayı görünürde "hiçbir şey olmamış" gibi yarıda bırakabiliyordu.
        /// Artık her adım kendi try/catch'i içinde çalışır VE en sonunda Application.Shutdown()
        /// AÇIKÇA çağrılır - pencerenin Closed olayının doğal akışına güvenmek yerine kapanışı
        /// garantiler.
        ///
        /// KÖK NEDEN (bulundu): burada eskiden temizlikten sonra AYRICA Close() da çağrılıyordu.
        /// Pencere Alt+F4/görev çubuğu "Kapat" gibi bir yoldan kapanırken akış zaten şöyleydi:
        /// Close() -> Closed olayı -> Window_Closed -> ExitApplication() -> (buradaki) Close() ->
        /// yani pencere KENDİ Closed olayının İÇİNDEN tekrar Close() çağrılıyordu. Bu, zaten
        /// kapanma sürecinde olan bir pencereyi tekrar kapatmaya çalışan iç içe geçmiş (re-entrant)
        /// bir çağrıydı - istisna fırlatmadan sessizce askıda kalabilen bilinen bir WPF tuzağı,
        /// hiçbir şey error.log'a düşmeden uygulamanın "kapanmıyormuş gibi" görünmesini açıklar.
        /// Application.Shutdown() zaten tüm pencereleri kapatıp süreci sonlandırmaya tek başına
        /// yeterli olduğundan Close() çağrısı hem gereksizdi hem de bu riski taşıyordu - kaldırıldı.
        /// </summary>
        private void ExitApplication()
        {
            if (isExiting) return;
            isExiting = true;

            if (Environment.GetEnvironmentVariable("AUTOMOTION_TRACE_EXIT") == "1")
            {
                try
                {
                    System.IO.File.AppendAllText("exit-trace.txt",
                        $"[{DateTime.Now:HH:mm:ss.fff}] ExitApplication cagrildi\r\n{Environment.StackTrace}\r\n\r\n");
                }
                catch { }
            }

            try { trayIcon.Visible = false; } catch { }
            try { soundEngine?.Dispose(); } catch { }
            try { audioCapture?.StopRecording(); audioCapture?.Dispose(); } catch { }
            try { cpuCounter?.Dispose(); } catch { }
            try { _ = iotService.DisconnectAsync(); } catch { }
            try { saveManager.Save("AutoSave", gameCore.Save()); } catch { }
            try { SavePanelLayout(); } catch { }
            // Bekleyen gecikmeli ayar yazması varsa (ör. son saniyede oynatılan çarpan kaydırıcısı)
            // süreç sonlanmadan diske insin.
            try { settings.Flush(); } catch { }
            try { trayIcon?.Dispose(); } catch { }
            try { windowIcon?.Dispose(); } catch { }

            System.Windows.Application.Current.Shutdown();
        }

        private void PanelToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            panelsVisible = !panelsVisible;
            AnimateVisibility(PanelsContainer, panelsVisible);
            PlayUiClick();
        }

        /// <summary>
        /// Tüm arayüzü (üst dock + kontrol panelleri + arkaplan ızgarası) gizleyip sadece
        /// araçların kaldığı "sadece araçlar" moduna geçer. Dock da gizlendiği için geri
        /// getirmek yalnızca G kısayoluyla mümkündür.
        /// </summary>
        private bool multiMonitorMode = false;

        // Pomodoro Odak Seansı: aktifken filonun yakıt tüketimi yarıya iner (gameCore.FuelMultiplier
        // üzerinden - zaten kayıt/yükleme ve testlerle desteklenen mevcut mekanizma), seans doğal
        // olarak bitince (iptal değil) filo büyüklüğüne göre nakit ödül verilir.
        private readonly PomodoroSession pomodoroSession = new();
        private double? preFocusFuelMultiplier;

        private void PomodoroBtn_Click(object sender, RoutedEventArgs e)
        {
            if (pomodoroSession.IsActive)
            {
                pomodoroSession.Cancel();
                RestoreFuelMultiplierAfterFocus();
                ShowToast(Loc.T("msg.odak_seansi_iptal_edildi"));
            }
            else
            {
                preFocusFuelMultiplier = gameCore.FuelMultiplier;
                gameCore.FuelMultiplier = gameCore.FuelMultiplier * 0.5;
                pomodoroSession.Start();
                ShowToast(Loc.T("msg.25_dakikalik_odak_seansi_basladi_filo"));
            }
            PomodoroBtn.Opacity = pomodoroSession.IsActive ? 1.0 : 0.4;
            PlayUiClick();
        }

        private void RestoreFuelMultiplierAfterFocus()
        {
            if (preFocusFuelMultiplier.HasValue)
            {
                gameCore.FuelMultiplier = preFocusFuelMultiplier.Value;
                preFocusFuelMultiplier = null;
            }
        }

        // Sürükle-Bırak Kargo: masaüstünden bir dosya sürükleyip bırakınca boşta bir araç bu
        // "kargo"yu rastgele bir teslimat noktasına taşır, ulaşınca nakit ödül verilir. Dosyanın
        // kendisi hiçbir şekilde okunmaz/işlenmez - yalnızca adı bildirimde gösterilir.
        private readonly Dictionary<Vehicle, string> activeCargoDeliveries = new();

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

            // raceVehicle/raceRival hariç tutulur: HasCargo=false olsalar da o an aktif bir Yazı
            // Yarışı'nda TargetX/TargetY ile yönlendiriliyorlar - onları taşıyıcı seçmek yarışı
            // sessizce bozardı (hedefleri kargo teslim noktasıyla değiştirilirdi).
            var carrier = gameCore.AutoMotions.FirstOrDefault(v => !v.HasCargo && !v.IsDestroyed && !v.IsDragged
                && v != raceVehicle && v != raceRival);
            if (carrier == null)
            {
                ShowToast(Loc.T("msg.kargoyu_tasiyacak_bosta_arac_yok"));
                return;
            }

            double canvasWidth = RenderCanvas.ActualWidth;
            double canvasHeight = RenderCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            string fileName = System.IO.Path.GetFileName(files[0]);
            carrier.HasCargo = true;
            carrier.TargetX = Random.Shared.NextDouble() * canvasWidth;
            carrier.TargetY = Random.Shared.NextDouble() * canvasHeight * 0.6; // tabana batmasın diye üst kısımda kal
            activeCargoDeliveries[carrier] = fileName;

            ShowToast(Loc.T("msg.file_cargo_started", carrier.Emoji, fileName));
        }

        private void CheckCargoDeliveries()
        {
            if (activeCargoDeliveries.Count == 0) return;

            List<Vehicle> completed = null;
            foreach (var (vehicle, fileName) in activeCargoDeliveries)
            {
                if (!vehicle.HasCargo || vehicle.IsDestroyed || !gameCore.AutoMotions.Contains(vehicle))
                {
                    (completed ??= new List<Vehicle>()).Add(vehicle);
                    continue;
                }

                double dx = vehicle.TargetX - vehicle.X;
                double dy = vehicle.TargetY - vehicle.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 20) continue;

                vehicle.HasCargo = false;
                vehicle.TargetX = -1;
                vehicle.TargetY = -1;
                gameCore.AddMoney(75);
                ShowToast(Loc.T("msg.file_cargo_delivered", fileName));
                soundEngine.PlayCash();
                (completed ??= new List<Vehicle>()).Add(vehicle);
            }

            if (completed != null)
                foreach (var v in completed) activeCargoDeliveries.Remove(v);
        }

        // IoT/MQTT Telemetri: isteğe bağlı, varsayılan kapalı - bkz. Services/IoTTelemetryService.cs
        // için kapsam/kısıtlama notları (gerçek donanım/broker gerektirir, bu ortamda doğrulanamaz).
        private readonly IoTTelemetryService iotService = new();
        private DateTime lastIotPublishAt = DateTime.MinValue;

        private async void IoTConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            PlayUiClick();

            if (iotService.IsConnected)
            {
                await iotService.DisconnectAsync();
                IoTStatusTxt.Text = Loc.T("ui.bagli_degil");
                IoTConnectBtn.Content = Loc.T("ui.baglan");
                ShowToast(Loc.T("msg.iot_baglantisi_kesildi"));
                return;
            }

            bool useTls = IoTUseTlsCheck.IsChecked == true;
            var (host, port) = MqttConnectionOptions.ParseAddress(IoTBrokerBox.Text, useTls);

            if (string.IsNullOrWhiteSpace(host))
            {
                ShowToast(Loc.T("msg.broker_adresi_bos_olamaz"));
                return;
            }

            var options = new MqttConnectionOptions
            {
                Host = host,
                Port = port,
                Topic = IoTTopicBox.Text,
                UseTls = useTls,
                AllowUntrustedCertificate = useTls && IoTAllowUntrustedCheck.IsChecked == true,
                Username = IoTUsernameBox.Text?.Trim() ?? "",
                Password = IoTPasswordBox.Password ?? ""
            };

            // MQTT'de kullanıcı adı/parola CONNECT paketinde AÇIK METİN taşınır - TLS'siz bir
            // bağlantıda broker parolası ağı dinleyen herkese görünür. Bağlantı engellenmez
            // (kullanıcının kendi yerel ağı olabilir) ama sessiz de kalınmaz.
            if (options.SendsCredentialsInClear)
                ShowToast(Loc.T("msg.iot_kimlik_tlssiz_uyari"));

            IoTStatusTxt.Text = Loc.T("ui.iot_connecting");
            bool connected = await iotService.ConnectAsync(options);

            if (connected)
            {
                IoTStatusTxt.Text = Loc.T(options.UseTls ? "ui.iot_connected_tls" : "ui.iot_connected", host, port);
                IoTConnectBtn.Content = "📡 Kes";
                ShowToast(Loc.T("msg.iot_broker_a_baglanildi_filo_telemetrisi"));
                SaveIoTPreferences();
            }
            else
            {
                // Eskiden her başarısızlık aynı jenerik mesajdı; artık broker'ın/TLS katmanının
                // söylediği asıl sebep de gösteriliyor (yanlış parola mı, sertifika mı, yanlış port mu).
                IoTStatusTxt.Text = string.IsNullOrEmpty(iotService.LastError)
                    ? Loc.T("ui.iot_connect_failed")
                    : Loc.T("ui.iot_connect_failed") + ": " + iotService.LastError;
                ShowToast(Loc.T("msg.broker_a_baglanilamadi_adresi_portu_kontrol"));
            }
        }

        /// <summary>TLS kapatılınca sertifika doğrulamasını atlama seçeneği anlamsızlaşır - devre
        /// dışı bırakılıp temizlenir ki kullanıcı "atlıyorum" sanmasın.</summary>
        private void IoTUseTlsCheck_Click(object sender, RoutedEventArgs e)
        {
            bool useTls = IoTUseTlsCheck.IsChecked == true;
            IoTAllowUntrustedCheck.IsEnabled = useTls;
            if (!useTls) IoTAllowUntrustedCheck.IsChecked = false;
            PlayUiClick();
        }

        /// <summary>Broker adresi/konusu/kullanıcı adı ve TLS tercihleri saklanır; PAROLA SAKLANMAZ
        /// (bkz. MqttConnectionOptions). Böylece kullanıcı her açılışta adresi yeniden yazmaz ama
        /// düz metin bir ayar dosyasında parola tutulmuş olmaz.</summary>
        private void SaveIoTPreferences()
        {
            settings.Current.IoTBrokerAddress = IoTBrokerBox.Text?.Trim() ?? "";
            settings.Current.IoTTopic = IoTTopicBox.Text?.Trim() ?? "";
            settings.Current.IoTUsername = IoTUsernameBox.Text?.Trim() ?? "";
            settings.Current.IoTUseTls = IoTUseTlsCheck.IsChecked == true;
            settings.Current.IoTAllowUntrustedCertificate = IoTAllowUntrustedCheck.IsChecked == true;
            settings.Save();
        }

        /// <summary>Kaydedilmiş IoT alanlarını açılışta geri yükler (parola hariç - o hiç saklanmaz).</summary>
        private void ApplyPersistedIoTPreferences()
        {
            var prefs = settings.Current;

            if (!string.IsNullOrWhiteSpace(prefs.IoTBrokerAddress)) IoTBrokerBox.Text = prefs.IoTBrokerAddress;
            if (!string.IsNullOrWhiteSpace(prefs.IoTTopic)) IoTTopicBox.Text = prefs.IoTTopic;
            IoTUsernameBox.Text = prefs.IoTUsername ?? "";

            IoTUseTlsCheck.IsChecked = prefs.IoTUseTls;
            IoTAllowUntrustedCheck.IsEnabled = prefs.IoTUseTls;
            IoTAllowUntrustedCheck.IsChecked = prefs.IoTUseTls && prefs.IoTAllowUntrustedCertificate;
        }

        private async void PublishIotTelemetryIfDue()
        {
            if (!iotService.IsConnected) return;
            if ((DateTime.Now - lastIotPublishAt).TotalSeconds < 15) return;
            lastIotPublishAt = DateTime.Now;

            await iotService.PublishFleetTelemetryAsync(
                gameCore.AutoMotions.Count, gameCore.GameData.Money, gameCore.GameData.TotalMileage);
        }

        // Klavye Kısayollarını Aç/Kapat: bkz. HandleWindowKeyDown içindeki not - serbest yazma
        // gerektiren özelliklerle (Yazı Yarışı) tekil-tuş kısayolları çakışmasın diye eklendi.
        private bool shortcutsEnabled = true;

        private void ToggleShortcutsBtn_Click(object sender, RoutedEventArgs e)
        {
            shortcutsEnabled = !shortcutsEnabled;
            ShortcutsBtn.Opacity = shortcutsEnabled ? 1.0 : 0.4;
            ShortcutsBtn.ToolTip = shortcutsEnabled
                ? "Klavye Kısayolları: Açık (yazarken kapatmak için tıkla)"
                : "Klavye Kısayolları: Kapalı (tekrar açmak için tıkla)";
            ShowToast(shortcutsEnabled
                ? "⌨️🔓 Klavye kısayolları açık"
                : "⌨️🔒 Klavye kısayolları kapalı - artık yazarken tetiklenmezler");
            PlayUiClick();
        }

        // Yazı Yarışı: oyuncunun gerçek yazma hızı (WPM) bir aracı, sabit tempolu bir rakibe karşı
        // pistte ilerletir. Global klavye kancası kullanılmaz - yalnızca bu pencereye gelen tuşlar
        // sayılır (bkz. HandleWindowKeyDown içindeki mevcut "Tuş Vuruşu Sismik Etkisi" ile aynı ilke).
        private readonly WpmRace wpmRace = new();
        private Vehicle raceVehicle;
        private Vehicle raceRival;
        private double raceStartX;
        private double raceFinishX;
        private double raceTrackY;

        private void WpmRaceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (wpmRace.IsActive)
            {
                CancelWpmRace();
                ShowToast(Loc.T("msg.yazi_yarisi_iptal_edildi"));
                PlayUiClick();
                return;
            }

            // UseLearningAI açık araçlar hariç tutulur: Q-learning navigasyonu (bkz.
            // Vehicle.UpdateLearningNavigation) keşif amaçlı gecikmeli/rastgele kararlar içerir - bu,
            // yazma hızına orantılı DÜZ ilerleme bekleyen yarış mantığıyla çakışıp yarışı tutarsız
            // (bazen duran, bazen sıçrayan) hissettirirdi.
            var candidates = gameCore.AutoMotions
                .Where(v => !v.IsDestroyed && !v.IsDragged && !v.IsLocked && !v.IsDriven
                            && !v.HasCargo && !v.UseLearningAI && !activeCargoDeliveries.ContainsKey(v))
                .ToList();
            if (candidates.Count < 2)
            {
                ShowToast(Loc.T("msg.yaris_icin_en_az_2_bosta"));
                return;
            }

            double canvasWidth = RenderCanvas.ActualWidth;
            double canvasHeight = RenderCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            raceVehicle = candidates[Random.Shared.Next(candidates.Count)];
            candidates.Remove(raceVehicle);
            raceRival = candidates[Random.Shared.Next(candidates.Count)];

            raceStartX = 60;
            raceFinishX = canvasWidth - 60;
            raceTrackY = canvasHeight * 0.3;

            wpmRace.Start();
            WpmRaceBtn.Opacity = 1.0;
            ShowToast(Loc.T("msg.typing_race_started", raceVehicle.Emoji, raceRival.Emoji, WpmRace.TargetWpm.ToString("0")));
            PlayUiClick();
        }

        private void CancelWpmRace()
        {
            wpmRace.Cancel();
            if (raceVehicle != null) { raceVehicle.TargetX = -1; raceVehicle.TargetY = -1; }
            if (raceRival != null) { raceRival.TargetX = -1; raceRival.TargetY = -1; }
            raceVehicle = null;
            raceRival = null;
            WpmRaceBtn.Opacity = 0.4;
        }

        private void UpdateWpmRace()
        {
            if (!wpmRace.IsActive) return;

            if (raceVehicle == null || raceRival == null
                || raceVehicle.IsDestroyed || raceRival.IsDestroyed
                || !gameCore.AutoMotions.Contains(raceVehicle) || !gameCore.AutoMotions.Contains(raceRival))
            {
                CancelWpmRace();
                return;
            }

            raceVehicle.TargetX = raceStartX + (raceFinishX - raceStartX) * Math.Clamp(wpmRace.PlayerProgress, 0, 1);
            raceVehicle.TargetY = raceTrackY;
            raceRival.TargetX = raceStartX + (raceFinishX - raceStartX) * Math.Clamp(wpmRace.RivalProgress, 0, 1);
            raceRival.TargetY = raceTrackY + 40;

            if (wpmRace.Tick(out bool didPlayerWin))
            {
                int measuredWpm = (int)wpmRace.CurrentWpm;
                CancelWpmRace();

                if (didPlayerWin)
                {
                    gameCore.AddMoney(150);
                    ShowToast(Loc.T("msg.typing_race_won", measuredWpm));
                    soundEngine.PlayCash();
                }
                else
                {
                    ShowToast(Loc.T("msg.typing_race_lost", measuredWpm));
                }
            }
        }

        private void CheckPomodoroCompletion()
        {
            if (!pomodoroSession.CheckCompleted()) return;

            RestoreFuelMultiplierAfterFocus();

            int truckCount = gameCore.AutoMotions.Count(v => v.Type == "truck");
            int bonus = 50 + truckCount * 25;
            gameCore.AddMoney(bonus);
            ShowToast(Loc.T("msg.focus_session_done", bonus));
            soundEngine.PlayCash();
        }
        private MiniHudWindow miniHud;

        private void ToggleMiniHud_Click(object sender, RoutedEventArgs e)
        {
            if (miniHud == null)
            {
                miniHud = new MiniHudWindow();
                miniHud.Closed += (s, args) => miniHud = null;
                miniHud.Left = SystemParameters.WorkArea.Right - 260;
                miniHud.Top = SystemParameters.WorkArea.Top + 20;
                miniHud.Show();
            }
            else
            {
                miniHud.Close();
            }
            PlayUiClick();
        }

        /// <summary>
        /// Tek ekran (görev çubuğu hariç WorkArea) ile tüm bağlı ekranları kapsayan sanal
        /// masaüstü (VirtualScreen) sınırları arasında geçiş yapar - araçlar birden fazla
        /// monitörde gezinebilsin diye. Not: ikincil ekranların görev çubuğu alanı hesaba
        /// katılmaz (Windows sadece birincil ekranın WorkArea'sını API üzerinden verir).
        /// </summary>
        private void ToggleMultiMonitor_Click(object sender, RoutedEventArgs e)
        {
            multiMonitorMode = !multiMonitorMode;

            if (multiMonitorMode)
            {
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;

                // Ekranlar Arası Atlama Rampası: her ekranın sol kenarını (birincil hariç) bir
                // "rampa" sınırı olarak kaydet - araç bu sınırı geçince zıplama efekti alır.
                monitorBoundaryX.Clear();
                monitorFloors.Clear();
                try
                {
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        double boundaryLeft = screen.Bounds.Left - Left;
                        if (boundaryLeft > 5) monitorBoundaryX.Add(boundaryLeft);

                        // Her ekranın KENDİ taban çizgisi: WorkingArea, .NET tarafından o ekranın
                        // KENDİ görev çubuğu payı düşülerek hesaplanır (yalnızca birincilde değil -
                        // "Tüm ekranlarda göster" açıksa ikincillerde de doğru sonuç verir).
                        monitorFloors.Add((screen.Bounds.Left - Left, screen.Bounds.Right - Left, screen.WorkingArea.Bottom - Top));
                    }
                }
                catch { }
            }
            else
            {
                ApplyFullScreenBoundsTaskbarOnBottom();
                monitorBoundaryX.Clear();
                monitorFloors.Clear();
            }

            isPseudoMaximized = false;
            PlayUiClick();
        }

        /// <summary>Çoklu-monitör modunda aracın X konumuna karşılık gelen ekranın KENDİ taban
        /// çizgisini (FloorY) döndürür - farklı çözünürlük/dikey konumdaki ikincil ekranlarda
        /// araçların tek bir sahte global zeminde havada asılı kalmasını/zeminin altına batmasını
        /// önler (bkz. Models/AutoMotion.cs Vehicle.Update floorAt parametresi). Eşleşen ekran
        /// bulunamazsa (uç durum, ör. pencere henüz taşınmadıysa) eski tek-zemin davranışına düşer.</summary>
        private double FloorYForX(double x)
        {
            foreach (var m in monitorFloors)
                if (x >= m.Left && x < m.Right) return m.FloorY;
            return RenderCanvas.ActualHeight;
        }

        /// <summary>
        /// Otomatik Hareket: kapatıldığında araçlar kendi kendine gezinmeyi (wandering AI)
        /// bırakır, sadece fizik (yerçekimi/sekme/duvar tırmanma) ve manuel sürükleme aktif kalır.
        /// Açıldığında normal davranışa geri döner.
        /// </summary>
        private void ToggleAutoMove_Click(object sender, RoutedEventArgs e)
        {
            gameCore.AutoMoveEnabled = !gameCore.AutoMoveEnabled;
            AutoMoveBtn.Opacity = gameCore.AutoMoveEnabled ? 1.0 : 0.4;
            AutoMoveBtn.ToolTip = gameCore.AutoMoveEnabled
                ? "Otomatik Hareket Aç/Kapat (A) - Şu an: Açık"
                : "Otomatik Hareket Aç/Kapat (A) - Şu an: Kapalı";
            PlayUiClick();
        }

        /// <summary>Gece modunu Otomatik -> Zorla Gece -> Zorla Gündüz -> Otomatik döngüsünde değiştirir (test/gösteri için).</summary>
        private void NightModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (forcedNightMode == null) forcedNightMode = true;
            else if (forcedNightMode == true) forcedNightMode = false;
            else forcedNightMode = null;

            NightModeBtn.Content = forcedNightMode switch { true => "🌙", false => "☀️", _ => "🌗" };
            NightModeBtn.ToolTip = forcedNightMode switch
            {
                true => "Gece Modu: Zorla Gece",
                false => "Gece Modu: Zorla Gündüz",
                _ => "Gece Modu: Otomatik"
            };
            PlayUiClick();
        }

        private void MusicSyncBtn_Click(object sender, RoutedEventArgs e)
        {
            musicSyncEnabled = !musicSyncEnabled;
            MusicSyncBtn.Opacity = musicSyncEnabled ? 1.0 : 0.4;
            MusicSyncBtn.ToolTip = musicSyncEnabled ? "Müzik Süspansiyon Senkronu: Açık" : "Müzik Süspansiyon Senkronu: Kapalı";
            PlayUiClick();
        }

        private void EffectsDensityBtn_Click(object sender, RoutedEventArgs e)
        {
            SetLowEffectsMode(!lowEffectsMode);

            settings.Current.LowEffectsMode = lowEffectsMode;
            settings.Save();
            PlayUiClick();
        }

        /// <summary>Efekt yoğunluğu durumunu ve butonun görünümünü tek yerden ayarlar - açılışta
        /// kaydedilmiş tercih uygulanırken de aynı yol kullanılır (bkz. ApplyPersistedPreferences).</summary>
        private void SetLowEffectsMode(bool enabled)
        {
            lowEffectsMode = enabled;
            EffectsDensityBtn.Opacity = enabled ? 0.4 : 1.0;
            EffectsDensityBtn.ToolTip = enabled ? "Efekt Yoğunluğu: Düşük (performans)" : "Efekt Yoğunluğu: Normal";
        }

        private void DirtEffectBtn_Click(object sender, RoutedEventArgs e)
        {
            SetDirtEffectEnabled(!dirtEffectEnabled);
            PlayUiClick();
        }

        private void DirtEffectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SetDirtEffectEnabled(DirtEffectCheckBox.IsChecked == true);
            PlayUiClick();
        }

        /// <summary>Üst çubuktaki 🟤 tuşu ile Ayarlar panelindeki checkbox aynı durumu paylaşır -
        /// biri değiştiğinde diğeri de senkron kalır.</summary>
        private void SetDirtEffectEnabled(bool enabled)
        {
            dirtEffectEnabled = enabled;
            DirtEffectBtn.Opacity = enabled ? 1.0 : 0.4;
            DirtEffectBtn.ToolTip = enabled ? "Kirlenme Efekti: Açık" : "Kirlenme Efekti: Kapalı";
            if (DirtEffectCheckBox != null) DirtEffectCheckBox.IsChecked = enabled;
        }

        /// <summary>
        /// Kaydedilmiş tercihleri (çarpanlar, tema, ses, efekt yoğunluğu, otomatik kayıt aralığı)
        /// açılışta motorlara ve arayüz kontrollerine uygular. Eskiden bunların hiçbiri kalıcı
        /// değildi: uygulama her açıldığında tema 0'a, ses açığa, efektler normale dönüyordu.
        ///
        /// Kaydırıcıya değer atamak ValueChanged'i tetiklediğinden ilgili motor ve etiket metni
        /// kendiliğinden senkron olur - bu yüzden kaydırıcılara yazmak yeterlidir.
        /// </summary>
        private void ApplyPersistedPreferences()
        {
            var prefs = settings.Current;

            // ── Kirlilik/yakıt çarpanları. Bozuk/elle düzenlenmiş ayar dosyası motoru anlamsız bir
            //    çarpanla çalıştırmasın.
            double dirt = prefs.DirtMultiplier > 0 ? prefs.DirtMultiplier : 1.0;
            double fuel = prefs.FuelMultiplier > 0 ? prefs.FuelMultiplier : 1.0;

            gameCore.DirtMultiplier = dirt;
            gameCore.FuelMultiplier = fuel;
            if (DirtMultiplierSlider != null) DirtMultiplierSlider.Value = dirt;
            if (FuelMultiplierSlider != null) FuelMultiplierSlider.Value = fuel;

            // ── Tema. Tema sayısı sürümler arasında değişebileceğinden indeks sınır dışıysa
            //    varsayılana düşülür (aksi halde SetTheme tanımsız bir temaya giderdi).
            if (prefs.ThemeIndex >= 0 && prefs.ThemeIndex < themeEngine.ThemeCount)
            {
                currentTheme = prefs.ThemeIndex;
                themeEngine.SetTheme(currentTheme);
            }

            // ── Ses seviyeleri ve sustur durumu.
            if (MasterVolumeSlider != null) MasterVolumeSlider.Value = Math.Clamp(prefs.MasterVolume, 0, 1);
            if (SfxVolumeSlider != null) SfxVolumeSlider.Value = Math.Clamp(prefs.SfxVolume, 0, 1);

            if (prefs.Muted && soundEngine != null && !soundEngine.IsMuted)
            {
                soundEngine.ToggleMute();
                SyncSoundButton();
            }

            // ── Düşük efekt (performans) modu.
            if (prefs.LowEffectsMode) SetLowEffectsMode(true);

            // ── Otomatik kayıt aralığı. Liste seçimi olay işleyicisi ÇIKARILARAK yapılır: aksi halde
            //    açılışta SelectionChanged tetiklenip tıklama sesi çalar ve ayar dosyası boş yere
            //    yeniden yazılırdı (InitializeLanguageCombo ile aynı yaklaşım).
            saveManager.AutoSaveIntervalSeconds = prefs.AutoSaveIntervalSeconds > 0
                ? prefs.AutoSaveIntervalSeconds
                : 60;

            if (AutoSaveIntervalCombo != null)
            {
                AutoSaveIntervalCombo.SelectionChanged -= AutoSaveIntervalCombo_SelectionChanged;
                foreach (var obj in AutoSaveIntervalCombo.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag is string tag
                        && int.TryParse(tag, out int seconds) && seconds == saveManager.AutoSaveIntervalSeconds)
                    {
                        AutoSaveIntervalCombo.SelectedItem = item;
                        break;
                    }
                }
                AutoSaveIntervalCombo.SelectionChanged += AutoSaveIntervalCombo_SelectionChanged;
            }
        }

        // Çarpanlar artık ayrıca settings.json'a da yazılır: eskiden yalnızca kayıt dosyasının içinde
        // yaşadıkları için, kayıt yüklemeden oynayan bir kullanıcıda her açılışta 1.0x'e dönüyorlardı.
        // Yazma SaveDeferred ile ertelenir - kaydırıcı sürüklenirken saniyede onlarca dosya yazılmasın.
        private void DirtMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gameCore == null || DirtMultiplierTxt == null) return;
            gameCore.DirtMultiplier = e.NewValue;
            DirtMultiplierTxt.Text = $"{e.NewValue:0.0}x";

            settings.Current.DirtMultiplier = e.NewValue;
            settings.SaveDeferred();
        }

        private void FuelMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gameCore == null || FuelMultiplierTxt == null) return;
            gameCore.FuelMultiplier = e.NewValue;
            FuelMultiplierTxt.Text = $"{e.NewValue:0.0}x";

            settings.Current.FuelMultiplier = e.NewValue;
            settings.SaveDeferred();
        }

        private void ToggleAllUI_Click(object sender, RoutedEventArgs e)
        {
            allUIHidden = !allUIHidden;
            AnimateVisibility(DockWrapper, !allUIHidden);
            AnimateVisibility(PanelsContainer, !allUIHidden && panelsVisible);
            PlayUiClick();
        }

        private void PanelBody_Toggle(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FrameworkElement body)
            {
                bool show = body.Visibility != Visibility.Visible;
                body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                btn.Content = show ? "⏷" : "⏵";
                PlayUiClick();
            }
        }

        private DispatcherTimer toastTimer;

        /// <summary>Sağ altta kısa süreliğine beliren bildirim (başarım/yakıt/hasar uyarısı) gösterir.</summary>
        private void ShowToast(string text)
        {
            ToastText.Text = text;
            ToastPanel.Opacity = 1;

            toastTimer?.Stop();
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(400) };
                ToastPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            toastTimer.Start();
        }

        private readonly Dictionary<Vehicle, DateTime> lastReminderTime = new();
        private DateTime lastReminderScan = DateTime.MinValue;
        private DateTime lastIdleReminder = DateTime.MinValue;

        /// <summary>Düşük yakıt/hasar/kirlilik/mutluluk durumundaki araçlar ve uzun süredir
        /// etkileşimsiz kalan oyuncu için proaktif bildirim gösterir (VPet'in zamanlanmış görev/hatırlatma
        /// sisteminin karşılığı). Gerçek geçen zamana (DateTime) göre çalışır, böylece oyun döngüsü
        /// kare hızından (60/144 FPS fark etmeksizin) bağımsız olarak aynı sıklıkta tetiklenir.</summary>
        private void CheckReminders()
        {
            var now = DateTime.Now;
            if ((now - lastReminderScan).TotalSeconds < 5) return;
            lastReminderScan = now;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (lastReminderTime.TryGetValue(vehicle, out DateTime lastTime) && (now - lastTime).TotalSeconds < 30)
                    continue; // araç başına ~30 saniye bekleme süresi

                string name = string.IsNullOrEmpty(vehicle.Name) ? vehicle.Type.ToUpper() : vehicle.Name;
                string msg = null;
                if (vehicle.Fuel < vehicle.MaxFuel * 0.15)
                    msg = $"⛽ {name}: Yakıt azalıyor!";
                else if (vehicle.HP < vehicle.MaxHP * 0.25)
                    msg = $"🔧 {name}: Onarım gerekiyor!";
                else if (vehicle.Dirtiness > 0.85)
                    msg = $"🧽 {name}: Epey kirlendi, bir yıkasan iyi olur!";
                else if (vehicle.Happiness < 20)
                    msg = $"😢 {name}: Keyfi pek yerinde değil, biraz ilgilen!";

                if (msg != null)
                {
                    ShowToast(msg);
                    lastReminderTime[vehicle] = now;
                    break;
                }
            }

            // Oyuncu uzun süredir (2 dakika+) hiç etkileşime girmediyse tek seferlik genel hatırlatma
            if ((now - lastUserActivity).TotalMinutes >= 2 && (now - lastIdleReminder).TotalMinutes >= 2
                && gameCore.AutoMotions.Count > 0)
            {
                lastIdleReminder = now;
                ShowToast(Loc.T("msg.h_l_oradayim_biraz_ilgilenmez_misin"));
            }
        }

        /// <summary>Zeminde yan yana gelen iki araç arasında rastgele bir "selamlaşma" (korna + 👋
        /// balonu) tetikler. Gerçek zamana göre (~1 sn'de bir) taranır, kare hızından bağımsızdır.</summary>
        private void CheckGreetings()
        {
            var now = DateTime.Now;
            if ((now - lastGreetScan).TotalSeconds < 1) return;
            lastGreetScan = now;

            var candidates = gameCore.AutoMotions
                .Where(v => v.EdgeSide == 0 && !v.IsDragged && !v.IsDestroyed && !v.IsSideHidden)
                .ToList();

            for (int i = 0; i < candidates.Count; i++)
            {
                var a = candidates[i];
                if (lastGreetTime.TryGetValue(a, out DateTime aLast) && (now - aLast).TotalSeconds < 20)
                    continue;

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    var b = candidates[j];
                    if (lastGreetTime.TryGetValue(b, out DateTime bLast) && (now - bLast).TotalSeconds < 20)
                        continue;

                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double closeRange = (a.Width + b.Width) / 2.0 + 25;

                    if (dist < closeRange && Random.Shared.NextDouble() < 0.35)
                    {
                        lastGreetTime[a] = now;
                        lastGreetTime[b] = now;
                        soundEngine.PlayHorn(a.Type);
                        greetingPops.Add(new GreetingPop
                        {
                            X = (a.X + b.X) / 2,
                            Y = Math.Min(a.Y - a.Height / 2, b.Y - b.Height / 2) - 12
                        });
                        return; // bir karede en fazla bir selamlaşma - gürültü kirliliğini önler
                    }
                }
            }
        }

        // Yumuşak fade ile göster/gizle
        private void AnimateVisibility(UIElement element, bool show)
        {
            var anim = new DoubleAnimation
            {
                From = show ? 0 : 1,
                To = show ? 1 : 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            if (show)
            {
                element.Visibility = Visibility.Visible;
                element.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else
            {
                anim.Completed += (s, e2) => element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        /// <summary>Auto (Height belirtilmemiş) bir panelin gerçek yerleşim yüksekliğini döndürür.
        /// Border.Height doğrudan okunursa Auto panellerde NaN döner - bottom-anchor hesaplarını
        /// bozup panelin ekranın üstüne (VehicleInfoPanel'in üstüne) yanlış konumlanmasına sebep oluyordu.</summary>
        private static double MeasuredHeight(FrameworkElement el)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return el.DesiredSize.Height;
        }

        /// <summary>
        /// Kullanıcı isteği: arayüz çok dağınık/karışık duruyordu - eskiden ekranın altı ayrı
        /// köşesine yerleşen İstatistikler/Seçili Araç/Vites/Eşyalar/Hızlı İşlemler/Eklentiler
        /// panelleri artık TEK bir "☰ Paneller" sekmeli penceresinde birleştiriliyor. Panellerin
        /// KOPYASI değil, AYNI Border nesneleri (tüm x:Name referansları, event handler'ları ve
        /// veri bağlamalarıyla birlikte) PanelsContainer'dan çıkarılıp ilgili TabItem'ın içeriği
        /// olarak yeniden ebeveynleniyor - bu yüzden geri kalan tüm kod (StatsPanelBody, GearVehicleNameTxt,
        /// AchievementsListControl vb.) hiç değişmeden çalışmaya devam ediyor.
        /// </summary>
        private void MoveConsolidatedPanelsIntoTabs()
        {
            void Move(Border panel, TabItem tab)
            {
                PanelsContainer.Children.Remove(panel);
                panelOriginalWidths[panel] = panel.Width; // Ayır butonuyla tekrar bağımsız pencereye çıkarken kullanılacak
                panelHomeTabs[panel] = tab;
                panel.Width = double.NaN; // artık sekme genişliğine göre otomatik boyutlanır
                // AnimatedPanel'in "Loaded" tetikleyicili opacity-fade'i sekme içeriğiyle uyumsuz
                // (bkz. TabbedPanel tanımındaki not) - sekmeler değiştikçe panel Unload/Load olup
                // tetikleyici tekrar tekrar ateşleniyor, bu da panelin kalıcı olarak görünmez
                // (Opacity≈0) takılı kalmasına sebep olabiliyordu.
                panel.Style = (Style)FindResource("TabbedPanel");
                panel.Opacity = 1;
                tab.Content = panel;
            }

            Move(StatsPanel, TabStats);
            Move(VehicleInfoPanel, TabVehicle);
            Move(GearPanel, TabGear);
            Move(ItemShopPanel, TabShop);
            Move(QuickActionsPanel, TabQuick);
            Move(ExpansionsPanel, TabExpansions);

            // Sekme içine taşınan panellerin görünürlüğünü artık hangi sekmenin seçili olduğu
            // belirliyor - eskiden panel bazında ayrı ayrı gizli/görünür olma anlamı kalmadı.
            StatsPanel.Visibility = Visibility.Visible;
            VehicleInfoPanel.Visibility = Visibility.Visible;
            ItemShopPanel.Visibility = Visibility.Visible;
            QuickActionsPanel.Visibility = Visibility.Visible;
            ExpansionsPanel.Visibility = Visibility.Visible;
            ExpansionsPanel.Opacity = 1;
            // GearPanel bilerek dokunulmadı: sürüş modu dışında Collapsed kalır (StartDriving/
            // StopDriving zaten AnimateVisibility ile bunu yönetiyor), böylece Vites sekmesi
            // sürmüyorken boş görünür.

            // Panel gövdelerinin bir kısmı eskiden "varsayılan katlı" başlıyordu (masaüstü daha az
            // kalabalık görünsün diye) - artık zaten tek bir sekmeli pencerede saklı olduklarından
            // o ekstra katlama gereksiz bir tıklama daha ekliyordu, kaldırıldı.
            ItemShopPanelBody.Visibility = Visibility.Visible;
            ItemShopCollapseBtn.Content = "⏷";
            QuickActionsScrollViewer.Visibility = Visibility.Visible;
            QuickActionsCollapseBtn.Content = "⏷";

            // ── Panel kutusunun içeriğiyle aynı boyda olmasını GARANTİLE ──
            //
            // ÖLÇÜLEN HATA: MasterPanel'in düzen kutusu ekranda y=698'de bitiyor ama içindeki sekme
            // kontrolü y=852'ye kadar ÇİZİLMEYE devam ediyordu. WPF, düzen kutusunun dışına taşan
            // içeriği çizer ama HIT-TEST'e almaz - panelin görünen alt/sağ bölgesi (kenarları ve
            // boyutlandırma tutamağı dahil) hiçbir fare olayı almıyordu.
            //
            // SEBEP: TabControl seçili sekmenin içeriğini GEÇ gerçekliyor. Panel ilk ölçüldüğünde
            // sekme içeriği (buraya yeni taşınmış StatsPanel) henüz gerçeklenmemiş olduğundan küçük
            // bir istenen boyut bildiriliyor; içerik gerçeklendiğinde WPF onu mevcut yuvaya
            // yerleştiriyor ama ATALARIN ölçüsünü yeniden istemiyor. Bu yüzden buradaki anlık
            // InvalidateMeasure yetmiyor - içerik daha gerçeklenmemiş oluyor.
            //
            // ÇÖZÜM: ölçümü ilk düzen turundan SONRAYA ertele; ayrıca sekme her değiştiğinde
            // (her sekmenin içeriği farklı yükseklikte) tekrarla.
            RemeasurePanelsAfterLayout();
            MasterTabControl.SelectionChanged += (s, e) => RemeasurePanelsAfterLayout();
        }

        /// <summary>Panellerin ölçüsünü, düzen turu tamamlandıktan SONRA yeniden istetir - sekme
        /// içeriği gerçeklendikten sonra panel kutusu içeriğin gerçek boyuna oturur.</summary>
        private void RemeasurePanelsAfterLayout()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var panel in AllResizablePanels())
                {
                    if (panel == null) continue;
                    panel.InvalidateMeasure();
                    renderedRectCache.Remove(panel);
                }
                MasterPanel.UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PositionDefaultPanels()
        {
            double w = RenderCanvas.ActualWidth;
            double h = RenderCanvas.ActualHeight;

            Canvas.SetLeft(MasterPanel, 10);
            Canvas.SetTop(MasterPanel, 70);
        }

        /// <summary>Sürükle-taşı/sabitleme durumu izlenen tüm panellerin (Border, Sabitle butonu) eşleşmesi.</summary>
        private IEnumerable<(Border panel, Button pinBtn)> AllLayoutTrackedPanels()
        {
            yield return (MasterPanel, MasterPinBtn);

            // Ayrılmış (↗️) paneller de artık bağımsız/sürüklenebilir Canvas öğeleri - konumları ve
            // sabitleme durumları da MasterPanel gibi kaydedilip geri yüklenmeli.
            if (detachedPanels.Contains(StatsPanel)) yield return (StatsPanel, StatsPinBtn);
            if (detachedPanels.Contains(VehicleInfoPanel)) yield return (VehicleInfoPanel, VehicleInfoPinBtn);
            if (detachedPanels.Contains(GearPanel)) yield return (GearPanel, GearPinBtn);
            if (detachedPanels.Contains(ItemShopPanel)) yield return (ItemShopPanel, ItemShopPinBtn);
            if (detachedPanels.Contains(QuickActionsPanel)) yield return (QuickActionsPanel, QuickActionsPinBtn);
            if (detachedPanels.Contains(ExpansionsPanel)) yield return (ExpansionsPanel, ExpansionsPinBtn);
        }

        /// <summary>İlk çalıştırmada (kısayolları/❓ butonunu henüz keşfetmemiş kullanıcı için) tek
        /// seferlik bir karşılama bildirimi gösterir - genel kısayollar önceden hiçbir yerde kalıcı
        /// gösterilmiyordu, kullanıcı tesadüfen bulana kadar bilmiyordu.</summary>
        private void ShowFirstRunOnboardingIfNeeded()
        {
            try
            {
                if (settings.Current.OnboardingShown) return;

                settings.Current.OnboardingShown = true;
                settings.Save();

                ShowToast(Loc.T("msg.hos_geldin_kisayollar_icin_sag_ustteki"));
            }
            catch { /* onboarding kritik değil, sessizce atlanır */ }
        }

        /// <summary>
        /// Paneller sürüklenip taşınsa/sabitlense bile uygulama kapatılıp açılınca hepsi varsayılan
        /// köşelerine dönüyordu - artık konum ve sabitleme durumu diske kaydedilip geri yükleniyor.
        /// </summary>
        private void SavePanelLayout()
        {
            try
            {
                var layout = new Dictionary<string, PanelLayoutEntry>();
                foreach (var (panel, pinBtn) in AllLayoutTrackedPanels())
                {
                    layout[panel.Name] = new PanelLayoutEntry
                    {
                        Scale = GetPanelScale(panel),
                        Left = Canvas.GetLeft(panel),
                        Top = Canvas.GetTop(panel),
                        Pinned = pinnedPanels.Contains(panel)
                    };
                }

                settings.Current.PanelLayout = layout;
                settings.Save();
            }
            catch { }
        }

        private void LoadPanelLayout()
        {
            try
            {
                var layout = settings.Current.PanelLayout;
                if (layout == null || layout.Count == 0) return;

                foreach (var (panel, pinBtn) in AllLayoutTrackedPanels())
                {
                    if (!layout.TryGetValue(panel.Name, out var entry)) continue;

                    // Büyüklük çarpanı: 0 = eski düzen dosyası (alan henüz yoktu) -> varsayılan kalır.
                    if (entry.Scale > 0) SetPanelScale(panel, entry.Scale);

                    if (double.IsNaN(entry.Left) || double.IsNaN(entry.Top)) continue;

                    Canvas.SetLeft(panel, entry.Left);
                    Canvas.SetTop(panel, entry.Top);

                    if (entry.Pinned)
                    {
                        pinnedPanels.Add(panel);
                        pinBtn.Content = "📍";
                        pinBtn.ToolTip = Loc.T("ui.unpin");
                    }
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // PANEL BÜYÜKLÜĞÜ
        //
        // Paneller sabit genişlikte tasarlandığından (ör. 320px) küçük ekranlarda yer kaplıyor,
        // büyük/yüksek çözünürlüklü ekranlarda ise küçük kalıyordu. Her panel başlığındaki ➖ / ➕
        // butonları paneli kademeli olarak küçültüp büyütür.
        //
        // Neden Width değil de LayoutTransform: genişliği değiştirmek yalnızca kutuyu esnetir, metin
        // aynı boyutta kalır ve okunabilirlik değişmez. LayoutTransform ölçeği İÇERİĞİ DE ölçekler -
        // yazılar, butonlar ve kaydırıcılar birlikte büyür. Ayrıca yalnızca tek bir özellik
        // değiştiği için her panel için ayrı düzen çalışması gerekmez.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private const double MinPanelScale = 0.7;
        private const double MaxPanelScale = 1.6;
        private const double PanelScaleStep = 0.1;

        private readonly Dictionary<Border, double> panelScales = new();

        private double GetPanelScale(Border panel)
            => panelScales.TryGetValue(panel, out var s) && s > 0 ? s : 1.0;

        private void SetPanelScale(Border panel, double scale)
        {
            if (panel == null) return;

            scale = Math.Clamp(scale, MinPanelScale, MaxPanelScale);
            panelScales[panel] = scale;

            // LayoutTransform (RenderTransform DEĞİL): düzen sistemi ölçeklenmiş boyutu hesaba katar,
            // böylece panel gerçekten o kadar yer kaplar ve komşu öğelerle çakışmaz.
            panel.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
                ? System.Windows.Media.Transform.Identity
                : new System.Windows.Media.ScaleTransform(scale, scale);
        }

        private void PanelScaleDown_Click(object sender, RoutedEventArgs e) => StepPanelScale(sender, -PanelScaleStep);
        private void PanelScaleUp_Click(object sender, RoutedEventArgs e) => StepPanelScale(sender, +PanelScaleStep);

        private void StepPanelScale(object sender, double delta)
        {
            if (sender is not FrameworkElement btn || btn.Tag is not Border panel) return;

            SetPanelScale(panel, GetPanelScale(panel) + delta);
            KeepPanelOnScreen(panel);
            PlayUiClick();
        }

        // ── Kenardan tutup boyutlandırma (resize grip) ──────────────────────────────────────────
        //
        // Başlıktaki ➖/➕ butonları kademeli ayar için; bu ise serbest, WYSIWYG ayar sağlar:
        // panelin sağ-alt köşesindeki tutamak sürüklendiğinde panel imleci TAKİP EDER (hedef
        // genişlik = imleç ile panelin sol kenarı arası). İki yöntem aynı `panelScales` durumunu
        // paylaşır, dolayısıyla kaydedilen düzen de ortaktır.

        private Border resizingPanel;
        private double resizeStartLeft;

        /// <summary>Her panelin içeriğini bir Grid'e sarıp sağ-alt köşesine boyutlandırma tutamağı
        /// ekler. Kod tarafında yapılıyor çünkü 7 panelin XAML'ini tek tek değiştirmek hem uzun hem
        /// de ileride panel eklenince unutulmaya açık olurdu.</summary>
        private void AttachResizeGrips()
        {
            foreach (var panel in AllResizablePanels())
            {
                if (panel == null || panel.Child is Grid g && g.Name == "PanelResizeHost") continue;

                var content = panel.Child;
                panel.Child = null;

                var host = new Grid { Name = "PanelResizeHost" };
                if (content != null) host.Children.Add(content);
                host.Children.Add(CreateResizeGrip(panel));

                panel.Child = host;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // KENARDAN BOYUTLANDIRMA
        //
        // Önceden yalnızca sağ-alt köşedeki 16x16 tutamaktan boyutlandırma vardı; kullanıcı isteği
        // üzerine artık gerçek bir pencere gibi HER KENARDAN ve HER KÖŞEDEN tutulabiliyor - ve bu
        // tüm panellere uygulanıyor (bkz. AllResizablePanels).
        //
        // Ölçek modeli değişmedi: paneller LayoutTransform ile ORANTILI ölçekleniyor (bkz.
        // SetPanelScale), yani içerik de birlikte büyüyor. Sürüklenen kenarın KARŞISINDAKİ kenar
        // sabit kalır: sağ kenardan çekince sol kenar, sol kenardan çekince sağ kenar yerinde durur.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        /// <summary>Kenara "yakın sayılan" bant kalınlığı - panelin kendi (ölçeklenmemiş) biriminde.</summary>
        private const double EdgeGrabThickness = 9;

        private ResizeEdge activeResizeEdge = ResizeEdge.None;
        private Border edgeResizePanel;
        private double edgeAnchorLeft, edgeAnchorTop, edgeAnchorRight, edgeAnchorBottom;

        /// <summary>
        /// Verilen pencere-koordinatlı nokta, bir panelin hangi kenarına yakın?
        ///
        /// Kenar yakalama PANEL üzerinde değil, PENCERE seviyesinde dinleniyor. Sebep ölçümle
        /// bulundu: panelin görünen alanının bir kısmı düzen kutusunun dışında kalabiliyor ve WPF
        /// orada panele hiç fare olayı göndermiyor (bkz. RenderedRectOf). Panel üzerinde
        /// dinlenseydi, tam da kullanıcının tutmak isteyeceği alt kenar ölü kalırdı. İmleç de
        /// pencere üzerinde ayarlanır - panel hit-test'e kapalı olsa bile görünür.
        /// </summary>
        private ResizeEdge DetectEdgeAt(Point windowPoint, out Border foundPanel)
        {
            foundPanel = null;

            foreach (var panel in AllResizablePanels())
            {
                if (panel == null || panel.Visibility != Visibility.Visible) continue;
                if (pinnedPanels.Contains(panel)) continue; // sabitlenmiş panel boyutlandırılmaz

                var r = RenderedRectOf(panel);
                if (r.IsEmpty) continue;

                // Kenar bandının dışına da bir miktar taşan bir tolerans kutusu
                var outer = new Rect(r.X - EdgeGrabThickness, r.Y - EdgeGrabThickness,
                                     r.Width + EdgeGrabThickness * 2, r.Height + EdgeGrabThickness * 2);
                if (!outer.Contains(windowPoint)) continue;

                bool left = Math.Abs(windowPoint.X - r.Left) <= EdgeGrabThickness;
                bool right = Math.Abs(windowPoint.X - r.Right) <= EdgeGrabThickness;
                bool top = Math.Abs(windowPoint.Y - r.Top) <= EdgeGrabThickness;
                bool bottom = Math.Abs(windowPoint.Y - r.Bottom) <= EdgeGrabThickness;

                ResizeEdge edge =
                    top && left ? ResizeEdge.TopLeft :
                    top && right ? ResizeEdge.TopRight :
                    bottom && left ? ResizeEdge.BottomLeft :
                    bottom && right ? ResizeEdge.BottomRight :
                    left ? ResizeEdge.Left :
                    right ? ResizeEdge.Right :
                    top ? ResizeEdge.Top :
                    bottom ? ResizeEdge.Bottom : ResizeEdge.None;

                if (edge != ResizeEdge.None)
                {
                    foundPanel = panel;
                    return edge;
                }
            }

            return ResizeEdge.None;
        }

        private static Cursor CursorForEdge(ResizeEdge edge) => edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
            ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
            _ => null
        };

        /// <summary>Pencereye gelen HER fare hareketi - kenar yakınlığına göre imleç verir, sürükleme
        /// sürüyorsa boyutu günceller. Window.PreviewMouseMove'a bağlanır.</summary>
        private void Window_EdgeResizeMouseMove(object sender, MouseEventArgs e)
        {
            var pt = e.GetPosition(this);

            if (activeResizeEdge != ResizeEdge.None)
            {
                UpdateEdgeResize(pt);
                e.Handled = true;
                return;
            }

            Cursor = CursorForEdge(DetectEdgeAt(pt, out _)); // panel hit-test'e kapalı olsa da görünür
        }

        private void Window_EdgeResizeMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(this);
            var edge = DetectEdgeAt(pt, out var panel);
            if (edge == ResizeEdge.None || panel == null) return;

            // Panelin şu anki dört kenarının PENCERE koordinatı - sürükleme boyunca karşı kenarı
            // sabit tutmak için başlangıçta bir kez ölçülür.
            var r = RenderedRectOf(panel);
            edgeAnchorLeft = r.Left;
            edgeAnchorTop = r.Top;
            edgeAnchorRight = r.Right;
            edgeAnchorBottom = r.Bottom;

            activeResizeEdge = edge;
            edgeResizePanel = panel;
            Cursor = CursorForEdge(edge);
            CaptureMouse();
            e.Handled = true;
        }

        private void Window_EdgeResizeMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (activeResizeEdge == ResizeEdge.None) return;
            EndEdgeResize();
            e.Handled = true;
        }

        /// <summary>Sürükleme boyunca yeni ölçeği hesaplar. Pencere seviyesinde dinlenir
        /// (bkz. Window_PreviewMouseMove) - imleç panelin dışına çıksa bile bırakılana kadar sürer.</summary>
        private void UpdateEdgeResize(Point cursorInWindow)
        {
            var panel = edgeResizePanel;
            if (panel == null || activeResizeEdge == ResizeEdge.None) return;
            if (panel.ActualWidth <= 0 || panel.ActualHeight <= 0) return;

            double scale = GetPanelScale(panel);
            var edge = activeResizeEdge;

            // Yatay: sürüklenen kenarın karşısındaki kenardan uzaklık = yeni GENİŞLİK (ölçekli).
            double? widthScale = edge switch
            {
                ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight => (cursorInWindow.X - edgeAnchorLeft) / panel.ActualWidth,
                ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft => (edgeAnchorRight - cursorInWindow.X) / panel.ActualWidth,
                _ => null
            };

            double? heightScale = edge switch
            {
                ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight => (cursorInWindow.Y - edgeAnchorTop) / panel.ActualHeight,
                ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight => (edgeAnchorBottom - cursorInWindow.Y) / panel.ActualHeight,
                _ => null
            };

            // Köşelerde iki eksen birden sürükleniyor: ölçek orantılı olduğu için kullanıcının daha
            // çok çektiği eksen kazanır (serbest en/boy oranı mümkün değil - içerik de ölçekleniyor).
            if (widthScale.HasValue && heightScale.HasValue) scale = Math.Max(widthScale.Value, heightScale.Value);
            else if (widthScale.HasValue) scale = widthScale.Value;
            else if (heightScale.HasValue) scale = heightScale.Value;

            SetPanelScale(panel, scale);
            double applied = GetPanelScale(panel); // SetPanelScale alt/üst sınıra kırpar
            renderedRectCache.Remove(panel);       // ölçek değişti, çizim alanı yeniden hesaplanmalı

            // Sol/üst kenardan çekilirken karşı kenarın yerinde kalması için panel kaydırılır.
            // (Sekme içindeki paneller Canvas çocuğu değildir - orada konum anlamsız, atlanır.)
            if (!double.IsNaN(Canvas.GetLeft(panel)))
            {
                if (edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
                    Canvas.SetLeft(panel, edgeAnchorRight - panel.ActualWidth * applied);
                if (edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
                    Canvas.SetTop(panel, edgeAnchorBottom - panel.ActualHeight * applied);
            }
        }

        private void EndEdgeResize()
        {
            if (activeResizeEdge == ResizeEdge.None) return;

            var panel = edgeResizePanel;
            activeResizeEdge = ResizeEdge.None;
            edgeResizePanel = null;

            ReleaseMouseCapture();
            Cursor = null;

            if (panel != null)
            {
                renderedRectCache.Remove(panel); // boyut değişti, önbellek geçersiz
                KeepPanelOnScreen(panel);
                SavePanelLayout(); // yeni büyüklük kalıcı olsun (bkz. SettingsService)
                PlayUiClick();
            }
        }

        private FrameworkElement CreateResizeGrip(Border panel)
        {
            var grip = new Border
            {
                Width = 16,
                Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(230, 0xFB, 0xCA, 0x1F)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                // NEGATİF kenar boşluğu KULLANILMAZ: Border içeriğini kırptığından, dışarı taşan
                // tutamak hem görünmez hem de tıklanamaz oluyordu. Panelin İÇİNDE duruyor.
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.SizeNWSE,
                Tag = panel,
                ToolTip = Loc.T("ui.kenardan_tutup_boyutlandir")
            };

            grip.MouseLeftButtonDown += ResizeGrip_MouseLeftButtonDown;
            grip.MouseMove += ResizeGrip_MouseMove;
            grip.MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
            return grip;
        }

        private IEnumerable<Border> AllResizablePanels()
        {
            yield return MasterPanel;
            yield return StatsPanel;
            yield return VehicleInfoPanel;
            yield return GearPanel;
            yield return ItemShopPanel;
            yield return QuickActionsPanel;
            yield return ExpansionsPanel;

            // Modal pencereler de kenardan boyutlandırılabilir: kullanıcı açısından bunlar da birer
            // "pencere" (Kayıtlar, Araç Ekle, Kısayollar, uyarı kutusu) ve sabit boyutları küçük
            // ekranlarda dar, büyük ekranlarda küçük kalıyordu. Yalnızca AÇIKKEN listeye girerler -
            // kapalı bir modalın kenarı ekranın ortasında görünmez bir tutma alanı yaratmasın.
            if (SaveListModal.Visibility == Visibility.Visible) yield return SaveListModalBox;
            if (SpawnModal.Visibility == Visibility.Visible) yield return SpawnModalBox;
            if (HelpModal.Visibility == Visibility.Visible) yield return HelpModalBox;
            if (CustomDialogOverlay.Visibility == Visibility.Visible) yield return CustomDialogBox;
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement grip || grip.Tag is not Border panel) return;

            resizingPanel = panel;
            // Panelin sol kenarının PENCERE koordinatındaki konumu - ölçek değişse de hedef
            // genişliği buradan ölçeceğiz.
            resizeStartLeft = PointFromScreen(panel.PointToScreen(new Point(0, 0))).X;

            grip.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (resizingPanel == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not FrameworkElement grip) return;

            double unscaledWidth = resizingPanel.ActualWidth;   // LayoutTransform ölçeği İÇERMEZ
            if (unscaledWidth <= 0) return;

            double cursorX = e.GetPosition(this).X;
            double desiredWidth = cursorX - resizeStartLeft;

            SetPanelScale(resizingPanel, desiredWidth / unscaledWidth);
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement grip) grip.ReleaseMouseCapture();
            if (resizingPanel != null)
            {
                KeepPanelOnScreen(resizingPanel);
                resizingPanel = null;
                PlayUiClick();
            }
            e.Handled = true;
        }

        /// <summary>Büyütülen bir panel ekranın dışına taşabilir - sol/üst konumunu görünür alanda
        /// tutar. (Ölçeklenmiş gerçek boyut için render boyutları kullanılır.)</summary>
        private void KeepPanelOnScreen(Border panel)
        {
            try
            {
                panel.UpdateLayout();
                double w = panel.ActualWidth * GetPanelScale(panel);
                double h = panel.ActualHeight * GetPanelScale(panel);
                if (w <= 0 || h <= 0) return;

                double left = Canvas.GetLeft(panel);
                double top = Canvas.GetTop(panel);
                if (double.IsNaN(left) || double.IsNaN(top)) return;

                Canvas.SetLeft(panel, Math.Max(0, Math.Min(left, ActualWidth - w)));
                Canvas.SetTop(panel, Math.Max(0, Math.Min(top, ActualHeight - h)));
            }
            catch { /* düzen henüz hazır olmayabilir */ }
        }

        // Panel sürükleme (⠿ tutamacından)
        private void PanelHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement header || header.Tag is not Border panel) return;
            if (pinnedPanels.Contains(panel)) return;

            draggedPanel = panel;
            var pos = e.GetPosition(PanelsContainer);
            panelDragOffset = new Point(pos.X - Canvas.GetLeft(panel), pos.Y - Canvas.GetTop(panel));
            header.CaptureMouse();
            e.Handled = true;
        }

        private void PanelHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedPanel == null || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(PanelsContainer);
            double newLeft = pos.X - panelDragOffset.X;
            double newTop = pos.Y - panelDragOffset.Y;

            if (PanelsContainer.ActualWidth > 0 && PanelsContainer.ActualHeight > 0)
            {
                newLeft = Math.Max(0, Math.Min(newLeft, PanelsContainer.ActualWidth - draggedPanel.ActualWidth));
                newTop = Math.Max(0, Math.Min(newTop, PanelsContainer.ActualHeight - draggedPanel.ActualHeight));
            }

            Canvas.SetLeft(draggedPanel, newLeft);
            Canvas.SetTop(draggedPanel, newTop);
        }

        private void PanelHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement header)
                header.ReleaseMouseCapture();
            draggedPanel = null;
        }

        // Panel sabitleme (📌 -> 📍)
        private void PanelPin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Border panel) return;

            if (pinnedPanels.Contains(panel))
            {
                pinnedPanels.Remove(panel);
                btn.Content = "📌";
                btn.ToolTip = "Sabitle";
            }
            else
            {
                pinnedPanels.Add(panel);
                btn.Content = "📍";
                btn.ToolTip = Loc.T("ui.unpin");
            }
            PlayUiClick();
        }

        /// <summary>Panelden Ayır (↗️): panel şu an "☰ Paneller" sekmeli penceresindeyse onu oradan
        /// çıkarıp masaüstünde bağımsız, sürüklenebilir, sabitlenebilir (📌) kendi mini penceresine
        /// dönüştürür. Panel zaten ayrılmışsa aynı buton onu geri sekmeye ("Paneller" penceresine)
        /// gönderir. Sabitle butonu ancak panel bu şekilde ayrıldığında bir anlam ifade eder - sekme
        /// içindeyken sürüklenecek bağımsız bir Canvas konumu yoktur.</summary>
        private void PanelDetach_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Border panel) return;
            if (!panelHomeTabs.TryGetValue(panel, out var homeTab)) return;

            if (detachedPanels.Contains(panel))
            {
                // Geri sekmeye gönder
                PanelsContainer.Children.Remove(panel);
                detachedPanels.Remove(panel);
                pinnedPanels.Remove(panel);
                panel.Width = double.NaN;
                homeTab.Content = panel;
                btn.Content = "↗️";
                btn.ToolTip = Loc.T("ui.panelden_ayir_bagimsiz_pencereye_cikar");
            }
            else
            {
                // Sekmeden ayır, bağımsız pencereye çıkar
                homeTab.Content = null;
                panel.Width = panelOriginalWidths.TryGetValue(panel, out var w) && !double.IsNaN(w) ? w : panel.ActualWidth;

                // Ekranın ortasına yakın, üst üste binmesin diye kademeli (cascade) bir konumda başlat.
                double left = 380 + (detachCascadeIndex % 4) * 40;
                double top = 90 + (detachCascadeIndex % 4) * 40;
                detachCascadeIndex++;

                Canvas.SetLeft(panel, left);
                Canvas.SetTop(panel, top);
                PanelsContainer.Children.Add(panel);
                detachedPanels.Add(panel);

                // TabItem içeriği olarak yaşarken seçili olmayan sekmelerin görsel ağaca hiç
                // bağlanmaması nedeniyle bazı framework'lerde miras kalan IsEnabled/hit-test
                // durumu bozulabiliyordu - panel bağımsız pencereye çıkarken bunlar açıkça
                // sıfırlanıyor, aksi halde ayrılan panel görünür ama tıklanamaz kalıyordu.
                panel.IsEnabled = true;
                panel.IsHitTestVisible = true;
                Panel.SetZIndex(panel, 1000 + detachCascadeIndex); // MasterPanel/Dock'un her zaman üstünde kalsın

                btn.Content = "↙️";
                btn.ToolTip = Loc.T("ui.reattach_to_tab");

                // Sürüş modu dışında Gear paneli sekmedeyken zaten Collapsed kalır (bkz.
                // MoveConsolidatedPanelsIntoTabs) - ayrıldığında kullanıcı görebilsin diye görünür yapılır.
                if (panel.Visibility != Visibility.Visible) panel.Visibility = Visibility.Visible;
            }

            PlayUiClick();
        }

        private void DockCollapseBtn_Click(object sender, RoutedEventArgs e)
        {
            dockExpanded = !dockExpanded;
            DockCollapseBtn.Content = dockExpanded ? "⏶" : "⏷";
            AnimateVisibility(DockIcons, dockExpanded);
            PlayUiClick();
        }

        // Button handlers
        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            currentTheme = (currentTheme + 1) % themeEngine.ThemeCount;
            themeEngine.SetTheme(currentTheme);

            settings.Current.ThemeIndex = currentTheme;
            settings.SaveDeferred(); // temalar arasında hızlıca gezinirken her tıklamada yazma olmasın
            PlayUiClick();
        }

        private void SoundBtn_Click(object sender, RoutedEventArgs e)
        {
            soundEngine.ToggleMute();
            SyncSoundButton();

            settings.Current.Muted = soundEngine.IsMuted;
            settings.Save();
            PlayUiClick();
        }

        /// <summary>Ses butonunu motorun sustur durumuna göre tazeler.
        /// Buton 36x36 sabit boyutlu tek-ikon kutusu - çok kelimeli metin ("Ses Kapalı") içine
        /// sığmayıp taşıyor ve görünmüyordu. Durum bu yüzden ikon + ToolTip ile gösteriliyor.</summary>
        private void SyncSoundButton()
        {
            SoundBtn.Content = soundEngine.IsMuted ? "🔇" : "🔊";
            SoundBtn.ToolTip = soundEngine.IsMuted ? "Ses Aç/Kapa (M) - Şu an: Sessiz" : "Ses Aç/Kapa (M) - Şu an: Açık";
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var saveName = $"AutoMotion_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

            // Kaydetme başarısız olabilir (disk dolu, klasör salt-okunur, dosya kilitli...). Eskiden
            // dönüş değeri hiç kontrol edilmiyor ve her durumda "✅ Oyun kaydedildi" deniyordu -
            // kullanıcı kaydettiğini sanıp ilerlemesini kaybediyordu.
            if (saveManager.Save(saveName, gameCore.Save()))
            {
                soundEngine.PlaySuccess();
                await ShowDialogAsync(Loc.T("msg.game_saved", saveName), Loc.T("dlg.success"), false);
            }
            else
            {
                await ShowDialogAsync(
                    $"❌ Oyun KAYDEDİLEMEDİ.\n\n{saveManager.LastError}",
                    "Kaydetme Hatası", false);
            }
        }

        private void RefreshSaveList()
        {
            var saves = saveManager.GetAllSaves();
            saves.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            SaveListControl.ItemsSource = new ObservableCollection<SaveInfo>(saves);
        }

        private void LoadBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshSaveList();
            SaveListModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        private void CloseSaveListModal_Click(object sender, RoutedEventArgs e)
        {
            SaveListModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private async void LoadSaveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string saveName)
            {
                var data = saveManager.Load(saveName);
                if (data != null)
                {
                    gameCore.Load(data);
                    // Ayarlar panelindeki kaydırıcıları da yüklenen değerlerle senkronize et - aksi
                    // halde slider görsel olarak varsayılan 1.0x'te kalır ama motor farklı bir
                    // çarpanla çalışıyormuş gibi görünürdü (bkz. GameSaveData.DirtMultiplier).
                    if (DirtMultiplierSlider != null) DirtMultiplierSlider.Value = gameCore.DirtMultiplier;
                    if (FuelMultiplierSlider != null) FuelMultiplierSlider.Value = gameCore.FuelMultiplier;
                    AssignGarageSlots(); // kilitli araçları kaydedilmiş garaj ızgarasına yeniden yerleştir
                    soundEngine.PlaySuccess();
                    SaveListModal.Visibility = Visibility.Collapsed;
                    await ShowDialogAsync(Loc.T("msg.game_loaded", saveName), Loc.T("dlg.success"), false);

                    // "Özlem" bildirimi: kayıt uzun süre önce alınmışsa araçların yakıt/mutluluk
                    // kaybettiğini kullanıcıya hatırlat (VPet'in gerçek-zaman ihtiyaç döngüsüne benzer).
                    if (gameCore.LastAbsenceHours > 1.0)
                    {
                        string awayText = gameCore.LastAbsenceHours >= 24
                            ? "1 günden fazla"
                            : $"~{(int)gameCore.LastAbsenceHours} saat";
                        ShowToast(Loc.T("msg.absence_notice", awayText));
                    }
                }
                else
                {
                    // Eskiden bozuk/eksik bir kayıt seçildiğinde HİÇBİR ŞEY olmuyordu - modal açık
                    // kalıyor, kullanıcı butona tekrar tekrar basıp uygulamayı donmuş sanıyordu.
                    await ShowDialogAsync(
                        $"❌ Kayıt yüklenemedi.\n\n{saveManager.LastError}",
                        "Yükleme Hatası", false);
                }
            }
        }

        private void DeleteSaveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string saveName)
            {
                saveManager.DeleteSave(saveName);
                PlayUiClick();
                RefreshSaveList();
            }
        }

        private void AddVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            SpawnModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        /// <summary>Seçili aracı (yoksa filodaki ilk aracı) bulunduğu yerden mevcut fare imleci
        /// konumuna çağırır - bir köşede sıkışmış/kaybolmuş bir aracı hızlıca geri getirmek için.
        /// Mevcut Shift+tık rota atama fiziğiyle aynı CustomPath mekanizmasını kullanır, böylece
        /// araç ışınlanmaz, yumuşakça sürerek gelir.</summary>
        private void CallVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            var target = selectedVehicle ?? gameCore.AutoMotions.FirstOrDefault();
            if (target == null)
            {
                ShowToast(Loc.T("msg.cagrilacak_arac_yok"));
                return;
            }
            if (target.IsDriven || target.IsLocked)
            {
                ShowToast(Loc.T("msg.bu_arac_suruluyor_kilitli_cagrilamaz"));
                return;
            }

            target.CustomPath.Clear();
            target.CustomPath.Add(lastMousePos);
            selectedVehicle = target;
            VehicleListBox.SelectedItem = target;

            ShowToast(Loc.T("msg.vehicle_summoned", VehicleDisplayName(target)));
            soundEngine.PlayDrift();
        }

        private void CloseSpawnModal_Click(object sender, RoutedEventArgs e)
        {
            SpawnModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        private void CloseHelpModal_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private void SpawnVehicleSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                string type = tag;
                string brand = "";
                int sep = tag.IndexOf('|');
                if (sep >= 0)
                {
                    type = tag.Substring(0, sep);
                    brand = tag.Substring(sep + 1);
                }

                var vehicle = new Vehicle(RenderCanvas.ActualWidth / 2, RenderCanvas.ActualHeight / 2, type, brand);
                gameCore.AddAutoMotion(vehicle);
                selectedVehicle = vehicle;
                soundEngine.PlaySuccess();
                SpawnModal.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Kullanıcının kendi araç görselini (jpg/png) içe aktarır: beyaz arkaplanı temizler,
        /// sıkı kırpar, %AppData%'a kaydeder ve varsayılan tekerlek konumlarıyla yeni bir
        /// "custom_" markası olarak canlı ekler (Araç Listesi entegrasyonuyla aynı boru hattı,
        /// çalışma zamanında/manuel dosya seçimiyle).
        /// </summary>
        private void ImportCustomVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Kendi Araç Görselini Seç",
                Filter = "Resim Dosyaları|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                source.EndInit();

                var processed = ImageBackgroundRemover.Process(source);

                string customDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion", "CustomVehicles");
                System.IO.Directory.CreateDirectory(customDir);

                string brandKey = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string filePath = System.IO.Path.Combine(customDir, brandKey + ".png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(processed));
                using (var stream = System.IO.File.Create(filePath))
                {
                    encoder.Save(stream);
                }

                var visual = new VehicleVisual
                {
                    Type = "car",
                    Brand = brandKey,
                    Label = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                    ImagePath = filePath,
                    AspectRatio = (double)processed.PixelWidth / processed.PixelHeight,
                    Wheels = new[]
                    {
                        new WheelSpec(0.20, 0.75, 0.18),
                        new WheelSpec(0.78, 0.75, 0.18)
                    }
                };
                customVisuals[brandKey] = visual;

                var vehicle = new Vehicle(RenderCanvas.ActualWidth / 2, RenderCanvas.ActualHeight / 2, "car", brandKey);
                gameCore.AddAutoMotion(vehicle);
                selectedVehicle = vehicle;
                soundEngine.PlaySuccess();
                ShowToast(Loc.T("msg.custom_vehicle_added", visual.Label));
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.image_import_failed", ex.Message));
            }
        }

        private async void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowDialogAsync(Loc.T("msg.tum_araclari_silmek_istediginden_emin_misin"), "Onay", true))
            {
                // Aynı sebeple: sürülen araç da silinen filoya dahilse önce sürüş modundan çıkılır.
                if (drivenVehicle != null) StopDriving();
                gameCore.Reset();
                InitializeGame();
                selectedVehicle = null;
                PlayUiClick();
            }
        }

        // ============ Mağaza: Para Karşılığı Eşya/Yükseltme Satın Alma ============

        private bool TryPurchase(Item item)
        {
            if (gameCore.GameData.Money < item.Cost)
            {
                PlayUiClick();
                return false;
            }

            gameCore.GameData.Money -= item.Cost;
            gameCore.AddItem(item);
            RefreshItemsList();
            soundEngine.PlaySuccess();
            return true;
        }

        private void AddFuelBtn_Click(object sender, RoutedEventArgs e)
        {
            var fuel = new Fuel("regular");
            fuel.Cost = CurrentFuelPrice();
            TryPurchase(fuel);
        }

        /// <summary>
        /// Borsa Tipi Yakıt Fiyatları: yakıt fiyatı sabit değil, zamanla yavaşça dalgalanır
        /// (~%40 aralığında sinüs eğrisi) - ucuzken stoklama stratejisi mantıklı hale gelir.
        /// </summary>
        private int CurrentFuelPrice()
        {
            double multiplier = 1.0 + Math.Sin(gameCore.GameTime * 0.0025) * 0.4;
            return Math.Max(3, (int)Math.Round(10 * multiplier));
        }

        private void AddRepairBtn_Click(object sender, RoutedEventArgs e)
        {
            TryPurchase(new RepairKit("standard"));
        }

        private void BuyUpgradeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string upgradeType)
            {
                TryPurchase(new Upgrade(upgradeType));
            }
        }

        private void BuyDecorationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string decorationType)
            {
                TryPurchase(new Decoration(decorationType));
            }
        }

        private void NeonColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorHex && selectedVehicle != null)
            {
                selectedVehicle.NeonColor = colorHex;
                PlayUiClick();
            }
        }

        // ============ Kullanılabilirlik Özellikleri: Uçuş / Kilit / Tek Tek Silme ============

        private void DeleteVehicle(Vehicle vehicle)
        {
            if (vehicle == null) return;

            if (drivenVehicle == vehicle)
            {
                StopDriving();
            }

            gameCore.RemoveAutoMotion(vehicle);
            selectedVehicles.Remove(vehicle);

            if (selectedVehicle == vehicle)
            {
                selectedVehicle = selectedVehicles.Count > 0 ? selectedVehicles[0] : null;
            }

            PlayUiClick();
        }

        private void DeleteVehicleRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Vehicle vehicle)
            {
                DeleteVehicle(vehicle);
            }
        }

        private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicles.Count > 1)
            {
                foreach (var v in selectedVehicles.ToList())
                {
                    // Sürülen araç toplu silmeye dahilse önce sürüş modundan çıkılır - aksi halde
                    // drivenVehicle artık filoda olmayan bir araca işaret etmeye devam eder ve
                    // Sürüş Modu tuşu bir daha ASLA yeni bir sürüş başlatamaz, sadece bu "hayalet"
                    // durumu kapatır (vites panelini anında kapatıyormuş gibi görünür).
                    if (drivenVehicle == v) StopDriving();
                    gameCore.RemoveAutoMotion(v);
                }
                selectedVehicles.Clear();
                selectedVehicle = null;
                PlayUiClick();
            }
            else if (selectedVehicle != null)
            {
                DeleteVehicle(selectedVehicle);
            }
        }

        private void GrowVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale += 0.1;
                PlayUiClick();
            }
        }

        private void ShrinkVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale -= 0.1;
                PlayUiClick();
            }
        }

        private void ResetVehicleScaleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale = Vehicle.DefaultScale;
                PlayUiClick();
            }
        }

        private void ToggleFlyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.IsFlying = !selectedVehicle.IsFlying;
                if (selectedVehicle.IsFlying)
                {
                    selectedVehicle.IsLocked = false;
                }
                PlayUiClick();
            }
        }

        private void AttachTrailer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string trailerType && selectedVehicle != null)
            {
                // Aynı dorse butonuna tekrar basılınca çıkar (toggle) - kullanıcı ayrı bir "✕ Çıkar"
                // butonu aramadan, aynı butona ikinci kez basarak dorseyi kaldırabilsin.
                selectedVehicle.TrailerType = (selectedVehicle.TrailerType == trailerType) ? "" : trailerType;
                UpdateTrailerButtonHighlights();
                PlayUiClick();
            }
        }

        /// <summary>Seçili aracın o an takılı dorsesine karşılık gelen butonu tam opak, diğerlerini
        /// soluk gösterir - "hangisi seçili" görsel geri bildirimi (bkz. AttachTrailer_Click).</summary>
        private void UpdateTrailerButtonHighlights()
        {
            string active = selectedVehicle?.TrailerType ?? "";
            TrailerFlatbedBtn.Opacity = active == "flatbed" ? 1.0 : 0.5;
            TrailerTankerBtn.Opacity = active == "tanker" ? 1.0 : 0.5;
            TrailerEquipmentBtn.Opacity = active == "equipment" ? 1.0 : 0.5;
            TrailerWagonBtn.Opacity = active == "wagon" ? 1.0 : 0.5;
        }

        private void ToggleLockBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.IsLocked = !selectedVehicle.IsLocked;
                // Kilitleme artık aracı "Sanal Otopark" ızgarasına ışınlamıyor - kullanıcı "olduğu
                // yerde kilitlesin" istedi, bu yüzden DockX/DockY hep -1 kalır ve Vehicle.Update'teki
                // "yoksa olduğu yerde donar" dalı devreye girer (bkz. AutoMotion.cs ~496).
                if (selectedVehicle.IsLocked)
                {
                    selectedVehicle.IsFlying = false;
                }
                selectedVehicle.DockX = -1;
                selectedVehicle.DockY = -1;
                PlayUiClick();
            }
        }

        private void ToggleLearningAIBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle == null) return;

            selectedVehicle.UseLearningAI = !selectedVehicle.UseLearningAI;
            ToggleLearningAIBtn.Opacity = selectedVehicle.UseLearningAI ? 1.0 : 0.4;
            ShowToast(selectedVehicle.UseLearningAI
                ? $"🧠 {selectedVehicle.Emoji} artık kargo/hedef teslimatında öğrenerek (Q-learning) yol buluyor"
                : $"🧠 {selectedVehicle.Emoji} için akıllı AI kapatıldı");
            PlayUiClick();
        }

        /// <summary>
        /// Sanal Otopark: tüm kilitli araçları ekranın sol alt köşesindeki görünmez bir garaj
        /// alanına, düzenli bir ızgara halinde (satır başına 8 araç) yeniden yerleştirir.
        /// Kilit her açılıp kapandığında yeniden çağrılır.
        /// </summary>
        private void AssignGarageSlots()
        {
            var lockedVehicles = gameCore.AutoMotions.Where(v => v.IsLocked).ToList();
            const int slotsPerRow = 8;
            const double slotSpacingX = 55;
            const double slotSpacingY = 45;
            double baseY = RenderCanvas.ActualHeight > 0 ? RenderCanvas.ActualHeight - 35 : 700;

            for (int i = 0; i < lockedVehicles.Count; i++)
            {
                lockedVehicles[i].DockX = 30 + (i % slotsPerRow) * slotSpacingX;
                lockedVehicles[i].DockY = baseY - (i / slotsPerRow) * slotSpacingY;
            }
        }

        /// <summary>
        /// Sürüş Modu aç/kapat: açıldığında seçili araç AI'dan çıkar, oyuncu ok tuşları/WASD ile
        /// 1:1 doğrudan kontrol eder; sağ tarafta araç tipine göre vites arayüzü açılır.
        /// </summary>
        private void ToggleDriveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (drivenVehicle != null)
            {
                StopDriving();
                return;
            }

            if (selectedVehicle == null)
            {
                ShowToast(Loc.T("msg.surus_modu_icin_once_bir_arac"));
                return;
            }

            StartDriving(selectedVehicle);
        }

        /// <summary>Verilen aracı sürüş moduna alır; vites paneli araç tipine göre kurulur.</summary>
        private void StartDriving(Vehicle vehicle)
        {
            if (drivenVehicle != null) drivenVehicle.IsDriven = false;

            drivenVehicle = vehicle;
            drivenVehicle.IsDriven = true;
            drivenVehicle.IsLocked = false;
            pressedDriveKeys.Clear();
            smoothedThrottle = 0;
            smoothedSteerInput = 0;
            SteeringWheelRotate.Angle = 0;

            bool isTwoWheeler = drivenVehicle.Type == "bike" || drivenVehicle.Type == "motorcycle";
            gearShifterIsTwoWheeler = isTwoWheeler;
            gearLastColumn = -1;
            BuildGearSlots(drivenVehicle);

            RangeSwitchBtn.Visibility = drivenVehicle.HasRangeSplitter ? Visibility.Visible : Visibility.Collapsed;
            drivenVehicle.RangeHigh = true;
            UpdateRangeSwitchVisual();

            drivenVehicle.Gear = 1;
            SetGear(1, animateKnob: false);
            GearVehicleNameTxt.Text = string.IsNullOrEmpty(drivenVehicle.Name) ? drivenVehicle.Emoji : $"{drivenVehicle.Emoji} {drivenVehicle.Name}";

            selectedVehicle = drivenVehicle;
            VehicleListBox.SelectedItem = drivenVehicle;

            AnimateVisibility(GearPanel, true);
            ShowMasterPanelTab(TabGear);
            PlayUiClick();
        }

        /// <summary>Tab: sürüş modundan çıkmadan bir sonraki araca geçer (filo içinde hızlı gezinme).</summary>
        private void SwitchDrivenVehicle()
        {
            var vehicles = gameCore.AutoMotions;
            if (vehicles.Count <= 1 || drivenVehicle == null) return;

            int currentIndex = vehicles.IndexOf(drivenVehicle);
            int nextIndex = (currentIndex + 1) % vehicles.Count;
            StartDriving(vehicles[nextIndex]);
        }

        private void StopDriving()
        {
            if (drivenVehicle != null)
            {
                drivenVehicle.IsDriven = false;
                drivenVehicle.IsBraking = false;
            }
            drivenVehicle = null;
            pressedDriveKeys.Clear();
            smoothedThrottle = 0;
            smoothedSteerInput = 0;
            SteeringWheelRotate.Angle = 0;
            AnimateVisibility(GearPanel, false);
            PlayUiClick();
        }

        private readonly Dictionary<int, Point> gearSlotPoints = new();
        private readonly Dictionary<int, TextBlock> gearSlotLabels = new();
        private readonly Dictionary<int, int> gearSlotColumns = new();

        // Vites kolunun GERÇEK bir şanzıman gibi kısıtlı ("kılavuzlu") sürüklenmesi için gereken
        // yuva geometrisi - bkz. GearKnob_MouseMove. Serbest 2D sürükleme yerine, kol sadece nötr
        // hizasındayken yanlara kayabilir; bir kulvara girince o kulvarın X'ine kilitlenip sadece
        // ileri/geri (Y) hareket edebilir - tıpkı gerçek bir H-pattern vites kolu gibi.
        private readonly Dictionary<int, double> gearColumnXPositions = new();
        private bool gearIsSequentialLayout;
        private double gearNeutralY;
        private double gearLaneTopY;
        private double gearLaneBottomY;
        private int gearDragColumn = -1;
        private const double GearNeutralBandTolerance = 15.0;

        /// <summary>Vites kolu topuzunun her geçerli vites için oturduğu konum (kutunun sol-üst köşesi, 26x26 topuz).</summary>
        private Point GetGearSlotPoint(int gear, bool isTwoWheeler) =>
            gearSlotPoints.TryGetValue(gear, out var p) ? p : new Point(58, 59);

        /// <summary>
        /// Vites kolu yuvasını (H-pattern veya sekans) aracın GERÇEK vites sayısına (TopGear) göre
        /// baştan kurar - 6 viteslik bir otomobil ile 8 viteslik bir traktör/3 viteslik bir bisiklet
        /// artık aynı sabit 4 pozisyonlu düzeni paylaşmıyor, her biri kendi gerçek şanzımanına uygun
        /// bir yuva görüyor. İki tekerlekliler (motosiklet/bisiklet) gerçek bir sekans şanzıman gibi
        /// TEK kulvarda "1 aşağı, geri kalanı yukarı" düzenini kullanır (gerçek motosiklet vites
        /// pedalı mantığı); diğerleri klasik H-pattern kullanır (gerekirse birden fazla kulvar).
        /// </summary>
        private void BuildGearSlots(Vehicle vehicle)
        {
            gearSlotPoints.Clear();
            gearSlotColumns.Clear();
            gearColumnXPositions.Clear();
            GearSlotLabelsCanvas.Children.Clear();
            gearSlotLabels.Clear();
            gearDragColumn = -1;

            bool isTwoWheeler = vehicle.Type == "bike" || vehicle.Type == "motorcycle";
            gearIsSequentialLayout = isTwoWheeler;
            int topGear = Math.Max(1, vehicle.TopGear);
            var pathData = new System.Text.StringBuilder();
            // Path mini-dili ondalık ayırıcı olarak nokta bekler - Türkçe (tr-TR) gibi virgülü ondalık
            // ayırıcı kullanan bir sistem kültüründe ToString() virgül üretip Geometry.Parse'ı
            // bozabilirdi (virgül zaten koordinat ayırıcısı). Kültürden bağımsız biçimlendirme ile
            // bu garanti altına alınıyor.
            static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (isTwoWheeler)
            {
                // Sekans şanzıman: tek dikey kulvar, alttan yukarı 1-N-2-3-4-5-6 (gerçek motosiklet
                // vites pedalı düzeni - "bir aşağı vites (1), geri kalanı yukarı").
                const double x = 68;
                const double spacing = 20;
                double bottomY = 130;

                double Y(int slot) => bottomY - slot * spacing; // slot 0=gear1, 1=N, 2=gear2, ...

                gearSlotPoints[1] = new Point(x, Y(0));
                gearSlotPoints[0] = new Point(x, Y(1));
                for (int g = 2; g <= topGear; g++)
                    gearSlotPoints[g] = new Point(x, Y(g));

                gearColumnXPositions[0] = x;
                gearLaneTopY = Y(topGear);
                gearLaneBottomY = Y(0);
                gearNeutralY = Y(1);

                pathData.Append($"M{Inv(x + 13)},{Inv(Y(0) + 13)} L{Inv(x + 13)},{Inv(Y(topGear) + 13)}");

                AddGearLabel(1, x, Y(0), "#3DA9FC");
                AddGearLabel(0, x, Y(1), "#95E1D3", 8);
                for (int g = 2; g <= topGear; g++)
                    AddGearLabel(g, x, Y(g), "#3DA9FC");
            }
            else
            {
                // Klasik H-pattern: her kulvarda 2 vites (tek sayı üstte, çift sayı altta) - gerçek
                // bir manuel şanzımanın 1-2 / 3-4 / 5-6 (/ 7-8) düzeni. Geri vites (R) en sağdaki
                // ekstra kulvarda, sadece üstte durur.
                const double firstColX = 7;
                const double colSpacing = 24;
                const double rowTopY = 4;
                const double rowBottomY = 113;
                const double rowMidY = 59;

                int colCountForGears = (topGear + 1) / 2; // ceil(topGear/2)
                double ColX(double col) => firstColX + col * colSpacing;

                gearLaneTopY = rowTopY;
                gearLaneBottomY = rowBottomY;
                gearNeutralY = rowMidY;

                for (int g = 1; g <= topGear; g++)
                {
                    int col = (g - 1) / 2;
                    bool top = g % 2 == 1;
                    var pt = new Point(ColX(col), top ? rowTopY : rowBottomY);
                    gearSlotPoints[g] = pt;
                    gearSlotColumns[g] = col;
                    gearColumnXPositions[col] = ColX(col);
                    AddGearLabel(g, pt.X, pt.Y, "#3DA9FC");

                    pathData.Append($"M{Inv(ColX(col) + 13)},{Inv(rowTopY + 13)} L{Inv(ColX(col) + 13)},{Inv(rowBottomY + 13)} ");
                }

                // Geri vites (R) - ayrı kulvar, sadece üstte
                var rPoint = new Point(ColX(colCountForGears), rowTopY);
                gearSlotPoints[-1] = rPoint;
                gearSlotColumns[-1] = colCountForGears;
                gearColumnXPositions[colCountForGears] = ColX(colCountForGears);
                AddGearLabel(-1, rPoint.X, rPoint.Y, "#FF6B6B", 9, "R");
                pathData.Append($"M{Inv(rPoint.X + 13)},{Inv(rowTopY + 13)} L{Inv(rPoint.X + 13)},{Inv(rowTopY + 13 + 30)} ");

                // Nötr (N) - kulvarların ortasında yatay bağlantı üzerinde
                double nCenterCol = (colCountForGears - 1) / 2.0;
                var nPoint = new Point(ColX(nCenterCol), rowMidY);
                gearSlotPoints[0] = nPoint;
                gearSlotColumns[0] = -99; // N özel: kulvar değişimi tetiklemesin
                AddGearLabel(0, nPoint.X, nPoint.Y, "#95E1D3", 8);

                pathData.Append($"M{Inv(ColX(0) + 13)},{Inv(rowMidY + 13)} L{Inv(ColX(colCountForGears - 1) + 13)},{Inv(rowMidY + 13)}");
            }

            GearTrackPath.Data = Geometry.Parse(pathData.ToString());
        }

        private void AddGearLabel(int gear, double x, double y, string color, double fontSize = 9, string text = null)
        {
            var lbl = new TextBlock
            {
                Text = text ?? gear.ToString(),
                FontFamily = (FontFamily)FindResource("PixelFont"),
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y);
            GearSlotLabelsCanvas.Children.Add(lbl);
            gearSlotLabels[gear] = lbl;
        }

        /// <summary>Aracın vitesini değiştirir; topuzu ilgili H-yuva konumuna hareket ettirir ve etiketleri günceller.
        /// Kulvar değişimlerinde (ör. 1'den 3'e) gerçek bir şanzıman gibi önce nötre uğrar.</summary>
        private void SetGear(int gear, bool animateKnob = true)
        {
            if (drivenVehicle == null) return;
            // Gerçek vites değişimi (ilk kurulum değil) ise kısa bir güç kesintisi uygulanır -
            // gerçek bir manuel şanzımanda debriyaj/vites geçişinde hissedilen "boşluk" hissi.
            if (animateKnob && drivenVehicle.Gear != gear)
            {
                drivenVehicle.GearShiftCooldown = 14;
            }
            drivenVehicle.Gear = gear;
            string rangeSuffix = drivenVehicle.HasRangeSplitter && gear > 0 ? (drivenVehicle.RangeHigh ? "Y" : "D") : "";
            GearCurrentTxt.Text = gear switch
            {
                -1 => "Vites: R",
                0 => "Vites: N",
                _ => $"Vites: {gear}{rangeSuffix}"
            };

            HighlightActiveGearSlot(gear);

            var target = GetGearSlotPoint(gear, gearShifterIsTwoWheeler);
            int currentColumn = GetGearColumn(gear);
            if (animateKnob && !gearShifterIsTwoWheeler && currentColumn != gearLastColumn && gearLastColumn != -1 && currentColumn != -99 && gearLastColumn != -99)
            {
                // Kulvar değişiyor: topuz önce nötre, sonra hedef vitese hareket eder (gerçek H-shift hissi).
                var neutral = GetGearSlotPoint(0, false);
                AnimateKnobTo(neutral, TimeSpan.FromMilliseconds(90), () => AnimateKnobTo(target, TimeSpan.FromMilliseconds(110)));
            }
            else if (animateKnob)
            {
                AnimateKnobTo(target, TimeSpan.FromMilliseconds(120));
            }
            else
            {
                Canvas.SetLeft(GearKnob, target.X);
                Canvas.SetTop(GearKnob, target.Y);
            }
            gearLastColumn = currentColumn;

            PopAnimate(GearCurrentTxtScale);
            soundEngine.PlayDrift(); // vites geçiş "klik" sesi
        }

        /// <summary>Verilen vitesin hangi H-pattern kulvarında olduğunu döndürür (BuildGearSlots'ta
        /// hesaplanıp gearSlotColumns'a kaydedilir) - kulvar değişimlerinde önce nötre uğrama animasyonu
        /// tetiklemek için kullanılır. N için özel -99 değeri döner (kulvar değişimi saymaz).</summary>
        private int GetGearColumn(int gear) => gearSlotColumns.TryGetValue(gear, out var c) ? c : -1;

        /// <summary>MudRunner tarzı "ağır" manuel vites kolu hissi: gerçek bir şanzıman kolu direnç
        /// gösterip hafifçe fazla gider (overshoot) sonra yuvaya oturur - basit bir ease-out yerine
        /// bu "hedefi aşıp geri toparlanma" hissi kolun gerçek bir mekanizma gibi ağırlığı olduğunu
        /// hissettirir. Vitese oturunca kısa bir mekanik "click/thunk" sarsıntısı (topuz burulması +
        /// hafif titreşim) eklenir - elle bastırıp yuvaya kilitlenen bir kolun hissi.
        /// </summary>
        private void AnimateKnobTo(Point target, TimeSpan duration, Action onComplete = null)
        {
            // BackEase: hedefe varmadan önce kısa bir "fazla gitme" (overshoot) sonra geri toparlanma -
            // ağır/dirençli bir mekanik kol hissi verir, düz ease-out'tan çok daha "gerçek".
            var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.65 };
            var animX = new DoubleAnimation(target.X, duration) { EasingFunction = easing };
            var animY = new DoubleAnimation(target.Y, duration) { EasingFunction = easing };
            if (onComplete != null) animY.Completed += (s, e) => onComplete();
            GearKnob.BeginAnimation(Canvas.LeftProperty, animX);
            GearKnob.BeginAnimation(Canvas.TopProperty, animY);

            // Topuz burulması (torque wobble): el kolu ittirirken hafifçe burulup yuvaya oturunca
            // düzelen bir dönüş - gerçek bir vites kolunun mekanik direncini/burulmasını taklit eder.
            double wobbleDir = Random.Shared.NextDouble() < 0.5 ? 1 : -1;
            var rotWobble = new DoubleAnimationUsingKeyFrames();
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(14 * wobbleDir, KeyTime.FromPercent(0.35), new QuadraticEase()));
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(-6 * wobbleDir, KeyTime.FromPercent(0.65), new QuadraticEase()));
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0), new QuadraticEase()));
            rotWobble.Duration = duration + TimeSpan.FromMilliseconds(90);
            GearKnobRotate.BeginAnimation(RotateTransform.AngleProperty, rotWobble);

            // Mekanik "thunk" - hedefe varınca topuz kısa süreliğine sıkışıp (küçülüp) sonra normale
            // dönerek yuvaya kilitlenme hissi verir (önceki tek yönlü "pop"tan daha sert/mekanik).
            var pop = new DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromPercent(0)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.18, KeyTime.FromPercent(0.55)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.8)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            GearKnobScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            GearKnobScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            // Vites miline oturma sarsıntısı: kolun gövdesi (GearShaftLine) de birlikte titrer - tüm
            // mekanizmanın sarsıldığı hissini güçlendirir.
            var shaftShake = new DoubleAnimationUsingKeyFrames();
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromPercent(0.5)));
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            GearShaftLine.BeginAnimation(OpacityProperty, shaftShake);
        }

        /// <summary>Aktif vitesin yuva etiketini parlak sarı vurguyla işaretler, diğerlerini söndürür.</summary>
        private void HighlightActiveGearSlot(int gear)
        {
            foreach (var (g, lbl) in gearSlotLabels)
            {
                bool active = g == gear;
                lbl.Effect = active ? new DropShadowEffect { Color = Color.FromRgb(0xFB, 0xCA, 0x1F), BlurRadius = 10, ShadowDepth = 0, Opacity = 1.0 } : null;
                lbl.Opacity = active ? 1.0 : 0.55;
            }
        }

        private bool gearShifterIsTwoWheeler;
        private int gearLastColumn = -1;
        private bool gearKnobDragging;

        /// <summary>Range (Aralık) anahtarına tıklanınca Düşük/Yüksek arasında geçiş yapar - gerçek bir
        /// kamyon şanzımanındaki splitter anahtarı gibi (aynı 6 ana vites, iki farklı hız/tork bandı).</summary>
        private void RangeSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (drivenVehicle == null || !drivenVehicle.HasRangeSplitter) return;
            drivenVehicle.RangeHigh = !drivenVehicle.RangeHigh;
            UpdateRangeSwitchVisual();
            SetGear(drivenVehicle.Gear, animateKnob: false); // metni/vurguyu yenile, topuz yerinde kalır
            PlayUiClick();
        }

        private void UpdateRangeSwitchVisual()
        {
            if (drivenVehicle == null) return;
            RangeSwitchBtn.Content = drivenVehicle.RangeHigh ? "Aralık: Yüksek ⛰" : "Aralık: Düşük 🔽";
        }

        // MudRunner'daki fare ile tutulan vites kolunun "sallanan/ağır" hissi: kol imlece ANINDA
        // ışınlanmıyor, bir yay-sönümleyici (spring-damper) fizikle geriden takip ediyor - hızlı
        // hareket ettirince kol geriden sallana sallana yetişiyor, tıpkı gerçek ağır bir kolu elle
        // tutup sallamak gibi. Hedef konum (fareyle kısıtlanmış yuva pozisyonu) ile GÖRÜNEN konum
        // ayrı tutuluyor; her karede (GameLoop_Tick) ikisi arasındaki fark yayla kapatılıyor.
        private double gearKnobTargetX, gearKnobTargetY;
        private double gearKnobActualX, gearKnobActualY;
        private double gearKnobVelX, gearKnobVelY;

        private void GearKnob_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (drivenVehicle == null) return;
            gearKnobDragging = true;
            GearKnob.CaptureMouse();
            GearKnob.BeginAnimation(Canvas.LeftProperty, null);
            GearKnob.BeginAnimation(Canvas.TopProperty, null);
            GearKnob.BeginAnimation(RotateTransform.AngleProperty, null);
            GearKnobRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            gearKnobTargetX = gearKnobActualX = Canvas.GetLeft(GearKnob);
            gearKnobTargetY = gearKnobActualY = Canvas.GetTop(GearKnob);
            gearKnobVelX = gearKnobVelY = 0;

            // Sürüklemeye, elin şu an içinde bulunduğu kulvardan başlar (gerçek bir vitesi tutup
            // ittirmek gibi) - N'den başlamıyorsa hemen yanlara kaymaz, önce o kulvarda kalır.
            int startCol = GetGearColumn(drivenVehicle.Gear);
            gearDragColumn = startCol == -99 ? -1 : startCol;
        }

        /// <summary>
        /// Vites kolu artık serbest bir 2D top gibi değil, GERÇEK bir şanzıman kolu gibi
        /// sürükleniyor: kol sadece nötr hizasındayken (yatay bar üzerinde) yanlara kayabilir; bir
        /// kulvara girdiğinde o kulvarın X'ine kilitlenip sadece ileri/geri (Y) hareket edebilir.
        /// Bu metod sadece HEDEF konumu günceller - görünen konum UpdateGearKnobDragPhysics'te
        /// (her oyun karesinde) yay fiziğiyle bu hedefi takip eder (bkz. MudRunner sallanma hissi).
        /// </summary>
        private void GearKnob_MouseMove(object sender, MouseEventArgs e)
        {
            if (!gearKnobDragging || drivenVehicle == null) return;
            var pos = e.GetPosition(GearShifterCanvas);

            double knobX, knobY;
            if (gearIsSequentialLayout)
            {
                // Sekans şanzıman: tek kulvar, X her zaman sabit - sadece ileri/geri (Y) serbest.
                knobX = gearColumnXPositions[0];
                knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
            }
            else
            {
                bool nearNeutral = Math.Abs(pos.Y - 13 - gearNeutralY) < GearNeutralBandTolerance;
                if (nearNeutral)
                {
                    // Nötr hizasında: kol yanlara serbestçe kayabilir (kulvar henüz seçilmedi).
                    gearDragColumn = -1;
                    knobY = gearNeutralY;
                    knobX = pos.X - 13;
                }
                else if (gearDragColumn == -1)
                {
                    // Nötr bardan yeni çıkılıyor: fareye en yakın kulvara "kilitlen".
                    double mouseColX = pos.X - 13;
                    gearDragColumn = gearColumnXPositions.OrderBy(kv => Math.Abs(kv.Value - mouseColX)).First().Key;
                    knobX = gearColumnXPositions[gearDragColumn];
                    knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
                }
                else
                {
                    // Zaten bir kulvarın içinde: X kilitli kalır, sadece ileri/geri hareket eder.
                    knobX = gearColumnXPositions[gearDragColumn];
                    knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
                }
            }

            gearKnobTargetX = Math.Clamp(knobX, 0, GearShifterCanvas.Width - 26);
            gearKnobTargetY = knobY;
        }

        /// <summary>Her oyun karesinde (GameLoop_Tick'ten) çağrılır - vites kolunu bir yay-sönümleyici
        /// ile hedef konuma doğru "sallana sallana" hareket ettirir ve hıza bağlı hafif bir burulma
        /// (pendulum sallanışı) ekler. Sadece sürüklerken çalışır.</summary>
        /// <summary>SnowRunner/MudRunner tarzı gerçek bir vites kolu hissi: kolun gövdesi (GearShaftLine/
        /// Shadow/Highlight) her karede topuzun O ANKİ (Canvas.GetLeft/Top - storyboard animasyonuyla,
        /// sürüklenirken veya boşta sallanırken FARK ETMEKSİZİN her zaman güncel render değerini verir)
        /// merkezine yeniden çizilir; taban ucu sabit mil yatağında kalır. Böylece topuz H-yuvasında
        /// nereye giderse gitsin kol onu görsel olarak takip edip gerçekçi biçimde eğilir - eskiden kol
        /// XAML'de sabit koordinatlarla çizilmiş tek bir çizgiydi ve topuzdan tamamen kopuk dururdu.</summary>
        private void SyncGearShaftLine()
        {
            if (GearPanel.Visibility != Visibility.Visible) return;

            double knobCenterX = Canvas.GetLeft(GearKnob) + GearKnob.Width / 2.0;
            double knobCenterY = Canvas.GetTop(GearKnob) + GearKnob.Height / 2.0;
            if (double.IsNaN(knobCenterX) || double.IsNaN(knobCenterY)) return;

            GearShaftLine.X1 = knobCenterX;
            GearShaftLine.Y1 = knobCenterY;
            GearShaftLineShadow.X1 = knobCenterX + 1.5;
            GearShaftLineShadow.Y1 = knobCenterY + 1.5;

            // Parlama çizgisi kolun gövdesine paralel, hafif içeride kalır - metal bir çubuğun üstündeki
            // ışık yansımasını taklit eder.
            GearShaftHighlight.X1 = knobCenterX - 1.5;
            GearShaftHighlight.Y1 = knobCenterY + 2;
        }

        private void UpdateGearKnobDragPhysics()
        {
            if (!gearKnobDragging) return;

            const double stiffness = 0.30;
            const double damping = 0.72;

            double dx = gearKnobTargetX - gearKnobActualX;
            double dy = gearKnobTargetY - gearKnobActualY;
            gearKnobVelX = (gearKnobVelX + dx * stiffness) * damping;
            gearKnobVelY = (gearKnobVelY + dy * stiffness) * damping;
            gearKnobActualX += gearKnobVelX;
            gearKnobActualY += gearKnobVelY;

            Canvas.SetLeft(GearKnob, gearKnobActualX);
            Canvas.SetTop(GearKnob, gearKnobActualY);

            // Sallanma (sway): kol hızlı hareket ettirilince ağırlığıyla hafifçe geriden "salınır" -
            // MudRunner'daki elle tutulup sallanan kol hissi.
            double sway = Math.Clamp(-gearKnobVelX * 3.5, -22, 22);
            GearKnobRotate.Angle = sway;
        }

        private void GearKnob_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!gearKnobDragging || drivenVehicle == null) return;
            gearKnobDragging = false;
            GearKnob.ReleaseMouseCapture();

            double curLeft = gearKnobActualX;
            double curTop = gearKnobActualY;

            int nearestGear = 0;
            double bestDist = double.MaxValue;
            foreach (var (g, slot) in gearSlotPoints)
            {
                double dist = Math.Pow(slot.X - curLeft, 2) + Math.Pow(slot.Y - curTop, 2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearestGear = g;
                }
            }

            SetGear(nearestGear);
        }

        /// <summary>Kısa bir "pop" (büyü-küçül) animasyonu - vites/hız değişimlerine canlılık katar.</summary>
        private void PopAnimate(ScaleTransform target)
        {
            var scaleAnim = new DoubleAnimationUsingKeyFrames();
            scaleAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.35, KeyTime.FromPercent(0)));
            scaleAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1)));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(220);
            target.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        // Klavye
        private bool suppressUiClickSound = false;

        /// <summary>Butonlara TIKLANINCA "tık" sesi verir, ama aynı buton bir klavye kısayoluyla
        /// (Window_KeyDown) tetiklendiğinde bu ses bastırılır - kullanıcı her tuşa bastığında ses
        /// çıkmasını istemedi. Fareyle gerçek bir butona tıklamanın sesli geri bildirimi korunuyor.</summary>
        private void PlayUiClick()
        {
            if (!suppressUiClickSound) soundEngine.PlayClick();
        }

        // === Ekran Görüntüsü / GIF Kaydet ===
        // Paylaşım özelliği (D-08, önceden ertelenmiş) - dış bağımlılık gerektirmez: PNG için
        // PngBitmapEncoder, GIF için .NET'in yerleşik GifBitmapEncoder'ı kullanılır. Pencerenin
        // KENDİSİ (WPF görsel ağacı) yakalanır - şeffaf temada arka plan da şeffaf kalır (gerçek
        // masaüstü değil, boş piksel olarak kaydedilir).
        private bool isRecordingGif;
        private List<BitmapSource> gifFrames;
        private DispatcherTimer gifCaptureTimer;

        private RenderTargetBitmap CaptureWindowBitmap()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = Math.Max(1, (int)(ActualWidth * dpi.DpiScaleX));
            int h = Math.Max(1, (int)(ActualHeight * dpi.DpiScaleY));
            var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(this);
            return rtb;
        }

        private string GetScreenshotDirectory()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion", "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        private void ScreenshotBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(GetScreenshotDirectory(), $"automotion_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(CaptureWindowBitmap()));
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                    encoder.Save(fs);
                ShowToast(Loc.T("msg.ekran_goruntusu_kaydedildi") + path);
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.ekran_goruntusu_alinamadi") + ex.Message);
            }
            PlayUiClick();
        }

        private void GifRecordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (isRecordingGif) return; // kayıt zaten sürüyor, tekrar tıklamayı yok say
            isRecordingGif = true;
            gifFrames = new List<BitmapSource>();
            GifRecordBtn.Content = "⏺️";
            ShowToast(Loc.T("msg.3_saniyelik_gif_kaydi_basladi"));
            PlayUiClick();

            const int totalFrames = 30; // ~100ms aralıkla 30 kare = 3 saniye
            int framesLeft = totalFrames;
            gifCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            gifCaptureTimer.Tick += (s, args) =>
            {
                gifFrames.Add(CaptureWindowBitmap());
                framesLeft--;
                if (framesLeft <= 0)
                {
                    gifCaptureTimer.Stop();
                    FinishGifRecording();
                }
            };
            gifCaptureTimer.Start();
        }

        private void FinishGifRecording()
        {
            isRecordingGif = false;
            GifRecordBtn.Content = "🎬";
            try
            {
                string path = System.IO.Path.Combine(GetScreenshotDirectory(), $"automotion_{DateTime.Now:yyyyMMdd_HHmmss}.gif");
                var encoder = new GifBitmapEncoder();
                foreach (var frame in gifFrames)
                {
                    // Kare gecikmesi (frame delay) - GifBitmapEncoder varsayılan olarak bunu
                    // ayarlamaz, bu yüzden çoğu görüntüleyicide GIF olması gerekenden çok hızlı
                    // oynar. "/grctlext/Delay" 1/100 saniye biriminde: 10 = 100ms/kare.
                    var metadata = new BitmapMetadata("gif");
                    metadata.SetQuery("/grctlext/Delay", (ushort)10);
                    encoder.Frames.Add(BitmapFrame.Create(frame, null, metadata, null));
                }
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                    encoder.Save(fs);
                ShowToast(Loc.T("msg.gif_kaydedildi") + path);
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.gif_kaydedilemedi") + ex.Message);
            }
            finally
            {
                gifFrames = null;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            lastUserActivity = DateTime.Now;

            suppressUiClickSound = true;
            try
            {
                HandleWindowKeyDown(e);
            }
            finally
            {
                suppressUiClickSound = false;
            }
        }

        private void HandleWindowKeyDown(KeyEventArgs e)
        {
            wpmRace.RegisterKeystroke();

            // Sürüş Modu aktifken klavye tamamen araç kontrolüne ayrılır (WASD/Ok tuşları),
            // diğer kısayollar (R, C, S, L...) yanlışlıkla tetiklenmesin diye devre dışı kalır.
            if (drivenVehicle != null)
            {
                if (e.Key == Key.Space || e.Key == Key.Escape)
                {
                    StopDriving();
                }
                else if (e.Key == Key.Tab)
                {
                    SwitchDrivenVehicle();
                }
                else if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
                {
                    pressedDriveKeys.Add(e.Key);
                }
                else if (e.Key >= Key.D0 && e.Key <= Key.D9)
                {
                    int gear = e.Key - Key.D0;
                    if (gear <= drivenVehicle.TopGear && gearSlotPoints.ContainsKey(gear))
                    {
                        SetGear(gear);
                    }
                }
                else if (e.Key == Key.X && !e.IsRepeat)
                {
                    RangeSwitchBtn_Click(null, null);
                }
                else if (e.Key == Key.H && !e.IsRepeat)
                {
                    soundEngine.PlayHorn(drivenVehicle.Type);
                }
                e.Handled = true;
                return;
            }

            // Klavye Kısayollarını Kapat: eklenen tek-harf kısayolların sayısı arttıkça (B, V, K, A,
            // T, M, R... gibi) bunlar, Yazı Yarışı (WPM) gibi serbest tuş vuruşu gerektiren
            // özelliklerle çakışıyordu - kullanıcı yazarken yanlışlıkla uçuş/kilit/AI modlarını açıp
            // kapatabiliyordu. Bu anahtar açıkken (kısayollar KAPALI) tuş vuruşu sayımı (yukarıdaki
            // wpmRace.RegisterKeystroke) hâlâ çalışır, sadece aşağıdaki tekil-tuş kısayolları atlanır.
            if (!shortcutsEnabled) return;

            switch (e.Key)
            {
                case Key.T:
                    ThemeBtn_Click(null, null);
                    break;
                case Key.M:
                    SoundBtn_Click(null, null);
                    break;
                case Key.R:
                    AddVehicleBtn_Click(null, null);
                    break;
                case Key.C:
                    ClearBtn_Click(null, null);
                    break;
                case Key.S:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        SaveBtn_Click(null, null);
                    break;
                case Key.L:
                    LoadBtn_Click(null, null);
                    break;
                case Key.F:
                    AddFuelBtn_Click(null, null);
                    break;
                case Key.H:
                    AddRepairBtn_Click(null, null);
                    break;
                case Key.Q:
                    CallVehicleBtn_Click(null, null);
                    break;
                case Key.E:
                    ExpansionsBtn_Click(null, null);
                    break;
                case Key.Delete:
                    DeleteSelectedBtn_Click(null, null);
                    break;
                case Key.B:
                    ToggleLearningAIBtn_Click(null, null);
                    break;
                case Key.V:
                    ToggleFlyBtn_Click(null, null);
                    break;
                case Key.K:
                    ToggleLockBtn_Click(null, null);
                    break;
                case Key.G:
                    ToggleAllUI_Click(null, null);
                    break;
                case Key.A:
                    ToggleAutoMove_Click(null, null);
                    break;
                case Key.Space:
                    // Sürüş modu henüz kapalıyken Boşluk = başlat (seçili araç varsa); açıkken
                    // durdurma zaten üstteki drivenVehicle != null dalında ele alınıyor.
                    ToggleDriveBtn_Click(null, null);
                    break;
                case Key.P:
                    // İmleç Trafik Değneği: basılı tutulduğunda imleç "Dur" işaretine döner,
                    // yakındaki araçlar acil fren yapar (bkz. GameLoop_Tick).
                    if (!stopSignActive)
                    {
                        stopSignActive = true;
                        RenderCanvas.Cursor = Cursors.No;
                    }
                    break;
                case Key.W:
                    // Detaylı Yıkama: basılı tutulup fare aracın üzerinde sürüklenince kir azalır
                    washModeActive = true;
                    break;
                case Key.Escape:
                    if (fuelCanActive) CancelFuelCan();
                    break;
            }

            // Tuş Vuruşu Sismik Etkisi: uygulama içinde yazılan her tuş, düşük ihtimalle rastgele
            // bir aracı hafifçe zıplatır - global bir klavye kancası KULLANILMAZ (gizlilik nedeniyle),
            // sadece bu pencereye gelen tuşlara tepki verilir.
            if (gameCore != null && gameCore.AutoMotions.Count > 0 && Random.Shared.NextDouble() < 0.15)
            {
                var shaken = gameCore.AutoMotions[Random.Shared.Next(gameCore.AutoMotions.Count)];
                if (!shaken.IsDragged && !shaken.IsLocked)
                {
                    shaken.VelocityY -= 0.8;
                }
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            pressedDriveKeys.Remove(e.Key);

            if (e.Key == Key.P)
            {
                stopSignActive = false;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
            else if (e.Key == Key.W)
            {
                washModeActive = false;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // Pencere Alt+F4, görev çubuğundan sağ-tık "Kapat" vb. başka bir yoldan da kapanabilir -
            // ExitApplication() zaten kendini tekrar çalıştırmaya karşı korumalı (bkz. isExiting),
            // bu yüzden asıl temizlik mantığı tek bir yerde (ExitApplication) toplanır.
            ExitApplication();
        }

        // Yağmuru aç/kapa
        private void ToggleRainBtn_Click(object sender, RoutedEventArgs e)
        {
            isRaining = !isRaining;
            if (!isRaining) rainDrops.Clear();
            if (isRaining && isSnowing) { isSnowing = false; snowFlakes.Clear(); }
            PlayUiClick();
        }

        // Karı aç/kapa (Kış Teması) - yağmurla aynı anda aktif olamaz
        private void ToggleSnowBtn_Click(object sender, RoutedEventArgs e)
        {
            isSnowing = !isSnowing;
            if (!isSnowing) snowFlakes.Clear();
            if (isSnowing && isRaining) { isRaining = false; rainDrops.Clear(); }
            PlayUiClick();
        }

        // Trafik Işığı ekle
        private void AddTrafficLightBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 200;
            double ry = RenderCanvas.ActualHeight - 80;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "traffic_light"));
            soundEngine.PlaySuccess();
        }

        // Manuel Doldurma: bidonu ele al (mod başlat)
        private void AddFuelCanBtn_Click(object sender, RoutedEventArgs e)
        {
            fuelCanActive = true;
            ShowToast(Loc.T("msg.bidon_elinde_bir_araca_tikla_iptal"));
            PlayUiClick();
        }

        private void CancelFuelCan()
        {
            fuelCanActive = false;
        }

        // Yakıt/Onarım İstasyonu ekle
        private void AddStationBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 40;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "station"));
            soundEngine.PlaySuccess();
        }

        // Masaüstü Yol Çalışması (trafik konisi) ekle
        private void AddConeBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 30;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "cone"));
            PlayUiClick();
        }

        // Trafik Polisi Çevirmesi ekle
        private void AddPoliceBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 40;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "police"));
            fineIssuedFor.Clear();
            PlayUiClick();
        }

        private readonly HashSet<Vehicle> fineIssuedFor = new();

        /// <summary>
        /// Trafik Polisi Çevirmesi: yolu kesen polis aracının yakınından geçen, bakımı düşük
        /// (HP %50'nin altında) araçlara bir kereliğine para cezası kesilir.
        /// </summary>
        private void UpdatePoliceStops()
        {
            var policeObstacles = gameCore.Obstacles.Where(o => o.Type == "police").ToList();
            if (policeObstacles.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (fineIssuedFor.Contains(vehicle)) continue;
                if (vehicle.HP >= vehicle.MaxHP * 0.5) continue;

                foreach (var police in policeObstacles)
                {
                    double dx = police.X - vehicle.X;
                    double dy = police.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 70)
                    {
                        fineIssuedFor.Add(vehicle);
                        int fine = Math.Min(gameCore.GameData.Money, 40);
                        if (fine > 0)
                        {
                            gameCore.AddMoney(-fine);
                            gameCore.GameData.PoliceFinesReceived++;
                            ShowToast(Loc.T("msg.unmaintained_fine", VehicleDisplayName(vehicle), fine));
                            soundEngine.PlayCrash();
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Geri Dönüşüm Çöp Kamyonu (görsel-only): ekran dışından bir kamyon simgesi belirip
        /// "geri dönüşüm kutusuna" doğru gidip kaybolur. Kullanıcı onayı olmadan gerçek Geri
        /// Dönüşüm Kutusu'na ASLA dokunulmaz (SHEmptyRecycleBin çağrılmaz) - tamamen kozmetik bir olay.
        /// </summary>
        private void AddRecycleTruckBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowToast(Loc.T("msg.geri_donusum_kamyonu_yola_cikti_gorsel"));
            var truckIcon = new TextBlock { Text = "♻️🚛", FontSize = 26 };
            double startX = -60;
            double y = RenderCanvas.ActualHeight - 50;
            Canvas.SetLeft(truckIcon, 0);
            Canvas.SetTop(truckIcon, y);
            RenderCanvas.Children.Add(truckIcon);

            var anim = new DoubleAnimation
            {
                From = startX,
                To = RenderCanvas.ActualWidth + 60,
                Duration = TimeSpan.FromSeconds(4),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (s, e2) => RenderCanvas.Children.Remove(truckIcon);
            var transform = new TranslateTransform();
            truckIcon.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, anim);

            soundEngine.PlaySuccess();
        }

        /// <summary>
        /// İstasyona yakın (zeminde, uçmayan/sürüklenmeyen) araçları yavaşça yakıt/HP dolduran döngü.
        /// </summary>
        private void UpdateStations()
        {
            var stations = gameCore.Obstacles.Where(o => o.Type == "station").ToList();
            if (stations.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.IsFlying) continue;

                foreach (var station in stations)
                {
                    double dx = station.X - vehicle.X;
                    double dy = station.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 55)
                    {
                        if (vehicle.Fuel < vehicle.MaxFuel)
                        {
                            vehicle.Fuel = Math.Min(vehicle.MaxFuel, vehicle.Fuel + 0.6);
                        }
                        if (vehicle.HP < vehicle.MaxHP)
                        {
                            vehicle.HP = Math.Min(vehicle.MaxHP, vehicle.HP + 1);
                        }
                        break;
                    }
                }
            }
        }

        // Kargo Görevi Başlat - mantığın tamamı artık MissionExpansion içinde (bkz. Expansions/MissionExpansion.cs)
        private void StartCargoBtn_Click(object sender, RoutedEventArgs e)
        {
            missionExpansion.StartNewMission(gameCore, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);
        }

        private void VehicleNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (selectedVehicle != null && VehicleNameBox != null)
            {
                selectedVehicle.Name = VehicleNameBox.Text;
                VehicleListBox.Items.Refresh(); // Listbox ismini anlık tazele
            }
        }

        private void VehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VehicleListBox.SelectedItem is Vehicle vehicle)
            {
                selectedVehicle = vehicle;
                selectedVehicles.Clear();
                selectedVehicles.Add(vehicle);

                // İsmi yazı kutusunda güncelle
                if (VehicleNameBox != null)
                {
                    VehicleNameBox.TextChanged -= VehicleNameBox_TextChanged;
                    VehicleNameBox.Text = vehicle.Name;
                    VehicleNameBox.TextChanged += VehicleNameBox_TextChanged;
                }

                UpdateUI();
                PlayUiClick();
            }
        }

        public System.Threading.Tasks.TaskCompletionSource<bool> dialogTcs;

        public System.Threading.Tasks.Task<bool> ShowDialogAsync(string message, string title, bool isYesNo)
        {
            dialogTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            CustomDialogTitle.Text = title.ToUpper();
            CustomDialogMessage.Text = message;
            
            if (isYesNo)
            {
                CustomDialogYesBtn.Visibility = Visibility.Visible;
                CustomDialogNoBtn.Visibility = Visibility.Visible;
                CustomDialogOkBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                CustomDialogYesBtn.Visibility = Visibility.Collapsed;
                CustomDialogNoBtn.Visibility = Visibility.Collapsed;
                CustomDialogOkBtn.Visibility = Visibility.Visible;
            }
            
            CustomDialogOverlay.Visibility = Visibility.Visible;
            PlayUiClick();
            
            return dialogTcs.Task;
        }

        public void CustomDialogYes_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            PlayUiClick();
            dialogTcs?.TrySetResult(true);
        }

        public void CustomDialogNo_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            PlayUiClick();
            dialogTcs?.TrySetResult(false);
        }

        public void CustomDialogOk_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            PlayUiClick();
            dialogTcs?.TrySetResult(true);
        }
    }

}
