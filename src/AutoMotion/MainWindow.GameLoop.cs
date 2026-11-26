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
    // Render döngüsü, oyun döngüsü (GameLoop_Tick) ve arayüz tazeleme.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        private DateTime lastUserActivity = DateTime.Now;
        private DateTime lastIdleFrameAt = DateTime.MinValue;

        /// <summary>Boşta Modu: kullanıcı uzun süre (5dk+) fare/klavye ile etkileşime girmediyse ve
        /// sürüş modunda değilse kare hızı ~15 FPS'e düşürülerek CPU/pil tüketimi azaltılır - masaüstü
        /// pet uygulamalarında sektör standardı bir davranıştır (VPet'in gece/boşta performans modu gibi).
        /// Herhangi bir fare/klavye etkinliğinde anında normal hıza döner.
        /// Artık zamanlayıcı aralığı değiştirilmiyor (döngü ekran yenilemesine bağlı); boştayken
        /// aradaki kareler basitçe atlanıyor.</summary>
        private bool ShouldSkipIdleFrame()
        {
            bool idle = drivenVehicle == null && (DateTime.Now - lastUserActivity).TotalMinutes > 5;
            if (!idle) return false;

            if ((DateTime.UtcNow - lastIdleFrameAt).TotalMilliseconds < 66) return true;
            lastIdleFrameAt = DateTime.UtcNow;
            return false;
        }

        /// <summary>
        /// Oyun döngüsü artık DispatcherTimer ile değil, WPF'in kompozisyon (render) olayıyla
        /// sürülüyor: CompositionTarget.Rendering ekranın GERÇEK yenileme hızında, tam da kare
        /// çizilmeden hemen önce tetiklenir.
        ///
        /// Neden değiştirildi: 7 ms'lik bir DispatcherTimer'ı en yüksek öncelikle (Send) çalıştırmak,
        /// ekran 60 Hz'deyken bile saniyede ~144 kez tüm sahneyi yeniden kuruyordu. Monitörün
        /// gösteremeyeceği bu fazladan kareler görünür bir akıcılık sağlamadan bir CPU çekirdeğinin
        /// neredeyse tamamını yakıyordu. Bu olayla döngü kendiliğinden ekranın hızına kilitlenir:
        /// Windows'ta ekran 144 Hz'e alındığında uygulama da otomatik olarak 144 FPS çalışır,
        /// 60 Hz'de ise 60 FPS - ve fizik artık kare hızından bağımsız olduğu için (bkz.
        /// GameCore.DeltaFrames) hareket her iki durumda da birebir aynı hızda görünür.
        /// </summary>
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // RENDER DÖNGÜSÜ ASKIYA ALMA (boşta CPU tüketimi)
        //
        // ÖLÇÜM (1920x1080, 60 Hz, sahnede hiç araç yokken):
        //   • CompositionTarget.Rendering aboneliği AÇIK  → bir çekirdeğin ~%9.9'u
        //   • abonelik KAPALI                              → ~%0.1'i
        //
        // Sebep: Rendering aboneliği WPF'in render thread'ini ekran yenileme hızında sürekli tetikte
        // tutar; pencere AllowsTransparency="True" ve tam ekran olduğundan her tik, 1920x1080'lik
        // per-piksel-alfa katmanlı yüzeyin yeniden blit edilmesi anlamına gelir. Çizilecek hiçbir şey
        // yokken bu tamamen boşa yanan CPU'dur - masaüstünde sürekli açık kalan bir uygulamada pil ve
        // fan açısından kabul edilemez.
        //
        // Bu yüzden sahne GERÇEKTEN boşken (hiç araç/engel/parçacık/hava olayı yok ve kullanıcı bir
        // süredir etkileşimde değil) döngüden tamamen çıkılır. 5 Hz'lik hafif bir gözcü zamanlayıcı
        // uyanma koşullarını izler; araç eklenmesi ise gözcüyü beklemeden anında uyandırır.
        //
        // NOT: Bu, WPF'in KENDİ Storyboard animasyonlarını (toast, panel geçişleri, diyaloglar)
        // etkilemez - onları WPF kendi zamanlayıcısıyla sürer ve gerektiğinde kompozisyonu kendisi
        // uyandırır. Askıya alınan yalnızca oyun döngüsüdür.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        private bool renderLoopActive;
        private DispatcherTimer renderLoopWatchdog;

        private void StartRenderLoop()
        {
            if (renderLoopActive) return;
            renderLoopActive = true;
            CompositionTarget.Rendering += OnCompositionRendering;
        }

        private void StopRenderLoop()
        {
            if (!renderLoopActive) return;
            renderLoopActive = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }

        /// <summary>Sahnede canlandırılacak/simüle edilecek hiçbir şey kalmadıysa ve kullanıcı da bir
        /// süredir etkileşimde değilse true. Muhafazakâr davranır: en ufak şüphede döngü açık kalır.</summary>
        private bool CanSuspendRenderLoop()
        {
            // Sahnede herhangi bir dinamik içerik varsa asla askıya alma.
            if (HasAnythingToRender()) return false;
            if (drivenVehicle != null) return false;

            // Devam eden bir fare etkileşimi (sürükleme, seçim kutusu, rampa çizimi) varsa bekle.
            if (Mouse.LeftButton == MouseButtonState.Pressed
                || Mouse.RightButton == MouseButtonState.Pressed
                || Mouse.MiddleButton == MouseButtonState.Pressed) return false;

            // Kullanıcı az önce bir şey yaptıysa (menü açtı, buton bekliyor) hemen uykuya dalma.
            if ((DateTime.Now - lastUserActivity).TotalSeconds < 2) return false;

            return true;
        }

        /// <summary>
        /// Sahnedeki motor sesini her karede günceller.
        ///
        /// Tasarım: filodaki her araç için ayrı bir ton çalmak hem kakofoni yaratır hem CPU yakar.
        /// Bunun yerine SAHNEYİ TEMSİL EDEN tek bir ton çalınır - sürülen araç varsa o, yoksa en
        /// hızlı hareket eden araç. Ses şiddeti o aracın hızına bağlı olduğundan, filo yavaşlayınca
        /// ses kendiliğinden kısılır; hiçbir şey hareket etmiyorsa tamamen susar.
        /// </summary>
        private void UpdateEngineAudio()
        {
            // Sürülen araç her zaman önceliklidir: oyuncu onu kontrol ediyor, sesi de o vermeli.
            if (drivenVehicle != null && !drivenVehicle.IsDestroyed && drivenVehicle.Fuel > 0)
            {
                double speed = Math.Sqrt(drivenVehicle.VelocityX * drivenVehicle.VelocityX
                                       + drivenVehicle.VelocityY * drivenVehicle.VelocityY);
                double gearTop = Math.Max(0.1, drivenVehicle.CurrentGearMaxSpeed);
                double rpm = Math.Clamp(speed / gearTop, 0, 1);

                // Boşta (N) veya dururken bile motor çalışıyor: rölantide taban bir ses kalır.
                soundEngine.UpdateEngineTone(drivenVehicle.Type, rpm, 0.55 + rpm * 0.45);
                return;
            }

            // Sürüş yoksa: en hızlı hareket eden, motoru olan aracı temsilci seç.
            Vehicle loudest = null;
            double loudestSpeed = 0;
            foreach (var v in gameCore.AutoMotions)
            {
                if (v.IsDestroyed || !v.HasEngine || v.Fuel <= 0) continue;
                double speed = Math.Sqrt(v.VelocityX * v.VelocityX + v.VelocityY * v.VelocityY);
                if (speed > loudestSpeed) { loudestSpeed = speed; loudest = v; }
            }

            if (loudest == null)
            {
                soundEngine.StopEngineTone();
                return;
            }

            double ratio = Math.Clamp(loudestSpeed / Math.Max(0.1, loudest.MaxSpeed * 2.5), 0, 1);
            // Arka plandaki filo, sürülen araçtan belirgin daha kısık duyulur.
            soundEngine.UpdateEngineTone(loudest.Type, ratio, ratio * 0.5);
        }

        private DateTime lastSteamStatsPush = DateTime.MinValue;

        /// <summary>
        /// Steam geri çağrılarını işler ve istatistik/durum bilgisini periyodik olarak günceller.
        ///
        /// ÖNEMLİ: bu, oyun döngüsüne DEĞİL 5 Hz'lik gözcü zamanlayıcısına bağlı. Oyun döngüsü sahne
        /// boşken tamamen askıya alınıyor (bkz. UpdateRenderLoopState); geri çağrılar oraya bağlansaydı
        /// masaüstünde hiç araç yokken Steam ile iletişim tamamen kesilirdi (arkadaş listesinde
        /// durum donar, başarımlar sunucuya gitmezdi).
        /// </summary>
        private void PumpSteam()
        {
            if (!SteamService.IsAvailable) return;

            SteamService.Bridge.RunCallbacks();

            // İstatistikler her 200 ms'de değil, ~10 saniyede bir gönderilir - Steam'e gereksiz
            // yük bindirmeden arkadaş listesindeki durumu güncel tutmaya fazlasıyla yeter.
            if ((DateTime.UtcNow - lastSteamStatsPush).TotalSeconds < 10) return;
            lastSteamStatsPush = DateTime.UtcNow;

            var data = gameCore.GameData;
            SteamService.Bridge.SetStat(SteamService.Stats.TotalVehicles, data.TotalVehicles);
            SteamService.Bridge.SetStat(SteamService.Stats.TotalEarnings, data.TotalEarnings);
            SteamService.Bridge.SetStat(SteamService.Stats.TotalDistanceKm, (int)data.TotalMileage);
            SteamService.UpdateRichPresence(gameCore.AutoMotions.Count, data.Money);
        }

        private void UpdateRenderLoopState()
        {
            if (renderLoopActive)
            {
                if (!CanSuspendRenderLoop()) return;

                StopRenderLoop();
                // Askıya alınmadan önce sahneyi bir kez temizle - donmuş bir kare kalmasın.
                // Yalnızca sahne katmanı boşaltılır: seçim kutusu/animasyon gibi sahne dışı öğeler
                // kendi ömürlerini yaşamaya devam eder (bkz. RetainedScene).
                scene.Clear();
                // Motor sesi susturulmalı (döngü durunca UpdateEngineAudio çağrılmaz ve ton son
                // değerinde sonsuza kadar çalmaya devam ederdi) VE ses aygıtı uykuya alınmalı:
                // NAudio'nun çıkış thread'i sessizlik üretmek için bile sürekli çalışıyor.
                soundEngine?.SuspendAudioDevice();
            }
            else
            {
                if (!CanSuspendRenderLoop())
                {
                    StartRenderLoop();
                    return;
                }

                // Döngü uyurken de tıklama-geçirgenliği canlı kalmalı: aksi halde pencere son
                // durumunda donar ve kullanıcı masaüstüne tıklayamaz hale gelebilir. Bu kontrol
                // sadece bir GetCursorPos + birkaç sınır karşılaştırması - 5 Hz'de bedeli yok sayılır.
                UpdateClickThrough();
            }
        }

        private void OnCompositionRendering(object sender, EventArgs e)
        {
            // Aynı kompozisyon karesinde birden fazla tetiklenmeyi ele (WPF bunu yapabiliyor)
            if (e is RenderingEventArgs args)
            {
                if (args.RenderingTime == lastRenderingTime) return;
                lastRenderingTime = args.RenderingTime;
            }

            if (ShouldSkipIdleFrame()) return;
            GameLoop_Tick(sender, e);
        }

        private TimeSpan lastRenderingTime = TimeSpan.MinValue;

        private void GameLoop_Tick(object sender, EventArgs e)
        {
            UpdateGearKnobDragPhysics();
            SyncGearShaftLine();

            double activeWinTop = -1;
            double activeWinLeft = -1;
            double activeWinRight = -1;

            try
            {
                IntPtr fgWnd = GetForegroundWindow();
                IntPtr myWnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (fgWnd != IntPtr.Zero && fgWnd != myWnd)
                {
                    if (GetWindowRect(fgWnd, out RECT rect))
                    {
                        // Win32 ekran koordinatlarını WPF pencere koordinatlarına eşle
                        activeWinTop = rect.Top - Top;
                        activeWinLeft = rect.Left - Left;
                        activeWinRight = rect.Right - Left;
                    }
                }
            }
            catch { }

            // Görev Çubuğu Otoyolu (Odak Modu): kullanıcı tam ekrana yakın bir pencerede
            // "çalışırken" (aktif pencere neredeyse ekranın tamamını kaplıyorsa) araçlar o
            // pencerenin üstüne tırmanmaz, sadece görev çubuğunun hemen üstündeki izole şeritte
            // dolaşır - pencere tırmanma fiziği bu durumda devre dışı bırakılır.
            bool focusMode = activeWinTop >= 0 && activeWinTop <= 4 && activeWinLeft <= 4;
            if (focusMode)
            {
                activeWinTop = -1;
                activeWinLeft = -1;
                activeWinRight = -1;
            }

            if (drivenVehicle != null && drivenVehicle.IsDestroyed)
            {
                StopDriving();
            }
            else if (drivenVehicle != null && drivenVehicle.Fuel <= 0)
            {
                // Yakıt biterken sürüş kontrolü açık kalıyordu ama araç güçsüzce yavaşlıyordu -
                // artık yakıt tükenince sürüş modundan otomatik çıkılıyor, oyuncuya bildirim veriliyor.
                ShowToast(Loc.T("msg.out_of_fuel_drive_exit", VehicleDisplayName(drivenVehicle)));
                StopDriving();
            }
            else if (drivenVehicle != null)
            {
                double vertical = (pressedDriveKeys.Contains(Key.W) || pressedDriveKeys.Contains(Key.Up) ? 1 : 0)
                                 - (pressedDriveKeys.Contains(Key.S) || pressedDriveKeys.Contains(Key.Down) ? 1 : 0);
                double horizontal = (pressedDriveKeys.Contains(Key.D) || pressedDriveKeys.Contains(Key.Right) ? 1 : 0)
                                   - (pressedDriveKeys.Contains(Key.A) || pressedDriveKeys.Contains(Key.Left) ? 1 : 0);

                // Gaz/fren girdisi ham tuş durumuyla (0/1) değil, her karede hedefe doğru yumuşakça
                // kayan bir değerle uygulanır - ani tuş basış/bırakışlarda sarsıntılı değil, akıcı
                // bir hızlanma/yavaşlama hissi verir (gerçek gaz pedalı gibi).
                // Not: genel zemin sürtünmesiyle çakışma düzeltildikten sonra gaz artık gerçekten
                // vitese göre tepki veriyor - girdi yumuşatması da hafifçe sıkılaştırıldı (daha
                // duyarlı ama hâlâ akıcı bir gaz pedalı hissi için).
                // Girdi yumuşatması da kare hızından bağımsız: 60 FPS'te "kare başına %20 yaklaş"
                // davranışı, 144 FPS'te aynı gerçek-zaman tepkisini verecek şekilde ölçeklenir.
                double inputDt = gameCore.DeltaFrames;
                smoothedThrottle += (vertical - smoothedThrottle) * (1 - Math.Pow(1 - 0.2, inputDt));
                smoothedSteerInput += (horizontal - smoothedSteerInput) * (1 - Math.Pow(1 - 0.26, inputDt));

                drivenVehicle.ApplyDriveInput(smoothedThrottle, smoothedSteerInput, inputDt);

                // Direksiyon göstergesi: gerçek bir direksiyon simidi gibi A/D ile döner (±75°)
                SteeringWheelRotate.Angle = smoothedSteerInput * 75.0;

                // Zeminde giden araçlarda S/Down freni olarak davranır (yerdeyken dikey girdi kullanılmıyor)
                bool isGroundVehicle = drivenVehicle.Type != "helicopter" && !drivenVehicle.IsFlying;
                drivenVehicle.IsBraking = isGroundVehicle && vertical < 0 && Math.Abs(drivenVehicle.VelocityX) > 0.5;
                if (isGroundVehicle && vertical < 0)
                {
                    // Yüksek hızda sert fren yapılırsa lastik izi bırakılır (gerçekçi drift/fren hissi).
                    // Islak zeminde tutuş azaldığı için daha düşük hızlarda bile patinaj/kayma başlar.
                    double skidThreshold = drivenVehicle.IsWet ? 2.0 : 3.5;
                    if (Math.Abs(drivenVehicle.VelocityX) > skidThreshold && drivenVehicle.EdgeSide == 0)
                    {
                        SpawnSkidMark(drivenVehicle);
                    }
                    drivenVehicle.VelocityX *= Math.Pow(0.88, gameCore.DeltaFrames);
                }

                // Lastik Patinajı (Wheelspin/Burnout): dururken veya çok düşük hızdayken gaza
                // sonuna kadar basılırsa motor torku lastik tutuşundan fazla gelir - gerçek bir
                // araçtaki gibi tekerlek kayar ve arkada duman/iz bırakır (sert fren izinin
                // simetriği: orada hız yüksekken ani durma, burada hız düşükken ani kalkış).
                if (isGroundVehicle && Math.Abs(vertical) > 0.8 && Math.Abs(drivenVehicle.VelocityX) < 1.2 && drivenVehicle.EdgeSide == 0 && drivenVehicle.Gear != 0)
                {
                    SpawnSkidMark(drivenVehicle);
                }

                // Motor sesi artık sürekli (bkz. UpdateEngineAudio) - burada yalnızca egzoz
                // patlatma (backfire) gibi ANLIK olaylar tetikleniyor.
                bool throttling = horizontal != 0 || (vertical != 0 && !isGroundVehicle);
                if (throttling)
                {
                    engineSoundCooldown -= gameCore.DeltaFrames;
                    if (engineSoundCooldown <= 0)
                    {
                        engineSoundCooldown = 12;

                        // Egzoz Patlatma (Backfire): tüm upgradeları alınmış (maksimum seviye) bir
                        // araç ani gaz verince küçük bir ihtimalle egzozdan alev/pop efekti çıkarır.
                        if (drivenVehicle.Upgrades.Count >= 4 && Random.Shared.NextDouble() < 0.12)
                        {
                            SpawnBackfire(drivenVehicle);
                        }
                    }
                }
                else
                {
                    engineSoundCooldown = 0;
                }

                // Hız göstergesi (HUD): piksel/kare hızını kabaca km/s hissi veren bir değere ölçekler.
                // Geçerli vitesin tepe hızına ne kadar yaklaşıldığına göre renk sarıdan kırmızıya döner (redline hissi).
                double displaySpeed = Math.Sqrt(drivenVehicle.VelocityX * drivenVehicle.VelocityX + drivenVehicle.VelocityY * drivenVehicle.VelocityY);
                SpeedTxt.Text = ((int)Math.Round(displaySpeed * Vehicle.SpeedDisplayScale)).ToString();

                double gearMax = drivenVehicle.CurrentGearMaxSpeed;
                double speedRatio = gearMax > 0 ? Math.Clamp(displaySpeed / gearMax, 0, 1) : 0;
                SpeedTxt.Foreground = speedRatio > 0.85
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x4B))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x6D));

                // Devir (RPM) barı: yeşilden (düşük devir) sarı/kırmızıya (redline) yumuşak geçiş
                RpmBarFill.Width = speedRatio * 128;
                RpmBarFill.Fill = new SolidColorBrush(speedRatio switch
                {
                    < 0.6 => Color.FromRgb(0x39, 0xFF, 0x14),
                    < 0.85 => Color.FromRgb(0xFF, 0xE6, 0x6D),
                    _ => Color.FromRgb(0xFF, 0x4B, 0x4B)
                });

                // Hız Hissi (Sense of Speed): vites tepe hızının %70'ini geçince, sürülen aracın
                // rastgele bir kenarından geriye doğru hızla kayan ince beyaz çizgiler belirir -
                // yüksek hızda sürüyormuş hissini güçlendiren, gerçek yarış oyunlarından tanıdık bir detay.
                if (speedRatio > 0.7 && !lowEffectsMode && Random.Shared.NextDouble() < (speedRatio - 0.7) * 0.6)
                {
                    double travelDirX = drivenVehicle.VelocityX;
                    double travelDirY = drivenVehicle.VelocityY;
                    double travelLen = Math.Sqrt(travelDirX * travelDirX + travelDirY * travelDirY);
                    if (travelLen > 0.01)
                    {
                        double perpX = -travelDirY / travelLen;
                        double perpY = travelDirX / travelLen;
                        double side = (Random.Shared.NextDouble() - 0.5) * drivenVehicle.Height * 1.8;
                        speedLines.Add(new SpeedLine
                        {
                            X = drivenVehicle.X - (travelDirX / travelLen) * drivenVehicle.Width + perpX * side,
                            Y = drivenVehicle.Y - (travelDirY / travelLen) * drivenVehicle.Width + perpY * side,
                            VelX = -travelDirX * 0.6,
                            VelY = -travelDirY * 0.6,
                            Length = 14 + Random.Shared.NextDouble() * 16,
                            Opacity = 0.35 + speedRatio * 0.25
                        });
                    }
                }
            }

            gameCore.Update(RenderCanvas.ActualWidth, RenderCanvas.ActualHeight, activeWinTop, activeWinLeft, activeWinRight,
                multiMonitorMode && monitorFloors.Count > 1 ? FloorYForX : null);
            UpdateStations();
            CheckHardwareStatus();
            UpdateTrafficJams();
            UpdateCarMeetEvent();
            UpdatePoliceStops();
            UpdatePoliceChases();
            UpdateMonitorJumpRamps();
            UpdatePhysicsRamps();
            UpdateMusicSync();

            // İmleç Trafik Değneği aktifken imlece yakın araçlar acil fren yapar
            if (stopSignActive)
            {
                foreach (var vehicle in gameCore.AutoMotions)
                {
                    if (vehicle.IsDragged) continue;
                    double dx = lastMousePos.X - vehicle.X;
                    double dy = lastMousePos.Y - vehicle.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 120)
                    {
                        vehicle.VelocityX *= 0.75;
                        vehicle.VelocityY *= 0.75;
                    }
                }
            }
            UpdateEngineAudio();
            expansionManager.UpdateAll(gameCore, gameCore.GameTime, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);
            CheckPomodoroCompletion();
            CheckCargoDeliveries();
            UpdateWpmRace();
            PublishIotTelemetryIfDue();
            CheckReminders();
            CheckGreetings();
            UpdateClickThrough();
            RenderGame();

            // Panel metinleri (para, yakıt, HP, başarım listesi...) 144 kez/sn güncellenmesine gerek
            // yok - her karede onlarca TextBlock'a yazmak WPF'te sürekli layout/measure tetikleyip
            // kayda değer CPU yakıyordu. Saniyede ~20 kez güncellemek göz için tamamen yeterli.
            if ((DateTime.UtcNow - lastUiRefresh).TotalMilliseconds >= 50)
            {
                lastUiRefresh = DateTime.UtcNow;
                UpdateUI();
            }

            saveManager.AutoSave(gameCore);
        }


        private DateTime lastUiRefresh = DateTime.MinValue;


        private void UpdateUI()
        {
            VehicleCountTxt.Text = gameCore.AutoMotions.Count.ToString();
            MoneyTxt.Text = $"${gameCore.GameData.Money}";
            AddFuelBtn.Content = Loc.T("ui.buy_fuel_price", CurrentFuelPrice());
            ItemCountTxt.Text = gameCore.Items.Count.ToString();
            TotalMileageTxt.Text = $"{(int)gameCore.GameData.TotalMileage} km";

            miniHud?.UpdateStats(gameCore.AutoMotions.Count, gameCore.GameData.Money, gameCore.GameData.TotalMileage);
            miniHud?.UpdateMiniMap(gameCore.AutoMotions, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);

            int unlockedCount = gameCore.GameData.UnlockedAchievements.Count;
            AchievementsSummaryTxt.Text = Loc.T("ui.achievements_summary", unlockedCount, GameCore.AchievementCatalog.Count);
            // Artık sadece kazanılanlar değil, TÜM başarımlar listeleniyor - henüz kazanılmayanlar
            // 🔒 ile soluk gösterilir, böylece oyuncu "sırada ne var" görebiliyor (VPet'in başarım
            // vitrinine benzer motivasyon hissi).
            if (lastAchievementUnlockedCount != unlockedCount || AchievementsListControl.ItemsSource == null)
            {
                lastAchievementUnlockedCount = unlockedCount;
                AchievementsListControl.ItemsSource = GameCore.AchievementCatalog
                    .Select(a => new AchievementRow
                    {
                        Label = (gameCore.GameData.UnlockedAchievements.Contains(a.Id) ? "✅ " : "🔒 ") + a.Label,
                        Unlocked = gameCore.GameData.UnlockedAchievements.Contains(a.Id)
                    })
                    .ToList();
            }

            if (selectedVehicle != null)
            {
                VehicleTypeTxt.Text = selectedVehicle.Emoji + " " + selectedVehicle.Type.ToUpper();
                VehicleScaleTxt.Text = $"{(int)Math.Round(selectedVehicle.Scale * 100)}%";
                HPTxt.Text = $"{selectedVehicle.HP}/{selectedVehicle.MaxHP}";
                FuelTxt.Text = $"{(int)selectedVehicle.Fuel}/{(int)selectedVehicle.MaxFuel}";
                HPBar.Value = selectedVehicle.HP;
                HPBar.Maximum = selectedVehicle.MaxHP;
                FuelBar.Value = selectedVehicle.Fuel;
                FuelBar.Maximum = selectedVehicle.MaxFuel;
                HappinessBar.Value = selectedVehicle.Happiness;
                TireWearBar.Value = selectedVehicle.TireWear;
                ToggleLearningAIBtn.Opacity = selectedVehicle.UseLearningAI ? 1.0 : 0.4;

                if (VehicleNameBox != null && !VehicleNameBox.IsFocused)
                {
                    VehicleNameBox.TextChanged -= VehicleNameBox_TextChanged;
                    VehicleNameBox.Text = selectedVehicle.Name;
                    VehicleNameBox.TextChanged += VehicleNameBox_TextChanged;
                }

                // Yükseltmeler iç kimlikleriyle ("speed", "efficiency") saklanıyor; ekranda eskiden bu
                // ham kimlikler ve sabit "Yok" görünüyordu - her dilde aynı İngilizce/Türkçe karışımı.
                UpgradesTxt.Text = selectedVehicle.Upgrades.Count > 0
                    ? string.Join(", ", selectedVehicle.Upgrades.Select(id => Loc.TOrDefault($"item.upgrade.{id}", id)))
                    : Loc.T("ui.yok");

                // Dorse / Ekipman paneli - sadece kamyon ve traktörlerde
                bool isTruck = selectedVehicle.Type == "truck";
                bool isTractor = selectedVehicle.Type == "tractor";
                TrailerPanel.Visibility = (isTruck || isTractor) ? Visibility.Visible : Visibility.Collapsed;
                TrailerFlatbedBtn.Visibility = isTruck ? Visibility.Visible : Visibility.Collapsed;
                TrailerTankerBtn.Visibility = isTruck ? Visibility.Visible : Visibility.Collapsed;
                TrailerEquipmentBtn.Visibility = isTractor ? Visibility.Visible : Visibility.Collapsed;
                UpdateTrailerButtonHighlights();
            }
            else
            {
                if (VehicleNameBox != null) VehicleNameBox.Text = "";
                VehicleTypeTxt.Text = Loc.T("ui.no_selection");
                HPTxt.Text = "0/0";
                FuelTxt.Text = "0/0";
                HPBar.Value = 0;
                FuelBar.Value = 0;
                HappinessBar.Value = 0;
                TireWearBar.Value = 0;
                UpgradesTxt.Text = Loc.T("ui.yok");
                TrailerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void AutoSaveIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (saveManager == null) return; // Window_Loaded'dan önce XAML varsayılan seçimi tetikleyebilir
            if (AutoSaveIntervalCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
            {
                saveManager.AutoSaveIntervalSeconds = seconds;
                settings.Current.AutoSaveIntervalSeconds = seconds;
                settings.Save();
                soundEngine?.PlayClick();
            }
        }

        private void RefreshItemsList()
        {
            var itemsList = new ObservableCollection<Item>(gameCore.Items);
            ItemsListControl.ItemsSource = itemsList;
        }
    }
}
