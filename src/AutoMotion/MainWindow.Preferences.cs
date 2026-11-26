using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoMotion.Expansions;
using AutoMotion.Models;
using AutoMotion.Services;
using AutoMotion.Views;
using AutoMotion.Utilities;
using Microsoft.Win32;

namespace AutoMotion
{
    // Dil, ses seviyeleri ve kalıcı tercihler; üst çubuk durum anahtarları.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        /// <summary>Kaydedilmiş dil tercihi; yoksa null (o durumda işletim sisteminin dili kullanılır).
        /// Pencere açılmadan ÖNCE (App.OnStartup) çağrıldığı için örnek alanına değil, birleşik ayar
        /// servisinin tekiline bakar.</summary>
        internal static string LoadLanguagePreference()
        {
            string code = SettingsService.Instance.Current.Language;
            return Loc.IsSupported(code) ? code : null;
        }

        private void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (soundEngine == null) return;
            soundEngine.MasterVolume = (float)e.NewValue;
            if (MasterVolumeTxt != null) MasterVolumeTxt.Text = $"{(int)Math.Round(e.NewValue * 100)}%";

            settings.Current.MasterVolume = e.NewValue;
            settings.SaveDeferred();
        }

        private void SfxVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (soundEngine == null) return;
            soundEngine.SfxVolume = (float)e.NewValue;
            if (SfxVolumeTxt != null) SfxVolumeTxt.Text = $"{(int)Math.Round(e.NewValue * 100)}%";

            settings.Current.SfxVolume = e.NewValue;
            settings.SaveDeferred();
        }

        /// <summary>Dil ile ilgili tüm kontrolleri (Ayarlar'daki liste + Kısayollar'daki geçiş
        /// butonu) geçerli dile göre tazeler.</summary>
        private void SyncLanguageControls()
        {
            if (LanguageCombo != null)
            {
                LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
                foreach (var obj in LanguageCombo.Items)
                {
                    if (obj is ComboBoxItem item && (string)item.Tag == Loc.Instance.Language)
                    {
                        LanguageCombo.SelectedItem = item;
                        break;
                    }
                }
                LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
            }

            RefreshLanguageToggleButton();
        }

        /// <summary>Ayarlar panelindeki dil listesini doldurur ve geçerli dili seçili yapar.
        /// Window_Loaded'dan çağrılır.</summary>
        private void InitializeLanguageCombo()
        {
            if (LanguageCombo == null) return;

            LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
            LanguageCombo.Items.Clear();
            foreach (var (code, displayName) in Loc.SupportedLanguages)
            {
                var item = new ComboBoxItem { Content = displayName, Tag = code };
                LanguageCombo.Items.Add(item);
                if (code == Loc.Instance.Language) LanguageCombo.SelectedItem = item;
            }
            LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
        }

        /// <summary>Kısayollar penceresindeki dil geçiş butonu: mevcut dil dışındaki İLK dile geçer
        /// (bugün iki dil olduğu için pratikte TR ↔ EN). Buton üzerinde her zaman GEÇİLECEK dilin adı
        /// yazar, böylece ne olacağı tıklamadan önce anlaşılır.</summary>
        private void HelpLanguageToggle_Click(object sender, RoutedEventArgs e)
        {
            var next = NextLanguage();
            if (next != null) SetLanguage(next);
            PlayUiClick();
        }

        private static string NextLanguage()
        {
            var languages = Loc.SupportedLanguages;
            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i].Code == Loc.Instance.Language)
                    return languages[(i + 1) % languages.Count].Code;
            }
            return languages.Count > 0 ? languages[0].Code : null;
        }

        /// <summary>Dil butonunun etiketini geçilecek dile göre günceller (XAML bağlaması değil,
        /// çünkü gösterilen metin geçerli dile DEĞİL, hedef dile bağlı).</summary>
        private void RefreshLanguageToggleButton()
        {
            if (HelpLanguageToggleBtn == null) return;

            string next = NextLanguage();
            var match = Loc.SupportedLanguages.FirstOrDefault(l => l.Code == next);
            HelpLanguageToggleBtn.Content = "🌐 " + (match.DisplayName ?? next);
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo?.SelectedItem is ComboBoxItem item && item.Tag is string code
                && code != Loc.Instance.Language)
            {
                SetLanguage(code);
            }
        }

        /// <summary>Bir aracın bildirimlerde gösterilecek adı: kullanıcı isim verdiyse o isim,
        /// vermediyse tip emojisi. Aynı üçlü ifade bildirim metinlerinde defalarca tekrarlanıyordu.</summary>
        private static string VehicleDisplayName(Vehicle vehicle)
        {
            if (vehicle == null) return "";
            return string.IsNullOrEmpty(vehicle.Name) ? vehicle.Emoji : vehicle.Name;
        }

        private void SetLanguage(string code)
        {
            Loc.Instance.SetLanguage(code);

            // Tercih yazılamazsa (LastError dolar) yalnızca bu oturum için geçerli olur - dil
            // değişiminin kendisi engellenmez.
            settings.Current.Language = code;
            settings.Save();

            // Kod içinde üretilen (XAML bağlaması olmayan) metinleri de tazele. Başarım listesi
            // normalde yalnızca kazanılan sayısı DEĞİŞTİĞİNDE yeniden kurulur; dil değişimi bu
            // sayıyı değiştirmediğinden liste eski dilde kalırdı - sayacı sıfırlayıp zorla tazeliyoruz.
            lastAchievementUnlockedCount = -1;
            UpdateUI();
            RefreshCodeBuiltTexts();

            // Dil kutusu ve geçiş butonu yeni durumu yansıtsın.
            SyncLanguageControls();
            ShowToast(Loc.T("toast.language_changed"));
        }

        /// <summary>
        /// Kodla atanan (XAML bağlaması OLMAYAN) metinleri geçerli dile göre yeniden kurar.
        ///
        /// Neden gerekli: {loc:Translate} bağlamaları dil değişince kendiliğinden tazelenir, ama
        /// durum butonlarının ipuçları (ör. "Ses - Şu an: Açık") ve öğe/eklenti/araç listeleri kod
        /// içinde bir kez yazılıyordu - dil değiştirildiğinde bir sonraki tıklamaya kadar eski dilde
        /// kalıyorlardı. Her ipucu, butonun KENDİ durum alanından yeniden hesaplanır; yani bu metot
        /// durumu değiştirmez, yalnızca metni tazeler.
        /// </summary>
        private void RefreshCodeBuiltTexts()
        {
            // Dil, pencere tam kurulmadan (Window_Loaded öncesi) değiştirilirse motorlar henüz yok;
            // o durumda tazelenecek bir şey de yok - butonlar ilk kurulumda doğru dille yazılır.
            if (gameCore == null || soundEngine == null || expansionManager == null) return;

            ShortcutsBtn.ToolTip = Loc.T(shortcutsEnabled
                ? "ui.klavye_kisayollari_acik_yazarken_kapatmak_icin"
                : "ui.shortcuts_off_tooltip");
            AutoMoveBtn.ToolTip = Loc.T(gameCore.AutoMoveEnabled ? "ui.auto_move_on" : "ui.auto_move_off");
            NightModeBtn.ToolTip = Loc.T(forcedNightMode switch
            {
                true => "ui.night_mode_forced_night",
                false => "ui.night_mode_forced_day",
                _ => "ui.gece_modu_otomatik"
            });
            MusicSyncBtn.ToolTip = Loc.T(musicSyncEnabled ? "ui.muzik_suspansiyon_senkronu_acik" : "ui.music_sync_off");
            EffectsDensityBtn.ToolTip = Loc.T(lowEffectsMode ? "ui.effects_density_low" : "ui.efekt_yogunlugu_normal");
            DirtEffectBtn.ToolTip = Loc.T(dirtEffectEnabled ? "ui.dirt_effect_on" : "ui.kirlenme_efekti_kapali");
            SyncSoundButton();
            UpdateRangeSwitchVisual();

            // Öğe/eklenti adları ve araç model etiketleri artık her okunuşta sözlükten çözülüyor
            // (bkz. Item.Name, Expansion.Name, Vehicle.DisplayModel); listelerin yeniden okuması yeterli.
            RefreshItemsList();
            RefreshExpansionsList();
            System.Windows.Data.CollectionViewSource.GetDefaultView(VehicleListBox.ItemsSource)?.Refresh();
        }

        /// <summary>
        /// Otomatik Hareket: kapatıldığında araçlar kendi kendine gezinmeyi (wandering AI)
        /// bırakır, sadece fizik (yerçekimi/sekme/duvar tırmanma) ve manuel sürükleme aktif kalır.
        /// Açıldığında normal davranışa geri döner.
        /// </summary>
        private void ToggleAutoMove_Click(object sender, RoutedEventArgs e)
        {
            gameCore.AutoMoveEnabled = !gameCore.AutoMoveEnabled;
            AutoMoveBtn.Opacity = gameCore.AutoMoveEnabled ? 1.0 : 0.4;
            AutoMoveBtn.ToolTip = Loc.T(gameCore.AutoMoveEnabled ? "ui.auto_move_on" : "ui.auto_move_off");
            PlayUiClick();
        }

        /// <summary>Gece modunu Otomatik -> Zorla Gece -> Zorla Gündüz -> Otomatik döngüsünde değiştirir (test/gösteri için).</summary>
        private void NightModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (forcedNightMode == null) forcedNightMode = true;
            else if (forcedNightMode == true) forcedNightMode = false;
            else forcedNightMode = null;

            NightModeBtn.Content = forcedNightMode switch { true => "🌙", false => "☀️", _ => "🌗" };
            NightModeBtn.ToolTip = Loc.T(forcedNightMode switch
            {
                true => "ui.night_mode_forced_night",
                false => "ui.night_mode_forced_day",
                _ => "ui.gece_modu_otomatik"
            });
            PlayUiClick();
        }

        private void MusicSyncBtn_Click(object sender, RoutedEventArgs e)
        {
            musicSyncEnabled = !musicSyncEnabled;
            MusicSyncBtn.Opacity = musicSyncEnabled ? 1.0 : 0.4;
            MusicSyncBtn.ToolTip = Loc.T(musicSyncEnabled ? "ui.muzik_suspansiyon_senkronu_acik" : "ui.music_sync_off");
            PlayUiClick();
        }

        private void EffectsDensityBtn_Click(object sender, RoutedEventArgs e)
        {
            SetLowEffectsMode(!lowEffectsMode);

            settings.Current.LowEffectsMode = lowEffectsMode;
            settings.Save();
            PlayUiClick();
        }

        /// <summary>Efekt yoğunluğu durumunu ve butonun görünümünü tek yerden ayarlar - açılışta
        /// kaydedilmiş tercih uygulanırken de aynı yol kullanılır (bkz. ApplyPersistedPreferences).</summary>
        private void SetLowEffectsMode(bool enabled)
        {
            lowEffectsMode = enabled;
            EffectsDensityBtn.Opacity = enabled ? 0.4 : 1.0;
            EffectsDensityBtn.ToolTip = Loc.T(enabled ? "ui.effects_density_low" : "ui.efekt_yogunlugu_normal");
        }

        private void DirtEffectBtn_Click(object sender, RoutedEventArgs e)
        {
            SetDirtEffectEnabled(!dirtEffectEnabled);
            PlayUiClick();
        }

        private void DirtEffectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SetDirtEffectEnabled(DirtEffectCheckBox.IsChecked == true);
            PlayUiClick();
        }

        /// <summary>Üst çubuktaki 🟤 tuşu ile Ayarlar panelindeki checkbox aynı durumu paylaşır -
        /// biri değiştiğinde diğeri de senkron kalır.</summary>
        private void SetDirtEffectEnabled(bool enabled)
        {
            dirtEffectEnabled = enabled;
            DirtEffectBtn.Opacity = enabled ? 1.0 : 0.4;
            DirtEffectBtn.ToolTip = Loc.T(enabled ? "ui.dirt_effect_on" : "ui.kirlenme_efekti_kapali");
            if (DirtEffectCheckBox != null) DirtEffectCheckBox.IsChecked = enabled;
        }

        /// <summary>
        /// Kaydedilmiş tercihleri (çarpanlar, tema, ses, efekt yoğunluğu, otomatik kayıt aralığı)
        /// açılışta motorlara ve arayüz kontrollerine uygular. Eskiden bunların hiçbiri kalıcı
        /// değildi: uygulama her açıldığında tema 0'a, ses açığa, efektler normale dönüyordu.
        ///
        /// Kaydırıcıya değer atamak ValueChanged'i tetiklediğinden ilgili motor ve etiket metni
        /// kendiliğinden senkron olur - bu yüzden kaydırıcılara yazmak yeterlidir.
        /// </summary>
        private void ApplyPersistedPreferences()
        {
            var prefs = settings.Current;

            // ── Kirlilik/yakıt çarpanları. Bozuk/elle düzenlenmiş ayar dosyası motoru anlamsız bir
            //    çarpanla çalıştırmasın.
            double dirt = prefs.DirtMultiplier > 0 ? prefs.DirtMultiplier : 1.0;
            double fuel = prefs.FuelMultiplier > 0 ? prefs.FuelMultiplier : 1.0;

            gameCore.DirtMultiplier = dirt;
            gameCore.FuelMultiplier = fuel;
            if (DirtMultiplierSlider != null) DirtMultiplierSlider.Value = dirt;
            if (FuelMultiplierSlider != null) FuelMultiplierSlider.Value = fuel;

            // ── Tema. Tema sayısı sürümler arasında değişebileceğinden indeks sınır dışıysa
            //    varsayılana düşülür (aksi halde SetTheme tanımsız bir temaya giderdi).
            if (prefs.ThemeIndex >= 0 && prefs.ThemeIndex < themeEngine.ThemeCount)
            {
                currentTheme = prefs.ThemeIndex;
                themeEngine.SetTheme(currentTheme);
            }

            // ── Ses seviyeleri ve sustur durumu.
            if (MasterVolumeSlider != null) MasterVolumeSlider.Value = Math.Clamp(prefs.MasterVolume, 0, 1);
            if (SfxVolumeSlider != null) SfxVolumeSlider.Value = Math.Clamp(prefs.SfxVolume, 0, 1);

            if (prefs.Muted && soundEngine != null && !soundEngine.IsMuted)
            {
                soundEngine.ToggleMute();
                SyncSoundButton();
            }

            // ── Düşük efekt (performans) modu.
            if (prefs.LowEffectsMode) SetLowEffectsMode(true);

            // ── Otomatik kayıt aralığı. Liste seçimi olay işleyicisi ÇIKARILARAK yapılır: aksi halde
            //    açılışta SelectionChanged tetiklenip tıklama sesi çalar ve ayar dosyası boş yere
            //    yeniden yazılırdı (InitializeLanguageCombo ile aynı yaklaşım).
            saveManager.AutoSaveIntervalSeconds = prefs.AutoSaveIntervalSeconds > 0
                ? prefs.AutoSaveIntervalSeconds
                : 60;

            if (AutoSaveIntervalCombo != null)
            {
                AutoSaveIntervalCombo.SelectionChanged -= AutoSaveIntervalCombo_SelectionChanged;
                foreach (var obj in AutoSaveIntervalCombo.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag is string tag
                        && int.TryParse(tag, out int seconds) && seconds == saveManager.AutoSaveIntervalSeconds)
                    {
                        AutoSaveIntervalCombo.SelectedItem = item;
                        break;
                    }
                }
                AutoSaveIntervalCombo.SelectionChanged += AutoSaveIntervalCombo_SelectionChanged;
            }
        }

        // Çarpanlar artık ayrıca settings.json'a da yazılır: eskiden yalnızca kayıt dosyasının içinde
        // yaşadıkları için, kayıt yüklemeden oynayan bir kullanıcıda her açılışta 1.0x'e dönüyorlardı.
        // Yazma SaveDeferred ile ertelenir - kaydırıcı sürüklenirken saniyede onlarca dosya yazılmasın.
        private void DirtMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gameCore == null || DirtMultiplierTxt == null) return;
            gameCore.DirtMultiplier = e.NewValue;
            DirtMultiplierTxt.Text = $"{e.NewValue:0.0}x";

            settings.Current.DirtMultiplier = e.NewValue;
            settings.SaveDeferred();
        }

        private void FuelMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gameCore == null || FuelMultiplierTxt == null) return;
            gameCore.FuelMultiplier = e.NewValue;
            FuelMultiplierTxt.Text = $"{e.NewValue:0.0}x";

            settings.Current.FuelMultiplier = e.NewValue;
            settings.SaveDeferred();
        }
    }
}
