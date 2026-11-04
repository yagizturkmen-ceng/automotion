using System;
using System.IO;
using AutoMotion.Models;
using AutoMotion.Services;

namespace AutoMotion.Tests;

// Not: SaveManager varsayılan yapıcıda gerçek %AppData%/AutoMotion/Saves klasörünü kullanır.
// Testler bunun yerine geçici, benzersiz bir klasör kullanan yapıcı overload'ı (SaveManager(string))
// üzerinden çalışır - kullanıcının gerçek kayıt dosyalarına dokunulmaz.
public class SaveManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SaveManager _saveManager;

    public SaveManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AutoMotionTests_" + Guid.NewGuid());
        _saveManager = new SaveManager(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static GameSaveData MakeSaveData(int vehicleCount = 1)
    {
        var core = new GameCore();
        for (int i = 0; i < vehicleCount; i++)
            core.AddAutoMotion(new Vehicle(i, i, "car"));
        return core.Save();
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsVehicleCount()
    {
        bool saved = _saveManager.Save("test-save", MakeSaveData(vehicleCount: 3));
        Assert.True(saved);

        var loaded = _saveManager.Load("test-save");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Vehicles.Count);
    }

    [Fact]
    public void Load_ReturnsNull_ForUnknownSaveName()
    {
        Assert.Null(_saveManager.Load("does-not-exist"));
    }

    [Fact]
    public void GetAllSaves_ListsAllSavedEntries()
    {
        _saveManager.Save("save-a", MakeSaveData());
        _saveManager.Save("save-b", MakeSaveData());

        var all = _saveManager.GetAllSaves();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.Name == "save-a");
        Assert.Contains(all, s => s.Name == "save-b");
    }

    [Fact]
    public void DeleteSave_RemovesEntryAndFile()
    {
        _saveManager.Save("to-delete", MakeSaveData());

        bool deleted = _saveManager.DeleteSave("to-delete");

        Assert.True(deleted);
        Assert.Null(_saveManager.Load("to-delete"));
        Assert.DoesNotContain(_saveManager.GetAllSaves(), s => s.Name == "to-delete");
    }

    [Fact]
    public void DeleteSave_ReturnsFalse_ForUnknownName()
    {
        Assert.False(_saveManager.DeleteSave("never-existed"));
    }

    [Fact]
    public void GetLatestSave_ReturnsMostRecentlyTimestampedSave()
    {
        _saveManager.Save("older", MakeSaveData());
        System.Threading.Thread.Sleep(20);
        _saveManager.Save("newer", MakeSaveData());

        var latest = _saveManager.GetLatestSave();

        Assert.NotNull(latest);
        Assert.Equal("newer", latest.Name);
    }

    [Fact]
    public void ClearAllSaves_RemovesEverySave()
    {
        _saveManager.Save("a", MakeSaveData());
        _saveManager.Save("b", MakeSaveData());

        bool cleared = _saveManager.ClearAllSaves();

        Assert.True(cleared);
        Assert.Empty(_saveManager.GetAllSaves());
    }

    // ------------------------------------------------------------------ dayanıklılık (regresyon)

    /// <summary>
    /// REGRESYON: Yeni bir SaveManager örneği (= uygulamanın yeniden başlatılması) kaydetme
    /// yaptığında ESKİ kayıtlar listede kalmalı.
    ///
    /// Eskiden kurucu indeksi hiç yüklemediği için _saveHistory boş başlıyor, ilk Save() çağrısı
    /// saves.json'ı tek kayıtla komple üzerine yazıyordu - önceki tüm kayıtlar (manuel kayıtlar
    /// dahil) listeden yok oluyordu. Oyun döngüsü 60 saniyede bir AutoSave çağırdığından bu, her
    /// oturumda tetiklenen gerçek bir veri kaybıydı.
    /// </summary>
    [Fact]
    public void Save_FromNewInstance_KeepsPreviouslySavedEntries()
    {
        _saveManager.Save("onceki-kayit", MakeSaveData(vehicleCount: 2));

        // Uygulamanın yeniden başlatılmasını temsil eder: aynı klasör, yepyeni örnek.
        var afterRestart = new SaveManager(_tempDir);
        afterRestart.Save("AutoSave_yeni", MakeSaveData(vehicleCount: 1));

        var all = afterRestart.GetAllSaves();
        Assert.Contains(all, s => s.Name == "onceki-kayit");
        Assert.Contains(all, s => s.Name == "AutoSave_yeni");

        var reloaded = afterRestart.Load("onceki-kayit");
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.Vehicles.Count);
    }

    [Fact]
    public void NewInstance_SeesSavesWrittenByPreviousInstance()
    {
        _saveManager.Save("kalici", MakeSaveData(vehicleCount: 4));

        var afterRestart = new SaveManager(_tempDir);

        var loaded = afterRestart.Load("kalici");
        Assert.NotNull(loaded);
        Assert.Equal(4, loaded.Vehicles.Count);
    }

    /// <summary>İndeks bozulursa kayıt dosyaları kaybolmamalı: bozuk indeks karantinaya alınır ve
    /// klasördeki .automotion dosyaları "Kurtarilan_..." adıyla geri kazandırılır.</summary>
    [Fact]
    public void CorruptIndex_RecoversSaveFilesFromDisk()
    {
        _saveManager.Save("kurtarilacak", MakeSaveData(vehicleCount: 5));

        File.WriteAllText(Path.Combine(_tempDir, "saves.json"), "{ bu gecerli json degil ]");

        var afterRestart = new SaveManager(_tempDir);
        var all = afterRestart.GetAllSaves();

        Assert.Single(all);
        Assert.StartsWith("Kurtarilan_", all[0].Name);
        Assert.Equal(5, all[0].VehicleCount);
    }

    /// <summary>Yetim kalmış (indekste görünmeyen) kayıt dosyaları silinmez, indekse geri alınır -
    /// eski sürümdeki indeks-ezilme hatasından etkilenmiş kullanıcılar kayıtlarını geri kazanır.</summary>
    [Fact]
    public void OrphanedSaveFile_IsRecoveredIntoIndex()
    {
        _saveManager.Save("yetim-olacak", MakeSaveData(vehicleCount: 2));

        // İndeksi tamamen sil - dosya diskte kalır, tıpkı eski hatadaki gibi.
        File.Delete(Path.Combine(_tempDir, "saves.json"));

        var afterRestart = new SaveManager(_tempDir);
        var all = afterRestart.GetAllSaves();

        Assert.Single(all);
        Assert.Equal(2, all[0].VehicleCount);
    }

    /// <summary>Çok sayıda yetim dosya varken (gerçek bir kurulumda 230'dan fazlası gözlendi) kayıt
    /// listesi boğulmamalı: en yeni 15 tanesi indekse alınır, geri kalanı SİLİNMEDEN "Arsiv" alt
    /// klasörüne taşınır.</summary>
    [Fact]
    public void ManyOrphans_RecoversNewestFew_AndArchivesTheRestWithoutDeleting()
    {
        for (int i = 0; i < 40; i++)
            _saveManager.Save($"kayit-{i:D2}", MakeSaveData(vehicleCount: 1));

        int totalFiles = Directory.GetFiles(_tempDir, "*.automotion").Length;
        Assert.Equal(40, totalFiles);

        // İndeksi sil - 40 dosyanın tamamı yetim kalır.
        File.Delete(Path.Combine(_tempDir, "saves.json"));

        var afterRestart = new SaveManager(_tempDir);
        var all = afterRestart.GetAllSaves();

        // Liste boğulmamalı.
        Assert.Equal(15, all.Count);
        Assert.All(all, s => Assert.StartsWith("Kurtarilan_", s.Name));

        // HİÇBİR dosya silinmemeli: 15'i kök klasörde, 25'i arşivde.
        int rootFiles = Directory.GetFiles(_tempDir, "*.automotion").Length;
        string archive = Path.Combine(_tempDir, "Arsiv");
        int archivedFiles = Directory.Exists(archive) ? Directory.GetFiles(archive, "*.automotion").Length : 0;

        Assert.Equal(15, rootFiles);
        Assert.Equal(25, archivedFiles);
        Assert.Equal(40, rootFiles + archivedFiles);
    }

    /// <summary>Arşivlenen dosyalar bir sonraki açılışta yeniden taranmamalı - aksi halde her
    /// açılışta yüzlerce JSON dosyası ayrıştırılıp kalıcı bir yavaşlama oluşurdu.</summary>
    [Fact]
    public void ArchivedOrphans_AreNotRescannedOnNextStart()
    {
        for (int i = 0; i < 25; i++)
            _saveManager.Save($"kayit-{i:D2}", MakeSaveData());
        File.Delete(Path.Combine(_tempDir, "saves.json"));

        _ = new SaveManager(_tempDir).GetAllSaves();   // 1. açılış: 15 kurtar, 10 arşivle
        var second = new SaveManager(_tempDir).GetAllSaves(); // 2. açılış

        Assert.Equal(15, second.Count); // arşivdekiler tekrar indekse sızmamalı
    }

    [Fact]
    public void Save_StampsCurrentSchemaVersion()
    {
        _saveManager.Save("surumlu", MakeSaveData());

        var loaded = _saveManager.Load("surumlu");

        Assert.NotNull(loaded);
        Assert.Equal(GameSaveData.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    /// <summary>Sürümleme öncesi (SchemaVersion alanı hiç olmayan) eski bir kayıt dosyası yüklenince
    /// migrasyondan geçip güncel sürüme damgalanmalı - sessizce yanlış yorumlanmamalı.</summary>
    [Fact]
    public void Load_MigratesLegacyUnversionedSave()
    {
        _saveManager.Save("eski", MakeSaveData(vehicleCount: 1));
        var entry = _saveManager.GetAllSaves().Find(s => s.Name == "eski");
        Assert.NotNull(entry);

        // SchemaVersion alanını dosyadan tamamen kaldır (sürümleme öncesi format).
        string json = File.ReadAllText(entry.FilePath);
        json = json.Replace($"\"SchemaVersion\": {GameSaveData.CurrentSchemaVersion},", "");
        File.WriteAllText(entry.FilePath, json);

        var loaded = _saveManager.Load("eski");

        Assert.NotNull(loaded);
        Assert.Equal(GameSaveData.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFilesBehind()
    {
        _saveManager.Save("atomik", MakeSaveData());

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void Load_SetsLastError_WhenSaveIsMissing()
    {
        Assert.Null(_saveManager.Load("olmayan-kayit"));
        Assert.False(string.IsNullOrEmpty(_saveManager.LastError));
    }
}
