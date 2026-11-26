using System;
using System.IO;
using System.Text;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Kayıt ve ayar dosyaları için ortak, çökmeye dayanıklı dosya yazımı.
    ///
    /// Neden ayrı: aynı "geçici dosya + takas" kodu SaveManager ve SettingsService içinde birebir
    /// kopyalanmıştı ve ikisinde de aynı eksik vardı: geçici dosya <c>File.WriteAllText</c> ile
    /// yazılıp HEMEN takas ediliyordu. WriteAllText veriyi işletim sisteminin önbelleğine bırakır,
    /// diske indiğini garanti etmez. NTFS dosya ADI/meta veri değişikliklerini günlükler ama içeriği
    /// günlüklemez; bu yüzden takastan hemen sonra elektrik kesilirse hedef dosya yeni adla ama
    /// BOŞ (0 bayt) içerikle kalabilir - yani "asla yarım kalmaz" vaadi tam da korunmak istenen
    /// senaryoda bozuluyordu. Burada içerik takastan ÖNCE <c>Flush(flushToDisk: true)</c> ile
    /// fiziksel diske zorlanır.
    /// </summary>
    public static class AtomicFile
    {
        // File.WriteAllText'in varsayılanıyla aynı: BOM'suz UTF-8 (mevcut dosyalarla birebir uyumlu).
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Metni atomik olarak yazar: hedef dosya ya eski (tutarlı) hâlinde kalır ya da yeni (tam)
        /// hâline geçer - arada yarım/boş bir hâl görülmez.
        /// </summary>
        public static void WriteAllText(string path, string contents)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Dosya yolu boş olamaz.", nameof(path));

            string tmp = path + ".tmp";

            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Utf8NoBom.GetBytes(contents ?? string.Empty);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

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

        /// <summary>Dosyayı varsa siler; kilitli/erişilemez olması çağıranı durdurmaz.</summary>
        public static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Silinemeyen dosya zararsız: çağıranlar bu dosyaya artık bakmıyor.
            }
        }
    }
}
