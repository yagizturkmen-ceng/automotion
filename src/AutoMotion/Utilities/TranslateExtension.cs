using System;
using System.Windows.Data;
using System.Windows.Markup;
using AutoMotion.Services;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// XAML işaretleme uzantısı: <c>Text="{loc:T ui.dock.theme}"</c>.
    ///
    /// Sabit bir metin döndürmek yerine <see cref="Loc.Instance"/> üzerindeki indeksleyiciye bir
    /// BAĞLAMA kurar. Böylece kullanıcı dili değiştirdiğinde Loc, PropertyChanged(null) yayınlar ve
    /// arayüzdeki tüm çeviriler uygulamayı yeniden başlatmadan anında tazelenir - Steam oyuncularının
    /// beklediği davranış budur.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class TranslateExtension : MarkupExtension
    {
        public TranslateExtension() { }

        public TranslateExtension(string key)
        {
            Key = key;
        }

        /// <summary>Çeviri anahtarı (ör. "ui.dock.theme").</summary>
        [ConstructorArgument("key")]
        public string Key { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return "";

            // Anahtar içindeki nokta/işaretler indeksleyici yolunda sorun çıkarmasın diye köşeli
            // parantezli yol kullanılıyor: [ui.dock.theme]
            var binding = new Binding($"[{Key}]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
