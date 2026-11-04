using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static string ProjectDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AutoMotion", "Assets", "Lang")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "AutoMotion");
        }
    }

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
        var tr = LoadLang("tr");
        var en = LoadLang("en");

        var missingInEn = tr.Keys.Except(en.Keys).OrderBy(k => k).ToList();
        var missingInTr = en.Keys.Except(tr.Keys).OrderBy(k => k).ToList();

        Assert.True(missingInEn.Count == 0, "İngilizcede eksik: " + string.Join(", ", missingInEn));
        Assert.True(missingInTr.Count == 0, "Türkçede eksik: " + string.Join(", ", missingInTr));
    }

    [Fact]
    public void NoTranslationValue_IsEmpty()
    {
        foreach (var code in new[] { "tr", "en" })
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

        foreach (var code in new[] { "tr", "en" })
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
        var tr = LoadLang("tr");
        var en = LoadLang("en");
        var rx = new Regex(@"\{(\d+)\}");

        foreach (var pair in tr)
        {
            var trSlots = rx.Matches(pair.Value).Select(m => m.Value).OrderBy(s => s).ToList();
            var enSlots = rx.Matches(en[pair.Key]).Select(m => m.Value).OrderBy(s => s).ToList();
            Assert.True(trSlots.SequenceEqual(enSlots),
                "'" + pair.Key + "' yer tutucuları uyuşmuyor: tr=" + string.Join(",", trSlots)
                + " en=" + string.Join(",", enSlots));
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
    /// </summary>
    [Fact]
    public void NoUserFacingTurkishStringsRemain_HardcodedInCode()
    {
        const string turkishOnly = "çğıöşüÇĞİÖŞÜ";
        var userFacing = new Regex(
            @"\b(ShowToast|ShowDialogAsync|MessageBox\.Show)\(|\.(Text|Content|ToolTip|Header)\s*=\s*\$?""");
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

                line = StripLineComment(line).Trim();
                if (line.Length == 0 || line.StartsWith("///", StringComparison.Ordinal)) continue;
                if (!userFacing.IsMatch(line)) continue;

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
    [InlineData("de", false)]
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
}
