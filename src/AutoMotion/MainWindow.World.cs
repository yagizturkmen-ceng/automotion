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
    // Dünya olayları: öğe kullanımı, polis, trafik, rampalar, görevler, yazı yarışı, hatırlatmalar, hava ve engeller.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        /// <summary>
        /// Eşya kartındaki "Kullan" butonu: önceden envanterdeki eşyaları kullanmanın hiçbir yolu
        /// yoktu (kartlarda sadece bilgi gösteriliyordu, sağ tık menüsü de her zaman körlemesine
        /// SON eklenen eşyayı kullanıyordu). Artık her kart kendi eşyasını, seçili araca, doğrudan uygulayabiliyor.
        /// </summary>
        private async void UseItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Item item) return;

            if (selectedVehicle == null)
            {
                ShowToast(Loc.T("msg.esyayi_kullanmak_icin_once_bir_arac"));
                return;
            }

            int index = gameCore.Items.IndexOf(item);
            if (index < 0) return;

            // Tamirci Mikro-Oyunu: RepairKit doğrudan uygulanmaz, önce vidaları sıkma mini
            // oyunu başlar - tam tamamlanırsa tam iyileşme, süre dolarsa yarım iyileşme olur.
            if (item is RepairKit repairKit)
            {
                if (selectedVehicle.HP >= selectedVehicle.MaxHP)
                {
                    ShowToast(Loc.T("msg.item_not_applicable", item.Name));
                    return;
                }
                gameCore.Items.RemoveAt(index);
                RefreshItemsList();
                StartRepairMinigame(selectedVehicle, repairKit);
                return;
            }

            var usedOnVehicle = selectedVehicle;
            if (gameCore.UseItem(index, selectedVehicle))
            {
                soundEngine.PlaySuccess();

                // Kart hemen listeden kaybolunca sert bir geçiş oluyordu - kısa bir "başarılı"
                // geri bildirimi gösterip ardından listeyi yeniliyoruz.
                btn.IsEnabled = false;
                btn.Content = Loc.T("ui.item_used");
                btn.Background = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14));
                await System.Threading.Tasks.Task.Delay(350);
                RefreshItemsList();

                // Polis Kovalamacası: hız/hızlanma upgrade'i alan araç bir ihtimalle polisin
                // dikkatini çeker.
                if (item is Upgrade upgrade && (upgrade.UpgradeType == "speed" || upgrade.UpgradeType == "acceleration")
                    && Random.Shared.NextDouble() < 0.5)
                {
                    SpawnPoliceChase(usedOnVehicle);
                }
            }
            else
            {
                ShowToast(Loc.T("msg.item_not_applicable", item.Name));
            }
        }

        /// <summary>
        /// Tamirci Mikro-Oyunu: aracın üzerinde beliren birkaç vida ikonunu süre dolmadan hızlıca
        /// tıklamayı gerektirir. Hepsi sıkılırsa tam iyileşme, süre dolarsa yarım iyileşme uygulanır.
        /// </summary>
        private void StartRepairMinigame(Vehicle vehicle, RepairKit repairKit)
        {
            const int boltCount = 4;
            var boltButtons = new List<Button>();
            // NOT: RenderCanvas her karede tamamen temizlenip yeniden çiziliyor (RenderGame), bu yüzden
            // bu geçici mini-oyun katmanı RenderCanvas'a değil, her karede silinmeyen PanelsContainer'a eklenir.
            var overlayCanvas = new Canvas { Width = RenderCanvas.ActualWidth, Height = RenderCanvas.ActualHeight, IsHitTestVisible = true };
            Canvas.SetLeft(overlayCanvas, 0);
            Canvas.SetTop(overlayCanvas, 0);
            Canvas.SetZIndex(overlayCanvas, 999);
            PanelsContainer.Children.Add(overlayCanvas);

            var rnd = Random.Shared;
            for (int i = 0; i < boltCount; i++)
            {
                double angle = i * (360.0 / boltCount) * Math.PI / 180.0;
                double bx = vehicle.X + Math.Cos(angle) * (vehicle.Width * 0.55);
                double by = vehicle.Y + Math.Sin(angle) * (vehicle.Height * 0.7) - 10;

                var boltBtn = new Button
                {
                    Content = "🔩",
                    FontSize = 16,
                    Width = 28,
                    Height = 28,
                    Style = (Style)FindResource("IconButton")
                };
                Canvas.SetLeft(boltBtn, bx - 14);
                Canvas.SetTop(boltBtn, by - 14);
                overlayCanvas.Children.Add(boltBtn);
                boltButtons.Add(boltBtn);
            }

            ShowToast(Loc.T("msg.tamirci_mikro_oyunu_vidalari_hizlica_sik"));
            PlayUiClick();

            // Vidalar araca göre sabit ekran konumunda çizildiği için, oyun sırasında araç
            // hareket edip vidalardan uzaklaşmasın diye geçici olarak kilitlenir.
            bool wasLocked = vehicle.IsLocked;
            vehicle.IsLocked = true;

            int remaining = boltCount;
            var timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

            void FinishMinigame(bool fullSuccess)
            {
                timeoutTimer.Stop();
                PanelsContainer.Children.Remove(overlayCanvas);
                vehicle.IsLocked = wasLocked;

                int healAmount = fullSuccess ? repairKit.HealAmount : repairKit.HealAmount / 2;
                vehicle.HP = Math.Min(vehicle.MaxHP, vehicle.HP + healAmount);
                if (fullSuccess) gameCore.GameData.RepairMinigameSuccessCount++;
                soundEngine.PlaySuccess();
                ShowToast(Loc.T(fullSuccess ? "msg.repair_full" : "msg.repair_partial"));
            }

            foreach (var boltBtn in boltButtons)
            {
                boltBtn.Click += (s, e) =>
                {
                    overlayCanvas.Children.Remove(boltBtn);
                    remaining--;
                    PlayUiClick();
                    if (remaining <= 0) FinishMinigame(true);
                };
            }

            timeoutTimer.Tick += (s, e) => FinishMinigame(false);
            timeoutTimer.Start();
        }

        /// <summary>
        /// Polis Kovalamacası: hedef aracın yakınına, onu kalıcı olarak takip eden özel bir
        /// polis aracı (🚓) doğurur. Yakalarsa (yeterince yaklaşırsa) para cezası kesip kovalamayı bırakır.
        /// </summary>
        private void SpawnPoliceChase(Vehicle target)
        {
            if (target == null) return;
            ShowToast(Loc.T("msg.polis_hiz_yapan_araci_fark_etti"));

            double spawnX = target.X < RenderCanvas.ActualWidth / 2 ? RenderCanvas.ActualWidth - 20 : 20;
            var police = new Vehicle(spawnX, Math.Max(20, target.Y - 60), "car")
            {
                IsPolice = true,
                ChaseTarget = target,
                Color = "#1565C0",
                NeonColor = "#00A0FF",
                Name = Loc.T("vehicle.police_name"),
                Emoji = "🚓",
                Brand = "" // gerçek marka görseli kullanılmasın, emoji ile ayırt edilsin
            };
            police.MaxSpeed *= 1.15; // hedefi gerçekten yakalayabilsin diye biraz daha hızlı
            gameCore.AddAutoMotion(police);
        }

        /// <summary>Polis aracı hedefine yeterince yaklaşınca cezayı kesip filoya geri döner (kovalamaca biter).</summary>
        private void UpdatePoliceChases()
        {
            foreach (var police in gameCore.AutoMotions.Where(v => v.IsPolice && v.ChaseTarget != null).ToList())
            {
                double dx = police.ChaseTarget.X - police.X;
                double dy = police.ChaseTarget.Y - police.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 35)
                {
                    int fine = Math.Min(gameCore.GameData.Money, 60);
                    if (fine > 0) gameCore.AddMoney(-fine);
                    gameCore.GameData.PoliceFinesReceived++;
                    ShowToast(Loc.T("msg.police_speed_fine", fine));
                    soundEngine.PlayCrash();
                    police.ChaseTarget = null;
                    gameCore.RemoveAutoMotion(police);
                }
            }
        }

        private void RefreshExpansionsList()
        {
            ExpansionsListControl.ItemsSource = null;
            ExpansionsListControl.ItemsSource = expansionManager.Expansions;
        }

        private void ExpansionsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Eklentiler artık ayrı bir yüzen panel değil, konsolide "☰ Paneller" menüsünün bir
            // sekmesi - bu buton menüyü açıp doğrudan o sekmeye geçiyor.
            ShowMasterPanelTab(TabExpansions);
            PlayUiClick();
        }

        /// <summary>Konsolide "☰ Paneller" menüsünü (kapalıysa) açar ve istenen sekmeye geçer -
        /// örn. sürüş moduna girince Vites sekmesine, Eklentiler butonuna basınca o sekmeye.</summary>
        private void ShowMasterPanelTab(TabItem tab)
        {
            if (MasterPanel.Visibility != Visibility.Visible)
            {
                panelsVisible = true;
                AnimateVisibility(PanelsContainer, true);
                AnimateVisibility(MasterPanel, true);
            }
            MasterTabControl.SelectedItem = tab;
        }

        private void ExpansionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Expansion expansion) return;

            expansionManager.ToggleExpansion(expansion.Id, gameCore);
            RefreshExpansionsList();
            PlayUiClick();
        }

        /// <summary>
        /// Fare yakalaması beklenmedik şekilde kaybolursa (ör. Alt-Tab, başka bir pencereye
        /// odaklanma) sürükleme durumunu güvenle sonlandırır - aksi halde araç "sürükleniyormuş"
        /// durumda takılı kalabilirdi.
        /// </summary>
        /// <summary>
        /// İşlemci (CPU) Takometresi + Batarya Uyarısı: sistem yükü/pil durumu periyodik olarak
        /// (saniyede birkaç kez değil, ~1.5 saniyede bir) kontrol edilir. Yüksek CPU yükünde
        /// araçlara geçici bir "hararet" hız/agresiflik bonusu ve görsel uyarı verilir; düşük
        /// pilde (şarjda değilken) araçlar yorgun düşüp yakıtları bitiyormuş gibi davranır.
        /// </summary>
        private void CheckHardwareStatus()
        {
            hardwareCheckCooldown--;
            if (hardwareCheckCooldown > 0) return;
            hardwareCheckCooldown = 90;

            if (cpuCounter != null)
            {
                try { currentCpuLoad = cpuCounter.NextValue(); } catch { currentCpuLoad = 0; }
            }

            if (currentCpuLoad > 85 && gameCore.GameTime - lastHeatWarningTick > 1200)
            {
                lastHeatWarningTick = gameCore.GameTime;
                ShowToast(Loc.T("msg.high_cpu_load", (int)currentCpuLoad));
                var hotVehicle = gameCore.AutoMotions.FirstOrDefault();
                if (hotVehicle != null) SpawnBackfire(hotVehicle);
            }

            try
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool isLowBattery = power.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Online
                                     && power.BatteryLifePercent > 0 && power.BatteryLifePercent < 0.15f;
                if (isLowBattery && gameCore.GameTime - lastBatteryWarningTick > 1800)
                {
                    lastBatteryWarningTick = gameCore.GameTime;
                    ShowToast(Loc.T("msg.low_battery", (int)(power.BatteryLifePercent * 100)));
                }
            }
            catch { }
        }

        /// <summary>
        /// Trafik Sıkışıklığı: bir kenara (EdgeSide) uzun süre neredeyse hareketsiz yığılan araçlar
        /// periyodik olarak korna çalar. Fareyle tıklanınca (bkz. Canvas_MouseDown) dağıtılabilirler.
        /// </summary>
        private void UpdateTrafficJams()
        {
            foreach (var vehicle in gameCore.AutoMotions)
            {
                bool isStuckAtEdge = vehicle.EdgeSide != 0 && !vehicle.IsDragged && !vehicle.IsDriven
                    && Math.Abs(vehicle.VelocityX) < 0.3 && Math.Abs(vehicle.VelocityY) < 0.3;

                if (isStuckAtEdge)
                {
                    jamStuckTicks.TryGetValue(vehicle, out double ticks);
                    double before = ticks;
                    ticks += gameCore.DeltaFrames;
                    jamStuckTicks[vehicle] = ticks;

                    // ~4 sn sonra, ~2.5 sn'de bir korna. Kesirli sayaçla "% 150 == 0" güvenilir
                    // olmadığından, 150'lik eşiğin geçildiği kare yakalanır.
                    if (ticks > 240 && Math.Floor(before / 150) != Math.Floor(ticks / 150))
                    {
                        soundEngine.PlayHorn(vehicle.Type);
                    }
                }
                else
                {
                    jamStuckTicks.Remove(vehicle);
                }
            }
        }

        /// <summary>
        /// Masaüstü Araba Buluşması: belirli aralıklarla tüm araçlar işi bırakıp ekranın ortasına
        /// toplanır, kısa bir süre orada kalıp sonra normal davranışa döner.
        /// </summary>
        private void UpdateCarMeetEvent()
        {
            if (!carMeetActive)
            {
                if (gameCore.GameTime < nextCarMeetTick || gameCore.AutoMotions.Count < 3) return;

                carMeetActive = true;
                carMeetTicksLeft = 420; // ~7 saniye
                double cx = RenderCanvas.ActualWidth / 2;
                double cy = RenderCanvas.ActualHeight - 60;
                int i = 0;
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsDragged || vehicle.IsLocked || vehicle.IsDriven) continue;
                    double angle = i * (360.0 / gameCore.AutoMotions.Count) * Math.PI / 180.0;
                    vehicle.TargetX = cx + Math.Cos(angle) * 90;
                    vehicle.TargetY = cy + Math.Sin(angle) * 22;
                    i++;
                }
                gameCore.GameData.CarMeetsAttended++;
                ShowToast(Loc.T("msg.masaustu_araba_bulusmasi_basladi"));
                soundEngine.PlaySuccess();
            }
            else
            {
                carMeetTicksLeft -= gameCore.DeltaFrames;
                if (carMeetTicksLeft <= 0)
                {
                    carMeetActive = false;
                    nextCarMeetTick = gameCore.GameTime + 36000; // bir sonraki buluşma ~10 dk sonra
                    foreach (var vehicle in gameCore.AutoMotions)
                    {
                        vehicle.TargetX = -1;
                        vehicle.TargetY = -1;
                    }
                    ShowToast(Loc.T("msg.bulusma_sona_erdi_araclar_yollarina_devam"));
                }
            }
        }

        /// <summary>
        /// Ekranlar Arası Atlama Rampası: çoklu-monitör modunda bir ekran sınırını hızla geçen
        /// araçlar sanal bir rampadan uçuyormuş gibi hafifçe zıplar ve kıvılcım bırakır.
        /// </summary>
        private void UpdateMonitorJumpRamps()
        {
            if (monitorBoundaryX.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (!lastVehicleX.TryGetValue(vehicle, out double prevX))
                {
                    lastVehicleX[vehicle] = vehicle.X;
                    continue;
                }

                if (Math.Abs(vehicle.VelocityX) > 1.0)
                {
                    foreach (var boundary in monitorBoundaryX)
                    {
                        bool crossed = (prevX < boundary && vehicle.X >= boundary) || (prevX > boundary && vehicle.X <= boundary);
                        if (crossed)
                        {
                            vehicle.VelocityY -= 3.5;
                            for (int i = 0; i < 4; i++)
                            {
                                particles.Add(new VehicleParticle
                                {
                                    X = boundary,
                                    Y = vehicle.Y + vehicle.Height / 2,
                                    VelocityX = (Random.Shared.NextDouble() - 0.5) * 2,
                                    VelocityY = -Random.Shared.NextDouble() * 2,
                                    Size = Random.Shared.Next(4, 8),
                                    Opacity = 1.0,
                                    Color = Color.FromRgb(0xFF, 0xD7, 0x00)
                                });
                            }
                            soundEngine.PlayDrift();
                            break;
                        }
                    }
                }

                lastVehicleX[vehicle] = vehicle.X;
            }
        }

        /// <summary>
        /// Fiziksel Rampa Çizimi: orta fare tuşuyla çizilen geçici çizgiler katı zemin gibi
        /// davranır - araçlar üzerine düşünce durur/seker, üzerinden geçince zıplayabilir.
        /// Ömrü dolan rampalar yavaşça kaybolur.
        /// </summary>
        private void UpdatePhysicsRamps()
        {
            for (int i = physicsRamps.Count - 1; i >= 0; i--)
            {
                var (a, b, life) = physicsRamps[i];
                life -= gameCore.DeltaFrames;
                if (life <= 0) { physicsRamps.RemoveAt(i); continue; }
                physicsRamps[i] = (a, b, life);
            }

            if (physicsRamps.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.VelocityY < 0) continue; // sadece düşerken/yere basarken

                double footY = vehicle.Y + vehicle.Height / 2;

                foreach (var (a, b, _) in physicsRamps)
                {
                    double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
                    if (vehicle.X < minX || vehicle.X > maxX) continue;
                    if (Math.Abs(b.X - a.X) < 0.01) continue;

                    double t = (vehicle.X - a.X) / (b.X - a.X);
                    double rampY = a.Y + (b.Y - a.Y) * t;

                    if (footY >= rampY - 4 && footY <= rampY + 14)
                    {
                        vehicle.Y = rampY - vehicle.Height / 2;
                        if (vehicle.VelocityY > 2)
                        {
                            vehicle.VelocityY *= -0.4; // rampadan sekme
                        }
                        else
                        {
                            vehicle.VelocityY = 0;
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Müzik Süspansiyon Senkronizasyonu: arka planda çalan müziğin anlık ses seviyesine göre
        /// (bas vuruşu gibi) tüm araçlar hafifçe zıplar - lowrider tarzı ritmik süspansiyon hissi.
        /// Ses tamamen sessizse veya kayıt başlatılamadıysa (audioCapture null) hiçbir şey olmaz.
        /// </summary>
        private void UpdateMusicSync()
        {
            if (audioCapture == null || !musicSyncEnabled) return;

            musicBounceCooldown--;
            if (musicBounceCooldown > 0) return;

            float level = currentAudioLevel;
            if (level < 0.35f) return; // sessiz/normal seviye - sadece belirgin vuruşlarda tepki ver

            musicBounceCooldown = 8; // art arda titremesin diye kısa bir bekleme
            double bounceStrength = Math.Min(1.0, level) * 2.2;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.IsFlying || vehicle.IsDriven) continue;
                if (vehicle.EdgeSide != 0) continue; // sadece zemindeki araçlar
                vehicle.VelocityY -= bounceStrength;
            }
        }

        private void SpawnBackfire(Vehicle vehicle)
        {
            double angleRad = vehicle.Angle + Math.PI; // araç arkası
            double offsetX = Math.Cos(angleRad) * (vehicle.Width / 2);
            double offsetY = Math.Sin(angleRad) * (vehicle.Height / 2);

            for (int i = 0; i < 5; i++)
            {
                particles.Add(new VehicleParticle
                {
                    X = vehicle.X + offsetX,
                    Y = vehicle.Y + offsetY,
                    VelocityX = Math.Cos(angleRad) * (1.5 + Random.Shared.NextDouble()) + (Random.Shared.NextDouble() - 0.5),
                    VelocityY = Math.Sin(angleRad) * (1.5 + Random.Shared.NextDouble()) + (Random.Shared.NextDouble() - 0.5) - 0.5,
                    Size = Random.Shared.Next(6, 12),
                    Opacity = 1.0,
                    Color = i % 2 == 0 ? Color.FromRgb(255, 140, 0) : Color.FromRgb(255, 60, 0)
                });
            }
            soundEngine.PlayCrash();
        }

        private void SpawnSkidMark(Vehicle vehicle)
        {
            if (lowEffectsMode) return;
            skidMarks.Add(new SkidMark
            {
                X = vehicle.X,
                Y = vehicle.Y + vehicle.Height / 2 - 2,
                Angle = vehicle.EdgeSide == 0 ? 0 : Math.PI / 2,
                Width = vehicle.Width * 0.7
            });
        }

        private void CheckCargoDeliveries()
        {
            if (activeCargoDeliveries.Count == 0) return;

            List<Vehicle> completed = null;
            foreach (var (vehicle, fileName) in activeCargoDeliveries)
            {
                if (!vehicle.HasCargo || vehicle.IsDestroyed || !gameCore.AutoMotions.Contains(vehicle))
                {
                    (completed ??= new List<Vehicle>()).Add(vehicle);
                    continue;
                }

                double dx = vehicle.TargetX - vehicle.X;
                double dy = vehicle.TargetY - vehicle.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 20) continue;

                vehicle.HasCargo = false;
                vehicle.TargetX = -1;
                vehicle.TargetY = -1;
                gameCore.AddMoney(75);
                ShowToast(Loc.T("msg.file_cargo_delivered", fileName));
                soundEngine.PlayCash();
                (completed ??= new List<Vehicle>()).Add(vehicle);
            }

            if (completed != null)
                foreach (var v in completed) activeCargoDeliveries.Remove(v);
        }

        // Yazı Yarışı: oyuncunun gerçek yazma hızı (WPM) bir aracı, sabit tempolu bir rakibe karşı
        // pistte ilerletir. Global klavye kancası kullanılmaz - yalnızca bu pencereye gelen tuşlar
        // sayılır (bkz. HandleWindowKeyDown içindeki mevcut "Tuş Vuruşu Sismik Etkisi" ile aynı ilke).
        private readonly WpmRace wpmRace = new();
        private Vehicle raceVehicle;
        private Vehicle raceRival;
        private double raceStartX;
        private double raceFinishX;
        private double raceTrackY;

        private void WpmRaceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (wpmRace.IsActive)
            {
                CancelWpmRace();
                ShowToast(Loc.T("msg.yazi_yarisi_iptal_edildi"));
                PlayUiClick();
                return;
            }

            // UseLearningAI açık araçlar hariç tutulur: Q-learning navigasyonu (bkz.
            // Vehicle.UpdateLearningNavigation) keşif amaçlı gecikmeli/rastgele kararlar içerir - bu,
            // yazma hızına orantılı DÜZ ilerleme bekleyen yarış mantığıyla çakışıp yarışı tutarsız
            // (bazen duran, bazen sıçrayan) hissettirirdi.
            var candidates = gameCore.AutoMotions
                .Where(v => !v.IsDestroyed && !v.IsDragged && !v.IsLocked && !v.IsDriven
                            && !v.HasCargo && !v.UseLearningAI && !activeCargoDeliveries.ContainsKey(v))
                .ToList();
            if (candidates.Count < 2)
            {
                ShowToast(Loc.T("msg.yaris_icin_en_az_2_bosta"));
                return;
            }

            double canvasWidth = RenderCanvas.ActualWidth;
            double canvasHeight = RenderCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            raceVehicle = candidates[Random.Shared.Next(candidates.Count)];
            candidates.Remove(raceVehicle);
            raceRival = candidates[Random.Shared.Next(candidates.Count)];

            raceStartX = 60;
            raceFinishX = canvasWidth - 60;
            raceTrackY = canvasHeight * 0.3;

            wpmRace.Start();
            WpmRaceBtn.Opacity = 1.0;
            ShowToast(Loc.T("msg.typing_race_started", raceVehicle.Emoji, raceRival.Emoji, WpmRace.TargetWpm.ToString("0")));
            PlayUiClick();
        }

        private void CancelWpmRace()
        {
            wpmRace.Cancel();
            if (raceVehicle != null) { raceVehicle.TargetX = -1; raceVehicle.TargetY = -1; }
            if (raceRival != null) { raceRival.TargetX = -1; raceRival.TargetY = -1; }
            raceVehicle = null;
            raceRival = null;
            WpmRaceBtn.Opacity = 0.4;
        }

        private void UpdateWpmRace()
        {
            if (!wpmRace.IsActive) return;

            if (raceVehicle == null || raceRival == null
                || raceVehicle.IsDestroyed || raceRival.IsDestroyed
                || !gameCore.AutoMotions.Contains(raceVehicle) || !gameCore.AutoMotions.Contains(raceRival))
            {
                CancelWpmRace();
                return;
            }

            raceVehicle.TargetX = raceStartX + (raceFinishX - raceStartX) * Math.Clamp(wpmRace.PlayerProgress, 0, 1);
            raceVehicle.TargetY = raceTrackY;
            raceRival.TargetX = raceStartX + (raceFinishX - raceStartX) * Math.Clamp(wpmRace.RivalProgress, 0, 1);
            raceRival.TargetY = raceTrackY + 40;

            if (wpmRace.Tick(out bool didPlayerWin))
            {
                int measuredWpm = (int)wpmRace.CurrentWpm;
                CancelWpmRace();

                if (didPlayerWin)
                {
                    gameCore.AddMoney(150);
                    ShowToast(Loc.T("msg.typing_race_won", measuredWpm));
                    soundEngine.PlayCash();
                }
                else
                {
                    ShowToast(Loc.T("msg.typing_race_lost", measuredWpm));
                }
            }
        }

        private void CheckPomodoroCompletion()
        {
            if (!pomodoroSession.CheckCompleted()) return;

            RestoreFuelMultiplierAfterFocus();

            int truckCount = gameCore.AutoMotions.Count(v => v.Type == "truck");
            int bonus = 50 + truckCount * 25;
            gameCore.AddMoney(bonus);
            ShowToast(Loc.T("msg.focus_session_done", bonus));
            soundEngine.PlayCash();
        }
        private MiniHudWindow miniHud;

        private readonly Dictionary<Vehicle, DateTime> lastReminderTime = new();
        private DateTime lastReminderScan = DateTime.MinValue;
        private DateTime lastIdleReminder = DateTime.MinValue;

        /// <summary>Düşük yakıt/hasar/kirlilik/mutluluk durumundaki araçlar ve uzun süredir
        /// etkileşimsiz kalan oyuncu için proaktif bildirim gösterir (VPet'in zamanlanmış görev/hatırlatma
        /// sisteminin karşılığı). Gerçek geçen zamana (DateTime) göre çalışır, böylece oyun döngüsü
        /// kare hızından (60/144 FPS fark etmeksizin) bağımsız olarak aynı sıklıkta tetiklenir.</summary>
        private void CheckReminders()
        {
            var now = DateTime.Now;
            if ((now - lastReminderScan).TotalSeconds < 5) return;
            lastReminderScan = now;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (lastReminderTime.TryGetValue(vehicle, out DateTime lastTime) && (now - lastTime).TotalSeconds < 30)
                    continue; // araç başına ~30 saniye bekleme süresi

                string name = string.IsNullOrEmpty(vehicle.Name) ? vehicle.Type.ToUpper() : vehicle.Name;
                string msg = null;
                if (vehicle.Fuel < vehicle.MaxFuel * 0.15)
                    msg = Loc.T("msg.reminder_fuel", name);
                else if (vehicle.HP < vehicle.MaxHP * 0.25)
                    msg = Loc.T("msg.reminder_repair", name);
                else if (vehicle.Dirtiness > 0.85)
                    msg = Loc.T("msg.reminder_dirty", name);
                else if (vehicle.Happiness < 20)
                    msg = Loc.T("msg.reminder_unhappy", name);

                if (msg != null)
                {
                    ShowToast(msg);
                    lastReminderTime[vehicle] = now;
                    break;
                }
            }

            // Oyuncu uzun süredir (2 dakika+) hiç etkileşime girmediyse tek seferlik genel hatırlatma
            if ((now - lastUserActivity).TotalMinutes >= 2 && (now - lastIdleReminder).TotalMinutes >= 2
                && gameCore.AutoMotions.Count > 0)
            {
                lastIdleReminder = now;
                ShowToast(Loc.T("msg.h_l_oradayim_biraz_ilgilenmez_misin"));
            }
        }

        /// <summary>Zeminde yan yana gelen iki araç arasında rastgele bir "selamlaşma" (korna + 👋
        /// balonu) tetikler. Gerçek zamana göre (~1 sn'de bir) taranır, kare hızından bağımsızdır.</summary>
        private void CheckGreetings()
        {
            var now = DateTime.Now;
            if ((now - lastGreetScan).TotalSeconds < 1) return;
            lastGreetScan = now;

            var candidates = gameCore.AutoMotions
                .Where(v => v.EdgeSide == 0 && !v.IsDragged && !v.IsDestroyed && !v.IsSideHidden)
                .ToList();

            for (int i = 0; i < candidates.Count; i++)
            {
                var a = candidates[i];
                if (lastGreetTime.TryGetValue(a, out DateTime aLast) && (now - aLast).TotalSeconds < 20)
                    continue;

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    var b = candidates[j];
                    if (lastGreetTime.TryGetValue(b, out DateTime bLast) && (now - bLast).TotalSeconds < 20)
                        continue;

                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double closeRange = (a.Width + b.Width) / 2.0 + 25;

                    if (dist < closeRange && Random.Shared.NextDouble() < 0.35)
                    {
                        lastGreetTime[a] = now;
                        lastGreetTime[b] = now;
                        soundEngine.PlayHorn(a.Type);
                        greetingPops.Add(new GreetingPop
                        {
                            X = (a.X + b.X) / 2,
                            Y = Math.Min(a.Y - a.Height / 2, b.Y - b.Height / 2) - 12
                        });
                        return; // bir karede en fazla bir selamlaşma - gürültü kirliliğini önler
                    }
                }
            }
        }

        // Yağmuru aç/kapa
        private void ToggleRainBtn_Click(object sender, RoutedEventArgs e)
        {
            isRaining = !isRaining;
            if (!isRaining) rainDrops.Clear();
            if (isRaining && isSnowing) { isSnowing = false; snowFlakes.Clear(); }
            PlayUiClick();
        }

        // Karı aç/kapa (Kış Teması) - yağmurla aynı anda aktif olamaz
        private void ToggleSnowBtn_Click(object sender, RoutedEventArgs e)
        {
            isSnowing = !isSnowing;
            if (!isSnowing) snowFlakes.Clear();
            if (isSnowing && isRaining) { isRaining = false; rainDrops.Clear(); }
            PlayUiClick();
        }

        // Trafik Işığı ekle
        private void AddTrafficLightBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 200;
            double ry = RenderCanvas.ActualHeight - 80;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "traffic_light"));
            soundEngine.PlaySuccess();
        }

        // Manuel Doldurma: bidonu ele al (mod başlat)
        private void AddFuelCanBtn_Click(object sender, RoutedEventArgs e)
        {
            fuelCanActive = true;
            ShowToast(Loc.T("msg.bidon_elinde_bir_araca_tikla_iptal"));
            PlayUiClick();
        }

        private void CancelFuelCan()
        {
            fuelCanActive = false;
        }

        // Yakıt/Onarım İstasyonu ekle
        private void AddStationBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 40;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "station"));
            soundEngine.PlaySuccess();
        }

        // Masaüstü Yol Çalışması (trafik konisi) ekle
        private void AddConeBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 30;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "cone"));
            PlayUiClick();
        }

        // Trafik Polisi Çevirmesi ekle
        private void AddPoliceBtn_Click(object sender, RoutedEventArgs e)
        {
            double rx = RenderCanvas.ActualWidth / 2 + (Random.Shared.NextDouble() - 0.5) * 300;
            double ry = RenderCanvas.ActualHeight - 40;
            gameCore.Obstacles.Add(new Obstacle(rx, ry, "police"));
            fineIssuedFor.Clear();
            PlayUiClick();
        }

        private readonly HashSet<Vehicle> fineIssuedFor = new();

        /// <summary>
        /// Trafik Polisi Çevirmesi: yolu kesen polis aracının yakınından geçen, bakımı düşük
        /// (HP %50'nin altında) araçlara bir kereliğine para cezası kesilir.
        /// </summary>
        private void UpdatePoliceStops()
        {
            var policeObstacles = gameCore.Obstacles.Where(o => o.Type == "police").ToList();
            if (policeObstacles.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (fineIssuedFor.Contains(vehicle)) continue;
                if (vehicle.HP >= vehicle.MaxHP * 0.5) continue;

                foreach (var police in policeObstacles)
                {
                    double dx = police.X - vehicle.X;
                    double dy = police.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 70)
                    {
                        fineIssuedFor.Add(vehicle);
                        int fine = Math.Min(gameCore.GameData.Money, 40);
                        if (fine > 0)
                        {
                            gameCore.AddMoney(-fine);
                            gameCore.GameData.PoliceFinesReceived++;
                            ShowToast(Loc.T("msg.unmaintained_fine", VehicleDisplayName(vehicle), fine));
                            soundEngine.PlayCrash();
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Geri Dönüşüm Çöp Kamyonu (görsel-only): ekran dışından bir kamyon simgesi belirip
        /// "geri dönüşüm kutusuna" doğru gidip kaybolur. Kullanıcı onayı olmadan gerçek Geri
        /// Dönüşüm Kutusu'na ASLA dokunulmaz (SHEmptyRecycleBin çağrılmaz) - tamamen kozmetik bir olay.
        /// </summary>
        private void AddRecycleTruckBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowToast(Loc.T("msg.geri_donusum_kamyonu_yola_cikti_gorsel"));
            var truckIcon = new TextBlock { Text = "♻️🚛", FontSize = 26 };
            double startX = -60;
            double y = RenderCanvas.ActualHeight - 50;
            Canvas.SetLeft(truckIcon, 0);
            Canvas.SetTop(truckIcon, y);
            RenderCanvas.Children.Add(truckIcon);

            var anim = new DoubleAnimation
            {
                From = startX,
                To = RenderCanvas.ActualWidth + 60,
                Duration = TimeSpan.FromSeconds(4),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (s, e2) => RenderCanvas.Children.Remove(truckIcon);
            var transform = new TranslateTransform();
            truckIcon.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, anim);

            soundEngine.PlaySuccess();
        }

        /// <summary>
        /// İstasyona yakın (zeminde, uçmayan/sürüklenmeyen) araçları yavaşça yakıt/HP dolduran döngü.
        /// </summary>
        private void UpdateStations()
        {
            var stations = gameCore.Obstacles.Where(o => o.Type == "station").ToList();
            if (stations.Count == 0) return;

            foreach (var vehicle in gameCore.AutoMotions)
            {
                if (vehicle.IsDragged || vehicle.IsFlying) continue;

                foreach (var station in stations)
                {
                    double dx = station.X - vehicle.X;
                    double dy = station.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 55)
                    {
                        if (vehicle.Fuel < vehicle.MaxFuel)
                        {
                            vehicle.Fuel = Math.Min(vehicle.MaxFuel, vehicle.Fuel + 0.6);
                        }
                        if (vehicle.HP < vehicle.MaxHP)
                        {
                            vehicle.HP = Math.Min(vehicle.MaxHP, vehicle.HP + 1);
                        }
                        break;
                    }
                }
            }
        }

        // Kargo Görevi Başlat - mantığın tamamı artık MissionExpansion içinde (bkz. Expansions/MissionExpansion.cs)
        private void StartCargoBtn_Click(object sender, RoutedEventArgs e)
        {
            missionExpansion.StartNewMission(gameCore, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);
        }
    }
}
