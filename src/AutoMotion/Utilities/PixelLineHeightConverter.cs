using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Bir TextBlock'un FontSize'ından GARANTİ satır yüksekliğini üretir (bkz. Styles.xaml içindeki
    /// örtük TextBlock stili).
    ///
    /// NEDEN VAR: arayüzün pixel fontu (Press Start 2P) dikey metriklerini YANLIŞ bildiriyor -
    /// font dosyasındaki değerler `ascent = 1000, descent = 0, lineGap = 0` (birim/em = 1000),
    /// yani WPF'e "bir satır tam olarak 1.0 em yer kaplar" diyor. Oysa gliflerin gerçek mürekkebi
    /// taban çizgisinin 374/1000 em ALTINA kadar iniyor (OS/2 winDescent = 374): Türkçedeki
    /// ç/ş/ğ kuyrukları, İngilizcedeki g/y/p/q hepsi bu bölgede.
    ///
    /// Sonuç: WPF her satır kutusunu gerçek mürekkepten ~%37 kısa ölçüyor; alt alta duran
    /// TextBlock'lar ve saran satırlar birbirinin üstüne biniyordu. Hata dört ayrı arayüz
    /// yüzeyinde ayrı ayrı ortaya çıkıp ayrı ayrı elle yamandı (ayarlar etiketleri, kısayol
    /// listesi, eklenti açıklamaları, IoT paneli). Bu dönüştürücü + örtük stil ikilisi yamaların
    /// yerine TEK bir kural koyar: her TextBlock, font boyutuyla orantılı, mürekkebi kesin olarak
    /// içine alan bir satır yüksekliği alır.
    ///
    /// Çarpan neden 1.5: gereken alt sınır (ascent + winDescent) / em = 1.374. 1.5 hem bu sınırın
    /// üstünde hem de elle yamanmış eski değerlerle (15/10, 19/13, 14/9) aynı aralıkta - yani
    /// mevcut görsel tasarımı değiştirmiyor, sadece kuralı tekilleştiriyor.
    /// AutoMotion.Tests/PixelFontMetricsTests bu çarpanı fontun GERÇEK metrikleriyle doğrular:
    /// font bir gün daha derin kuyruklu bir sürümle değişirse test kırılır.
    /// </summary>
    public sealed class PixelLineHeightConverter : IValueConverter
    {
        /// <summary>Satır yüksekliği = FontSize × bu çarpan (yukarı yuvarlanır).</summary>
        public const double Factor = 1.5;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double fontSize && fontSize > 0 && !double.IsNaN(fontSize) && !double.IsInfinity(fontSize))
                return Math.Ceiling(fontSize * Factor);

            // Boyut bilinmiyorsa özelliğe hiç dokunma - WPF'in kendi (otomatik) satır yüksekliği kalır.
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
