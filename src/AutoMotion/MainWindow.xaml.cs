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

        static MainWindow()
        {
            EventManager.RegisterClassHandler(typeof(ToolTip), FrameworkElement.LoadedEvent, new RoutedEventHandler(ToolTip_ForceTopmost));
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

            // Kendiliğinden kapanma koruması (Kalan İşler 2.6) - bkz. Window_Closing.
            Closing += Window_Closing;
            if (System.Windows.Application.Current != null)
                System.Windows.Application.Current.SessionEnding += (s, e) => isSessionEnding = true;
        }

        private bool isSessionEnding;
        private bool trayHintShown;

        /// <summary>
        /// Kalan İşler 2.6 - "uygulama kendiliğinden kapandı": iki olayda da kapanış TEMİZDİ
        /// (otomatik kayıt yazılmış), yani bir çökme değil, pencereye gelen bir kapatma isteğiydi.
        /// Tam ekran bir oyun açılırken/kapanırken odak kısa süre bu ekranı kaplayan, her zaman
        /// üstte duran şeffaf pencereye düşebiliyor; o an oyunu kapatmak için basılan Alt+F4 ya da
        /// görev çubuğundan gelen "Kapat" doğrudan AutoMotion'a gidiyor ve kullanıcı farkına bile
        /// varmadan filosunu kapatıyordu.
        ///
        /// Artık yalnızca BİLİNÇLİ çıkış yolları (üst çubuktaki ✕, tepsi menüsündeki Çıkış) ve
        /// Windows oturumunun kapanması uygulamayı sonlandırır. Diğer her kapatma isteği iptal
        /// edilip pencere tepsiye gizlenir; ilk seferde tepsiden bir bilgi balonu gösterilir.
        /// Her durumda sebep session.log'a yazılır - böylece bir sonraki "neden kapandı?" sorusu
        /// tahminle değil kayıtla cevaplanır.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isExiting || isSessionEnding)
            {
                if (isSessionEnding && !isExiting) LogSessionEvent("Windows oturumu kapanıyor - uygulama sonlandırılıyor");
                return;
            }

            e.Cancel = true;
            LogSessionEvent("Dışarıdan kapatma isteği (Alt+F4 / görev çubuğu / sistem) tepsiye yönlendirildi");
            Hide();

            if (!trayHintShown)
            {
                trayHintShown = true;
                try
                {
                    trayIcon?.ShowBalloonTip(4000, "AutoMotion", Loc.T("tray.still_running_hint"),
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                catch { /* balon gösterilemezse de uygulama tepside çalışmaya devam eder */ }
            }
        }

        /// <summary>Oturum olaylarını (çıkış sebebi vb.) %AppData%\AutoMotion\session.log'a tek satır
        /// olarak yazar. Dosya 256 KB'ı geçince sıfırlanır; yazılamaması uygulamayı etkilemez.</summary>
        private static void LogSessionEvent(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "session.log");

                var info = new System.IO.FileInfo(path);
                if (info.Exists && info.Length > 256 * 1024) System.IO.File.Delete(path);

                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
            catch { }
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
            RefreshCodeBuiltTexts();        // durum ipuçları açılışta da kaydedilmiş duruma göre yazılsın
            StartHealthLog();               // uzun koşu sağlık kaydı (session.log) - bkz. MainWindow.Health.cs

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
            ExitApplication("üst çubuktaki kapat düğmesi");
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
        private void ExitApplication(string reason = "bilinmeyen")
        {
            if (isExiting) return;
            isExiting = true;
            LogSessionEvent("Çıkış: " + reason);
            StopHealthLog();

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

        // Klavye Kısayollarını Aç/Kapat: bkz. HandleWindowKeyDown içindeki not - serbest yazma
        // gerektiren özelliklerle (Yazı Yarışı) tekil-tuş kısayolları çakışmasın diye eklendi.
        private bool shortcutsEnabled = true;

        private void ToggleShortcutsBtn_Click(object sender, RoutedEventArgs e)
        {
            shortcutsEnabled = !shortcutsEnabled;
            ShortcutsBtn.Opacity = shortcutsEnabled ? 1.0 : 0.4;
            ShortcutsBtn.ToolTip = Loc.T(shortcutsEnabled
                ? "ui.klavye_kisayollari_acik_yazarken_kapatmak_icin"
                : "ui.shortcuts_off_tooltip");
            ShowToast(Loc.T(shortcutsEnabled ? "msg.shortcuts_on" : "msg.shortcuts_off"));
            PlayUiClick();
        }

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

        /// <summary>Butonlara TIKLANINCA "tık" sesi verir, ama aynı buton bir klavye kısayoluyla
        /// (Window_KeyDown) tetiklendiğinde bu ses bastırılır - kullanıcı her tuşa bastığında ses
        /// çıkmasını istemedi. Fareyle gerçek bir butona tıklamanın sesli geri bildirimi korunuyor.</summary>
        private void PlayUiClick()
        {
            if (!suppressUiClickSound) soundEngine.PlayClick();
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
