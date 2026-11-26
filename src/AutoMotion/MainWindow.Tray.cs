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
    // Sistem tepsisi, Windows ile başlatma, görev çubuğu simgesi.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        private const string StartWithWindowsRunKeyName = "AutoMotion";

        private static bool IsStartWithWindowsEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue(StartWithWindowsRunKeyName) != null;
            }
            catch { return false; }
        }

        private static void SetStartWithWindows(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (enabled)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(StartWithWindowsRunKeyName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(StartWithWindowsRunKeyName, false);
                }
            }
            catch { /* yetersiz izin vb. - sessizce yoksay, kritik değil */ }
        }

        /// <summary>
        /// Sistem tepsisi simgesi: pencere gizlendiğinde uygulama arka planda çalışmaya
        /// devam eder, tepsi simgesine sağ tıklayarak hızlı erişim sağlanır.
        /// </summary>
        private void InitializeTrayIcon()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var showHideItem = menu.Items.Add("", null, (s, e) => ToggleWindowVisibility());
            var addVehicleItem = menu.Items.Add("", null, (s, e) => { ShowWindowFromTray(); AddVehicleBtn_Click(null, null); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Dil seçimi: WinForms tepsi menüsü XAML bağlaması kullanamadığından metinler
            // LanguageChanged olayında elle tazelenir (bkz. RefreshTrayMenuTexts).
            var languageItem = new System.Windows.Forms.ToolStripMenuItem();
            foreach (var (code, displayName) in Loc.SupportedLanguages)
            {
                var entry = new System.Windows.Forms.ToolStripMenuItem(displayName) { Tag = code };
                entry.Click += (s, e) => SetLanguage(code);
                languageItem.DropDownItems.Add(entry);
            }
            menu.Items.Add(languageItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Windows ile Başlat: çoğu masaüstü pet/widget uygulamasında (VPet, Rainmeter) beklenen
            // standart bir özellik - HKCU\...\Run altına kendi exe yolunu yazar/siler.
            var startWithWindowsItem = new System.Windows.Forms.ToolStripMenuItem
            {
                CheckOnClick = true,
                Checked = IsStartWithWindowsEnabled()
            };
            startWithWindowsItem.CheckedChanged += (s, e) => SetStartWithWindows(startWithWindowsItem.Checked);
            menu.Items.Add(startWithWindowsItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exitItem = menu.Items.Add("", null, (s, e) => ExitApplication("tepsi menüsü"));

            // Tepsi menüsü metinleri dil değişiminde tazelenir.
            void RefreshTrayMenuTexts()
            {
                showHideItem.Text = Loc.T("tray.show_hide");
                addVehicleItem.Text = Loc.T("tray.add_vehicle");
                languageItem.Text = Loc.T("tray.language");
                startWithWindowsItem.Text = Loc.T("tray.start_with_windows");
                exitItem.Text = Loc.T("tray.exit");

                foreach (System.Windows.Forms.ToolStripMenuItem entry in languageItem.DropDownItems)
                    entry.Checked = (string)entry.Tag == Loc.Instance.Language;
            }

            RefreshTrayMenuTexts();
            Loc.Instance.LanguageChanged += (s, e) => RefreshTrayMenuTexts();

            System.Drawing.Icon customIcon = null;
            try
            {
                var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo.ico"));
                if (sri != null)
                {
                    using (var stream = sri.Stream)
                    {
                        // Sistem tepsisi ikonu her zaman küçük (16x16/32x32) gösterildiğinden boyut
                        // açıkça istenir - aksi halde belirsiz varsayılan boyut seçimi bulanıklaşmaya
                        // sebep olabiliyordu.
                        customIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
                    }
                }
            }
            catch
            {
                customIcon = System.Drawing.SystemIcons.Application;
            }

            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = customIcon ?? System.Drawing.SystemIcons.Application,
                Text = "AutoMotion",
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += (s, e) => ToggleWindowVisibility();
        }

        private void ToggleWindowVisibility()
        {
            if (IsVisible) Hide();
            else ShowWindowFromTray();
        }

        /// <summary>
        /// Başarım kazanıldığında aracın çevresinde kısa bir kıvılcım patlaması (3.4 / C-1).
        ///
        /// Mevcut parçacık sistemi kullanılıyor - yeni bir çizim yolu açmıyor, dolayısıyla
        /// önbelleklenmiş bitmap parıltısından ve sahne havuzlarından kendiliğinden yararlanıyor.
        /// Araç yoksa (filo boş) kutlama ekranın üst ortasında belirir; bildirimi görsel olarak
        /// destekleyen kısa bir vurgu, ekranı kaplayan bir gösteri değil.
        /// </summary>
        private void PlayAchievementCelebration(Vehicle target)
        {
            if (lowEffectsMode) return;

            double x = target?.X ?? RenderCanvas.ActualWidth / 2;
            double y = target != null ? target.Y - target.Height / 2 : 120;
            if (double.IsNaN(x) || x <= 0) return;

            foreach (var spark in Celebration.BuildBurst(Random.Shared))
            {
                particles.Add(new VehicleParticle
                {
                    X = x,
                    Y = y,
                    VelocityX = spark.VelocityX,
                    VelocityY = spark.VelocityY,
                    Size = spark.Size,
                    Opacity = 1.0,
                    Color = (Color)ColorConverter.ConvertFromString(spark.Color)
                });
            }

            // Sahne boşken render döngüsü askıya alınmış olabilir - kutlama görünmeden kaybolmasın.
            StartRenderLoop();
        }

        private void ShowWindowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;

        // Pencere kapanana kadar canlı tutulmalı - bkz. ApplyTaskbarIconWorkaround.
        private System.Drawing.Icon windowIcon;
        private System.Drawing.Icon windowIconSmall;

        /// <summary>
        /// AllowsTransparency="True" + WindowStyle="None" kombinasyonundaki WPF pencerelerinde
        /// bilinen bir sorun: XAML'deki Window.Icon özelliği görev çubuğu düğmesine doğru şekilde
        /// yansımıyor, Windows onun yerine varsayılan/genel bir simge gösteriyor.
        ///
        /// Asıl kök sebep (ve önceki denemenin neden işe yaramadığı): ikon nesnesi eskiden `using`
        /// ile hemen bu metodun sonunda dispose ediliyordu - bu, WM_SETICON'a verilen HICON handle'ını
        /// anında geçersiz kılıyordu. WM_SETICON, verilen handle'ı KOPYALAMAZ, sadece referans alır;
        /// pencere kendisi görünürde çalışmaya devam etse de, Görev Çubuğu/Alt+Tab/Görev Yöneticisi
        /// gibi ikonu GECİKMELİ (lazy, ihtiyaç anında) okuyan yerler artık geçersiz bir handle'a
        /// bakıp jenerik/varsayılan ikona düşüyordu - "logoyu koydum ama hâlâ varsayılan görünüyor"
        /// şikayetinin gerçek nedeni buydu. Çözüm: ikonu pencere kapanana kadar canlı tutmak (instance
        /// alanında saklamak) ve ayrıca WM_SETICON'a ek olarak pencere SINIFININ (GCLP_HICON/HICONSM)
        /// ikonunu da ayarlamak - bazı kabuk (shell) yüzeyleri per-pencere yerine sınıf ikonunu okur.
        /// </summary>
        private void ApplyTaskbarIconWorkaround()
        {
            try
            {
                if (windowIcon == null)
                {
                    // ÖNEMLİ: parametresiz `new Icon(stream)` .ico içindeki BOYUTU BELİRSİZ/varsayılan
                    // (genelde ~32x32) bir kareyi seçiyordu ve bu AYNI küçük bitmap hem ICON_SMALL
                    // hem ICON_BIG (görev çubuğu/Alt+Tab'daki büyük simge) için kullanılıyordu - büyük
                    // slotta 32x32 kaynak zorla büyütülünce bulanık/küçük-kalmış görünüyordu ("logo çok
                    // küçük duruyor"). logo.ico içinde 16/32/48/64/128/256 kareleri gömülü olduğundan
                    // (bkz. Register/logo.ico) artık boyut AÇIKÇA istenip GDI+'nin en YAKIN/en BÜYÜK
                    // net kareyi seçmesi sağlanıyor: küçük simge için 16x16, büyük simge/sınıf ikonu
                    // için 256x256.
                    var iconUri = new Uri("pack://application:,,,/Assets/logo.ico");

                    var sriSmall = Application.GetResourceStream(iconUri);
                    if (sriSmall == null) return;
                    using (var streamSmall = sriSmall.Stream)
                        windowIconSmall = new System.Drawing.Icon(streamSmall, new System.Drawing.Size(16, 16));

                    var sriBig = Application.GetResourceStream(iconUri);
                    if (sriBig == null) return;
                    using (var streamBig = sriBig.Stream)
                        windowIcon = new System.Drawing.Icon(streamBig, new System.Drawing.Size(256, 256));
                }

                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, windowIconSmall.Handle);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, windowIcon.Handle);
                SetClassLongPtr(hwnd, GCLP_HICON, windowIcon.Handle);
                SetClassLongPtr(hwnd, GCLP_HICONSM, windowIconSmall.Handle);
            }
            catch { /* ikon kozmetik - başarısız olursa sessizce atlanır */ }
        }
    }
}
