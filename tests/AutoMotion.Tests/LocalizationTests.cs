using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMotion.Models;
using AutoMotion.Services;

namespace AutoMotion.Tests;

/// <summary>
/// Yerelleştirme altyapısı ve dil dosyalarının bütünlüğü.
///
/// En kritik testler dosya bütünlüğüyle ilgili: bir çeviri anahtarı eksikse ya da XAML bir anahtarı
/// sözlükte olmadan çağırıyorsa, kullanıcı arayüzde ham anahtar metni ("ui.arac_ekle_r") görür.
/// Bu, derleme hatası vermeyen ama Steam'e giden bir kalite hatası olurdu - o yüzden test edilir.
/// </summary>
public class LocalizationTests
{
    private static string ProjectDir => TestPaths.AppDir;

    private static Dictionary<string, string> LoadLang(string code)
    {
        string path = Path.Combine(ProjectDir, "Assets", "Lang", code + ".json");
        Assert.True(File.Exists(path), "Dil dosyası bulunamadı: " + path);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(parsed);
        return parsed;
    }

    // ------------------------------------------------------------------ dosya bütünlüğü

    [Fact]
    public void EveryLanguage_HasExactlyTheSameKeys()
    {
        // Referans İngilizce (yedek dil). Desteklenen her dil - yeni eklenenler dahil, listeye
        // eklendiği anda - aynı anahtar setine sahip olmalı.
        var en = LoadLang("en");

        foreach (var (code, _) in Loc.SupportedLanguages)
        {
            var lang = LoadLang(code);
            var missing = en.Keys.Except(lang.Keys).OrderBy(k => k).ToList();
            var extra = lang.Keys.Except(en.Keys).OrderBy(k => k).ToList();

            Assert.True(missing.Count == 0, code + ".json'da eksik: " + string.Join(", ", missing));
            Assert.True(extra.Count == 0, code + ".json'da fazla: " + string.Join(", ", extra));
        }
    }

    [Fact]
    public void NoTranslationValue_IsEmpty()
    {
        foreach (var (code, _) in Loc.SupportedLanguages)
            foreach (var pair in LoadLang(code))
                Assert.False(string.IsNullOrWhiteSpace(pair.Value),
                    code + ".json içindeki '" + pair.Key + "' boş.");
    }

    /// <summary>Çeviri değerleri XML kaçış dizisi içermemeli - bunlar XAML'den çıkarılırken
    /// çözülmeliydi; kalırsa kullanıcı ekranda ham kaçış dizisini görür.</summary>
    [Fact]
    public void NoTranslationValue_ContainsRawXmlEntities()
    {
        string[] entities = { "&amp;", "&lt;", "&gt;", "&quot;" };

        foreach (var (code, _) in Loc.SupportedLanguages)
            foreach (var pair in LoadLang(code))
                foreach (var entity in entities)
                    Assert.False(pair.Value.Contains(entity),
                        code + ".json '" + pair.Key + "' hâlâ XML kaçış dizisi içeriyor: " + pair.Value);
    }

    /// <summary>Türkçe ve İngilizce dizelerdeki biçim yer tutucuları aynı olmalı - eksik/fazla
    /// yer tutucu çalışma zamanında FormatException'a yol açar.</summary>
    [Fact]
    public void FormatPlaceholders_MatchAcrossLanguages()
    {
        var en = LoadLang("en");
        var rx = new Regex(@"\{(\d+)\}");

        foreach (var (code, _) in Loc.SupportedLanguages)
        {
            foreach (var pair in LoadLang(code))
            {
                var slots = rx.Matches(pair.Value).Select(m => m.Value).OrderBy(s => s).ToList();
                var enSlots = rx.Matches(en[pair.Key]).Select(m => m.Value).OrderBy(s => s).ToList();
                Assert.True(slots.SequenceEqual(enSlots),
                    "'" + pair.Key + "' yer tutucuları uyuşmuyor: " + code + "=" + string.Join(",", slots)
                    + " en=" + string.Join(",", enSlots));
            }
        }
    }

    // ------------------------------------------------------------------ XAML ↔ sözlük tutarlılığı

