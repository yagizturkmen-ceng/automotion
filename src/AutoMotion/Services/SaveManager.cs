using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoMotion.Models;

namespace AutoMotion.Services
{
    /// <summary>
    /// Oyun kaydetme/yükleme sistemi.
    ///
    /// Dayanıklılık kuralları (hepsi gerçek veri kaybı vakalarından doğdu, bkz. sınıf içi notlar):
    /// 1. İndeks (saves.json) HER ZAMAN diskten okunmuş olmalı - yoksa üzerine yazmak eski kayıtları
    ///    görünmez kılar.
    /// 2. Her dosya yazımı atomiktir (geçici dosya + takas) - yarım yazılmış kayıt dosyası olmaz.
    /// 3. İndeks bozulursa/kaybolursa klasördeki kayıt dosyalarından yeniden inşa edilir.
    /// 4. Kayıt verisi sürümlüdür (GameSaveData.SchemaVersion) - ileride alan değişse bile eski
    ///    kayıtlar migrasyondan geçirilir, sessizce sıfırlanmaz.
    /// </summary>
    public class SaveManager
    {
        private readonly string _savesDirectory;
        private readonly string _savesFile;
        private List<SaveInfo> _saveHistory = new();
        private DateTime _lastAutoSave = DateTime.Now;

        /// <summary>İndeksin diskten en az bir kez okunduğunu işaretler.
        ///
        /// KRİTİK: Eskiden bu bayrak yoktu ve kurucu indeksi hiç yüklemiyordu. Oyun döngüsü 60
        /// saniyede bir AutoSave çağırdığından, kullanıcı "Kayıt Listesi"ni hiç açmadıysa ilk otomatik
        /// kayıt saves.json'ı TEK bir kayıtla komple üzerine yazıyor, daha önceki tüm kayıtlar
        /// (manuel kayıtlar dahil) listeden yok oluyordu - dosyalar diskte duruyor ama hiçbir
        /// arayüzden erişilemiyordu. Artık yazma yapan her yol önce EnsureHistoryLoaded() çağırıyor.</summary>
        private bool _historyLoaded;

        /// <summary>Son başarısız işlemin sebebi - arayüz kullanıcıya gerçek hatayı gösterebilsin diye.
        /// Eskiden tüm hatalar boş catch bloklarında yutuluyor, kullanıcı kaydettiğini sanıp
        /// kaybediyordu.</summary>
        public string LastError { get; private set; }

        public SaveManager() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion", "Saves"))
        {
        }

        /// <summary>Testlerin gerçek %AppData% klasörüne yazmadan izole bir dizin kullanabilmesi için.</summary>
        public SaveManager(string savesDirectory)
        {
            _savesDirectory = savesDirectory;
            _savesFile = Path.Combine(_savesDirectory, "saves.json");

            if (!Directory.Exists(_savesDirectory))
                Directory.CreateDirectory(_savesDirectory);

            // İndeks AÇILIŞTA yüklenir - tembel yüklemeye bırakılmaz.
            //
            // Neden: indeks bakımı (bozuk indeksi karantinaya alma, yetim dosyaları kurtarma/arşivleme)
            // yalnızca LoadHistory çalıştığında yapılır. Tembel yüklemede bu, ilk Save/Load çağrısına
            // kadar ertelenirdi - ve oyun döngüsü sahne boşken askıya alındığından (bkz.
            // MainWindow.UpdateRenderLoopState) otomatik kayıt da çalışmayacağı için, hiç araç
            // eklemeyen bir kullanıcıda bakım HİÇ çalışmayabilirdi. Açılışta yüklemek davranışı
            // deterministik kılar: uygulama her başladığında kayıt klasörü tutarlı hale gelir.
            EnsureHistoryLoaded();
        }

        // ---------------------------------------------------------------- yazma / okuma

