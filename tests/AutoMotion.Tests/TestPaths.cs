using System;
using System.IO;

namespace AutoMotion.Tests;

/// <summary>
/// Testlerin kaynak dosyalara (dil dosyaları, csproj, .cs kaynakları) ulaşmak için kullandığı ortak yol.
///
/// İki klasör düzeni desteklenir:
///   • GitHub deposu:  &lt;kök&gt;/src/AutoMotion  +  &lt;kök&gt;/tests/AutoMotion.Tests
///   • Eski yerel düzen: &lt;kök&gt;/AutoMotion   +  &lt;kök&gt;/AutoMotion.Tests
///
/// Eskiden her test dosyası kendi kopyasında yalnızca "üst klasörlerden birinin içinde AutoMotion/
/// var mı" diye bakıyordu. GitHub düzeninde src/ bir ATA değil KARDEŞ klasör olduğu için yol hiç
/// bulunamıyor, bu testler CI'da kırılıyordu.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> appDir = new(FindAppDir);

    /// <summary>Uygulama projesinin klasörü (AutoMotion.csproj'un bulunduğu yer).</summary>
    public static string AppDir => appDir.Value;

    private static string FindAppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(dir.FullName, "AutoMotion"),
                Path.Combine(dir.FullName, "src", "AutoMotion")
            })
            {
                if (Directory.Exists(Path.Combine(candidate, "Assets", "Lang")))
                    return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "AutoMotion proje klasörü bulunamadı (ne <kök>/AutoMotion ne <kök>/src/AutoMotion).");
    }
}
