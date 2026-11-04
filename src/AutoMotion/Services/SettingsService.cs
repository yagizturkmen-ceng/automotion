using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace AutoMotion.Services
{
    /// <summary>Bir panelin kaydedilmiş konumu/büyüklüğü/sabitleme durumu.
    /// (Eskiden MainWindow içinde özel bir iç sınıftı; artık ayar şemasının parçası.)</summary>
    public class PanelLayoutEntry
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Pinned { get; set; }

        /// <summary>Panelin kullanıcı tarafından ayarlanmış büyüklük çarpanı (bkz. PanelScale).
        /// 0 = kaydedilmemiş (eski düzen dosyaları) -> varsayılan 1.0 kullanılır.</summary>
        public double Scale { get; set; }
    }

    /// <summary>
    /// Uygulama genelindeki tüm kullanıcı tercihleri. Tek dosya (settings.json), tek şema.
    /// </summary>
    public class AppSettings
    {
        /// <summary>Ayar dosyası şema sürümü. Kayıt dosyalarındaki mantığın aynısı: alanlar
        /// ileride değişirse eski dosya sessizce varsayılana düşmesin, migrasyondan geçsin.
        /// 0 = sürümsüz/eski, 1 = ilk birleşik şema.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; }

        /// <summary>Seçilen dil kodu. null/boş = tercih yok, işletim sisteminin diline düşülür.</summary>
        public string Language { get; set; }

        /// <summary>İlk çalıştırma karşılama bildirimi gösterildi mi (eski onboarding_shown.txt).</summary>
        public bool OnboardingShown { get; set; }

        /// <summary>Ayarlar panelindeki kirlilik/yakıt tüketimi çarpanları. Bunlar bir oyun
        /// durumu değil, kullanıcı tercihi olduğu için artık burada tutulur - eskiden yalnızca
        /// kayıt dosyasının içinde yaşıyorlardı, dolayısıyla hiç kayıt yüklemeyen bir kullanıcıda
        /// her açılışta 1.0x'e dönüyorlardı.</summary>
        public double DirtMultiplier { get; set; } = 1.0;
        public double FuelMultiplier { get; set; } = 1.0;

        /// <summary>Seçili tema indeksi (ThemeEngine sırasına göre).</summary>
        public int ThemeIndex { get; set; }

        /// <summary>Ses tercihleri. Varsayılanlar XAML'deki kaydırıcı başlangıç değerleriyle aynı.</summary>
        public bool Muted { get; set; }
        public double MasterVolume { get; set; } = 0.8;
        public double SfxVolume { get; set; } = 0.85;

        /// <summary>Düşük efekt (performans) modu.</summary>
        public bool LowEffectsMode { get; set; }

        /// <summary>Otomatik kayıt aralığı (saniye). Ayarlar panelindeki listeyle aynı değerler.</summary>
        public int AutoSaveIntervalSeconds { get; set; } = 60;

        /// <summary>IoT/MQTT bağlantı alanları. PAROLA BİLEREK YOK: düz metin bir ayar dosyasında
        /// broker parolası tutmak, TLS desteği eklemenin amacını baştan bozardı - parola her
        /// oturumda yeniden girilir (bkz. Services/IoTTelemetryService.cs).</summary>
        public string IoTBrokerAddress { get; set; } = "";
        public string IoTTopic { get; set; } = "";
        public string IoTUsername { get; set; } = "";
        public bool IoTUseTls { get; set; }
        public bool IoTAllowUntrustedCertificate { get; set; }

        /// <summary>Panel adı → konum/büyüklük/sabitleme (eski panel_layout.json).</summary>
        public Dictionary<string, PanelLayoutEntry> PanelLayout { get; set; } = new();
    }

    /// <summary>
    /// Birleşik ayar dosyası (%AppData%\AutoMotion\settings.json).
    ///
    /// NEDEN: tercihler üç ayrı dosyaya (language.txt, panel_layout.json, onboarding_shown.txt) ve
    /// bir de kayıt dosyasının içine (kirlilik/yakıt çarpanları) dağılmıştı. Her biri kendi ad-hoc
    /// okuma/yazma koduyla geliyordu: kimi atomik değildi, kimi hatayı yutuyordu, hiçbirinin sürümü
    /// yoktu ve yeni bir tercih eklemek her seferinde yeni bir dosya demekti.
    ///
    /// Kurallar (SaveManager'daki dayanıklılık kurallarıyla aynı hat):
    /// 1. Yazma atomiktir (geçici dosya + File.Replace takası) - yarım yazılmış ayar dosyası olmaz.
    /// 2. Bozuk/okunamaz dosya uygulamayı çökertmez; varsayılanlara düşülür ve dosya .corrupt
    ///    uzantısıyla kenara alınır (sessizce üzerine yazıp kullanıcının düzenini kaybetmemek için).
    /// 3. İlk açılışta eski dağınık dosyalar okunup tek dosyaya taşınır, sonra silinir (migrasyon).
    /// 4. Sürükleme sırasında saniyede onlarca yazma olmasın diye SaveDeferred() gecikmeli/birleşik
    ///    yazar; kapanışta Flush() bekleyen yazmayı garantiler.
    /// </summary>
    public class SettingsService : IDisposable
    {
        /// <summary>Gecikmeli yazmanın bekleme süresi. Kaydırıcı sürüklenirken her tik için değil,
        /// sürükleme durduktan sonra tek bir yazma yapılır.</summary>
        private const int DeferredSaveDelayMs = 800;

        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());

        /// <summary>Uygulama genelindeki tek örnek. Testler yalıtılmış bir dizin için
        /// <see cref="SettingsService(string)"/> yapıcısını kullanır.</summary>
        public static SettingsService Instance => _instance.Value;

        private readonly string _directory;
        private readonly string _filePath;
        private readonly object _ioLock = new();
        private Timer _deferredSaveTimer;
        private bool _disposed;

        public SettingsService() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion"))
        {
        }

        /// <summary>Testlerin gerçek %AppData% klasörüne yazmadan izole bir dizin kullanabilmesi için.</summary>
        public SettingsService(string directory)
        {
            _directory = directory;
            _filePath = Path.Combine(_directory, "settings.json");
            Current = Load();
        }

        /// <summary>Geçerli ayarlar. Doğrudan değiştirilip ardından <see cref="Save"/> ya da
        /// <see cref="SaveDeferred"/> çağrılır.</summary>
        public AppSettings Current { get; private set; }

        /// <summary>Son yazma/okuma hatası (yoksa null). Kaydetme sessizce başarısız olduğunda
        /// çağıranın durumu görebilmesi için - eski ad-hoc kod hataları tamamen yutuyordu.</summary>
        public string LastError { get; private set; }

        /// <summary>Ayarların bu açılışta eski dağınık dosyalardan taşınıp taşınmadığı
        /// (yalnızca teşhis/test amaçlı).</summary>
        public bool MigratedFromLegacyFiles { get; private set; }

        public string FilePath => _filePath;

        // ---------------------------------------------------------------- okuma

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        Migrate(settings);
                        settings.PanelLayout ??= new Dictionary<string, PanelLayoutEntry>();
                        LastError = null;
                        return settings;
                    }

                    // "null" içeren geçerli ama anlamsız JSON: bozuk dosya muamelesi görür.
                    QuarantineCorruptFile();
                }
            }
            catch (Exception ex)
            {
                LastError = $"Ayarlar okunamadı: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                QuarantineCorruptFile();
            }

            // settings.json yok (ilk açılış ya da bu sürüme ilk geçiş): eski dosyalardan devral.
            var migrated = MigrateFromLegacyFiles();
            if (MigratedFromLegacyFiles)
            {
                // Taşınan tercihler ancak yeni dosyaya YAZILABİLDİYSE eskiler silinebilir.
                if (Write(migrated)) DeleteLegacyFiles();
            }
            return migrated;
        }

        /// <summary>Şema sürümü yükseldiğinde eski ayar dosyalarını dönüştürür. Şimdilik yalnızca
        /// damgayı günceller - ilk sürümlü şemada dönüştürülecek alan yok.</summary>
        private static void Migrate(AppSettings settings)
        {
            if (settings.SchemaVersion >= AppSettings.CurrentSchemaVersion) return;
            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        }

        // ---------------------------------------------------------------- eski dosyalardan taşıma

        private string LegacyLanguagePath => Path.Combine(_directory, "language.txt");
        private string LegacyOnboardingPath => Path.Combine(_directory, "onboarding_shown.txt");
        private string LegacyPanelLayoutPath => Path.Combine(_directory, "panel_layout.json");

        /// <summary>
        /// Eski dağınık tercih dosyalarını okuyup tek şemaya doldurur. Hiçbiri yoksa (temiz kurulum)
        /// varsayılanlar döner ve migrasyon bayrağı açılmaz.
        ///
        /// Kirlilik/yakıt çarpanları burada YOK: onlar kayıt dosyasının içinde yaşıyordu ve hangi
        /// kaydın "geçerli" olduğu ancak oyun yüklendiğinde belli olur. Kayıt yüklendiğinde
        /// MainWindow o değerleri ayarlara yazar (bkz. LoadSaveRow_Click) - böylece eski
        /// kayıtlardaki çarpanlar da kaybolmaz.
        /// </summary>
        private AppSettings MigrateFromLegacyFiles()
        {
            var settings = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion };

            try
            {
                if (File.Exists(LegacyLanguagePath))
                {
                    string code = File.ReadAllText(LegacyLanguagePath).Trim();
                    if (Loc.IsSupported(code)) settings.Language = code;
                    MigratedFromLegacyFiles = true;
                }
            }
            catch { /* okunamayan eski dosya: o tercih varsayılana düşer */ }

            try
            {
                if (File.Exists(LegacyOnboardingPath))
                {
                    settings.OnboardingShown = true;
                    MigratedFromLegacyFiles = true;
                }
            }
            catch { }

            try
            {
                if (File.Exists(LegacyPanelLayoutPath))
                {
                    string json = File.ReadAllText(LegacyPanelLayoutPath);
                    var layout = JsonSerializer.Deserialize<Dictionary<string, PanelLayoutEntry>>(json);
                    if (layout != null) settings.PanelLayout = layout;
                    MigratedFromLegacyFiles = true;
                }
            }
            catch { }

            return settings;
        }

        private void DeleteLegacyFiles()
        {
            TryDeleteFile(LegacyLanguagePath);
            TryDeleteFile(LegacyOnboardingPath);
            TryDeleteFile(LegacyPanelLayoutPath);
        }

        // ---------------------------------------------------------------- yazma

        /// <summary>Ayarları hemen diske yazar. Başarısızlıkta false döner ve LastError dolar.</summary>
        public bool Save()
        {
            CancelDeferredSave();
            return Write(Current);
        }

        /// <summary>Yazmayı kısa bir süre erteler ve aradaki tüm çağrıları tek yazmada birleştirir.
        /// Kaydırıcı sürükleme gibi saniyede onlarca değişiklik üreten yollarda kullanılır.</summary>
        public void SaveDeferred()
        {
            if (_disposed) return;

            lock (_ioLock)
            {
                if (_deferredSaveTimer == null)
                    _deferredSaveTimer = new Timer(_ => Write(Current), null, DeferredSaveDelayMs, Timeout.Infinite);
                else
                    _deferredSaveTimer.Change(DeferredSaveDelayMs, Timeout.Infinite);
            }
        }

        /// <summary>Bekleyen gecikmeli yazma varsa hemen uygular. Kapanış yolunda çağrılır -
        /// yoksa son saniyedeki ayar değişikliği süreçle birlikte uçardı.</summary>
        public void Flush() => Save();

        private void CancelDeferredSave()
        {
            lock (_ioLock)
            {
                _deferredSaveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private bool Write(AppSettings settings)
        {
            if (settings == null) return false;

            try
            {
                lock (_ioLock)
                {
                    settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

                    if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);

                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    WriteAllTextAtomic(_filePath, json);
                }

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Ayarlar yazılamadı: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
        }

        private static void WriteAllTextAtomic(string path, string contents)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);

            if (File.Exists(path))
            {
                // File.Replace NTFS üzerinde atomik takas yapar.
                File.Replace(tmp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        /// <summary>Bozuk ayar dosyasını silmek yerine kenara alır: kullanıcı düzenini elle
        /// kurtarabilir, biz de üzerine yazmadan temiz varsayılanlarla devam ederiz.</summary>
        private void QuarantineCorruptFile()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                string target = _filePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
                File.Move(_filePath, target);
            }
            catch { /* taşınamıyorsa da açılış engellenmemeli - üzerine yazılır */ }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { /* silinemezse zararsız: settings.json artık tek kaynak, eskiye bakılmıyor */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Timer timer;
            lock (_ioLock)
            {
                timer = _deferredSaveTimer;
                _deferredSaveTimer = null;
            }
            timer?.Dispose();
        }
    }
}
