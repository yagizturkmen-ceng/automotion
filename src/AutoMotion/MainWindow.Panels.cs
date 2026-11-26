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
    // Paneller: göster/gizle, yerleşim, ölçek, kenardan boyutlandırma, sürükleme, sabitleme.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        private void ToggleAllUI_Click(object sender, RoutedEventArgs e)
        {
            allUIHidden = !allUIHidden;
            AnimateVisibility(DockWrapper, !allUIHidden);
            AnimateVisibility(PanelsContainer, !allUIHidden && panelsVisible);
            PlayUiClick();
        }

        private void PanelBody_Toggle(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FrameworkElement body)
            {
                bool show = body.Visibility != Visibility.Visible;
                body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                btn.Content = show ? "⏷" : "⏵";
                PlayUiClick();
            }
        }

        private DispatcherTimer toastTimer;

        /// <summary>Sağ altta kısa süreliğine beliren bildirim (başarım/yakıt/hasar uyarısı) gösterir.</summary>
        private void ShowToast(string text)
        {
            ToastText.Text = text;
            ToastPanel.Opacity = 1;

            toastTimer?.Stop();
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(400) };
                ToastPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            toastTimer.Start();
        }

        // Yumuşak fade ile göster/gizle
        private void AnimateVisibility(UIElement element, bool show)
        {
            var anim = new DoubleAnimation
            {
                From = show ? 0 : 1,
                To = show ? 1 : 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            if (show)
            {
                element.Visibility = Visibility.Visible;
                element.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            else
            {
                anim.Completed += (s, e2) => element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        /// <summary>Auto (Height belirtilmemiş) bir panelin gerçek yerleşim yüksekliğini döndürür.
        /// Border.Height doğrudan okunursa Auto panellerde NaN döner - bottom-anchor hesaplarını
        /// bozup panelin ekranın üstüne (VehicleInfoPanel'in üstüne) yanlış konumlanmasına sebep oluyordu.</summary>
        private static double MeasuredHeight(FrameworkElement el)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return el.DesiredSize.Height;
        }

        /// <summary>
        /// Kullanıcı isteği: arayüz çok dağınık/karışık duruyordu - eskiden ekranın altı ayrı
        /// köşesine yerleşen İstatistikler/Seçili Araç/Vites/Eşyalar/Hızlı İşlemler/Eklentiler
        /// panelleri artık TEK bir "☰ Paneller" sekmeli penceresinde birleştiriliyor. Panellerin
        /// KOPYASI değil, AYNI Border nesneleri (tüm x:Name referansları, event handler'ları ve
        /// veri bağlamalarıyla birlikte) PanelsContainer'dan çıkarılıp ilgili TabItem'ın içeriği
        /// olarak yeniden ebeveynleniyor - bu yüzden geri kalan tüm kod (StatsPanelBody, GearVehicleNameTxt,
        /// AchievementsListControl vb.) hiç değişmeden çalışmaya devam ediyor.
        /// </summary>
        private void MoveConsolidatedPanelsIntoTabs()
        {
            void Move(Border panel, TabItem tab)
            {
                PanelsContainer.Children.Remove(panel);
                panelOriginalWidths[panel] = panel.Width; // Ayır butonuyla tekrar bağımsız pencereye çıkarken kullanılacak
                panelHomeTabs[panel] = tab;
                panel.Width = double.NaN; // artık sekme genişliğine göre otomatik boyutlanır
                // AnimatedPanel'in "Loaded" tetikleyicili opacity-fade'i sekme içeriğiyle uyumsuz
                // (bkz. TabbedPanel tanımındaki not) - sekmeler değiştikçe panel Unload/Load olup
                // tetikleyici tekrar tekrar ateşleniyor, bu da panelin kalıcı olarak görünmez
                // (Opacity≈0) takılı kalmasına sebep olabiliyordu.
                panel.Style = (Style)FindResource("TabbedPanel");
                panel.Opacity = 1;
                tab.Content = panel;
            }

            Move(StatsPanel, TabStats);
            Move(VehicleInfoPanel, TabVehicle);
            Move(GearPanel, TabGear);
            Move(ItemShopPanel, TabShop);
            Move(QuickActionsPanel, TabQuick);
            Move(ExpansionsPanel, TabExpansions);

            // Sekme içine taşınan panellerin görünürlüğünü artık hangi sekmenin seçili olduğu
            // belirliyor - eskiden panel bazında ayrı ayrı gizli/görünür olma anlamı kalmadı.
            StatsPanel.Visibility = Visibility.Visible;
            VehicleInfoPanel.Visibility = Visibility.Visible;
            ItemShopPanel.Visibility = Visibility.Visible;
            QuickActionsPanel.Visibility = Visibility.Visible;
            ExpansionsPanel.Visibility = Visibility.Visible;
            ExpansionsPanel.Opacity = 1;
            // GearPanel bilerek dokunulmadı: sürüş modu dışında Collapsed kalır (StartDriving/
            // StopDriving zaten AnimateVisibility ile bunu yönetiyor), böylece Vites sekmesi
            // sürmüyorken boş görünür.

            // Panel gövdelerinin bir kısmı eskiden "varsayılan katlı" başlıyordu (masaüstü daha az
            // kalabalık görünsün diye) - artık zaten tek bir sekmeli pencerede saklı olduklarından
            // o ekstra katlama gereksiz bir tıklama daha ekliyordu, kaldırıldı.
            ItemShopPanelBody.Visibility = Visibility.Visible;
            ItemShopCollapseBtn.Content = "⏷";
            QuickActionsScrollViewer.Visibility = Visibility.Visible;
            QuickActionsCollapseBtn.Content = "⏷";

            // ── Panel kutusunun içeriğiyle aynı boyda olmasını GARANTİLE ──
            //
            // ÖLÇÜLEN HATA: MasterPanel'in düzen kutusu ekranda y=698'de bitiyor ama içindeki sekme
            // kontrolü y=852'ye kadar ÇİZİLMEYE devam ediyordu. WPF, düzen kutusunun dışına taşan
            // içeriği çizer ama HIT-TEST'e almaz - panelin görünen alt/sağ bölgesi (kenarları ve
            // boyutlandırma tutamağı dahil) hiçbir fare olayı almıyordu.
            //
            // SEBEP: TabControl seçili sekmenin içeriğini GEÇ gerçekliyor. Panel ilk ölçüldüğünde
            // sekme içeriği (buraya yeni taşınmış StatsPanel) henüz gerçeklenmemiş olduğundan küçük
            // bir istenen boyut bildiriliyor; içerik gerçeklendiğinde WPF onu mevcut yuvaya
            // yerleştiriyor ama ATALARIN ölçüsünü yeniden istemiyor. Bu yüzden buradaki anlık
            // InvalidateMeasure yetmiyor - içerik daha gerçeklenmemiş oluyor.
            //
            // ÇÖZÜM: ölçümü ilk düzen turundan SONRAYA ertele; ayrıca sekme her değiştiğinde
            // (her sekmenin içeriği farklı yükseklikte) tekrarla.
            RemeasurePanelsAfterLayout();
            MasterTabControl.SelectionChanged += (s, e) => RemeasurePanelsAfterLayout();
        }

        /// <summary>Panellerin ölçüsünü, düzen turu tamamlandıktan SONRA yeniden istetir - sekme
        /// içeriği gerçeklendikten sonra panel kutusu içeriğin gerçek boyuna oturur.</summary>
        private void RemeasurePanelsAfterLayout()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var panel in AllResizablePanels())
                {
                    if (panel == null) continue;
                    panel.InvalidateMeasure();
                    renderedRectCache.Remove(panel);
                }
                MasterPanel.UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PositionDefaultPanels()
        {
            double w = RenderCanvas.ActualWidth;
            double h = RenderCanvas.ActualHeight;

            Canvas.SetLeft(MasterPanel, 10);
            Canvas.SetTop(MasterPanel, 70);
        }

        /// <summary>Sürükle-taşı/sabitleme durumu izlenen tüm panellerin (Border, Sabitle butonu) eşleşmesi.</summary>
        private IEnumerable<(Border panel, Button pinBtn)> AllLayoutTrackedPanels()
        {
            yield return (MasterPanel, MasterPinBtn);

            // Ayrılmış (↗️) paneller de artık bağımsız/sürüklenebilir Canvas öğeleri - konumları ve
            // sabitleme durumları da MasterPanel gibi kaydedilip geri yüklenmeli.
            if (detachedPanels.Contains(StatsPanel)) yield return (StatsPanel, StatsPinBtn);
            if (detachedPanels.Contains(VehicleInfoPanel)) yield return (VehicleInfoPanel, VehicleInfoPinBtn);
            if (detachedPanels.Contains(GearPanel)) yield return (GearPanel, GearPinBtn);
            if (detachedPanels.Contains(ItemShopPanel)) yield return (ItemShopPanel, ItemShopPinBtn);
            if (detachedPanels.Contains(QuickActionsPanel)) yield return (QuickActionsPanel, QuickActionsPinBtn);
            if (detachedPanels.Contains(ExpansionsPanel)) yield return (ExpansionsPanel, ExpansionsPinBtn);
        }

        /// <summary>İlk çalıştırmada (kısayolları/❓ butonunu henüz keşfetmemiş kullanıcı için) tek
        /// seferlik bir karşılama bildirimi gösterir - genel kısayollar önceden hiçbir yerde kalıcı
        /// gösterilmiyordu, kullanıcı tesadüfen bulana kadar bilmiyordu.</summary>
        private void ShowFirstRunOnboardingIfNeeded()
        {
            try
            {
                if (settings.Current.OnboardingShown) return;

                settings.Current.OnboardingShown = true;
                settings.Save();

                ShowToast(Loc.T("msg.hos_geldin_kisayollar_icin_sag_ustteki"));
            }
            catch { /* onboarding kritik değil, sessizce atlanır */ }
        }

        /// <summary>
        /// Paneller sürüklenip taşınsa/sabitlense bile uygulama kapatılıp açılınca hepsi varsayılan
        /// köşelerine dönüyordu - artık konum ve sabitleme durumu diske kaydedilip geri yükleniyor.
        /// </summary>
        private void SavePanelLayout()
        {
            try
            {
                var layout = new Dictionary<string, PanelLayoutEntry>();
                foreach (var (panel, pinBtn) in AllLayoutTrackedPanels())
                {
                    layout[panel.Name] = new PanelLayoutEntry
                    {
                        Scale = GetPanelScale(panel),
                        Left = Canvas.GetLeft(panel),
                        Top = Canvas.GetTop(panel),
                        Pinned = pinnedPanels.Contains(panel)
                    };
                }

                settings.Current.PanelLayout = layout;
                settings.Save();
            }
            catch { }
        }

        private void LoadPanelLayout()
        {
            try
            {
                var layout = settings.Current.PanelLayout;
                if (layout == null || layout.Count == 0) return;

                foreach (var (panel, pinBtn) in AllLayoutTrackedPanels())
                {
                    if (!layout.TryGetValue(panel.Name, out var entry)) continue;

                    // Büyüklük çarpanı: 0 = eski düzen dosyası (alan henüz yoktu) -> varsayılan kalır.
                    if (entry.Scale > 0) SetPanelScale(panel, entry.Scale);

                    if (double.IsNaN(entry.Left) || double.IsNaN(entry.Top)) continue;

                    Canvas.SetLeft(panel, entry.Left);
                    Canvas.SetTop(panel, entry.Top);

                    if (entry.Pinned)
                    {
                        pinnedPanels.Add(panel);
                        pinBtn.Content = "📍";
                        pinBtn.ToolTip = Loc.T("ui.unpin");
                    }
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // PANEL BÜYÜKLÜĞÜ
        //
        // Paneller sabit genişlikte tasarlandığından (ör. 320px) küçük ekranlarda yer kaplıyor,
        // büyük/yüksek çözünürlüklü ekranlarda ise küçük kalıyordu. Her panel başlığındaki ➖ / ➕
        // butonları paneli kademeli olarak küçültüp büyütür.
        //
        // Neden Width değil de LayoutTransform: genişliği değiştirmek yalnızca kutuyu esnetir, metin
        // aynı boyutta kalır ve okunabilirlik değişmez. LayoutTransform ölçeği İÇERİĞİ DE ölçekler -
        // yazılar, butonlar ve kaydırıcılar birlikte büyür. Ayrıca yalnızca tek bir özellik
        // değiştiği için her panel için ayrı düzen çalışması gerekmez.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private const double MinPanelScale = 0.7;
        private const double MaxPanelScale = 1.6;
        private const double PanelScaleStep = 0.1;

        private readonly Dictionary<Border, double> panelScales = new();

        private double GetPanelScale(Border panel)
            => panelScales.TryGetValue(panel, out var s) && s > 0 ? s : 1.0;

        private void SetPanelScale(Border panel, double scale)
        {
            if (panel == null) return;

            scale = Math.Clamp(scale, MinPanelScale, MaxPanelScale);
            panelScales[panel] = scale;

            // LayoutTransform (RenderTransform DEĞİL): düzen sistemi ölçeklenmiş boyutu hesaba katar,
            // böylece panel gerçekten o kadar yer kaplar ve komşu öğelerle çakışmaz.
            panel.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
                ? System.Windows.Media.Transform.Identity
                : new System.Windows.Media.ScaleTransform(scale, scale);
        }

        private void PanelScaleDown_Click(object sender, RoutedEventArgs e) => StepPanelScale(sender, -PanelScaleStep);
        private void PanelScaleUp_Click(object sender, RoutedEventArgs e) => StepPanelScale(sender, +PanelScaleStep);

        private void StepPanelScale(object sender, double delta)
        {
            if (sender is not FrameworkElement btn || btn.Tag is not Border panel) return;

            SetPanelScale(panel, GetPanelScale(panel) + delta);
            KeepPanelOnScreen(panel);
            PlayUiClick();
        }

        // ── Kenardan tutup boyutlandırma (resize grip) ──────────────────────────────────────────
        //
        // Başlıktaki ➖/➕ butonları kademeli ayar için; bu ise serbest, WYSIWYG ayar sağlar:
        // panelin sağ-alt köşesindeki tutamak sürüklendiğinde panel imleci TAKİP EDER (hedef
        // genişlik = imleç ile panelin sol kenarı arası). İki yöntem aynı `panelScales` durumunu
        // paylaşır, dolayısıyla kaydedilen düzen de ortaktır.

        private Border resizingPanel;
        private double resizeStartLeft;

        /// <summary>Her panelin içeriğini bir Grid'e sarıp sağ-alt köşesine boyutlandırma tutamağı
        /// ekler. Kod tarafında yapılıyor çünkü 7 panelin XAML'ini tek tek değiştirmek hem uzun hem
        /// de ileride panel eklenince unutulmaya açık olurdu.</summary>
        private void AttachResizeGrips()
        {
            foreach (var panel in AllResizablePanels())
            {
                if (panel == null || panel.Child is Grid g && g.Name == "PanelResizeHost") continue;

                var content = panel.Child;
                panel.Child = null;

                var host = new Grid { Name = "PanelResizeHost" };
                if (content != null) host.Children.Add(content);
                host.Children.Add(CreateResizeGrip(panel));

                panel.Child = host;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // KENARDAN BOYUTLANDIRMA
        //
        // Önceden yalnızca sağ-alt köşedeki 16x16 tutamaktan boyutlandırma vardı; kullanıcı isteği
        // üzerine artık gerçek bir pencere gibi HER KENARDAN ve HER KÖŞEDEN tutulabiliyor - ve bu
        // tüm panellere uygulanıyor (bkz. AllResizablePanels).
        //
        // Ölçek modeli değişmedi: paneller LayoutTransform ile ORANTILI ölçekleniyor (bkz.
        // SetPanelScale), yani içerik de birlikte büyüyor. Sürüklenen kenarın KARŞISINDAKİ kenar
        // sabit kalır: sağ kenardan çekince sol kenar, sol kenardan çekince sağ kenar yerinde durur.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        /// <summary>Kenara "yakın sayılan" bant kalınlığı - panelin kendi (ölçeklenmemiş) biriminde.</summary>
        private const double EdgeGrabThickness = 9;

        private ResizeEdge activeResizeEdge = ResizeEdge.None;
        private Border edgeResizePanel;
        private double edgeAnchorLeft, edgeAnchorTop, edgeAnchorRight, edgeAnchorBottom;

        /// <summary>
        /// Verilen pencere-koordinatlı nokta, bir panelin hangi kenarına yakın?
        ///
        /// Kenar yakalama PANEL üzerinde değil, PENCERE seviyesinde dinleniyor. Sebep ölçümle
        /// bulundu: panelin görünen alanının bir kısmı düzen kutusunun dışında kalabiliyor ve WPF
        /// orada panele hiç fare olayı göndermiyor (bkz. RenderedRectOf). Panel üzerinde
        /// dinlenseydi, tam da kullanıcının tutmak isteyeceği alt kenar ölü kalırdı. İmleç de
        /// pencere üzerinde ayarlanır - panel hit-test'e kapalı olsa bile görünür.
        /// </summary>
        private ResizeEdge DetectEdgeAt(Point windowPoint, out Border foundPanel)
        {
            foundPanel = null;

            foreach (var panel in AllResizablePanels())
            {
                if (panel == null || panel.Visibility != Visibility.Visible) continue;
                if (pinnedPanels.Contains(panel)) continue; // sabitlenmiş panel boyutlandırılmaz

                var r = RenderedRectOf(panel);
                if (r.IsEmpty) continue;

                // Kenar bandının dışına da bir miktar taşan bir tolerans kutusu
                var outer = new Rect(r.X - EdgeGrabThickness, r.Y - EdgeGrabThickness,
                                     r.Width + EdgeGrabThickness * 2, r.Height + EdgeGrabThickness * 2);
                if (!outer.Contains(windowPoint)) continue;

                bool left = Math.Abs(windowPoint.X - r.Left) <= EdgeGrabThickness;
                bool right = Math.Abs(windowPoint.X - r.Right) <= EdgeGrabThickness;
                bool top = Math.Abs(windowPoint.Y - r.Top) <= EdgeGrabThickness;
                bool bottom = Math.Abs(windowPoint.Y - r.Bottom) <= EdgeGrabThickness;

                ResizeEdge edge =
                    top && left ? ResizeEdge.TopLeft :
                    top && right ? ResizeEdge.TopRight :
                    bottom && left ? ResizeEdge.BottomLeft :
                    bottom && right ? ResizeEdge.BottomRight :
                    left ? ResizeEdge.Left :
                    right ? ResizeEdge.Right :
                    top ? ResizeEdge.Top :
                    bottom ? ResizeEdge.Bottom : ResizeEdge.None;

                if (edge != ResizeEdge.None)
                {
                    foundPanel = panel;
                    return edge;
                }
            }

            return ResizeEdge.None;
        }

        private static Cursor CursorForEdge(ResizeEdge edge) => edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
            ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
            _ => null
        };

        /// <summary>Pencereye gelen HER fare hareketi - kenar yakınlığına göre imleç verir, sürükleme
        /// sürüyorsa boyutu günceller. Window.PreviewMouseMove'a bağlanır.</summary>
        private void Window_EdgeResizeMouseMove(object sender, MouseEventArgs e)
        {
            var pt = e.GetPosition(this);

            if (activeResizeEdge != ResizeEdge.None)
            {
                UpdateEdgeResize(pt);
                e.Handled = true;
                return;
            }

            Cursor = CursorForEdge(DetectEdgeAt(pt, out _)); // panel hit-test'e kapalı olsa da görünür
        }

        private void Window_EdgeResizeMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(this);
            var edge = DetectEdgeAt(pt, out var panel);
            if (edge == ResizeEdge.None || panel == null) return;

            // Panelin şu anki dört kenarının PENCERE koordinatı - sürükleme boyunca karşı kenarı
            // sabit tutmak için başlangıçta bir kez ölçülür.
            var r = RenderedRectOf(panel);
            edgeAnchorLeft = r.Left;
            edgeAnchorTop = r.Top;
            edgeAnchorRight = r.Right;
            edgeAnchorBottom = r.Bottom;

            activeResizeEdge = edge;
            edgeResizePanel = panel;
            Cursor = CursorForEdge(edge);
            CaptureMouse();
            e.Handled = true;
        }

        private void Window_EdgeResizeMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (activeResizeEdge == ResizeEdge.None) return;
            EndEdgeResize();
            e.Handled = true;
        }

        /// <summary>Sürükleme boyunca yeni ölçeği hesaplar. Pencere seviyesinde dinlenir
        /// (bkz. Window_PreviewMouseMove) - imleç panelin dışına çıksa bile bırakılana kadar sürer.</summary>
        private void UpdateEdgeResize(Point cursorInWindow)
        {
            var panel = edgeResizePanel;
            if (panel == null || activeResizeEdge == ResizeEdge.None) return;
            if (panel.ActualWidth <= 0 || panel.ActualHeight <= 0) return;

            double scale = GetPanelScale(panel);
            var edge = activeResizeEdge;

            // Yatay: sürüklenen kenarın karşısındaki kenardan uzaklık = yeni GENİŞLİK (ölçekli).
            double? widthScale = edge switch
            {
                ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight => (cursorInWindow.X - edgeAnchorLeft) / panel.ActualWidth,
                ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft => (edgeAnchorRight - cursorInWindow.X) / panel.ActualWidth,
                _ => null
            };

            double? heightScale = edge switch
            {
                ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight => (cursorInWindow.Y - edgeAnchorTop) / panel.ActualHeight,
                ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight => (edgeAnchorBottom - cursorInWindow.Y) / panel.ActualHeight,
                _ => null
            };

            // Köşelerde iki eksen birden sürükleniyor: ölçek orantılı olduğu için kullanıcının daha
            // çok çektiği eksen kazanır (serbest en/boy oranı mümkün değil - içerik de ölçekleniyor).
            if (widthScale.HasValue && heightScale.HasValue) scale = Math.Max(widthScale.Value, heightScale.Value);
            else if (widthScale.HasValue) scale = widthScale.Value;
            else if (heightScale.HasValue) scale = heightScale.Value;

            SetPanelScale(panel, scale);
            double applied = GetPanelScale(panel); // SetPanelScale alt/üst sınıra kırpar
            renderedRectCache.Remove(panel);       // ölçek değişti, çizim alanı yeniden hesaplanmalı

            // Sol/üst kenardan çekilirken karşı kenarın yerinde kalması için panel kaydırılır.
            // (Sekme içindeki paneller Canvas çocuğu değildir - orada konum anlamsız, atlanır.)
            if (!double.IsNaN(Canvas.GetLeft(panel)))
            {
                if (edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
                    Canvas.SetLeft(panel, edgeAnchorRight - panel.ActualWidth * applied);
                if (edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
                    Canvas.SetTop(panel, edgeAnchorBottom - panel.ActualHeight * applied);
            }
        }

        private void EndEdgeResize()
        {
            if (activeResizeEdge == ResizeEdge.None) return;

            var panel = edgeResizePanel;
            activeResizeEdge = ResizeEdge.None;
            edgeResizePanel = null;

            ReleaseMouseCapture();
            Cursor = null;

            if (panel != null)
            {
                renderedRectCache.Remove(panel); // boyut değişti, önbellek geçersiz
                KeepPanelOnScreen(panel);
                SavePanelLayout(); // yeni büyüklük kalıcı olsun (bkz. SettingsService)
                PlayUiClick();
            }
        }

        private FrameworkElement CreateResizeGrip(Border panel)
        {
            var grip = new Border
            {
                Width = 16,
                Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(230, 0xFB, 0xCA, 0x1F)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                // NEGATİF kenar boşluğu KULLANILMAZ: Border içeriğini kırptığından, dışarı taşan
                // tutamak hem görünmez hem de tıklanamaz oluyordu. Panelin İÇİNDE duruyor.
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.SizeNWSE,
                Tag = panel,
                ToolTip = Loc.T("ui.kenardan_tutup_boyutlandir")
            };

            grip.MouseLeftButtonDown += ResizeGrip_MouseLeftButtonDown;
            grip.MouseMove += ResizeGrip_MouseMove;
            grip.MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
            return grip;
        }

        private IEnumerable<Border> AllResizablePanels()
        {
            yield return MasterPanel;
            yield return StatsPanel;
            yield return VehicleInfoPanel;
            yield return GearPanel;
            yield return ItemShopPanel;
            yield return QuickActionsPanel;
            yield return ExpansionsPanel;

            // Modal pencereler de kenardan boyutlandırılabilir: kullanıcı açısından bunlar da birer
            // "pencere" (Kayıtlar, Araç Ekle, Kısayollar, uyarı kutusu) ve sabit boyutları küçük
            // ekranlarda dar, büyük ekranlarda küçük kalıyordu. Yalnızca AÇIKKEN listeye girerler -
            // kapalı bir modalın kenarı ekranın ortasında görünmez bir tutma alanı yaratmasın.
            if (SaveListModal.Visibility == Visibility.Visible) yield return SaveListModalBox;
            if (SpawnModal.Visibility == Visibility.Visible) yield return SpawnModalBox;
            if (HelpModal.Visibility == Visibility.Visible) yield return HelpModalBox;
            if (CustomDialogOverlay.Visibility == Visibility.Visible) yield return CustomDialogBox;
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement grip || grip.Tag is not Border panel) return;

            resizingPanel = panel;
            // Panelin sol kenarının PENCERE koordinatındaki konumu - ölçek değişse de hedef
            // genişliği buradan ölçeceğiz.
            resizeStartLeft = PointFromScreen(panel.PointToScreen(new Point(0, 0))).X;

            grip.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (resizingPanel == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not FrameworkElement grip) return;

            double unscaledWidth = resizingPanel.ActualWidth;   // LayoutTransform ölçeği İÇERMEZ
            if (unscaledWidth <= 0) return;

            double cursorX = e.GetPosition(this).X;
            double desiredWidth = cursorX - resizeStartLeft;

            SetPanelScale(resizingPanel, desiredWidth / unscaledWidth);
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement grip) grip.ReleaseMouseCapture();
            if (resizingPanel != null)
            {
                KeepPanelOnScreen(resizingPanel);
                resizingPanel = null;
                PlayUiClick();
            }
            e.Handled = true;
        }

        /// <summary>Büyütülen bir panel ekranın dışına taşabilir - sol/üst konumunu görünür alanda
        /// tutar. (Ölçeklenmiş gerçek boyut için render boyutları kullanılır.)</summary>
        private void KeepPanelOnScreen(Border panel)
        {
            try
            {
                panel.UpdateLayout();
                double w = panel.ActualWidth * GetPanelScale(panel);
                double h = panel.ActualHeight * GetPanelScale(panel);
                if (w <= 0 || h <= 0) return;

                double left = Canvas.GetLeft(panel);
                double top = Canvas.GetTop(panel);
                if (double.IsNaN(left) || double.IsNaN(top)) return;

                Canvas.SetLeft(panel, Math.Max(0, Math.Min(left, ActualWidth - w)));
                Canvas.SetTop(panel, Math.Max(0, Math.Min(top, ActualHeight - h)));
            }
            catch { /* düzen henüz hazır olmayabilir */ }
        }

        // Panel sürükleme (⠿ tutamacından)
        private void PanelHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement header || header.Tag is not Border panel) return;
            if (pinnedPanels.Contains(panel)) return;

            draggedPanel = panel;
            var pos = e.GetPosition(PanelsContainer);
            panelDragOffset = new Point(pos.X - Canvas.GetLeft(panel), pos.Y - Canvas.GetTop(panel));
            header.CaptureMouse();
            e.Handled = true;
        }

        private void PanelHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedPanel == null || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(PanelsContainer);
            double newLeft = pos.X - panelDragOffset.X;
            double newTop = pos.Y - panelDragOffset.Y;

            if (PanelsContainer.ActualWidth > 0 && PanelsContainer.ActualHeight > 0)
            {
                newLeft = Math.Max(0, Math.Min(newLeft, PanelsContainer.ActualWidth - draggedPanel.ActualWidth));
                newTop = Math.Max(0, Math.Min(newTop, PanelsContainer.ActualHeight - draggedPanel.ActualHeight));
            }

            Canvas.SetLeft(draggedPanel, newLeft);
            Canvas.SetTop(draggedPanel, newTop);
        }

        private void PanelHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement header)
                header.ReleaseMouseCapture();
            draggedPanel = null;
        }

        // Panel sabitleme (📌 -> 📍)
        private void PanelPin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Border panel) return;

            if (pinnedPanels.Contains(panel))
            {
                pinnedPanels.Remove(panel);
                btn.Content = "📌";
                btn.ToolTip = Loc.T("ui.sabitle");
            }
            else
            {
                pinnedPanels.Add(panel);
                btn.Content = "📍";
                btn.ToolTip = Loc.T("ui.unpin");
            }
            PlayUiClick();
        }

        /// <summary>Panelden Ayır (↗️): panel şu an "☰ Paneller" sekmeli penceresindeyse onu oradan
        /// çıkarıp masaüstünde bağımsız, sürüklenebilir, sabitlenebilir (📌) kendi mini penceresine
        /// dönüştürür. Panel zaten ayrılmışsa aynı buton onu geri sekmeye ("Paneller" penceresine)
        /// gönderir. Sabitle butonu ancak panel bu şekilde ayrıldığında bir anlam ifade eder - sekme
        /// içindeyken sürüklenecek bağımsız bir Canvas konumu yoktur.</summary>
        private void PanelDetach_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Border panel) return;
            if (!panelHomeTabs.TryGetValue(panel, out var homeTab)) return;

            if (detachedPanels.Contains(panel))
            {
                // Geri sekmeye gönder
                PanelsContainer.Children.Remove(panel);
                detachedPanels.Remove(panel);
                pinnedPanels.Remove(panel);
                panel.Width = double.NaN;
                homeTab.Content = panel;
                btn.Content = "↗️";
                btn.ToolTip = Loc.T("ui.panelden_ayir_bagimsiz_pencereye_cikar");
            }
            else
            {
                // Sekmeden ayır, bağımsız pencereye çıkar
                homeTab.Content = null;
                panel.Width = panelOriginalWidths.TryGetValue(panel, out var w) && !double.IsNaN(w) ? w : panel.ActualWidth;

                // Ekranın ortasına yakın, üst üste binmesin diye kademeli (cascade) bir konumda başlat.
                double left = 380 + (detachCascadeIndex % 4) * 40;
                double top = 90 + (detachCascadeIndex % 4) * 40;
                detachCascadeIndex++;

                Canvas.SetLeft(panel, left);
                Canvas.SetTop(panel, top);
                PanelsContainer.Children.Add(panel);
                detachedPanels.Add(panel);

                // TabItem içeriği olarak yaşarken seçili olmayan sekmelerin görsel ağaca hiç
                // bağlanmaması nedeniyle bazı framework'lerde miras kalan IsEnabled/hit-test
                // durumu bozulabiliyordu - panel bağımsız pencereye çıkarken bunlar açıkça
                // sıfırlanıyor, aksi halde ayrılan panel görünür ama tıklanamaz kalıyordu.
                panel.IsEnabled = true;
                panel.IsHitTestVisible = true;
                Panel.SetZIndex(panel, 1000 + detachCascadeIndex); // MasterPanel/Dock'un her zaman üstünde kalsın

                btn.Content = "↙️";
                btn.ToolTip = Loc.T("ui.reattach_to_tab");

                // Sürüş modu dışında Gear paneli sekmedeyken zaten Collapsed kalır (bkz.
                // MoveConsolidatedPanelsIntoTabs) - ayrıldığında kullanıcı görebilsin diye görünür yapılır.
                if (panel.Visibility != Visibility.Visible) panel.Visibility = Visibility.Visible;
            }

            PlayUiClick();
        }

        private void DockCollapseBtn_Click(object sender, RoutedEventArgs e)
        {
            dockExpanded = !dockExpanded;
            DockCollapseBtn.Content = dockExpanded ? "⏶" : "⏷";
            AnimateVisibility(DockIcons, dockExpanded);
            PlayUiClick();
        }
    }
}
