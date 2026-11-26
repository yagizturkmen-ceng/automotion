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
    // Sürüş modu ve vites kolu.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        /// <summary>
        /// Sürüş Modu aç/kapat: açıldığında seçili araç AI'dan çıkar, oyuncu ok tuşları/WASD ile
        /// 1:1 doğrudan kontrol eder; sağ tarafta araç tipine göre vites arayüzü açılır.
        /// </summary>
        private void ToggleDriveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (drivenVehicle != null)
            {
                StopDriving();
                return;
            }

            if (selectedVehicle == null)
            {
                ShowToast(Loc.T("msg.surus_modu_icin_once_bir_arac"));
                return;
            }

            StartDriving(selectedVehicle);
        }

        /// <summary>Verilen aracı sürüş moduna alır; vites paneli araç tipine göre kurulur.</summary>
        private void StartDriving(Vehicle vehicle)
        {
            if (drivenVehicle != null) drivenVehicle.IsDriven = false;

            drivenVehicle = vehicle;
            drivenVehicle.IsDriven = true;
            drivenVehicle.IsLocked = false;
            pressedDriveKeys.Clear();
            smoothedThrottle = 0;
            smoothedSteerInput = 0;
            SteeringWheelRotate.Angle = 0;

            bool isTwoWheeler = drivenVehicle.Type == "bike" || drivenVehicle.Type == "motorcycle";
            gearShifterIsTwoWheeler = isTwoWheeler;
            gearLastColumn = -1;
            BuildGearSlots(drivenVehicle);

            RangeSwitchBtn.Visibility = drivenVehicle.HasRangeSplitter ? Visibility.Visible : Visibility.Collapsed;
            drivenVehicle.RangeHigh = true;
            UpdateRangeSwitchVisual();

            drivenVehicle.Gear = 1;
            SetGear(1, animateKnob: false);
            GearVehicleNameTxt.Text = string.IsNullOrEmpty(drivenVehicle.Name) ? drivenVehicle.Emoji : $"{drivenVehicle.Emoji} {drivenVehicle.Name}";

            selectedVehicle = drivenVehicle;
            VehicleListBox.SelectedItem = drivenVehicle;

            AnimateVisibility(GearPanel, true);
            ShowMasterPanelTab(TabGear);
            PlayUiClick();
        }

        /// <summary>Tab: sürüş modundan çıkmadan bir sonraki araca geçer (filo içinde hızlı gezinme).</summary>
        private void SwitchDrivenVehicle()
        {
            var vehicles = gameCore.AutoMotions;
            if (vehicles.Count <= 1 || drivenVehicle == null) return;

            int currentIndex = vehicles.IndexOf(drivenVehicle);
            int nextIndex = (currentIndex + 1) % vehicles.Count;
            StartDriving(vehicles[nextIndex]);
        }

        private void StopDriving()
        {
            if (drivenVehicle != null)
            {
                drivenVehicle.IsDriven = false;
                drivenVehicle.IsBraking = false;
            }
            drivenVehicle = null;
            pressedDriveKeys.Clear();
            smoothedThrottle = 0;
            smoothedSteerInput = 0;
            SteeringWheelRotate.Angle = 0;
            AnimateVisibility(GearPanel, false);
            PlayUiClick();
        }

        private readonly Dictionary<int, Point> gearSlotPoints = new();
        private readonly Dictionary<int, TextBlock> gearSlotLabels = new();
        private readonly Dictionary<int, int> gearSlotColumns = new();

        // Vites kolunun GERÇEK bir şanzıman gibi kısıtlı ("kılavuzlu") sürüklenmesi için gereken
        // yuva geometrisi - bkz. GearKnob_MouseMove. Serbest 2D sürükleme yerine, kol sadece nötr
        // hizasındayken yanlara kayabilir; bir kulvara girince o kulvarın X'ine kilitlenip sadece
        // ileri/geri (Y) hareket edebilir - tıpkı gerçek bir H-pattern vites kolu gibi.
        private readonly Dictionary<int, double> gearColumnXPositions = new();
        private bool gearIsSequentialLayout;
        private double gearNeutralY;
        private double gearLaneTopY;
        private double gearLaneBottomY;
        private int gearDragColumn = -1;
        private const double GearNeutralBandTolerance = 15.0;

        /// <summary>Vites kolu topuzunun her geçerli vites için oturduğu konum (kutunun sol-üst köşesi, 26x26 topuz).</summary>
        private Point GetGearSlotPoint(int gear, bool isTwoWheeler) =>
            gearSlotPoints.TryGetValue(gear, out var p) ? p : new Point(58, 59);

        /// <summary>
        /// Vites kolu yuvasını (H-pattern veya sekans) aracın GERÇEK vites sayısına (TopGear) göre
        /// baştan kurar - 6 viteslik bir otomobil ile 8 viteslik bir traktör/3 viteslik bir bisiklet
        /// artık aynı sabit 4 pozisyonlu düzeni paylaşmıyor, her biri kendi gerçek şanzımanına uygun
        /// bir yuva görüyor. İki tekerlekliler (motosiklet/bisiklet) gerçek bir sekans şanzıman gibi
        /// TEK kulvarda "1 aşağı, geri kalanı yukarı" düzenini kullanır (gerçek motosiklet vites
        /// pedalı mantığı); diğerleri klasik H-pattern kullanır (gerekirse birden fazla kulvar).
        /// </summary>
        private void BuildGearSlots(Vehicle vehicle)
        {
            gearSlotPoints.Clear();
            gearSlotColumns.Clear();
            gearColumnXPositions.Clear();
            GearSlotLabelsCanvas.Children.Clear();
            gearSlotLabels.Clear();
            gearDragColumn = -1;

            bool isTwoWheeler = vehicle.Type == "bike" || vehicle.Type == "motorcycle";
            gearIsSequentialLayout = isTwoWheeler;
            int topGear = Math.Max(1, vehicle.TopGear);
            var pathData = new System.Text.StringBuilder();
            // Path mini-dili ondalık ayırıcı olarak nokta bekler - Türkçe (tr-TR) gibi virgülü ondalık
            // ayırıcı kullanan bir sistem kültüründe ToString() virgül üretip Geometry.Parse'ı
            // bozabilirdi (virgül zaten koordinat ayırıcısı). Kültürden bağımsız biçimlendirme ile
            // bu garanti altına alınıyor.
            static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (isTwoWheeler)
            {
                // Sekans şanzıman: tek dikey kulvar, alttan yukarı 1-N-2-3-4-5-6 (gerçek motosiklet
                // vites pedalı düzeni - "bir aşağı vites (1), geri kalanı yukarı").
                const double x = 68;
                const double spacing = 20;
                double bottomY = 130;

                double Y(int slot) => bottomY - slot * spacing; // slot 0=gear1, 1=N, 2=gear2, ...

                gearSlotPoints[1] = new Point(x, Y(0));
                gearSlotPoints[0] = new Point(x, Y(1));
                for (int g = 2; g <= topGear; g++)
                    gearSlotPoints[g] = new Point(x, Y(g));

                gearColumnXPositions[0] = x;
                gearLaneTopY = Y(topGear);
                gearLaneBottomY = Y(0);
                gearNeutralY = Y(1);

                pathData.Append($"M{Inv(x + 13)},{Inv(Y(0) + 13)} L{Inv(x + 13)},{Inv(Y(topGear) + 13)}");

                AddGearLabel(1, x, Y(0), "#3DA9FC");
                AddGearLabel(0, x, Y(1), "#95E1D3", 8);
                for (int g = 2; g <= topGear; g++)
                    AddGearLabel(g, x, Y(g), "#3DA9FC");
            }
            else
            {
                // Klasik H-pattern: her kulvarda 2 vites (tek sayı üstte, çift sayı altta) - gerçek
                // bir manuel şanzımanın 1-2 / 3-4 / 5-6 (/ 7-8) düzeni. Geri vites (R) en sağdaki
                // ekstra kulvarda, sadece üstte durur.
                const double firstColX = 7;
                const double colSpacing = 24;
                const double rowTopY = 4;
                const double rowBottomY = 113;
                const double rowMidY = 59;

                int colCountForGears = (topGear + 1) / 2; // ceil(topGear/2)
                double ColX(double col) => firstColX + col * colSpacing;

                gearLaneTopY = rowTopY;
                gearLaneBottomY = rowBottomY;
                gearNeutralY = rowMidY;

                for (int g = 1; g <= topGear; g++)
                {
                    int col = (g - 1) / 2;
                    bool top = g % 2 == 1;
                    var pt = new Point(ColX(col), top ? rowTopY : rowBottomY);
                    gearSlotPoints[g] = pt;
                    gearSlotColumns[g] = col;
                    gearColumnXPositions[col] = ColX(col);
                    AddGearLabel(g, pt.X, pt.Y, "#3DA9FC");

                    pathData.Append($"M{Inv(ColX(col) + 13)},{Inv(rowTopY + 13)} L{Inv(ColX(col) + 13)},{Inv(rowBottomY + 13)} ");
                }

                // Geri vites (R) - ayrı kulvar, sadece üstte
                var rPoint = new Point(ColX(colCountForGears), rowTopY);
                gearSlotPoints[-1] = rPoint;
                gearSlotColumns[-1] = colCountForGears;
                gearColumnXPositions[colCountForGears] = ColX(colCountForGears);
                AddGearLabel(-1, rPoint.X, rPoint.Y, "#FF6B6B", 9, "R");
                pathData.Append($"M{Inv(rPoint.X + 13)},{Inv(rowTopY + 13)} L{Inv(rPoint.X + 13)},{Inv(rowTopY + 13 + 30)} ");

                // Nötr (N) - kulvarların ortasında yatay bağlantı üzerinde
                double nCenterCol = (colCountForGears - 1) / 2.0;
                var nPoint = new Point(ColX(nCenterCol), rowMidY);
                gearSlotPoints[0] = nPoint;
                gearSlotColumns[0] = -99; // N özel: kulvar değişimi tetiklemesin
                AddGearLabel(0, nPoint.X, nPoint.Y, "#95E1D3", 8);

                pathData.Append($"M{Inv(ColX(0) + 13)},{Inv(rowMidY + 13)} L{Inv(ColX(colCountForGears - 1) + 13)},{Inv(rowMidY + 13)}");
            }

            GearTrackPath.Data = Geometry.Parse(pathData.ToString());
        }

        private void AddGearLabel(int gear, double x, double y, string color, double fontSize = 9, string text = null)
        {
            var lbl = new TextBlock
            {
                Text = text ?? gear.ToString(),
                FontFamily = (FontFamily)FindResource("PixelFont"),
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y);
            GearSlotLabelsCanvas.Children.Add(lbl);
            gearSlotLabels[gear] = lbl;
        }

        /// <summary>Aracın vitesini değiştirir; topuzu ilgili H-yuva konumuna hareket ettirir ve etiketleri günceller.
        /// Kulvar değişimlerinde (ör. 1'den 3'e) gerçek bir şanzıman gibi önce nötre uğrar.</summary>
        private void SetGear(int gear, bool animateKnob = true)
        {
            if (drivenVehicle == null) return;
            // Gerçek vites değişimi (ilk kurulum değil) ise kısa bir güç kesintisi uygulanır -
            // gerçek bir manuel şanzımanda debriyaj/vites geçişinde hissedilen "boşluk" hissi.
            if (animateKnob && drivenVehicle.Gear != gear)
            {
                drivenVehicle.GearShiftCooldown = 14;
            }
            drivenVehicle.Gear = gear;
            string rangeSuffix = drivenVehicle.HasRangeSplitter && gear > 0 ? (drivenVehicle.RangeHigh ? "Y" : "D") : "";
            GearCurrentTxt.Text = gear switch
            {
                -1 => "Vites: R",
                0 => "Vites: N",
                _ => $"Vites: {gear}{rangeSuffix}"
            };

            HighlightActiveGearSlot(gear);

            var target = GetGearSlotPoint(gear, gearShifterIsTwoWheeler);
            int currentColumn = GetGearColumn(gear);
            if (animateKnob && !gearShifterIsTwoWheeler && currentColumn != gearLastColumn && gearLastColumn != -1 && currentColumn != -99 && gearLastColumn != -99)
            {
                // Kulvar değişiyor: topuz önce nötre, sonra hedef vitese hareket eder (gerçek H-shift hissi).
                var neutral = GetGearSlotPoint(0, false);
                AnimateKnobTo(neutral, TimeSpan.FromMilliseconds(90), () => AnimateKnobTo(target, TimeSpan.FromMilliseconds(110)));
            }
            else if (animateKnob)
            {
                AnimateKnobTo(target, TimeSpan.FromMilliseconds(120));
            }
            else
            {
                Canvas.SetLeft(GearKnob, target.X);
                Canvas.SetTop(GearKnob, target.Y);
            }
            gearLastColumn = currentColumn;

            PopAnimate(GearCurrentTxtScale);
            soundEngine.PlayDrift(); // vites geçiş "klik" sesi
        }

        /// <summary>Verilen vitesin hangi H-pattern kulvarında olduğunu döndürür (BuildGearSlots'ta
        /// hesaplanıp gearSlotColumns'a kaydedilir) - kulvar değişimlerinde önce nötre uğrama animasyonu
        /// tetiklemek için kullanılır. N için özel -99 değeri döner (kulvar değişimi saymaz).</summary>
        private int GetGearColumn(int gear) => gearSlotColumns.TryGetValue(gear, out var c) ? c : -1;

        /// <summary>MudRunner tarzı "ağır" manuel vites kolu hissi: gerçek bir şanzıman kolu direnç
        /// gösterip hafifçe fazla gider (overshoot) sonra yuvaya oturur - basit bir ease-out yerine
        /// bu "hedefi aşıp geri toparlanma" hissi kolun gerçek bir mekanizma gibi ağırlığı olduğunu
        /// hissettirir. Vitese oturunca kısa bir mekanik "click/thunk" sarsıntısı (topuz burulması +
        /// hafif titreşim) eklenir - elle bastırıp yuvaya kilitlenen bir kolun hissi.
        /// </summary>
        private void AnimateKnobTo(Point target, TimeSpan duration, Action onComplete = null)
        {
            // BackEase: hedefe varmadan önce kısa bir "fazla gitme" (overshoot) sonra geri toparlanma -
            // ağır/dirençli bir mekanik kol hissi verir, düz ease-out'tan çok daha "gerçek".
            var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.65 };
            var animX = new DoubleAnimation(target.X, duration) { EasingFunction = easing };
            var animY = new DoubleAnimation(target.Y, duration) { EasingFunction = easing };
            if (onComplete != null) animY.Completed += (s, e) => onComplete();
            GearKnob.BeginAnimation(Canvas.LeftProperty, animX);
            GearKnob.BeginAnimation(Canvas.TopProperty, animY);

            // Topuz burulması (torque wobble): el kolu ittirirken hafifçe burulup yuvaya oturunca
            // düzelen bir dönüş - gerçek bir vites kolunun mekanik direncini/burulmasını taklit eder.
            double wobbleDir = Random.Shared.NextDouble() < 0.5 ? 1 : -1;
            var rotWobble = new DoubleAnimationUsingKeyFrames();
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(14 * wobbleDir, KeyTime.FromPercent(0.35), new QuadraticEase()));
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(-6 * wobbleDir, KeyTime.FromPercent(0.65), new QuadraticEase()));
            rotWobble.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0), new QuadraticEase()));
            rotWobble.Duration = duration + TimeSpan.FromMilliseconds(90);
            GearKnobRotate.BeginAnimation(RotateTransform.AngleProperty, rotWobble);

            // Mekanik "thunk" - hedefe varınca topuz kısa süreliğine sıkışıp (küçülüp) sonra normale
            // dönerek yuvaya kilitlenme hissi verir (önceki tek yönlü "pop"tan daha sert/mekanik).
            var pop = new DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromPercent(0)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.18, KeyTime.FromPercent(0.55)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.8)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            GearKnobScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            GearKnobScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            // Vites miline oturma sarsıntısı: kolun gövdesi (GearShaftLine) de birlikte titrer - tüm
            // mekanizmanın sarsıldığı hissini güçlendirir.
            var shaftShake = new DoubleAnimationUsingKeyFrames();
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromPercent(0.5)));
            shaftShake.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            GearShaftLine.BeginAnimation(OpacityProperty, shaftShake);
        }

        /// <summary>Aktif vitesin yuva etiketini parlak sarı vurguyla işaretler, diğerlerini söndürür.</summary>
        private void HighlightActiveGearSlot(int gear)
        {
            foreach (var (g, lbl) in gearSlotLabels)
            {
                bool active = g == gear;
                lbl.Effect = active ? new DropShadowEffect { Color = Color.FromRgb(0xFB, 0xCA, 0x1F), BlurRadius = 10, ShadowDepth = 0, Opacity = 1.0 } : null;
                lbl.Opacity = active ? 1.0 : 0.55;
            }
        }

        private bool gearShifterIsTwoWheeler;
        private int gearLastColumn = -1;
        private bool gearKnobDragging;

        /// <summary>Range (Aralık) anahtarına tıklanınca Düşük/Yüksek arasında geçiş yapar - gerçek bir
        /// kamyon şanzımanındaki splitter anahtarı gibi (aynı 6 ana vites, iki farklı hız/tork bandı).</summary>
        private void RangeSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (drivenVehicle == null || !drivenVehicle.HasRangeSplitter) return;
            drivenVehicle.RangeHigh = !drivenVehicle.RangeHigh;
            UpdateRangeSwitchVisual();
            SetGear(drivenVehicle.Gear, animateKnob: false); // metni/vurguyu yenile, topuz yerinde kalır
            PlayUiClick();
        }

        private void UpdateRangeSwitchVisual()
        {
            if (drivenVehicle == null) return;
            RangeSwitchBtn.Content = Loc.T(drivenVehicle.RangeHigh ? "ui.aralik_yuksek" : "ui.range_low");
        }

        // MudRunner'daki fare ile tutulan vites kolunun "sallanan/ağır" hissi: kol imlece ANINDA
        // ışınlanmıyor, bir yay-sönümleyici (spring-damper) fizikle geriden takip ediyor - hızlı
        // hareket ettirince kol geriden sallana sallana yetişiyor, tıpkı gerçek ağır bir kolu elle
        // tutup sallamak gibi. Hedef konum (fareyle kısıtlanmış yuva pozisyonu) ile GÖRÜNEN konum
        // ayrı tutuluyor; her karede (GameLoop_Tick) ikisi arasındaki fark yayla kapatılıyor.
        private double gearKnobTargetX, gearKnobTargetY;
        private double gearKnobActualX, gearKnobActualY;
        private double gearKnobVelX, gearKnobVelY;

        private void GearKnob_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (drivenVehicle == null) return;
            gearKnobDragging = true;
            GearKnob.CaptureMouse();
            GearKnob.BeginAnimation(Canvas.LeftProperty, null);
            GearKnob.BeginAnimation(Canvas.TopProperty, null);
            GearKnob.BeginAnimation(RotateTransform.AngleProperty, null);
            GearKnobRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            gearKnobTargetX = gearKnobActualX = Canvas.GetLeft(GearKnob);
            gearKnobTargetY = gearKnobActualY = Canvas.GetTop(GearKnob);
            gearKnobVelX = gearKnobVelY = 0;

            // Sürüklemeye, elin şu an içinde bulunduğu kulvardan başlar (gerçek bir vitesi tutup
            // ittirmek gibi) - N'den başlamıyorsa hemen yanlara kaymaz, önce o kulvarda kalır.
            int startCol = GetGearColumn(drivenVehicle.Gear);
            gearDragColumn = startCol == -99 ? -1 : startCol;
        }

        /// <summary>
        /// Vites kolu artık serbest bir 2D top gibi değil, GERÇEK bir şanzıman kolu gibi
        /// sürükleniyor: kol sadece nötr hizasındayken (yatay bar üzerinde) yanlara kayabilir; bir
        /// kulvara girdiğinde o kulvarın X'ine kilitlenip sadece ileri/geri (Y) hareket edebilir.
        /// Bu metod sadece HEDEF konumu günceller - görünen konum UpdateGearKnobDragPhysics'te
        /// (her oyun karesinde) yay fiziğiyle bu hedefi takip eder (bkz. MudRunner sallanma hissi).
        /// </summary>
        private void GearKnob_MouseMove(object sender, MouseEventArgs e)
        {
            if (!gearKnobDragging || drivenVehicle == null) return;
            var pos = e.GetPosition(GearShifterCanvas);

            double knobX, knobY;
            if (gearIsSequentialLayout)
            {
                // Sekans şanzıman: tek kulvar, X her zaman sabit - sadece ileri/geri (Y) serbest.
                knobX = gearColumnXPositions[0];
                knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
            }
            else
            {
                bool nearNeutral = Math.Abs(pos.Y - 13 - gearNeutralY) < GearNeutralBandTolerance;
                if (nearNeutral)
                {
                    // Nötr hizasında: kol yanlara serbestçe kayabilir (kulvar henüz seçilmedi).
                    gearDragColumn = -1;
                    knobY = gearNeutralY;
                    knobX = pos.X - 13;
                }
                else if (gearDragColumn == -1)
                {
                    // Nötr bardan yeni çıkılıyor: fareye en yakın kulvara "kilitlen".
                    double mouseColX = pos.X - 13;
                    gearDragColumn = gearColumnXPositions.OrderBy(kv => Math.Abs(kv.Value - mouseColX)).First().Key;
                    knobX = gearColumnXPositions[gearDragColumn];
                    knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
                }
                else
                {
                    // Zaten bir kulvarın içinde: X kilitli kalır, sadece ileri/geri hareket eder.
                    knobX = gearColumnXPositions[gearDragColumn];
                    knobY = Math.Clamp(pos.Y - 13, gearLaneTopY, gearLaneBottomY);
                }
            }

            gearKnobTargetX = Math.Clamp(knobX, 0, GearShifterCanvas.Width - 26);
            gearKnobTargetY = knobY;
        }

        /// <summary>Her oyun karesinde (GameLoop_Tick'ten) çağrılır - vites kolunu bir yay-sönümleyici
        /// ile hedef konuma doğru "sallana sallana" hareket ettirir ve hıza bağlı hafif bir burulma
        /// (pendulum sallanışı) ekler. Sadece sürüklerken çalışır.</summary>
        /// <summary>SnowRunner/MudRunner tarzı gerçek bir vites kolu hissi: kolun gövdesi (GearShaftLine/
        /// Shadow/Highlight) her karede topuzun O ANKİ (Canvas.GetLeft/Top - storyboard animasyonuyla,
        /// sürüklenirken veya boşta sallanırken FARK ETMEKSİZİN her zaman güncel render değerini verir)
        /// merkezine yeniden çizilir; taban ucu sabit mil yatağında kalır. Böylece topuz H-yuvasında
        /// nereye giderse gitsin kol onu görsel olarak takip edip gerçekçi biçimde eğilir - eskiden kol
        /// XAML'de sabit koordinatlarla çizilmiş tek bir çizgiydi ve topuzdan tamamen kopuk dururdu.</summary>
        private void SyncGearShaftLine()
        {
            if (GearPanel.Visibility != Visibility.Visible) return;

            double knobCenterX = Canvas.GetLeft(GearKnob) + GearKnob.Width / 2.0;
            double knobCenterY = Canvas.GetTop(GearKnob) + GearKnob.Height / 2.0;
            if (double.IsNaN(knobCenterX) || double.IsNaN(knobCenterY)) return;

            GearShaftLine.X1 = knobCenterX;
            GearShaftLine.Y1 = knobCenterY;
            GearShaftLineShadow.X1 = knobCenterX + 1.5;
            GearShaftLineShadow.Y1 = knobCenterY + 1.5;

            // Parlama çizgisi kolun gövdesine paralel, hafif içeride kalır - metal bir çubuğun üstündeki
            // ışık yansımasını taklit eder.
            GearShaftHighlight.X1 = knobCenterX - 1.5;
            GearShaftHighlight.Y1 = knobCenterY + 2;
        }

        private void UpdateGearKnobDragPhysics()
        {
            if (!gearKnobDragging) return;

            const double stiffness = 0.30;
            const double damping = 0.72;

            double dx = gearKnobTargetX - gearKnobActualX;
            double dy = gearKnobTargetY - gearKnobActualY;
            gearKnobVelX = (gearKnobVelX + dx * stiffness) * damping;
            gearKnobVelY = (gearKnobVelY + dy * stiffness) * damping;
            gearKnobActualX += gearKnobVelX;
            gearKnobActualY += gearKnobVelY;

            Canvas.SetLeft(GearKnob, gearKnobActualX);
            Canvas.SetTop(GearKnob, gearKnobActualY);

            // Sallanma (sway): kol hızlı hareket ettirilince ağırlığıyla hafifçe geriden "salınır" -
            // MudRunner'daki elle tutulup sallanan kol hissi.
            double sway = Math.Clamp(-gearKnobVelX * 3.5, -22, 22);
            GearKnobRotate.Angle = sway;
        }

        private void GearKnob_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!gearKnobDragging || drivenVehicle == null) return;
            gearKnobDragging = false;
            GearKnob.ReleaseMouseCapture();

            double curLeft = gearKnobActualX;
            double curTop = gearKnobActualY;

            int nearestGear = 0;
            double bestDist = double.MaxValue;
            foreach (var (g, slot) in gearSlotPoints)
            {
                double dist = Math.Pow(slot.X - curLeft, 2) + Math.Pow(slot.Y - curTop, 2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearestGear = g;
                }
            }

            SetGear(nearestGear);
        }

        /// <summary>Kısa bir "pop" (büyü-küçül) animasyonu - vites/hız değişimlerine canlılık katar.</summary>
        private void PopAnimate(ScaleTransform target)
        {
            var scaleAnim = new DoubleAnimationUsingKeyFrames();
            scaleAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.35, KeyTime.FromPercent(0)));
            scaleAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1)));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(220);
            target.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        // Klavye
        private bool suppressUiClickSound = false;
    }
}
