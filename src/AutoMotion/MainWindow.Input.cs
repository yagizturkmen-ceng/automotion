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
    // Fare ve klavye girdisi, araç sağ tık menüsü.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        // Mouse etkileşimi
        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point posAny = e.GetPosition(RenderCanvas);

            // Fiziksel Rampa Çizimi: orta fare tuşuyla çizime başla
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                isDrawingRamp = true;
                lastRampPoint = posAny;
                RenderCanvas.CaptureMouse();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(RenderCanvas);

                // Manuel Doldurma: bidon elimizdeyken bir araca tıklarsak dolduruyoruz
                if (fuelCanActive)
                {
                    foreach (var vehicle in gameCore.AutoMotions)
                    {
                        if (vehicle.IsPointInside(pos.X, pos.Y))
                        {
                            vehicle.Fuel = Math.Min(vehicle.MaxFuel, vehicle.Fuel + 50);
                            ShowToast(Loc.T("msg.manual_refuel_done", VehicleDisplayName(vehicle)));
                            soundEngine.PlaySuccess();
                            break;
                        }
                    }
                    CancelFuelCan();
                    return;
                }

                // Shift tuşu basılıysa ve seçili araç(lar) varsa ROTA/GRUP HEDEFİ ata
                if (Keyboard.Modifiers == ModifierKeys.Shift && (selectedVehicle != null || selectedVehicles.Count > 0))
                {
                    var groupTargets = selectedVehicles.Count > 1 ? selectedVehicles : new List<Vehicle> { selectedVehicle };

                    if (groupTargets.Count > 1)
                    {
                        // VIP Konvoy: dağınık gitmek yerine en "lüks" (en ağır/büyük) araç öne
                        // geçer, geri kalanlar onun arkasında V-formasyonu alır.
                        var lead = groupTargets.OrderByDescending(v => v.Mass).First();
                        double dirAngle = Math.Atan2(pos.Y - lead.Y, pos.X - lead.X);
                        double behindAngle = dirAngle + Math.PI;

                        lead.CustomPath.Clear();
                        lead.CustomPath.Add(pos);

                        int slot = 0;
                        foreach (var v in groupTargets)
                        {
                            if (v == null || v == lead) continue;
                            slot++;
                            int side = slot % 2 == 0 ? 1 : -1;
                            int rank = (slot + 1) / 2;
                            double spread = rank * 28;
                            double perpAngle = behindAngle + side * (Math.PI / 4);
                            var slotPoint = new Point(
                                pos.X + Math.Cos(behindAngle) * rank * 24 + Math.Cos(perpAngle) * spread,
                                pos.Y + Math.Sin(behindAngle) * rank * 24 + Math.Sin(perpAngle) * spread);

                            v.CustomPath.Clear();
                            v.CustomPath.Add(slotPoint);
                        }
                    }
                    else if (selectedVehicle != null)
                    {
                        selectedVehicle.CustomPath.Clear();
                        selectedVehicle.CustomPath.Add(pos);
                    }

                    soundEngine.PlayDrift();
                    return;
                }

                // Önce araç tıklamalarını tara
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsPointInside(pos.X, pos.Y))
                    {
                        // Trafik Sıkışıklığı: bir kenarda uzun süre birikmiş araca tıklanınca
                        // dağıtılır - dürtülüp zeminin ortasına doğru fırlatılır.
                        if (jamStuckTicks.TryGetValue(vehicle, out double jamTicks) && jamTicks > 240)
                        {
                            jamStuckTicks.Remove(vehicle);
                            double dispersDir = vehicle.EdgeSide == -1 ? 1 : -1;
                            vehicle.VelocityX = dispersDir * (3 + Random.Shared.NextDouble() * 2);
                            vehicle.VelocityY = -2;
                            soundEngine.PlayHorn(vehicle.Type);
                            return;
                        }

                        selectedVehicle = vehicle;
                        draggedVehicle = vehicle;
                        dragOffset = new Point(pos.X - vehicle.X, pos.Y - vehicle.Y);

                        // Sürükleme durumunu başlat
                        vehicle.IsDragged = true;
                        vehicle.DragLastX = vehicle.X;
                        vehicle.DragLastY = vehicle.Y;
                        vehicle.DragManualRotation = 0; // her yeni tutuşta manuel döndürme sıfırdan başlasın
                        vehicle.VelocityX = 0;
                        vehicle.VelocityY = 0;

                        selectedVehicles.Clear(); // Çoklu seçimi sıfırla

                        // Fareyi yakala: yakalama olmadan fare çok hızlı hareket ettirildiğinde
                        // imleç anlık olarak pencere/canvas dışına çıkabiliyor, bu durumda MouseMove
                        // olayları kaçırılıp araç "havada asılı/takılı" kalmış gibi görünüyordu.
                        RenderCanvas.CaptureMouse();

                        RenderCanvas.Cursor = Cursors.Hand;

                        // Liste seçileni de eşle
                        VehicleListBox.SelectedItem = vehicle;
                        return;
                    }
                }

                // Araç tıklanmadıysa engelleri tara
                foreach (var obs in gameCore.Obstacles)
                {
                    if (obs.IsPointInside(pos.X, pos.Y))
                    {
                        draggedObstacle = obs;
                        obstacleDragOffset = new Point(pos.X - obs.X, pos.Y - obs.Y);
                        RenderCanvas.CaptureMouse();
                        RenderCanvas.Cursor = Cursors.SizeAll;
                        PlayUiClick();
                        return;
                    }
                }

                // Boş ekrana tıklanmışsa, Çoklu Seçim çerçevesini (Drag Selection) başlat
                isDragSelecting = true;
                dragSelectionStart = pos;
                selectedVehicles.Clear();
                selectedVehicle = null;

                if (selectionBox != null) RenderCanvas.Children.Remove(selectionBox);
                selectionBox = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                    StrokeThickness = 1.0,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)),
                    Visibility = Visibility.Visible
                };
                Canvas.SetLeft(selectionBox, pos.X);
                Canvas.SetTop(selectionBox, pos.Y);
                RenderCanvas.Children.Add(selectionBox);
            }
        }

        /// <summary>
        /// Fare, sürüklemeden/tıklamadan sadece bir aracın yakınından geçince küçük bir ihtimalle
        /// mutlu tepki (zıplama) versin - global klavye/fare kancası kullanmadan güvenli bir
        /// "etkinliğe tepki" hissi vermek için (Bongo Cat/Shimeji tarzı).
        /// </summary>
        private void CheckHoverReaction(Point pos)
        {
            if (draggedVehicle != null || draggedObstacle != null || isDragSelecting) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                double dx = pos.X - vehicle.X;
                double dy = pos.Y - vehicle.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < Math.Max(vehicle.Width, vehicle.Height) * 0.9)
                {
                    // Önceden sadece fareyle üzerinden geçince (hiçbir tıklama/okşama olmadan) küçük
                    // bir ihtimalle araç zıplayıp sallanıyordu - kullanıcı için rahatsız edici bir
                    // "aşağı yukarı hareket" hissi veriyordu. Artık sadece bilinçli okşama hareketi
                    // (CheckPetting - sağa-sola hızlı sürtme) tepki tetikliyor, salt hover tetiklemiyor.
                    CheckPetting(vehicle, pos.X);
                }
                else
                {
                    pettingState.Remove(vehicle);
                }
            }
        }

        /// <summary>
        /// Araç Okşama: fare aracın üzerindeyken kısa sürede birkaç kez sağa-sola sürtülürse
        /// (yön değiştirme sayısı eşiği aşarsa) araç motorunu bağırtıp zıplar ve kalp gösterir.
        /// </summary>
        private void CheckPetting(Vehicle vehicle, double mouseX)
        {
            pettingState.TryGetValue(vehicle, out var state);

            double deltaX = mouseX - state.lastX;
            if (Math.Abs(deltaX) < 1.5)
            {
                pettingState[vehicle] = (mouseX, state.direction, state.flips, state.windowTicks + 1);
                return;
            }

            int dir = deltaX > 0 ? 1 : -1;
            int flips = state.flips;
            int windowTicks = state.windowTicks + 1;

            if (state.direction != 0 && dir != state.direction)
            {
                flips++;
            }

            if (windowTicks > 40) // ~0.65sn pencere dolunca sıfırla
            {
                flips = 0;
                windowTicks = 0;
            }

            if (flips >= 3)
            {
                vehicle.ReactToClick();
                vehicle.Happiness = Math.Min(100, vehicle.Happiness + 15);
                soundEngine.PlayRev(vehicle.Type);
                ShowPettingHeart(vehicle);
                flips = 0;
                windowTicks = 0;
            }

            pettingState[vehicle] = (mouseX, dir, flips, windowTicks);
        }

        private void ShowPettingHeart(Vehicle vehicle)
        {
            var heart = new TextBlock { Text = "💖", FontSize = 16 };
            Canvas.SetLeft(heart, vehicle.X - 8);
            Canvas.SetTop(heart, vehicle.Y - vehicle.Height / 2 - 26);
            RenderCanvas.Children.Add(heart);

            var floatUp = new DoubleAnimation { From = 0, To = -20, Duration = TimeSpan.FromMilliseconds(700) };
            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(700) };
            fadeOut.Completed += (s, e) => RenderCanvas.Children.Remove(heart);
            var transform = new TranslateTransform();
            heart.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, floatUp);
            heart.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>Bir araç sürüklenirken Ctrl+fare tekerleği ile aracı manuel döndürmeye yarar -
        /// kullanıcı isteği: özellikle bir duvar/tavandan alınıp bırakılacak aracın yönünü bırakmadan
        /// önce elle düzeltebilsin. Ctrl basılı değilse veya sürüklenen bir araç yoksa hiçbir şey yapmaz.</summary>
        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (draggedVehicle == null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            draggedVehicle.DragManualRotation += e.Delta > 0 ? 15 : -15;
            draggedVehicle.Rotation = draggedVehicle.DragManualRotation;
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(RenderCanvas);
            lastMousePos = pos;
            lastUserActivity = DateTime.Now;
            CheckHoverReaction(pos);

            // Fiziksel Rampa Çizimi: orta tuş basılıyken sürüklenen yol kalıcı bir rampa segmenti olur
            if (isDrawingRamp && e.MiddleButton == MouseButtonState.Pressed)
            {
                double segLen = Math.Sqrt(Math.Pow(pos.X - lastRampPoint.X, 2) + Math.Pow(pos.Y - lastRampPoint.Y, 2));
                if (segLen > 8)
                {
                    physicsRamps.Add((lastRampPoint, pos, 600)); // ~10 saniye ömür
                    lastRampPoint = pos;
                }
                return;
            }

            // Detaylı Yıkama: W basılıyken sol tuş basılı sürüklenirse üzerindeki aracın kiri azalır
            if (washModeActive && e.LeftButton == MouseButtonState.Pressed)
            {
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsPointInside(pos.X, pos.Y))
                    {
                        bool wasDirty = vehicle.Dirtiness > 0.05;
                        vehicle.Dirtiness = Math.Max(0, vehicle.Dirtiness - 0.03);
                        if (wasDirty && vehicle.Dirtiness <= 0.001)
                        {
                            gameCore.GameData.VehiclesWashedClean++;
                        }
                        break;
                    }
                }
            }

            // Shift rota çizim modu aktifse noktaları ekle (yalnızca tekil seçimde serbest çizim - grup komutunda tek nokta yeterli)
            if (Keyboard.Modifiers == ModifierKeys.Shift && selectedVehicle != null && selectedVehicles.Count <= 1 && e.LeftButton == MouseButtonState.Pressed)
            {
                if (selectedVehicle.CustomPath.Count > 0)
                {
                    var lastPt = selectedVehicle.CustomPath.Last();
                    double d = Math.Sqrt((lastPt.X - pos.X) * (lastPt.X - pos.X) + (lastPt.Y - pos.Y) * (lastPt.Y - pos.Y));
                    if (d > 12)
                    {
                        selectedVehicle.CustomPath.Add(pos);
                    }
                }
                else
                {
                    selectedVehicle.CustomPath.Add(pos);
                }
                return;
            }

            // Ekran kenarında saklanan araçların üstüne gelindiğinde geri çıkması (Hover Pop-out)
            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsSideHidden)
                {
                    double dist = Math.Sqrt((vehicle.X - pos.X) * (vehicle.X - pos.X) + (vehicle.Y - pos.Y) * (vehicle.Y - pos.Y));
                    if (dist < 55) // Mouse imleci yeterince yakınsa saklanma durumunu boz
                    {
                        vehicle.IsSideHidden = false;
                        if (vehicle.HiddenDirection == "left")
                        {
                            vehicle.X = vehicle.Width + 15;
                            vehicle.VelocityX = 4.0;
                        }
                        else
                        {
                            vehicle.X = RenderCanvas.ActualWidth - vehicle.Width - 15;
                            vehicle.VelocityX = -4.0;
                        }
                        soundEngine.PlaySuccess();
                    }
                }
            }

            if (draggedVehicle != null && e.LeftButton == MouseButtonState.Pressed)
            {
                double targetX = pos.X - dragOffset.X;
                double targetY = pos.Y - dragOffset.Y;

                // Fırlatma hızı hesabı (VPet benzeri fırlatma ivmesi) - ağır araçlar (kamyon/tren)
                // aynı fare hızıyla sürüklendiğinde daha yavaş/ağır fırlar, hafifler (bisiklet/motor)
                // gerçekçi biçimde çok daha kolay fırlatılabiliyor.
                double flingFactor = Math.Clamp(0.75 / Math.Sqrt(draggedVehicle.Mass), 0.3, 1.3);
                draggedVehicle.VelocityX = (targetX - draggedVehicle.X) * flingFactor;
                draggedVehicle.VelocityY = (targetY - draggedVehicle.Y) * flingFactor;

                draggedVehicle.X = targetX;
                draggedVehicle.Y = targetY;
                RenderCanvas.Cursor = Cursors.ScrollAll;
            }
            else if (draggedObstacle != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Engeli sürükle
                draggedObstacle.X = pos.X - obstacleDragOffset.X;
                draggedObstacle.Y = pos.Y - obstacleDragOffset.Y;
                RenderCanvas.Cursor = Cursors.SizeAll;
            }
            else if (isDragSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                // Çoklu seçim kutusu boyutlarını güncelle
                double x = Math.Min(pos.X, dragSelectionStart.X);
                double y = Math.Min(pos.Y, dragSelectionStart.Y);
                double width = Math.Max(1.0, Math.Abs(pos.X - dragSelectionStart.X));
                double height = Math.Max(1.0, Math.Abs(pos.Y - dragSelectionStart.Y));

                Canvas.SetLeft(selectionBox, x);
                Canvas.SetTop(selectionBox, y);
                selectionBox.Width = width;
                selectionBox.Height = height;
            }
        }

        private void RenderCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (draggedVehicle != null)
            {
                draggedVehicle.IsDragged = false;
                draggedVehicle.ResetEdgeStateAfterDrag();
                draggedVehicle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
            draggedObstacle = null;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDrawingRamp && e.MiddleButton == MouseButtonState.Released)
            {
                isDrawingRamp = false;
                RenderCanvas.ReleaseMouseCapture();
                return;
            }

            if (draggedVehicle != null)
            {
                // Sürüklemeyi sonlandır, limitli fırlatma hızı ile serbest bırak
                draggedVehicle.IsDragged = false;
                draggedVehicle.IsFlying = false; // bırakınca yerçekimine tabi olup düşsün

                // Sürükleme öncesi bir duvara/tavana tırmanmış (EdgeSide != 0) olabilir; bu durumda
                // bırakıldığı yeni konumdan bağımsız olarak yerçekimi o eski duvara doğru yanlamasına
                // çekiyor ve araç havada "asılı" kalıyormuş gibi görünüyordu. Bırakınca zemin moduna
                // sıfırlanır - gerçek konumuna göre gerekiyorsa fizik kendi kenar tırmanmasını yeniden
                // tespit eder. ResetEdgeStateAfterDrag ayrıca gövde açısını (WallAngleSmoothed) da
                // zemin yönelimine akıcı geçişle döndürür - aksi halde araç tavandan/duvardan
                // bırakılınca zeminde kalıcı olarak ters/yan görünürdü (bkz. Models/AutoMotion.cs).
                draggedVehicle.ResetEdgeStateAfterDrag();

                draggedVehicle.VelocityX = Math.Clamp(draggedVehicle.VelocityX, -25, 25);
                draggedVehicle.VelocityY = Math.Clamp(draggedVehicle.VelocityY, -25, 25);

                draggedVehicle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
                RenderCanvas.ReleaseMouseCapture();
            }

            if (draggedObstacle != null)
            {
                draggedObstacle = null;
                RenderCanvas.Cursor = Cursors.Arrow;
                RenderCanvas.ReleaseMouseCapture();
            }

            if (isDragSelecting)
            {
                // Çoklu seçim alanını hesapla ve içindeki araçları seç
                double x = Canvas.GetLeft(selectionBox);
                double y = Canvas.GetTop(selectionBox);
                double w = selectionBox.Width;
                double h = selectionBox.Height;
                Rect selectRect = new Rect(x, y, w, h);

                foreach (var vehicle in gameCore.AutoMotions)
                {
                    Rect vehRect = new Rect(vehicle.X - vehicle.Width / 2, vehicle.Y - vehicle.Height / 2, vehicle.Width, vehicle.Height);
                    if (selectRect.IntersectsWith(vehRect))
                    {
                        selectedVehicles.Add(vehicle);
                    }
                }

                if (selectedVehicles.Count > 0)
                {
                    selectedVehicle = selectedVehicles[0];
                    VehicleListBox.SelectedItem = selectedVehicle;
                    soundEngine.PlaySuccess();
                }

                RenderCanvas.Children.Remove(selectionBox);
                selectionBox = null;
                isDragSelecting = false;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
        }

        private void Canvas_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(RenderCanvas);

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsPointInside(pos.X, pos.Y))
                {
                    selectedVehicle = vehicle;
                    ShowVehicleContextMenu(vehicle);
                    return;
                }
            }
        }

        /// <summary>Bir araca sağ tıklayınca hızlı erişim menüsü (kilitle/uçur/dorse/sil/eşya kullan)</summary>
        private void ShowVehicleContextMenu(Vehicle vehicle)
        {
            var menu = new ContextMenu();

            var lockItem = new MenuItem { Header = Loc.T(vehicle.IsLocked ? "ctx.unlock" : "ctx.lock") };
            lockItem.Click += (s, e) =>
            {
                vehicle.IsLocked = !vehicle.IsLocked;
                // Kilitlerken artık garaj ızgarasına ışınlanmıyor - araç TAM OLARAK bulunduğu
                // konumda donuyor (bkz. ToggleLockBtn_Click).
                vehicle.DockX = -1;
                vehicle.DockY = -1;
                PlayUiClick();
            };
            menu.Items.Add(lockItem);

            var flyItem = new MenuItem { Header = Loc.T(vehicle.IsFlying ? "ctx.fly_off" : "ctx.fly_on") };
            flyItem.Click += (s, e) => { vehicle.IsFlying = !vehicle.IsFlying; if (vehicle.IsFlying) vehicle.IsLocked = false; PlayUiClick(); };
            menu.Items.Add(flyItem);

            if (gameCore.Items.Count > 0)
            {
                var useItem = new MenuItem { Header = Loc.T("ctx.use_item") };
                useItem.Click += (s, e) =>
                {
                    if (gameCore.UseItem(gameCore.Items.Count - 1, vehicle))
                    {
                        soundEngine.PlaySuccess();
                        RefreshItemsList();
                    }
                };
                menu.Items.Add(useItem);
            }

            var trailerMenu = new MenuItem { Header = Loc.T("ui.dorse_ekipman") };
            foreach (var kv in Vehicle.TrailerCatalog)
            {
                string trailerKey = kv.Key;
                var trailerItem = new MenuItem { Header = $"{kv.Value.emoji} {Vehicle.TrailerLabel(trailerKey)}" };
                trailerItem.Click += (s, e) => { vehicle.TrailerType = trailerKey; PlayUiClick(); };
                trailerMenu.Items.Add(trailerItem);
            }
            trailerMenu.Items.Add(new Separator());
            var removeTrailerItem = new MenuItem { Header = "✕ " + Loc.T("ui.dorseyi_cikar") };
            removeTrailerItem.Click += (s, e) => { vehicle.TrailerType = ""; PlayUiClick(); };
            trailerMenu.Items.Add(removeTrailerItem);
            menu.Items.Add(trailerMenu);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = Loc.T("ctx.delete") };
            deleteItem.Click += (s, e) =>
            {
                gameCore.RemoveAutoMotion(vehicle);
                if (selectedVehicle == vehicle) selectedVehicle = null;
                PlayUiClick();
            };
            menu.Items.Add(deleteItem);

            RenderCanvas.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            lastUserActivity = DateTime.Now;

            suppressUiClickSound = true;
            try
            {
                HandleWindowKeyDown(e);
            }
            finally
            {
                suppressUiClickSound = false;
            }
        }

        private void HandleWindowKeyDown(KeyEventArgs e)
        {
            wpmRace.RegisterKeystroke();

            // Sürüş Modu aktifken klavye tamamen araç kontrolüne ayrılır (WASD/Ok tuşları),
            // diğer kısayollar (R, C, S, L...) yanlışlıkla tetiklenmesin diye devre dışı kalır.
            if (drivenVehicle != null)
            {
                if (e.Key == Key.Space || e.Key == Key.Escape)
                {
                    StopDriving();
                }
                else if (e.Key == Key.Tab)
                {
                    SwitchDrivenVehicle();
                }
                else if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
                {
                    pressedDriveKeys.Add(e.Key);
                }
                else if (e.Key >= Key.D0 && e.Key <= Key.D9)
                {
                    int gear = e.Key - Key.D0;
                    if (gear <= drivenVehicle.TopGear && gearSlotPoints.ContainsKey(gear))
                    {
                        SetGear(gear);
                    }
                }
                else if (e.Key == Key.X && !e.IsRepeat)
                {
                    RangeSwitchBtn_Click(null, null);
                }
                else if (e.Key == Key.H && !e.IsRepeat)
                {
                    soundEngine.PlayHorn(drivenVehicle.Type);
                }
                e.Handled = true;
                return;
            }

            // Klavye Kısayollarını Kapat: eklenen tek-harf kısayolların sayısı arttıkça (B, V, K, A,
            // T, M, R... gibi) bunlar, Yazı Yarışı (WPM) gibi serbest tuş vuruşu gerektiren
            // özelliklerle çakışıyordu - kullanıcı yazarken yanlışlıkla uçuş/kilit/AI modlarını açıp
            // kapatabiliyordu. Bu anahtar açıkken (kısayollar KAPALI) tuş vuruşu sayımı (yukarıdaki
            // wpmRace.RegisterKeystroke) hâlâ çalışır, sadece aşağıdaki tekil-tuş kısayolları atlanır.
            if (!shortcutsEnabled) return;

            switch (e.Key)
            {
                case Key.T:
                    ThemeBtn_Click(null, null);
                    break;
                case Key.M:
                    SoundBtn_Click(null, null);
                    break;
                case Key.R:
                    AddVehicleBtn_Click(null, null);
                    break;
                case Key.C:
                    ClearBtn_Click(null, null);
                    break;
                case Key.S:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        SaveBtn_Click(null, null);
                    break;
                case Key.L:
                    LoadBtn_Click(null, null);
                    break;
                case Key.F:
                    AddFuelBtn_Click(null, null);
                    break;
                case Key.H:
                    AddRepairBtn_Click(null, null);
                    break;
                case Key.Q:
                    CallVehicleBtn_Click(null, null);
                    break;
                case Key.E:
                    ExpansionsBtn_Click(null, null);
                    break;
                case Key.Delete:
                    DeleteSelectedBtn_Click(null, null);
                    break;
                case Key.B:
                    ToggleLearningAIBtn_Click(null, null);
                    break;
                case Key.V:
                    ToggleFlyBtn_Click(null, null);
                    break;
                case Key.K:
                    ToggleLockBtn_Click(null, null);
                    break;
                case Key.G:
                    ToggleAllUI_Click(null, null);
                    break;
                case Key.A:
                    ToggleAutoMove_Click(null, null);
                    break;
                case Key.Space:
                    // Sürüş modu henüz kapalıyken Boşluk = başlat (seçili araç varsa); açıkken
                    // durdurma zaten üstteki drivenVehicle != null dalında ele alınıyor.
                    ToggleDriveBtn_Click(null, null);
                    break;
                case Key.P:
                    // İmleç Trafik Değneği: basılı tutulduğunda imleç "Dur" işaretine döner,
                    // yakındaki araçlar acil fren yapar (bkz. GameLoop_Tick).
                    if (!stopSignActive)
                    {
                        stopSignActive = true;
                        RenderCanvas.Cursor = Cursors.No;
                    }
                    break;
                case Key.W:
                    // Detaylı Yıkama: basılı tutulup fare aracın üzerinde sürüklenince kir azalır
                    washModeActive = true;
                    break;
                case Key.Escape:
                    if (fuelCanActive) CancelFuelCan();
                    break;
            }

            // Tuş Vuruşu Sismik Etkisi: uygulama içinde yazılan her tuş, düşük ihtimalle rastgele
            // bir aracı hafifçe zıplatır - global bir klavye kancası KULLANILMAZ (gizlilik nedeniyle),
            // sadece bu pencereye gelen tuşlara tepki verilir.
            if (gameCore != null && gameCore.AutoMotions.Count > 0 && Random.Shared.NextDouble() < 0.15)
            {
                var shaken = gameCore.AutoMotions[Random.Shared.Next(gameCore.AutoMotions.Count)];
                if (!shaken.IsDragged && !shaken.IsLocked)
                {
                    shaken.VelocityY -= 0.8;
                }
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            pressedDriveKeys.Remove(e.Key);

            if (e.Key == Key.P)
            {
                stopSignActive = false;
                RenderCanvas.Cursor = Cursors.Arrow;
            }
            else if (e.Key == Key.W)
            {
                washModeActive = false;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // Pencere Alt+F4, görev çubuğundan sağ-tık "Kapat" vb. başka bir yoldan da kapanabilir -
            // ExitApplication() zaten kendini tekrar çalıştırmaya karşı korumalı (bkz. isExiting),
            // bu yüzden asıl temizlik mantığı tek bir yerde (ExitApplication) toplanır.
            ExitApplication(isSessionEnding ? "Windows oturumu kapandı" : "pencere kapandı");
        }
    }
}
