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
    // Win32 birlikte çalışma: pencere sınırları, tıklama geçirgenliği, panel isabet testleri.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        // Win32 API pencereleri okumak için (Window Clambering)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private System.Windows.Forms.NotifyIcon trayIcon;

        /// <summary>
        /// Pencereyi (ve dolayısıyla araçların gezineceği alanı) ekranın tamamına yayar - sadece
        /// ALT kenar görev çubuğunun üstünde durur, sol/sağ/üst kenarlar tam ekran sınırına kadar
        /// gider (WorkArea kullanmanın aksine, görev çubuğu sadece alt kenarı kısıtlar).
        /// </summary>
        private void ApplyFullScreenBoundsTaskbarOnBottom()
        {
            var workArea = SystemParameters.WorkArea;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = workArea.Bottom; // görev çubuğunun üst kenarı
        }

        // Ana pencere Topmost=true olduğu için normal ToolTip popup'ları bazen bu pencerenin
        // ARKASINDA açılıp görünmez oluyordu (WPF'in bilinen bir Topmost+ToolTip sorunu) - bu
        // yüzden butonların üstüne gelince "ne olduğu" hiç görünmüyordu. Her ToolTip açıldığında
        // kendi native penceresini de zorla topmost yaparak çözüyoruz.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static void ToolTip_ForceTopmost(object sender, RoutedEventArgs e)
        {
            if (sender is ToolTip tooltip && PresentationSource.FromVisual(tooltip) is System.Windows.Interop.HwndSource hwndSource)
            {
                SetWindowPos(hwndSource.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        // ---- Tam Tıklama-Geçirgenliği (Click-Through) ----
        // Kullanıcı isteği: arka plan tamamen şeffaf (Tam Şeffaf/Şeffaf tema) olduğunda, üstünde
        // hiçbir araç/panel olmayan boş alanlara tıklanınca fare tıklaması bu pencereyi DEĞİL,
        // arkadaki gerçek masaüstü/uygulamayı almalı (VPet ve benzeri masaüstü dostlarının
        // standart davranışı). WPF'in kendi AllowsTransparency+Background=Transparent özelliği
        // sadece GÖRSEL olarak şeffaflık sağlar, fare olaylarını yine de bu pencere yutar - gerçek
        // tıklama geçirgenliği için Win32 WS_EX_TRANSPARENT bayrağı gerekir. Bu bayrak set/kaldır
        // edilirken pencere ya TAMAMEN tıklama-geçirgen ya da TAMAMEN normal olur; bu yüzden fare
        // imlecinin bir araç/panel üzerinde olup olmadığını her karede kontrol edip bayrağı buna
        // göre anlık olarak açıp kapatıyoruz (araç/panel üzerindeyken normal, boşluktayken geçirgen).
        private IntPtr mainHwnd = IntPtr.Zero;
        private bool clickThroughActive = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void SetClickThrough(bool enable)
        {
            if (mainHwnd == IntPtr.Zero || enable == clickThroughActive) return;
            int exStyle = GetWindowLong(mainHwnd, GWL_EXSTYLE);
            exStyle = enable ? (exStyle | WS_EX_TRANSPARENT) : (exStyle & ~WS_EX_TRANSPARENT);
            SetWindowLong(mainHwnd, GWL_EXSTYLE, exStyle);
            clickThroughActive = enable;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Win32Point lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Win32Point { public int X; public int Y; }

        /// <summary>Sadece tamamen şeffaf temalarda (Şeffaf/Tam Şeffaf) çalışır - renkli/koyu bir
        /// arka plan teması seçiliyken tıklama geçirgenliği anlamsız olurdu (görünen renkli katmana
        /// "görünmez" tıklamak kafa karıştırıcı olurdu), o yüzden o durumda her zaman normal davranır.</summary>
        private Win32Point lastClickThroughCursor = new() { X = int.MinValue, Y = int.MinValue };
        private DateTime lastClickThroughEval = DateTime.MinValue;
        private double cachedDpiScaleX, cachedDpiScaleY;

        /// <summary>Pencere başka DPI'lı bir monitöre taşındığında önbelleklenmiş ölçek geçersizleşir.</summary>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            cachedDpiScaleX = 0; // bir sonraki değerlendirmede yeniden okunur
        }

        private void UpdateClickThrough()
        {
            bool themeIsFullyTransparent = currentTheme == 0 || currentTheme == 6;
            if (!themeIsFullyTransparent) { SetClickThrough(false); return; }
            if (mainHwnd == IntPtr.Zero) return;

            // Herhangi bir fare tuşu basılıyken (sürükleme, çoklu seçim kutusu, rampa çizimi vb.
            // devam eden bir etkileşim) bayrağı DEĞİŞTİRME - aksi halde etkileşim ortasında pencere
            // aniden tıklama-geçirgen olup fare olaylarını kaybedebilir.
            if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed)
                return;

            // KRİTİK HATA (önceki sürümde): WPF'in Mouse.GetPosition() metodu OS'a canlı sormaz -
            // InputManager'ın EN SON ALDIĞI fare olayını önbellekten döndürür. Pencere tıklama-geçirgen
            // (WS_EX_TRANSPARENT) hale gelir gelmez bu pencereye artık HİÇ fare olayı gelmiyor, yani
            // konum tamamen DONUYOR. Donan nokta genelde "boşluk" olduğundan (zaten o yüzden geçirgen
            // olmuştu), kod bir daha asla "araç/panel üstündeyim" sonucuna varamıyor ve pencere
            // SONSUZA DEK tıklama-geçirgen kalıp kilitleniyordu (görev çubuğu ikonu, kapat butonu,
            // HİÇBİR ŞEY tıklanamıyordu). Çözüm: ham Win32 GetCursorPos ile HER ZAMAN canlı sorgu -
            // pencere fare olayı alsın almasın çalışır. ScreenToClient fiziksel piksel döndürdüğü için
            // DPI ölçeğine bölünerek WPF'in mantıksal birimine çevrilir (ör. %125 ölçeklemede /1.25).
            if (!GetCursorPos(out var screenPt)) return;

            // İmleç kıpırdamadıysa sonuç da değişemez: aşağıdaki iş (DPI hesabı, filo taraması,
            // panel dikdörtgenleri) her karede tekrarlanıyordu. Yine de tamamen imlece bağlamıyoruz -
            // panel taşınıp boyutlandığında imleç sabitken de durum değişebilir, o yüzden kısa bir
            // aralıkla (150 ms) her hâlükârda tazeleniyor.
            bool cursorMoved = screenPt.X != lastClickThroughCursor.X || screenPt.Y != lastClickThroughCursor.Y;
            if (!cursorMoved && (DateTime.UtcNow - lastClickThroughEval).TotalMilliseconds < 150) return;

            lastClickThroughCursor = screenPt;
            lastClickThroughEval = DateTime.UtcNow;

            var clientPt = screenPt;
            if (!ScreenToClient(mainHwnd, ref clientPt)) return;

            // DPI ölçeği pencere taşınmadıkça değişmez; her karede sormak yerine önbelleklenir
            // (monitörler arası taşımada OnDpiChanged tazeliyor).
            if (cachedDpiScaleX <= 0)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                cachedDpiScaleX = dpi.DpiScaleX;
                cachedDpiScaleY = dpi.DpiScaleY;
            }

            Point pt = new Point(clientPt.X / cachedDpiScaleX, clientPt.Y / cachedDpiScaleY);

            bool overInteractive = IsPointOverVehicleOrObstacle(pt) || IsPointOverVisiblePanel(pt);
            SetClickThrough(!overInteractive);
        }

        private bool IsPointOverVehicleOrObstacle(Point pt)
        {
            const double margin = 6;
            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (pt.X >= vehicle.X - vehicle.Width / 2 - margin && pt.X <= vehicle.X + vehicle.Width / 2 + margin &&
                    pt.Y >= vehicle.Y - vehicle.Height / 2 - margin && pt.Y <= vehicle.Y + vehicle.Height / 2 + margin)
                    return true;
            }
            foreach (var obs in gameCore.Obstacles)
            {
                if (pt.X >= obs.X - obs.Width / 2 - margin && pt.X <= obs.X + obs.Width / 2 + margin &&
                    pt.Y >= obs.Y - obs.Height / 2 - margin && pt.Y <= obs.Y + obs.Height / 2 + margin)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// İmlecin altında GERÇEKTEN bir arayüz öğesi var mı?
        ///
        /// Eskiden bu, her yüzeyin dikdörtgeni hesaplanıp (<c>PointToScreen</c> + ActualWidth/Height)
        /// nokta o dikdörtgende mi diye bakılarak çözülüyordu. ÖLÇÜLEN HATA: konsolide panelin
        /// İÇERİĞİ, panelin kendi kutusunu taşıyor - MasterPanel ekranda y=698'de bitiyor ama
        /// içindeki sekme kontrolü y=852'ye kadar çizilmeye devam ediyor. Dikdörtgen hesabı bu
        /// taşan bölgeyi "boş masaüstü" sayıp pencereyi orada tıklama-geçirgen yapıyordu: panelin
        /// görünen alt ~150 pikseli (BOYUTLANDIRMA TUTAMAKLARI dahil) hiçbir fare olayı almıyordu.
        /// Panel köşesinden sürükleyerek boyutlandırmanın "çalışmaması" bundandı.
        /// (<c>VisualTreeHelper.GetDescendantBounds</c> de çözmüyor: düzen kırpması yüzünden panelin
        /// kendi boyutunu döndürüyor.)
        ///
        /// Çözüm: dikdörtgen matematiği yerine WPF'in KENDİ hit-test'i. "Bu noktada çizili bir öğe
        /// var mı, varsa etkileşimli bir yüzeye mi ait?" sorusunu doğrudan soruyor; taşma, dönüşüm,
        /// ölçek ve düzensiz şekiller kendiliğinden doğru ele alınıyor.
        /// </summary>
        private bool IsPointOverVisiblePanel(Point pt)
        {
            foreach (var surface in InteractiveSurfaces())
            {
                if (surface == null || surface.Visibility != Visibility.Visible) continue;

                var r = RenderedRectOf(surface);
                if (r.IsEmpty) continue;

                // Kenar bandı KADAR dışarı taşırılır: kenardan boyutlandırma (bkz. DetectEdgeAt)
                // kenara dışarıdan yaklaşırken de çalışmalı. Taşırma olmadan pencere tam kenar
                // pikselinde tıklama-geçirgen kalıyordu - yani tutulmak istenen yer ölü oluyordu.
                // (Rect.Contains sağ/alt kenarda dışlayıcıdır, o sınır durumunu da bu kapsıyor.)
                r.Inflate(EdgeGrabThickness, EdgeGrabThickness);
                if (r.Contains(pt)) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // "GERÇEKTEN ÇİZİLEN ALAN" HESABI
        //
        // Bir panelin ekranda kapladığı yer, panelin DÜZEN KUTUSUNDAN büyük olabilir: konsolide
        // "Paneller" penceresinde içerik kutuyu ~150 piksel taşıyor (ölçüldü: panel kutusu y=698'de
        // bitiyor, içindeki sekme kontrolü y=852'ye kadar çiziliyor). WPF taşan kısmı ÇİZER ama
        // ne hit-test'e alır ne de GetDescendantBounds'a yansıtır (düzen kırpması).
        //
        // Sonuç, iki somut hataydı: (a) panelin görünen alt bölgesi "boş masaüstü" sayılıp pencere
        // orada tıklama-geçirgen yapılıyordu, (b) o bölgedeki boyutlandırma tutamağı/kenar hiçbir
        // fare olayı almıyordu. İkisi de aşağıdaki birleşim hesabıyla çözülüyor: görsel ağaç
        // gezilip TÜM alt öğelerin çizim sınırları birleştiriliyor.
        //
        // Maliyet: ağaç gezintisi her karede yapılamaz, o yüzden panel başına önbelleklenip
        // yalnızca boyut/ölçek değiştiğinde veya kısa bir süre geçtiğinde yenileniyor.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private readonly Dictionary<FrameworkElement, (Rect Rect, DateTime At)> renderedRectCache = new();

        private static readonly TimeSpan RenderedRectTtl = TimeSpan.FromMilliseconds(400);

        private Rect RenderedRectOf(FrameworkElement element)
        {
            if (renderedRectCache.TryGetValue(element, out var cached) && DateTime.UtcNow - cached.At < RenderedRectTtl)
                return cached.Rect;

            var rect = ComputeRenderedRect(element);
            renderedRectCache[element] = (rect, DateTime.UtcNow);
            return rect;
        }

        /// <summary>Öğenin ve tüm alt öğelerinin çizim sınırlarının birleşimi - PENCERE koordinatında.</summary>
        private Rect ComputeRenderedRect(FrameworkElement element)
        {
            try
            {
                var local = UnionDescendantBounds(element, 0);
                if (local.IsEmpty) return Rect.Empty;

                var topLeft = PointFromScreen(element.PointToScreen(local.TopLeft));
                var bottomRight = PointFromScreen(element.PointToScreen(local.BottomRight));
                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return Rect.Empty; // düzen henüz hazır değil
            }
        }

        /// <summary>Görsel ağacı gezip sınırları birleştirir (öğenin KENDİ koordinat uzayında).
        /// Derinlik sınırı, patolojik derin ağaçlarda maliyeti sabit tutmak için.</summary>
        private static Rect UnionDescendantBounds(Visual visual, int depth)
        {
            var bounds = VisualTreeHelper.GetDescendantBounds(visual);
            if (visual is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                bounds.Union(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));

            if (depth >= 12) return bounds;

            int count = VisualTreeHelper.GetChildrenCount(visual);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(visual, i) is not Visual child) continue;
                if (child is UIElement ui && ui.Visibility != Visibility.Visible) continue;

                var childBounds = UnionDescendantBounds(child, depth + 1);
                if (childBounds.IsEmpty) continue;

                // Çocuğun kutusunu ebeveynin uzayına taşı (offset + dönüşümler dahil).
                var t = child.TransformToAncestor(visual);
                bounds.Union(t.TransformBounds(childBounds));
            }

            return bounds;
        }

        /// <summary>
        /// Fare olaylarını ALMASI gereken tüm görünür yüzeyler.
        ///
        /// KRİTİK: "Panelden Ayır" ile bağımsız pencereye çıkarılan paneller de buraya DAHİL olmalı.
        /// Önceden yalnızca sabit bir liste (MasterPanel, modallar, dock) kontrol ediliyordu; ayrılan
        /// bir panelin üzerine gelindiğinde kod burayı "boş masaüstü" sanıp pencereyi tıklama-geçirgen
        /// (WS_EX_TRANSPARENT) yapıyordu. Sonuç: ayrılan panel HİÇ fare olayı almıyor, üzerindeki
        /// butonlar tıklanamıyordu - panel görünüyor ama tamamen ölü duruyordu.
        /// </summary>
        private IEnumerable<FrameworkElement> InteractiveSurfaces()
        {
            yield return MasterPanel;
            yield return DockWrapper;
            yield return SaveListModal;
            yield return SpawnModal;
            yield return HelpModal;
            yield return CustomDialogOverlay;
            yield return ToastPanel;

            // Ayrılmış paneller (bağımsız, sürüklenebilir mini pencereler)
            foreach (var panel in detachedPanels)
                yield return panel;
        }
    }
}