        public bool Save(string name, GameSaveData gameData)
        {
            if (gameData == null)
            {
                LastError = "Kaydedilecek oyun verisi yok.";
                return false;
            }

            EnsureHistoryLoaded();

            try
            {
                gameData.SchemaVersion = GameSaveData.CurrentSchemaVersion;

                var saveInfo = new SaveInfo
                {
                    Name = name,
                    FilePath = Path.Combine(_savesDirectory, $"{Guid.NewGuid()}.automotion"),
                    Timestamp = DateTime.Now,
                    PlayTime = gameData.GameData?.PlayTime ?? 0,
                    VehicleCount = gameData.Vehicles?.Count ?? 0
                };

                string json = JsonSerializer.Serialize(gameData, new JsonSerializerOptions { WriteIndented = true });
                WriteAllTextAtomic(saveInfo.FilePath, json);

                // Aynı isimde bir kayıt varsa (ör. kapanıştaki sabit "AutoSave" adı) indekste
                // ikizlenmesin - eski girdi ve dosyası temizlenip yenisiyle değiştirilir.
                var existing = _saveHistory.Where(s => s.Name == name).ToList();
                foreach (var old in existing)
                {
                    TryDeleteFile(old.FilePath);
                    _saveHistory.Remove(old);
                }

                _saveHistory.Add(saveInfo);

                if (!SaveHistory())
                {
                    // İndeks yazılamadıysa kayıt dosyası yetim kalır - bir dahaki açılışta
                    // RebuildHistoryFromDisk onu geri toplar, ama kullanıcıya yine de haber verilmeli.
                    return false;
                }

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Kaydetme hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
        }

        public GameSaveData Load(string saveName)
        {
            EnsureHistoryLoaded();

            try
            {
                var saveInfo = _saveHistory.Find(s => s.Name == saveName);
                if (saveInfo == null)
                {
                    LastError = $"'{saveName}' adlı kayıt bulunamadı.";
                    return null;
                }
                if (!File.Exists(saveInfo.FilePath))
                {
                    LastError = $"'{saveName}' kaydının dosyası diskte yok (silinmiş olabilir).";
                    return null;
                }

                string json = File.ReadAllText(saveInfo.FilePath);
                var data = JsonSerializer.Deserialize<GameSaveData>(json);
                if (data == null)
                {
                    LastError = $"'{saveName}' kaydı okunamadı (dosya boş/bozuk).";
                    return null;
                }

                MigrateIfNeeded(data);
                LastError = null;
                return data;
            }
            catch (JsonException ex)
            {
                LastError = $"'{saveName}' kaydı bozuk: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return null;
            }
            catch (Exception ex)
            {
                LastError = $"Yükleme hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return null;
            }
        }

        /// <summary>Kayıt şeması sürüm geçişleri. Sürümsüz (SchemaVersion=0) dosyalar, alan adlarının
        /// hiç değişmediği ilk sürümden gelir - bugün içerik dönüşümü gerekmiyor, sadece sürüm
        /// damgalanıyor. İleride bir alan yeniden adlandırıldığında dönüşüm buraya eklenir; böylece
        /// eski kayıtlar sessizce varsayılana düşmek yerine doğru şekilde taşınır.</summary>
        private static void MigrateIfNeeded(GameSaveData data)
        {
            if (data.SchemaVersion >= GameSaveData.CurrentSchemaVersion) return;

            // v0 -> v1: yapısal değişiklik yok (sürümleme bu noktada eklendi).
            data.SchemaVersion = GameSaveData.CurrentSchemaVersion;
        }

        // ---------------------------------------------------------------- liste / silme

        public List<SaveInfo> GetAllSaves()
        {
            EnsureHistoryLoaded();
            return _saveHistory;
        }

        public bool DeleteSave(string saveName)
        {
            EnsureHistoryLoaded();

            try
            {
                var saveInfo = _saveHistory.Find(s => s.Name == saveName);
                if (saveInfo == null) return false;

                TryDeleteFile(saveInfo.FilePath);
                _saveHistory.Remove(saveInfo);
                SaveHistory();
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Silme hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
        }

        public bool ClearAllSaves()
        {
            EnsureHistoryLoaded();

            try
            {
                foreach (var save in _saveHistory)
                    TryDeleteFile(save.FilePath);

                _saveHistory.Clear();
                SaveHistory();
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Temizleme hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
        }

        public SaveInfo GetLatestSave()
        {
            EnsureHistoryLoaded();
            if (_saveHistory.Count == 0) return null;

            SaveInfo latest = _saveHistory[0];
            foreach (var save in _saveHistory)
            {
                if (save.Timestamp > latest.Timestamp)
                    latest = save;
            }
            return latest;
        }

        // ---------------------------------------------------------------- otomatik kayıt

        private const int MaxAutoSaves = 5;

        /// <summary>Otomatik kayıt aralığı (saniye) - kullanıcı arayüzden değiştirebilir, varsayılan 60sn.</summary>
        public int AutoSaveIntervalSeconds { get; set; } = 60;

        public void AutoSave(GameCore gameCore)
        {
            if ((DateTime.Now - _lastAutoSave).TotalSeconds >= AutoSaveIntervalSeconds)
            {
                _lastAutoSave = DateTime.Now;
                Save($"AutoSave_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", gameCore.Save());
                PruneOldAutoSaves();
            }
        }

        private void PruneOldAutoSaves()
        {
            try
            {
                EnsureHistoryLoaded();

                var autoSaves = _saveHistory.FindAll(s => s.Name != null && s.Name.StartsWith("AutoSave_"));
                autoSaves.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

                bool removedAny = false;
                for (int i = MaxAutoSaves; i < autoSaves.Count; i++)
                {
                    TryDeleteFile(autoSaves[i].FilePath);
                    _saveHistory.Remove(autoSaves[i]);
                    removedAny = true;
                }

                if (removedAny) SaveHistory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Otomatik kayıt budama hatası: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- indeks kalıcılığı

        private bool SaveHistory()
        {
            try
            {
                string json = JsonSerializer.Serialize(_saveHistory, new JsonSerializerOptions { WriteIndented = true });
                WriteAllTextAtomic(_savesFile, json);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Kayıt listesi yazılamadı: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
        }

        private void EnsureHistoryLoaded()
        {
            if (_historyLoaded) return;
            LoadHistory();
        }

        private void LoadHistory()
        {
            _historyLoaded = true;

            if (!File.Exists(_savesFile))
            {
                // İndeks hiç yok: klasörde kayıt dosyası varsa (ör. eski sürümdeki indeks ezilme
                // hatasından yetim kalmış kayıtlar) onları geri topla.
                _saveHistory = new List<SaveInfo>();
                RecoverOrphanedSaveFiles();
                return;
            }

            try
            {
                string json = File.ReadAllText(_savesFile);
                _saveHistory = JsonSerializer.Deserialize<List<SaveInfo>>(json) ?? new List<SaveInfo>();
            }
            catch (Exception ex)
            {
                // İndeks bozuk: silme, bir kenara kaldır (adli kopya) ve diskten yeniden inşa et.
                System.Diagnostics.Debug.WriteLine($"Kayıt listesi bozuk, yeniden inşa ediliyor: {ex.Message}");
                QuarantineCorruptIndex();
                _saveHistory = new List<SaveInfo>();
            }

            RecoverOrphanedSaveFiles();
        }

        private void QuarantineCorruptIndex()
        {
            try
            {
                string quarantine = Path.Combine(_savesDirectory,
                    $"saves.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(_savesFile, quarantine, overwrite: true);
            }
            catch { /* karantina başarısız olsa da yeniden inşa devam etmeli */ }
        }

        /// <summary>Kurtarma sırasında indekse geri alınacak azami yetim kayıt sayısı.</summary>
        private const int MaxRecoveredOrphans = 15;

        /// <summary>Kurtarma kapsamı dışında kalan eski yetim dosyaların taşındığı klasör adı.</summary>
        private const string ArchiveFolderName = "Arsiv";

        /// <summary>
        /// Klasörde duran ama indekste görünmeyen .automotion dosyalarını ele alır. Eski sürümdeki
        /// indeks-ezilme hatasından (bkz. _historyLoaded notu) etkilenmiş kullanıcılarda bu dosyalar
        /// yüzlerce adede ulaşabiliyor - gerçek bir kurulumda 230'dan fazlası gözlendi.
        ///
        /// Politika: HİÇBİR DOSYA SİLİNMEZ.
        ///  • En yeni <see cref="MaxRecoveredOrphans"/> tanesi "Kurtarilan_..." adıyla indekse alınır -
        ///    kullanıcı kayıtlarına arayüzden yeniden erişebilir.
        ///  • Daha eskiler <see cref="ArchiveFolderName"/> alt klasörüne TAŞINIR: veri korunur ama
        ///    (a) kayıt listesi yüzlerce girdiyle boğulmaz, (b) her açılışta yüzlerce JSON dosyası
        ///    yeniden ayrıştırılmaz (aksi halde kalıcı bir açılış yavaşlaması olurdu).
        /// </summary>
        private void RecoverOrphanedSaveFiles()
        {
            try
            {
                var known = new HashSet<string>(
                    _saveHistory.Where(s => !string.IsNullOrEmpty(s.FilePath))
                                .Select(s => Path.GetFullPath(s.FilePath)),
                    StringComparer.OrdinalIgnoreCase);

                var orphans = Directory.GetFiles(_savesDirectory, "*.automotion")
                                       .Where(f => !known.Contains(Path.GetFullPath(f)))
                                       .OrderByDescending(File.GetLastWriteTime)
                                       .ToList();
                if (orphans.Count == 0) return;

                bool recoveredAny = false;

                foreach (var file in orphans.Take(MaxRecoveredOrphans))
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<GameSaveData>(File.ReadAllText(file));
                        if (data == null) continue;

                        var stamp = data.Timestamp != default ? data.Timestamp : File.GetLastWriteTime(file);
                        _saveHistory.Add(new SaveInfo
                        {
                            Name = $"Kurtarilan_{stamp:yyyy-MM-dd_HH-mm-ss}_{Path.GetFileNameWithoutExtension(file)[..4]}",
                            FilePath = file,
                            Timestamp = stamp,
                            PlayTime = data.GameData?.PlayTime ?? 0,
                            VehicleCount = data.Vehicles?.Count ?? 0
                        });
                        recoveredAny = true;
                    }
                    catch { /* gerçekten bozuk bir dosya - indekse alma, diskte dursun */ }
                }

                ArchiveRemainingOrphans(orphans.Skip(MaxRecoveredOrphans));

                if (recoveredAny) SaveHistory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Yetim kayıt taraması başarısız: {ex.Message}");
            }
        }

        private void ArchiveRemainingOrphans(IEnumerable<string> files)
        {
            string archiveDir = Path.Combine(_savesDirectory, ArchiveFolderName);

            foreach (var file in files)
            {
                try
                {
                    Directory.CreateDirectory(archiveDir);
                    string target = Path.Combine(archiveDir, Path.GetFileName(file));
                    if (File.Exists(target)) continue; // aynı adlı arşiv varsa dokunma (GUID adlar zaten benzersiz)
                    File.Move(file, target);
                }
                catch { /* taşınamayan dosya yerinde kalır - bir sonraki açılışta yeniden denenir */ }
            }
        }

        // ---------------------------------------------------------------- dosya yardımcıları

        /// <summary>Atomik metin yazımı: önce geçici dosyaya yazılır, sonra hedefle takas edilir.
        /// Böylece yazma sırasında elektrik kesintisi/zorla kapanma olursa hedef dosya ya eski
        /// (tutarlı) halinde kalır ya da yeni (tam) haline geçer - asla yarım kalmaz.</summary>
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

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { /* dosya kilitliyse indeksten düşmesi yine de doğru davranış */ }
        }
    }

    public class SaveInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DateTime Timestamp { get; set; }
        public int PlayTime { get; set; }
        public int VehicleCount { get; set; }
    }
}