    /// <summary>REGRESYON: XAML'de çevirisi istenen HER anahtar sözlükte olmalı. Aksi halde
    /// arayüzde çevirinin yerine ham anahtar metni görünür.</summary>
    [Fact]
    public void EveryKeyUsedInXaml_ExistsInBothLanguages()
    {
        var tr = LoadLang("tr");
        var en = LoadLang("en");
        var rx = new Regex(@"\{loc:Translate\s+([^\}\s]+)\s*\}");

        string objDir = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        string binDir = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;

        var xamlFiles = Directory.GetFiles(ProjectDir, "*.xaml", SearchOption.AllDirectories)
                                 .Where(f => !f.Contains(objDir) && !f.Contains(binDir))
                                 .ToList();
        Assert.NotEmpty(xamlFiles);

        var used = new HashSet<string>();
        foreach (var file in xamlFiles)
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                used.Add(m.Groups[1].Value);

        Assert.NotEmpty(used);

        var missingTr = used.Where(k => !tr.ContainsKey(k)).OrderBy(k => k).ToList();
        var missingEn = used.Where(k => !en.ContainsKey(k)).OrderBy(k => k).ToList();

        Assert.True(missingTr.Count == 0, "tr.json'da eksik XAML anahtarları: " + string.Join(", ", missingTr));
        Assert.True(missingEn.Count == 0, "en.json'da eksik XAML anahtarları: " + string.Join(", ", missingEn));
    }

    /// <summary>
    /// REGRESYON KORUMASI: kullanıcıya görünen hiçbir metin C# kodunda SABİT Türkçe olarak
    /// kalmamalı - hepsi Loc.T üzerinden çözümlenmeli.
    ///
    /// Bu testin değeri, yeni yazılan kodun sessizce Türkçe sabit metin eklemesini engellemesi:
    /// derleme hatası vermez, testler geçer, ama İngilizce oynayan bir Steam oyuncusu araya
    /// karışmış Türkçe bir bildirim görür. Tarama yalnızca kullanıcıya görünen çağrı/atama
    /// kalıplarına bakar (bildirim, diyalog, kontrol metni, tepsi menüsü); yorumlar ve iç
    /// mantık dizeleri kapsam dışıdır.
    ///
    /// Kapsam genişletildi: eski desen yalnızca <c>x.ToolTip = "..."</c> gibi TEK satırlık
    /// atamaları yakalıyordu; şu kalıplar gözden kaçıyor ve kodda ~30 sabit Türkçe metin birikmişti:
    /// nesne başlatıcıları (<c>new MenuItem { Header = "..." }</c>), koşul ifadesinin alt satıra
    /// taştığı atamalar (<c>? "..." : "..."</c>), switch kolları (<c>true => "..."</c>), birden çok
    /// satıra yayılan çağrı argümanları ve dialog Title/Filter alanları. Bilerek kullanıcıya
    /// gösterilmeyen bir dize (ör. yalnızca log'a yazılan) satır sonuna <c>// loc:ignore</c> ile
    /// işaretlenebilir.
    /// </summary>
    [Fact]
    public void NoUserFacingTurkishStringsRemain_HardcodedInCode()
    {
        const string turkishOnly = "çğıöşüÇĞİÖŞÜ";
        var userFacingCall = new Regex(@"\b(ShowToast|ShowDialogAsync|MessageBox\.Show)\(");
        var userFacing = new Regex(
            @"\b(Text|Content|ToolTip|Header|Title|Filter|StatusLabel)\s*=>?\s*"   // atama / başlatıcı / ifade gövdesi
            + @"|^[?:]\s*\$?"""                                                     // alt satıra taşan koşul kolu
            + @"|=>\s*\$?""");                                                      // switch kolu
        var stringLiteral = new Regex(@"""(?:[^""\\]|\\.)*""");

        string objDir = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        string binDir = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        string scratchDir = Path.DirectorySeparatorChar + "scratch" + Path.DirectorySeparatorChar;

        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(ProjectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(objDir) || file.Contains(binDir) || file.Contains(scratchDir)) continue;

            var lines = File.ReadAllLines(file);
            bool inBlockComment = false;
            int openCallDepth = 0; // çok satırlı ShowToast(/ShowDialogAsync( argümanlarını izlemek için

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (inBlockComment)
                {
                    int close = line.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0) continue;
                    line = line.Substring(close + 2);
                    inBlockComment = false;
                }

                int open = line.IndexOf("/*", StringComparison.Ordinal);
                if (open >= 0)
                {
                    if (line.IndexOf("*/", open, StringComparison.Ordinal) < 0) inBlockComment = true;
                    line = line.Substring(0, open);
                }

                if (line.Contains("loc:ignore", StringComparison.Ordinal)) continue;
                line = StripLineComment(line).Trim();
                if (line.Length == 0 || line.StartsWith("///", StringComparison.Ordinal)) continue;

                bool continuesCall = openCallDepth > 0;
                bool startsCall = userFacingCall.IsMatch(line);
                if (continuesCall || startsCall)
                {
                    string noStrings = stringLiteral.Replace(line, "");
                    openCallDepth += noStrings.Count(c => c == '(') - noStrings.Count(c => c == ')');
                    if (openCallDepth < 0) openCallDepth = 0;
                }

