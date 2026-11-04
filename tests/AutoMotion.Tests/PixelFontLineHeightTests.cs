using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Arayüz metinlerinin satır bindirmesine karşı korunması.
///
/// KÖK NEDEN (ölçüldü): arayüzün pixel fontu (Press Start 2P) dikey metriklerini yanlış bildiriyor -
/// ascent = 1000, descent = 0, lineGap = 0 (em = 1000 birim). WPF bu bildirime göre bir satırı tam
/// 1.0 em yüksekliğinde ölçüyor. Oysa gliflerin gerçek mürekkebi taban çizgisinin 374/1000 em
/// ALTINA iniyor (OS/2 winDescent): ç/ş/ğ/g/y/p kuyrukları. Her satır kutusu mürekkepten ~%37 kısa
/// ölçüldüğü için alt alta duran metinler birbirine biniyordu - hata dört ayrı yüzeyde ayrı ayrı
/// yamandı. Çözüm tek kurala indirildi: Styles.xaml'deki örtük TextBlock stili + FontSize'a oranlı
/// garanti satır yüksekliği (bkz. Utilities/PixelLineHeightConverter.cs).
///
/// Buradaki testler o kuralın hem MATEMATİĞİNİ (çarpan fontun gerçek metriklerini karşılıyor mu)
/// hem de GERÇEKTEN UYGULANDIĞINI (WPF düzen motoruyla ölçerek) doğrular.
/// </summary>
public class PixelFontLineHeightTests
{
    private static string ProjectDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AutoMotion", "Assets", "Lang")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "AutoMotion");
        }
    }

    // ------------------------------------------------------------------ font metrikleri

    /// <summary>
    /// REGRESYON KORUMASI: satır yüksekliği çarpanı, fontun GERÇEK dikey mürekkep alanını
    /// karşılamalı. Font bir gün daha derin kuyruklu bir sürümle değiştirilirse (ya da başka bir
    /// pixel fonta geçilirse) bu test kırılır ve çarpanın güncellenmesi gerektiğini söyler -
    /// aksi halde bindirme hatası sessizce geri gelirdi.
    /// </summary>
    [Fact]
    public void LineHeightFactor_CoversActualFontInkExtent()
    {
        var metrics = ReadVerticalMetrics(Path.Combine(ProjectDir, "Assets", "Fonts", "PressStart2P-Regular.ttf"));

        // Gereken en küçük satır yüksekliği: taban çizgisinin üstü (ascent) + altına taşan
        // mürekkep (winDescent). Font descent = 0 bildirdiği için WPF'in kendi hesabı bunu ıskalıyor.
        double requiredFactor = (metrics.Ascent + metrics.WinDescent) / (double)metrics.UnitsPerEm;

        Assert.True(PixelLineHeightConverter.Factor >= requiredFactor,
            $"Satır yüksekliği çarpanı yetersiz: {PixelLineHeightConverter.Factor} < gereken {requiredFactor:0.###} "
            + $"(ascent={metrics.Ascent}, winDescent={metrics.WinDescent}, em={metrics.UnitsPerEm})");
    }

    /// <summary>Hatanın kaynağını sabitler: font gerçekten "descent = 0" diyor ama mürekkep
    /// taban çizgisinin altına iniyor. Bu ikisi eşit olsaydı düzeltmeye gerek kalmazdı.</summary>
    [Fact]
    public void PixelFont_UnderreportsItsDescent()
    {
        var metrics = ReadVerticalMetrics(Path.Combine(ProjectDir, "Assets", "Fonts", "PressStart2P-Regular.ttf"));

        Assert.Equal(0, metrics.Descent);
        Assert.True(metrics.WinDescent > 0, "Font kuyruk bırakmıyorsa bu düzeltmenin gerekçesi kalmaz.");
    }

    [Theory]
    [InlineData(7, 11)]    // sürüş ipucu satırları
    [InlineData(9, 14)]    // ayar etiketleri
    [InlineData(10, 15)]   // eklenti açıklamaları
    [InlineData(16, 24)]   // açılış ekranı başlığı
    public void Converter_ProducesLineHeightThatClearsTheInk(double fontSize, double expected)
    {
        var converter = new PixelLineHeightConverter();

        var result = converter.Convert(fontSize, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, Assert.IsType<double>(result));
    }

    [Fact]
    public void Converter_LeavesLineHeightUntouched_WhenFontSizeIsUnknown()
    {
        var converter = new PixelLineHeightConverter();
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        // Boyut bilinmiyorsa WPF'in kendi otomatik satır yüksekliği kalmalı (UnsetValue).
        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert(double.NaN, typeof(double), null, culture));
        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert(0.0, typeof(double), null, culture));
        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert(null, typeof(double), null, culture));
        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert("9", typeof(double), null, culture));
    }

    // ------------------------------------------------------------------ gerçek WPF düzeni

    /// <summary>
    /// Kuralın kâğıt üzerinde değil, GERÇEKTEN çalıştığını doğrular: derlenmiş Styles.xaml
    /// yüklenir, örtük stil bir TextBlock'a uygulanır ve WPF düzen motoruyla ölçülür.
    /// </summary>
    [Fact]
    public void ImplicitStyle_GivesEveryTextBlockEnoughRoomForItsInk()
    {
        RunOnStaThread(() =>
        {
            var styles = LoadCompiledStyles();
            var style = Assert.IsType<Style>(styles[typeof(TextBlock)]);

            var text = new TextBlock { Text = "Yağış çekiciği", FontSize = 9, Style = style };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // 9 × 1.5 = 13.5 -> 14
            Assert.Equal(14, text.LineHeight);
            Assert.Equal(LineStackingStrategy.BlockLineHeight, text.LineStackingStrategy);
            Assert.True(text.DesiredSize.Height >= 14,
                $"Ölçülen yükseklik {text.DesiredSize.Height}, mürekkebi taşımaya yetmiyor.");
        });
    }

    /// <summary>
    /// Asıl hatanın kendisi: alt alta iki metin. Kuralsız TextBlock, fontun bildirdiği (kısa) satır
    /// kutusunu kullanır ve komşusunun mürekkebine girer. Test bunu KIYASLAYARAK ölçer - kuralın
    /// gerçekten fazladan dikey alan açtığını, yani sorunu çözdüğünü gösterir. Aksi halde stil
    /// bir gün etkisiz hale gelse (ör. Setter silinse) testler yine yeşil kalırdı.
    /// </summary>
    [Fact]
    public void StackedTextBlocks_GetMoreRoom_WithTheRuleThanWithout()
    {
        RunOnStaThread(() =>
        {
            var styles = LoadCompiledStyles();
            var style = Assert.IsType<Style>(styles[typeof(TextBlock)]);

            double guarded = MeasureStackedPairHeight(style, out double gap);

            // "Kuralsız" hâl, kanvasların kullandığı iptal mekanizmasının aynısıyla üretilir:
            // boş bir örtük stil, garanti satır yüksekliğini devre dışı bırakır.
            double unguarded = MeasureStackedPairHeight(new Style(typeof(TextBlock)), out _);

            Assert.True(guarded > unguarded,
                $"Kural fazladan dikey alan açmıyor: kurallı={guarded}, kuralsız={unguarded}");

            // Kutular ayrık olmalı: ikinci satırın üstü, birincinin altından önce başlamamalı.
            Assert.True(gap >= 0, $"İkinci satır birincinin içine giriyor (boşluk={gap}).");
        });
    }

    /// <summary>
    /// Açılış ekranı sabit yükseklikte (430) ve içeriği ona tam oturuyor; satır yüksekliği kuralı
    /// metin kutularını büyüttüğü için bu payın hâlâ yettiği DOĞRULANMALI - yoksa "Yükleniyor..."
    /// satırı sessizce pencerenin dışında kalırdı. Pencere GÖSTERİLMEDEN, gerçek XAML'i üzerinden
    /// ölçülür (animasyon Loaded'da başladığı için kurucu zararsızdır).
    /// </summary>
    [Fact]
    public void SplashWindow_ContentStillFitsItsFixedHeight()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();

            var splash = new AutoMotion.Views.SplashWindow();
            var content = Assert.IsAssignableFrom<FrameworkElement>(splash.Content);

            content.Measure(new Size(splash.Width, double.PositiveInfinity));

            Assert.True(content.DesiredSize.Height <= splash.Height,
                $"Açılış ekranı içeriği pencereye sığmıyor: gereken {content.DesiredSize.Height}, "
                + $"pencere {splash.Height}. Satır yüksekliği kuralı payı tüketmiş olabilir.");
        });
    }

    /// <summary>Alt alta iki ayar etiketini ölçer; toplam yüksekliği döndürür, aradaki boşluğu
    /// (ikinci satırın üstü − birincinin altı) dışarı verir.</summary>
    private static double MeasureStackedPairHeight(Style? style, out double gap)
    {
        var panel = new StackPanel();
        var first = new TextBlock { Text = "Ses Seviyeleri", FontSize = 9, Style = style };
        var second = new TextBlock { Text = "Ana: %80", FontSize = 9, Style = style };
        panel.Children.Add(first);
        panel.Children.Add(second);

        // Yükseklik SERBEST bırakılır (PositiveInfinity): ölçülen değer metnin gerçekten
        // istediği yükseklik olsun, düzenlenen kutunun boyu değil.
        panel.Measure(new Size(400, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 400, panel.DesiredSize.Height));

        double firstBottom = first.TranslatePoint(new Point(0, first.ActualHeight), panel).Y;
        double secondTop = second.TranslatePoint(new Point(0, 0), panel).Y;
        gap = secondTop - firstBottom;

        return panel.DesiredSize.Height;
    }

    /// <summary>Adlandırılmış stiller örtük stili devralmaz - bu yüzden Styles.xaml'deki metin
    /// stilleri kuralı BasedOn ile açıkça almalı. Aksi halde o yüzeylerde hata geri gelir.</summary>
    [Theory]
    [InlineData("HeaderText")]
    [InlineData("BodyText")]
    public void NamedTextStyles_InheritTheRule(string styleKey)
    {
        RunOnStaThread(() =>
        {
            var styles = LoadCompiledStyles();
            var style = Assert.IsType<Style>(styles[styleKey]);

            var text = new TextBlock { Text = "Ağırlık", Style = style };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.Equal(LineStackingStrategy.BlockLineHeight, text.LineStackingStrategy);
            Assert.True(text.LineHeight >= text.FontSize * PixelLineHeightConverter.Factor - 0.001,
                $"'{styleKey}' garanti satır yüksekliğini almıyor (LineHeight={text.LineHeight}, FontSize={text.FontSize}).");
        });
    }

    // ------------------------------------------------------------------ kuralın kapsamı

    /// <summary>
    /// Mutlak konumlandırılan kanvaslar kuralın DIŞINDA kalmalı: sahnedeki emoji/sprite'lar ve
    /// vites yuvası etiketleri Canvas.Top ile piksel hassasiyetinde yerleştiriliyor, satır
    /// kutusunu büyütmek onları kaydırırdı. Boş örtük stil bu iptali sağlar - biri onu silerse
    /// araçlar ve vites etiketleri sessizce kayar, bu test uyarır.
    /// </summary>
    [Theory]
    [InlineData("RenderCanvas")]
    [InlineData("GearSlotLabelsCanvas")]
    public void AbsolutelyPositionedCanvases_OptOutOfTheRule(string canvasName)
    {
        string xaml = File.ReadAllText(Path.Combine(ProjectDir, "MainWindow.xaml"));

        int start = xaml.IndexOf($"Name=\"{canvasName}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{canvasName} MainWindow.xaml içinde bulunamadı.");

        int end = xaml.IndexOf("</Canvas>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{canvasName} kapanış etiketi bulunamadı.");

        string body = xaml.Substring(start, end - start);
        Assert.Contains("<Style TargetType=\"TextBlock\"/>", body.Replace(" />", "/>"));
    }

    /// <summary>Kural tek yerde yaşamalı: XAML'de elle LineHeight verilen yer kalmamalı, aksi
    /// halde yeniden dört ayrı ad-hoc yamaya doğru kayarız.</summary>
    [Fact]
    public void NoXamlSurface_SetsItsOwnLineHeight()
    {
        string objDir = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        string binDir = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;

        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(ProjectDir, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(objDir) || file.Contains(binDir)) continue;
            if (Path.GetFileName(file) == "Styles.xaml") continue; // kuralın kendisi burada tanımlı

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"LineHeight\s*=\s*""\d"))
                    offenders.Add(Path.GetFileName(file) + ":" + (i + 1));
        }

        Assert.True(offenders.Count == 0,
            "Elle LineHeight verilmiş satırlar (Styles.xaml'deki tek kural yeterli):\n  "
            + string.Join("\n  ", offenders));
    }

    // ------------------------------------------------------------------ yardımcılar

    private static ResourceDictionary LoadCompiledStyles()
    {
        // Derlenmiş kaynak (BAML) üzerinden yüklenir - böylece test, uygulamanın gerçekten
        // kullandığı sözlüğü doğrular; ayrıca clr-namespace referansları doğru derlemede çözülür.
        EnsureApplication();
        return new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AutoMotion;component/Styles.xaml", UriKind.Absolute)
        };
    }

    private static void EnsureApplication()
    {
        // pack:// şemasının kayıtlı olması için tek bir Application örneği yeterli (Run çağrılmaz).
        // Kaynak sözlüğü de uygulamadaki gibi bağlanır: pencereler {StaticResource PixelFont} gibi
        // başvuruları ancak Application.Resources üzerinden çözebilir.
        if (Application.Current == null)
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AutoMotion;component/Styles.xaml", UriKind.Absolute)
            });
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private readonly record struct VerticalMetrics(int UnitsPerEm, int Ascent, int Descent, int WinDescent);

    /// <summary>TrueType dosyasından dikey metrikleri okur (head.unitsPerEm, hhea.ascender/descender,
    /// OS/2.usWinDescent). Testin fontun GERÇEK değerlerine bakması, çarpanı bir varsayım olmaktan
    /// çıkarıp doğrulanabilir bir olgu yapar.</summary>
    private static VerticalMetrics ReadVerticalMetrics(string ttfPath)
    {
        Assert.True(File.Exists(ttfPath), "Font bulunamadı: " + ttfPath);
        byte[] f = File.ReadAllBytes(ttfPath);

        int U16(int o) => (f[o] << 8) | f[o + 1];
        int S16(int o) { int v = U16(o); return v >= 0x8000 ? v - 0x10000 : v; }
        int U32(int o) => (f[o] << 24) | (f[o + 1] << 16) | (f[o + 2] << 8) | f[o + 3];

        int tableCount = U16(4);
        var tables = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            tables[System.Text.Encoding.ASCII.GetString(f, record, 4)] = U32(record + 8);
        }

        int head = tables["head"], hhea = tables["hhea"], os2 = tables["OS/2"];
        return new VerticalMetrics(
            UnitsPerEm: U16(head + 18),
            Ascent: S16(hhea + 4),
            Descent: S16(hhea + 6),
            WinDescent: U16(os2 + 76));
    }
}
