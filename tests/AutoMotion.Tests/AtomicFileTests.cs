using System;
using System.IO;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Kayıt ve ayar dosyalarının ortak yazım yolu. Davranış sözleşmesi: hedef her zaman tam içerikle
/// oluşur/güncellenir, geride .tmp artığı kalmaz ve kodlama eski File.WriteAllText ile birebir
/// aynıdır (BOM'suz UTF-8) - aksi halde mevcut kullanıcı kayıtlarıyla uyumsuzluk doğardı.
/// </summary>
public class AtomicFileTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "AutoMotion.AtomicFileTests." + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    [Fact]
    public void WriteAllText_CreatesNewFile_WithoutLeftoverTemp()
    {
        string path = Path.Combine(dir, "a.json");

        AtomicFile.WriteAllText(path, "{\"x\":1}");

        Assert.Equal("{\"x\":1}", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_ReplacesExistingFile()
    {
        string path = Path.Combine(dir, "b.json");
        File.WriteAllText(path, "eski içerik - uzun uzun uzun uzun");

        AtomicFile.WriteAllText(path, "yeni");

        Assert.Equal("yeni", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_UsesUtf8WithoutBom_LikeFileWriteAllText()
    {
        string path = Path.Combine(dir, "c.json");

        AtomicFile.WriteAllText(path, "ğüşİöç");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("ğüşİöç", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllText_OverwritesStaleTempFromEarlierCrash()
    {
        string path = Path.Combine(dir, "d.json");
        File.WriteAllText(path + ".tmp", "önceki çökmeden kalan yarım dosya");

        AtomicFile.WriteAllText(path, "tam");

        Assert.Equal("tam", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void TryDelete_IsSilentForMissingOrEmptyPath()
    {
        var ex = Record.Exception(() =>
        {
            AtomicFile.TryDelete(Path.Combine(dir, "yok.json"));
            AtomicFile.TryDelete("");
            AtomicFile.TryDelete(null);
        });
        Assert.Null(ex);
    }
}