                if (!continuesCall && !startsCall && !userFacing.IsMatch(line)) continue;

                foreach (Match m in stringLiteral.Matches(line))
                {
                    string body = m.Value.Trim('"');
                    if (body.Any(ch => turkishOnly.IndexOf(ch) >= 0))
                        offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + body);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Kodda sabit Türkçe kullanıcı metni kaldı (Loc.T kullanılmalı):\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Satır sonu // yorumunu atar; dize içindeki // korunur.</summary>
    private static string StripLineComment(string line)
    {
        bool inString = false, escaped = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') escaped = true;
            else if (ch == '"') inString = !inString;
            else if (ch == '/' && !inString && i + 1 < line.Length && line[i + 1] == '/')
                return line.Substring(0, i);
        }
        return line;
    }

    /// <summary>İngilizce dosyada Türkçeye özgü karakter kalmışsa o satır çevrilmemiş demektir.</summary>
    [Fact]
    public void EnglishFile_HasNoUntranslatedTurkishCharacters()
    {
        const string turkishOnly = "çğıöşüÇĞİÖŞÜ";

        var leftovers = LoadLang("en")
            .Where(kv => kv.Value.Any(ch => turkishOnly.IndexOf(ch) >= 0))
            .Select(kv => kv.Key + " = " + kv.Value)
            .ToList();

        Assert.True(leftovers.Count == 0, "Çevrilmemiş görünen satırlar: " + string.Join(" | ", leftovers));
    }

    // ------------------------------------------------------------------ çözümleme davranışı

    [Fact]
    public void T_FallsBackToFallbackLanguage_ThenToKeyItself()
    {
        Loc.Instance.LoadForTest("tr",
            current: new Dictionary<string, string> { ["var.olan"] = "Türkçe değer" },
            fallback: new Dictionary<string, string>
            {
                ["var.olan"] = "English value",
                ["sadece.en"] = "Only English"
            });

        Assert.Equal("Türkçe değer", Loc.T("var.olan"));
        Assert.Equal("Only English", Loc.T("sadece.en"));   // geçerli dilde yok -> yedeğe düşer
        Assert.Equal("hic.yok", Loc.T("hic.yok"));          // hiçbirinde yok -> anahtarın kendisi
        Assert.Equal("", Loc.T(""));
    }

    [Fact]
    public void T_WithArguments_FormatsPlaceholders()
    {
        Loc.Instance.LoadForTest("tr",
            new Dictionary<string, string> { ["selam"] = "Merhaba {0}, {1} aracın var" }, null);

        Assert.Equal("Merhaba Yağız, 3 aracın var", Loc.T("selam", "Yağız", 3));
    }

    /// <summary>Bozuk bir çeviri (ör. çevirmen yer tutucuyu silmiş ama kod argüman geçiyor)
    /// uygulamayı çökertmemeli - biçimlendirilmemiş dize döner.</summary>
    [Fact]
    public void T_WithBrokenFormatString_DoesNotThrow()
    {
        Loc.Instance.LoadForTest("tr",
            new Dictionary<string, string> { ["bozuk"] = "Eksik yer tutucu {" }, null);

        var ex = Record.Exception(() => Loc.T("bozuk", "x"));
        Assert.Null(ex);
    }

    [Fact]
    public void Indexer_ReturnsSameResultAsT()
    {
        Loc.Instance.LoadForTest("tr", new Dictionary<string, string> { ["a.b"] = "Değer" }, null);

        Assert.Equal(Loc.T("a.b"), Loc.Instance["a.b"]);
    }

    [Fact]
    public void SetLanguage_RaisesPropertyChanged_SoBindingsRefresh()
    {
        bool raised = false;
        System.ComponentModel.PropertyChangedEventHandler handler = (s, e) => raised = true;

        Loc.Instance.PropertyChanged += handler;
        try { Loc.Instance.LoadForTest("en", new Dictionary<string, string>(), null); }
        finally { Loc.Instance.PropertyChanged -= handler; }

        Assert.True(raised);
    }

    [Theory]
    [InlineData("tr", true)]
    [InlineData("en", true)]
    [InlineData("EN", true)]
    [InlineData("de", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    public void IsSupported_RecognisesOnlyShippedLanguages(string code, bool expected)
    {
        Assert.Equal(expected, Loc.IsSupported(code));
    }

    [Fact]
    public void IsSupported_ReturnsFalseForNull()
    {
        Assert.False(Loc.IsSupported(null));
    }

    // ------------------------------------------------------------------ model katmanı adları
    // Bu testler Loc'un paylaşılan durumunu değiştirdiği için bilerek BU sınıfta duruyor: xUnit
    // aynı sınıfın testlerini sırayla koşturur, başka sınıflarla yarışa girmezler.

    [Fact]
    public void TOrDefault_ReturnsTranslation_ThenFallbackText_NeverRawKey()
    {
        Loc.Instance.LoadForTest("en", new Dictionary<string, string> { ["x.var"] = "Exists" }, null);

        Assert.Equal("Exists", Loc.TOrDefault("x.var", "Yedek"));
        Assert.Equal("Yedek", Loc.TOrDefault("x.yok", "Yedek"));
        Assert.Equal("Yedek", Loc.TOrDefault("", "Yedek"));
    }

    /// <summary>Öğe adları kayıt dosyasına yazılmaz (yalnızca tür/alt tür) ve her okunuşta
    /// sözlükten çözülür - dil değişince envanterdeki mevcut öğeler de yeni dile geçmeli.</summary>
    [Fact]
    public void ItemNames_FollowLanguage_AndFallBackToTurkish()
    {
        var fuel = new Fuel("premium");

        Loc.Instance.LoadForTest("en", new Dictionary<string, string>
        {
            ["item.fuel.premium"] = "Premium Fuel",
            ["item.fuel.premium.desc"] = "{0} fuel units"
        }, null);
        Assert.Equal("Premium Fuel", fuel.Name);
        Assert.Equal("100 fuel units", fuel.Description);

        Loc.Instance.LoadForTest("tr", new Dictionary<string, string>(), null);
        Assert.Equal("🏁 Premium Yakıt", fuel.Name);
        Assert.Equal("100 yakıt birimi", fuel.Description);
    }

    [Fact]
    public void UpgradeDescription_NoLongerShowsInternalEffectId()
    {
        Loc.Instance.LoadForTest("tr", new Dictionary<string, string>(), null);
        var upgrade = new Upgrade("speed");

        Assert.DoesNotContain("speed", upgrade.Description);
        Assert.Contains("20", upgrade.Description);
    }

    [Fact]
    public void VehicleModelAndTrailerLabels_AreLocalized()
    {
        Loc.Instance.LoadForTest("en", new Dictionary<string, string>
        {
            ["vehicle.model.truck.rig"] = "Heavy-Duty Tractor",
            ["trailer.tanker"] = "Tanker Trailer",
            ["vehicle.type.helicopter"] = "Helicopter"
        }, null);

        Assert.Equal("Heavy-Duty Tractor", VehicleVisuals.Get("truck", "rig").Label);
        Assert.Equal("Tanker Trailer", Vehicle.TrailerLabel("tanker"));
        Assert.Equal("Helicopter", new Vehicle(0, 0, "helicopter").DisplayModel);

        Loc.Instance.LoadForTest("tr", new Dictionary<string, string>(), null);
        Assert.Equal("Ağır Vasıta Çekici", VehicleVisuals.Get("truck", "rig").Label);
        Assert.Equal("Tanker Dorse", Vehicle.TrailerLabel("tanker"));
    }

    /// <summary>Koddaki her model anahtarı iki dil dosyasında da bulunmalı - aksi halde İngilizce
    /// arayüzde sessizce Türkçe yedek metin görünür.</summary>
    [Fact]
    public void ModelKeys_ExistInBothLanguageFiles()
    {
        var tr = LoadLang("tr");
        var en = LoadLang("en");
        var keys = new List<string>();

        foreach (var type in new[] { "car", "truck", "bus", "motorcycle", "tractor" })
            foreach (var brand in VehicleVisuals.BrandsFor(type))
                keys.Add($"vehicle.model.{type}.{brand}");
        foreach (var trailer in Vehicle.TrailerCatalog.Keys) keys.Add($"trailer.{trailer}");
        foreach (var sub in new[] { "regular", "premium", "turbo" }) { keys.Add($"item.fuel.{sub}"); keys.Add($"item.fuel.{sub}.desc"); }
        foreach (var sub in new[] { "standard", "advanced", "full" }) { keys.Add($"item.repair.{sub}"); keys.Add($"item.repair.{sub}.desc"); }
        foreach (var sub in new[] { "speed", "acceleration", "efficiency", "durability" }) { keys.Add($"item.upgrade.{sub}"); keys.Add($"item.upgrade.{sub}.desc"); }
        foreach (var sub in new[] { "paint", "sticker", "spoiler", "lights" }) { keys.Add($"item.decoration.{sub}"); keys.Add($"item.decoration.{sub}.desc"); }
        foreach (var id in new[] { "mission", "economy", "traffic" }) { keys.Add($"expansion.{id}.name"); keys.Add($"expansion.{id}.desc"); }

        var missing = keys.Where(k => !tr.ContainsKey(k) || !en.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Dil dosyalarında eksik model anahtarları: " + string.Join(", ", missing));
    }
}
