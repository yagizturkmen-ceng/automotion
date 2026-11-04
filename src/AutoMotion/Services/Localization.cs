using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutoMotion.Services
{
    /// <summary>
    /// Uygulama metinlerinin dil kaynağı.
    ///
    /// Neden .resx değil de JSON: (a) diller çalışma zamanında, uygulamayı yeniden başlatmadan
    /// değiştirilebiliyor - Steam oyuncuları bunu bekler; (b) çeviri dosyaları düz metin olduğundan
    /// bir çevirmene gönderilip geri alınabiliyor, Visual Studio gerektirmiyor; (c) ileride Steam
    /// Workshop üzerinden topluluk çevirisi eklemek yalnızca bir dosya bırakmakla mümkün olur.
    ///
    /// Çözümleme sırası: geçerli dil → İngilizce (yedek) → anahtarın kendisi. Böylece bir çeviri
    /// eksik kalsa bile arayüzde boşluk değil, en kötü ihtimalle anahtar metni görünür.
    ///
    /// XAML tarafında <c>{loc:T Key=...}</c> işaretleme uzantısıyla kullanılır (bkz.
    /// TranslateExtension) - bağlamalar dil değişince kendiliğinden tazelenir.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        /// <summary>Uygulamanın desteklediği diller (kod → arayüzde gösterilecek ad).</summary>
        public static readonly IReadOnlyList<(string Code, string DisplayName)> SupportedLanguages =
            new[] { ("tr", "Türkçe"), ("en", "English") };

        public const string FallbackLanguage = "en";

        private Dictionary<string, string> _current = new(StringComparer.Ordinal);
        private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
        private string _language = "";

        private Loc() { }

        /// <summary>Geçerli dil kodu ("tr" / "en").</summary>
        public string Language => _language;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Dil değiştiğinde tetiklenir - XAML dışında elle kurulmuş metinleri (ör. kod
        /// içinde üretilen liste başlıkları) tazelemek isteyen yerler buna abone olur.</summary>
        public event EventHandler LanguageChanged;

        /// <summary>
        /// XAML bağlamalarının kullandığı indeksleyici: <c>{Binding [key], Source={x:Static ...}}</c>.
        /// Dil değişince PropertyChanged(null) yayınlandığı için tüm bağlamalar yeniden okunur.
        /// </summary>
        public string this[string key] => T(key);

        /// <summary>Bir anahtarı geçerli dile çevirir. Anahtar yoksa İngilizceye, o da yoksa
        /// anahtarın kendisine düşer (arayüzde asla boş metin görünmez).</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var self = Instance;

            if (self._current.TryGetValue(key, out var value)) return value;
            if (self._fallback.TryGetValue(key, out var fallback)) return fallback;
            return key;
        }

        /// <summary>Biçimlendirilmiş çeviri: <c>T("toast.fuel_empty", vehicleName)</c>.
        /// Çeviri dizesindeki yer tutucular {0}, {1}... şeklindedir.</summary>
        public static string T(string key, params object[] args)
        {
            string format = T(key);
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException)
            {
                // Bozuk bir çeviri dizesi (ör. çevirmen {0}'ı silmiş) uygulamayı çökertmemeli.
                return format;
            }
        }

        /// <summary>Uygulama açılışında çağrılır. Kaydedilmiş bir dil tercihi yoksa işletim
        /// sisteminin diline bakar; o da desteklenmiyorsa İngilizceye düşer.</summary>
        public static void Initialize(string preferredLanguage = null)
        {
            string language = preferredLanguage;

            if (string.IsNullOrWhiteSpace(language))
            {
                string osLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                language = IsSupported(osLanguage) ? osLanguage : FallbackLanguage;
            }

            Instance.SetLanguage(language);
        }

        public static bool IsSupported(string code)
            => !string.IsNullOrWhiteSpace(code)
               && SupportedLanguages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>Dili değiştirir ve tüm bağlamaları tazeler.</summary>
        public void SetLanguage(string code)
        {
            if (!IsSupported(code)) code = FallbackLanguage;
            if (string.Equals(code, _language, StringComparison.OrdinalIgnoreCase) && _current.Count > 0) return;

            _language = code.ToLowerInvariant();
            _current = LoadDictionary(_language);
            _fallback = _language == FallbackLanguage ? _current : LoadDictionary(FallbackLanguage);

            // null özellik adı = "bu nesnedeki HER şey değişti": tüm {loc:T} bağlamaları yeniden okur.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Dil dosyalarının bulunduğu klasör (exe'nin yanında). Dosyalar gömülü kaynak
        /// DEĞİL, gevşek dosya olarak kopyalanır - böylece bir çeviri düzeltmesi için yeniden
        /// derleme gerekmez, dosyayı değiştirip uygulamayı yeniden başlatmak yeterlidir (araç
        /// görsellerinde de aynı yaklaşım kullanılıyor, bkz. AutoMotion.csproj).</summary>
        private static string LanguageDirectory =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Lang");

        private static Dictionary<string, string> LoadDictionary(string code)
        {
            try
            {
                string path = Path.Combine(LanguageDirectory, $"{code}.json");
                if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                return parsed == null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                // Bozuk/eksik bir dil dosyası uygulamayı açılışta çökertmemeli - anahtarlar
                // görünür kalır ama uygulama çalışır.
                System.Diagnostics.Debug.WriteLine($"Dil dosyası yüklenemedi ({code}): {ex.Message}");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        /// <summary>Testler için: sözlükleri doğrudan besler (diske dokunmadan).</summary>
        internal void LoadForTest(string language, Dictionary<string, string> current, Dictionary<string, string> fallback)
        {
            _language = language;
            _current = new Dictionary<string, string>(current, StringComparer.Ordinal);
            _fallback = new Dictionary<string, string>(fallback ?? current, StringComparer.Ordinal);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Bir dil dosyasındaki tüm anahtarlar (eksik çeviri denetimi için).</summary>
        public static IReadOnlyCollection<string> KeysFor(string code) => LoadDictionary(code).Keys;
    }
}
