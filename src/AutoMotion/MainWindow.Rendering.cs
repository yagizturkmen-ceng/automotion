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
    // Sahne çizim katmanı: ızgara/gece katmanları, ana RenderGame() kare döngüsü, araç gövde/tekerlek
    // render önbelleği (bkz. CachedVehicleBody/CachedWheel) ve DrawVehicle/DrawVehicleImage/
    // DrawTrailerImage. MainWindow.xaml.cs'in geri kalanından (girdi/UI olay işleyicileri, oyun
    // döngüsü/kayıt/eklenti mantığı) ayrı tutulur - tek başına ~1150 satırdı, dosyayı bölmenin
    // ilk adımı olarak ayrı bir partial class dosyasına taşındı.
    public partial class MainWindow : Window
    {
        /// <summary>Sahne görsel ağacını kareler arasında koruyan katman yöneticisi
        /// (bkz. Utilities/RetainedScene.cs). Window_Loaded'da kurulur.</summary>
        private RetainedScene scene;

        private Brush lastGridBrush;
        private bool lastGridHidden;

        /// <summary>
        /// Arka plan ızgarasını tek bir döşemeli (tiled) fırçayla çizer. Izgara sabit olduğundan
        /// yalnızca tema veya "tüm arayüzü gizle" durumu değiştiğinde yeniden kurulur - eskiden her
        /// karede ekran boyutuna göre ~60 ayrı Line nesnesi oluşturuluyordu ve 144 FPS'te bu tek
        /// başına belirgin bir CPU yüküydü.
        /// </summary>
        private void UpdateGridLayer()
        {
            var gridBrush = themeEngine.GetGridColorCached();
            if (ReferenceEquals(gridBrush, lastGridBrush) && allUIHidden == lastGridHidden) return;

            lastGridBrush = gridBrush;
            lastGridHidden = allUIHidden;

            if (allUIHidden)
            {
                GridLayer.Fill = null;
                return;
            }

            const double gridSize = 50;
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(gridSize, 0)));
            geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, gridSize)));

            var drawing = new GeometryDrawing(null, new Pen(gridBrush, 0.5), geometry);
            var tile = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, gridSize, gridSize),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            tile.Freeze();
            GridLayer.Fill = tile;
        }

        /// <summary>Tema geçişinin hızı: 60 FPS'te kare başına bu kadar solar, yani ~0.5 saniyede
        /// tamamlanır. Hava durumundan (2 sn) belirgin daha hızlı - tema değişimi kullanıcının
        /// AZ ÖNCE yaptığı bir eylem, geri bildirimi gecikmemeli.</summary>
        private const double ThemeFadePerFrame = 1.0 / 30.0;

        private Brush lastThemeBrush;
        private double themeFadeOpacity;

        /// <summary>
        /// Tema değiştiğinde eski arka planı yeni arka planın üstünde bırakıp opaklığını sıfıra
        /// rampalar - renk anında sıçramak yerine çapraz solar (kalan işler 3.4 / B-3).
        /// </summary>
        private void UpdateThemeTransition(double fxDt)
        {
            var brush = themeEngine.GetBackgroundBrushCached();

            if (!ReferenceEquals(brush, lastThemeBrush))
            {
                // Yeni tema alta, ESKİ tema üste (tam opak) - üstteki solarak yeniyi açığa çıkarır.
                if (lastThemeBrush != null)
                {
                    ThemeFadeLayer.Fill = lastThemeBrush;
                    themeFadeOpacity = 1.0;
                }
                ThemeLayer.Fill = brush;
                lastThemeBrush = brush;
            }

            if (themeFadeOpacity > 0)
            {
                themeFadeOpacity = Ramp.Approach(themeFadeOpacity, 0, ThemeFadePerFrame, fxDt);
                ThemeFadeLayer.Opacity = themeFadeOpacity;

                // Solma bitince fırçayı bırak: ekranı kaplayan bir katmanı boşuna çizdirmeyelim.
                if (themeFadeOpacity <= 0) ThemeFadeLayer.Fill = null;
            }
        }

        private double lastNightFactor = -1;

        private void UpdateNightLayer()
        {
            double nightFactor = lowEffectsMode ? 0 : GetNightFactor();

            // Fırtınalı (8) ve Sentetik Gece (10) temaları zaten kendi koyu/moody renk paletleriyle
            // geliyor - üzerine tam kuvvette gece katmanı bindirmek onları donuk/çamurlu bir griye
            // dönüştürüyordu. Bu iki temada gece katmanının etkisi yumuşatılır; diğer temalarda
            // (özellikle şeffaf masaüstü modu ve Kış'ın parlak buz mavisi) tam kuvvette kalır.
            if (currentTheme == 8 || currentTheme == 10) nightFactor *= 0.35;

            if (Math.Abs(nightFactor - lastNightFactor) < 0.004) return; // ~1/255 alfa adımı
            lastNightFactor = nightFactor;

            if (nightFactor <= 0.001)
            {
                NightLayer.Fill = null;
                return;
            }
            var brush = new SolidColorBrush(Color.FromArgb((byte)(nightFactor * 90), 5, 8, 20));
            brush.Freeze();
            NightLayer.Fill = brush;
        }

        /// <summary>Sahnede çizilecek dinamik bir şey var mı? (araç, engel, parçacık, hava durumu,
        /// imleç modu, seçim kutusu...) Hiçbiri yoksa render tamamen atlanabilir.</summary>
        private bool HasAnythingToRender()
        {
            return gameCore.AutoMotions.Count > 0
                || gameCore.Obstacles.Count > 0
                || particles.Count > 0 || skidMarks.Count > 0 || exhaustPuffs.Count > 0 || damageSmoke.Count > 0
                || speedLines.Count > 0 || greetingPops.Count > 0 || physicsRamps.Count > 0
                || rainDrops.Count > 0 || rainSplashes.Count > 0 || snowFlakes.Count > 0
                || isRaining || isSnowing
                || fuelCanActive || stopSignActive || selectionBox != null;
        }

        private void RenderGame()
        {
            // Sahne görsel ağacı kareler arasında KORUNUR: elemanlar aynı sırayla yeniden
            // yerleştirilir, sıra değişmediyse WPF ağacına hiç dokunulmaz (bkz. RetainedScene).
            scene.BeginFrame();
            PruneVehicleRenderCaches();

            // Hareket bulanıklığı bütçesi bu karede en hızlı araçlara ayrılır (bkz.
            // MaxBlurredWheelsPerFrame): filodaki en yüksek hızın belirgin altında kalanlarda
            // bulanıklık zaten fark edilmiyor, ama her biri ayrı bir render geçişine mal oluyordu.
            blurredWheelsThisFrame = 0;
            frameMaxTravelSpeed = 0;
            foreach (var v in gameCore.AutoMotions)
            {
                double s = Math.Sqrt(v.VelocityX * v.VelocityX + v.VelocityY * v.VelocityY);
                if (s > frameMaxTravelSpeed) frameMaxTravelSpeed = s;
            }
            TrimAllEffectLists();
            UpdateGridLayer();

            // Görsel efektlerin (yağmur/kar/duman/iz/hız çizgisi) hızı da tıpkı araç fiziği gibi
            // "60 FPS'te kare başına" ayarlanmıştı; 144 FPS'te 2.4 kat hızlı akıyorlardı. Aynı
            // delta ölçeğiyle gerçek zamana sabitleniyorlar.
            double fxDt = gameCore.DeltaFrames;

            // Arka plan artık RenderCanvas.Background'da değil, kendi katmanında - tema değişiminde
            // çapraz solma yapılabilsin diye (bkz. UpdateThemeTransition).
            UpdateThemeTransition(fxDt);

            // Izgara ve gündüz/gece koyulaşması artık her karede yeniden üretilmiyor: ikisi de kendi
            // sabit katmanlarında (GridLayer/NightLayer), yalnızca gerçekten değiştiklerinde güncelleniyor.
            UpdateNightLayer();

            // Çizilecek hiçbir dinamik içerik yoksa görsel ağaca HİÇ dokunma. WPF yalnızca ağaç
            // kirlendiğinde yeniden kompozisyon yapar; saydam tam ekran bir pencerede bu kompozisyon
            // 144 Hz'de tek başına belirgin CPU yakıyordu - boş masaüstünde artık neredeyse sıfır.
            if (!HasAnythingToRender())
            {
                // Sahnede hiçbir şey yoksa da kırpma yapılmalı: son karede çizilmiş elemanlar
                // (ör. silinen son araç) ekranda asılı kalmasın.
                scene.EndFrame();
                return;
            }

            // Fiziksel Rampa Çizimi: aktif rampa çizgilerini çiz (ömrü dolanlar UpdatePhysicsRamps'ta silinir)
            foreach (var (a, b, life) in physicsRamps)
            {
                var rampLine = scene.Rent<Line>(ScenePool.PhysicsRamp);
                rampLine.X1 = a.X; rampLine.Y1 = a.Y; rampLine.X2 = b.X; rampLine.Y2 = b.Y;
                rampLine.StrokeThickness = 4;
                rampLine.StrokeStartLineCap = PenLineCap.Round;
                rampLine.StrokeEndLineCap = PenLineCap.Round;
                SetStroke(rampLine, Color.FromArgb((byte)Math.Min(255, life / 2), 0xFB, 0xCA, 0x1F));
            }

            // Manuel Doldurma: bidon imleci takip eder
            if (fuelCanActive)
            {
                var canIcon = scene.Rent<TextBlock>(ScenePool.FuelCanCursor);
                canIcon.Text = "🛢️";
                canIcon.FontSize = 22;
                Canvas.SetLeft(canIcon, lastMousePos.X - 11);
                Canvas.SetTop(canIcon, lastMousePos.Y - 11);
            }

            // İmleç Trafik Değneği aktifken imlecin etrafında "Dur" halkası göster
            if (stopSignActive)
            {
                var stopRing = scene.Rent<Ellipse>(ScenePool.StopSignRing);
                stopRing.Width = 240; stopRing.Height = 240;
                stopRing.StrokeThickness = 2;
                stopRing.Fill = Brushes.Transparent;
                SetStroke(stopRing, Color.FromArgb(70, 255, 60, 60));
                Canvas.SetLeft(stopRing, lastMousePos.X - 120);
                Canvas.SetTop(stopRing, lastMousePos.Y - 120);
            }


            // Lastik İzleri (Skid Marks) - yavaşça solar, zeminin üstünde/araçların altında çizilir
            for (int i = skidMarks.Count - 1; i >= 0; i--)
            {
                var mark = skidMarks[i];
                mark.Opacity *= Math.Pow(0.988, fxDt);
                if (mark.Opacity < 0.03) { skidMarks.RemoveAt(i); continue; }

                var skidLine = scene.Rent<Line>(ScenePool.SkidMark);
                skidLine.X1 = mark.X - Math.Cos(mark.Angle) * mark.Width / 2;
                skidLine.Y1 = mark.Y - Math.Sin(mark.Angle) * mark.Width / 2;
                skidLine.X2 = mark.X + Math.Cos(mark.Angle) * mark.Width / 2;
                skidLine.Y2 = mark.Y + Math.Sin(mark.Angle) * mark.Width / 2;
                skidLine.StrokeThickness = 3;
                skidLine.StrokeStartLineCap = PenLineCap.Round;
                skidLine.StrokeEndLineCap = PenLineCap.Round;
                SetStroke(skidLine, Color.FromArgb((byte)(mark.Opacity * 255), 20, 20, 20));
            }

            // Egzoz Dumanı: hızlı giden araçların arkasında yükselip dağılan hafif duman parçacıkları
            for (int i = exhaustPuffs.Count - 1; i >= 0; i--)
            {
                var puff = exhaustPuffs[i];
                puff.X += puff.VelX * fxDt;
                puff.Y += puff.VelY * fxDt;
                puff.VelY -= 0.015 * fxDt; // hafifçe yükselir
                puff.Size += 0.25 * fxDt;
                puff.Opacity *= Math.Pow(0.94, fxDt);
                if (puff.Opacity < 0.03) { exhaustPuffs.RemoveAt(i); continue; }

                var puffDot = scene.Rent<Ellipse>(ScenePool.ExhaustPuff);
                puffDot.Width = puff.Size;
                puffDot.Height = puff.Size;
                SetFill(puffDot, Color.FromArgb((byte)(puff.Opacity * 255), 180, 180, 185));
                Canvas.SetLeft(puffDot, puff.X - puff.Size / 2);
                Canvas.SetTop(puffDot, puff.Y - puff.Size / 2);
            }

            // Hasar Dumanı: ağır hasarlı araçların motorundan çıkan koyu, egzozdan daha yavaş
            // yükselen ve daha geniş yayılan duman.
            for (int i = damageSmoke.Count - 1; i >= 0; i--)
            {
                var smoke = damageSmoke[i];
                smoke.X += smoke.VelX * fxDt;
                smoke.Y += smoke.VelY * fxDt;
                smoke.VelY -= 0.006 * fxDt;
                smoke.VelX *= Math.Pow(0.99, fxDt);
                smoke.Size += 0.42 * fxDt; // egzozdan daha hızlı yayılır (yoğun/yağlı duman hissi)
                smoke.Opacity *= Math.Pow(0.965, fxDt);
                if (smoke.Opacity < 0.03) { damageSmoke.RemoveAt(i); continue; }

                var smokeDot = scene.Rent<Ellipse>(ScenePool.DamageSmoke);
                smokeDot.Width = smoke.Size;
                smokeDot.Height = smoke.Size;
                smokeDot.Fill = DamageSmokeBrush;
                smokeDot.Opacity = smoke.Opacity;
                Canvas.SetLeft(smokeDot, smoke.X - smoke.Size / 2);
                Canvas.SetTop(smokeDot, smoke.Y - smoke.Size / 2);
            }

            // Selamlaşma Balonları (👋): yükselip solarak kaybolur - gerçek zamana göre (kare
            // hızından bağımsız) sabit ~1.1 saniye sürer
            for (int i = greetingPops.Count - 1; i >= 0; i--)
            {
                var pop = greetingPops[i];
                double elapsed = (DateTime.Now - pop.CreatedAt).TotalSeconds;
                if (elapsed >= GreetingPop.DurationSeconds) { greetingPops.RemoveAt(i); continue; }

                double frac = 1 - elapsed / GreetingPop.DurationSeconds;
                var popTxt = scene.Rent<TextBlock>(ScenePool.GreetingPop);
                popTxt.Text = "👋";
                popTxt.FontSize = 16;
                popTxt.Opacity = Math.Clamp(frac, 0, 1);
                Canvas.SetLeft(popTxt, pop.X - 8);
                Canvas.SetTop(popTxt, pop.Y - 8 - (1 - frac) * 16);
            }

            // Hız Çizgileri: sürüşte yüksek hızda beliren, hızla geriye doğru kayıp solan çizgiler
            for (int i = speedLines.Count - 1; i >= 0; i--)
            {
                var sl = speedLines[i];
                sl.X += sl.VelX * fxDt;
                sl.Y += sl.VelY * fxDt;
                sl.Opacity *= Math.Pow(0.90, fxDt);
                if (sl.Opacity < 0.03) { speedLines.RemoveAt(i); continue; }

                double dirLen = Math.Sqrt(sl.VelX * sl.VelX + sl.VelY * sl.VelY);
                double ux = dirLen > 0.001 ? sl.VelX / dirLen : 1;
                double uy = dirLen > 0.001 ? sl.VelY / dirLen : 0;
                var streak = scene.Rent<Line>(ScenePool.SpeedLine);
                streak.X1 = sl.X;
                streak.Y1 = sl.Y;
                streak.X2 = sl.X - ux * sl.Length;
                streak.Y2 = sl.Y - uy * sl.Length;
                streak.StrokeThickness = 1.6;
                streak.StrokeStartLineCap = PenLineCap.Round;
                streak.StrokeEndLineCap = PenLineCap.Round;
                SetStroke(streak, Color.FromArgb((byte)(sl.Opacity * 255), 255, 255, 255));
            }

            // Hava durumu artık AÇIK/KAPALI değil, YOĞUNLUĞU rampayla değişiyor (bkz. 3.4 / B-2):
            // yağmur bastırarak başlıyor, dinerek bitiyor. Eskiden bayrak değişir değişmez tam
            // şiddetle başlıyor, kapanınca da yeni damla üretimi bir anda kesiliyordu.
            rainIntensity = Ramp.Approach(rainIntensity, isRaining ? 1.0 : 0.0, WeatherRampPerFrame, fxDt);
            snowIntensity = Ramp.Approach(snowIntensity, isSnowing ? 1.0 : 0.0, WeatherRampPerFrame, fxDt);

            // Yağmur Damlalarını Çiz (Weather System)
            if (rainIntensity > 0.001 || rainDrops.Count > 0 || rainSplashes.Count > 0)
            {
                if (RenderCanvas.ActualWidth > 0)
                {
                    // Doğuş sayısı hem kare hızından bağımsız hem de yoğunlukla orantılı: 60 FPS'te
                    // tam şiddette kare başına 3 damla (kesirli artık bir sonraki kareye devredilir).
                    rainSpawnAccumulator += 3 * rainIntensity * fxDt;
                    int spawn = (int)rainSpawnAccumulator;
                    rainSpawnAccumulator -= spawn;
                    for (int r = 0; r < spawn; r++)
                    {
                        rainDrops.Add(new RainDrop
                        {
                            X = Random.Shared.NextDouble() * RenderCanvas.ActualWidth,
                            Y = -10,
                            Speed = Random.Shared.Next(9, 16),
                            Length = Random.Shared.Next(15, 26)
                        });
                    }
                }

                for (int i = rainDrops.Count - 1; i >= 0; i--)
                {
                    var drop = rainDrops[i];
                    drop.Y += drop.Speed * fxDt;
                    drop.X -= 0.8 * fxDt; // Hafif rüzgar eğimi
                    if (drop.Y > RenderCanvas.ActualHeight)
                    {
                        if (!lowEffectsMode) rainSplashes.Add(new RainSplash { X = drop.X, Y = RenderCanvas.ActualHeight });
                        rainDrops.RemoveAt(i);
                        continue;
                    }

                    var line = scene.Rent<Line>(ScenePool.RainDrop);
                    line.X1 = drop.X;
                    line.Y1 = drop.Y;
                    line.X2 = drop.X - 1.2;
                    line.Y2 = drop.Y + drop.Length;
                    line.Stroke = RainDropBrush;
                    line.StrokeThickness = 1.0;
                }

                for (int i = rainSplashes.Count - 1; i >= 0; i--)
                {
                    var splash = rainSplashes[i];
                    splash.Age += 0.09 * fxDt;
                    if (splash.Age >= 1)
                    {
                        rainSplashes.RemoveAt(i);
                        continue;
                    }

                    double size = 3 + splash.Age * 12;
                    var ring = scene.Rent<Ellipse>(ScenePool.RainSplash);
                    ring.Width = size;
                    ring.Height = size * 0.4; // zemine yatık bir elips - yandan bakılan bir sıçrama hissi
                    ring.Stroke = RainDropBrush;
                    ring.StrokeThickness = 1.0;
                    ring.Fill = Brushes.Transparent;
                    ring.Opacity = 1 - splash.Age;
                    Canvas.SetLeft(ring, splash.X - size / 2);
                    Canvas.SetTop(ring, splash.Y - size * 0.2);
                }
            }

            // Kar Tanelerini Çiz (Kış Teması) - yağmurla aynı anda AKTİF OLMAZ ama sisteme
            // simetrik: daha yavaş düşer, yanlamasına salınarak (rüzgarda uçuşma hissi) süzülür.
            if (snowIntensity > 0.001 || snowFlakes.Count > 0)
            {
                if (RenderCanvas.ActualWidth > 0)
                {
                    snowSpawnAccumulator += 2 * snowIntensity * fxDt;
                    int spawn = (int)snowSpawnAccumulator;
                    snowSpawnAccumulator -= spawn;
                    for (int s = 0; s < spawn; s++)
                    {
                        snowFlakes.Add(new SnowFlake
                        {
                            X = Random.Shared.NextDouble() * RenderCanvas.ActualWidth,
                            Y = -10,
                            Speed = 0.6 + Random.Shared.NextDouble() * 1.4,
                            Size = 2 + Random.Shared.NextDouble() * 3,
                            Drift = 0.4 + Random.Shared.NextDouble() * 0.8,
                            DriftPhase = Random.Shared.NextDouble() * Math.PI * 2
                        });
                    }
                }

                for (int i = snowFlakes.Count - 1; i >= 0; i--)
                {
                    var flake = snowFlakes[i];
                    flake.Y += flake.Speed * fxDt;
                    flake.DriftPhase += 0.03 * fxDt;
                    flake.X += Math.Sin(flake.DriftPhase) * flake.Drift * fxDt;
                    if (flake.Y > RenderCanvas.ActualHeight)
                    {
                        snowFlakes.RemoveAt(i);
                        continue;
                    }

                    var dot = scene.Rent<Ellipse>(ScenePool.SnowFlake);
                    dot.Width = flake.Size;
                    dot.Height = flake.Size;
                    dot.Fill = SnowFlakeBrush;
                    Canvas.SetLeft(dot, flake.X - flake.Size / 2);
                    Canvas.SetTop(dot, flake.Y - flake.Size / 2);
                }
            }

            // Engelleri (Obstacles) Çiz (Trafik Işıkları, Kargo Paketi ve Hedefler)
            foreach (var obs in gameCore.Obstacles)
            {
                var obsBorder = scene.Rent<Border>(ScenePool.ObstacleBox);
                obsBorder.Width = obs.Width;
                obsBorder.Height = obs.Height;
                obsBorder.Background = Brushes.Transparent;

                // Emoji ve parıltı efekti kutuyla birlikte yaşar - eskiden her karede yeni bir
                // TextBlock ve yeni bir DropShadowEffect üretiliyordu (efekt nesnesi WPF'te ucuz
                // değildir: her biri ayrı bir kompozisyon geçişi kurar).
                if (obsBorder.Child is not TextBlock obsTxt)
                {
                    obsTxt = new TextBlock { TextAlignment = TextAlignment.Center };
                    obsBorder.Child = obsTxt;
                }
                obsTxt.Text = obs.Emoji;
                obsTxt.FontSize = obs.Type == "traffic_light" ? 22 : 24;

                if (obsBorder.Effect is not DropShadowEffect obsGlow)
                {
                    obsGlow = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.6 };
                    obsBorder.Effect = obsGlow;
                }
                obsGlow.Color = obs.Type == "traffic_light"
                    ? (obs.State == "red" ? Colors.Red : (obs.State == "yellow" ? Colors.Yellow : Colors.Green))
                    : obs.Type == "station" ? Colors.LimeGreen
                    : obs.Type == "cone" ? Colors.OrangeRed
                    : obs.Type == "police" ? Colors.DeepSkyBlue
                    : Colors.Orange;

                Canvas.SetLeft(obsBorder, obs.X - obs.Width / 2);
                Canvas.SetTop(obsBorder, obs.Y - obs.Height / 2);
            }

            // Parçacıkları Güncelle ve Çiz (Neon Trail)
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.X += p.VelocityX * fxDt;
                p.Y += p.VelocityY * fxDt;
                p.Opacity -= 0.04 * fxDt; // sönümleme
                if (p.Opacity <= 0)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                // Parıltı, eleman başına GPU bulanıklık geçişi (DropShadowEffect) yerine
                // önbelleklenmiş radyal gradyanla çiziliyor - bkz. GetParticleGlowBrush. Gradyanın
                // sönümlenme payını içerecek şekilde çap iki katına çıkarılıp aynı merkeze
                // hizalanıyor, böylece görsel boyut/parlaklık eskisiyle eşleşiyor.
                double glowSize = p.Size * 2;
                var shape = scene.Rent<Ellipse>(ScenePool.Particle);
                shape.Width = glowSize;
                shape.Height = glowSize;
                shape.Fill = GetParticleGlowBrush(p.Color);
                shape.Opacity = p.Opacity;
                Canvas.SetLeft(shape, p.X - glowSize / 2);
                Canvas.SetTop(shape, p.Y - glowSize / 2);
            }

            // Araçları çiz ve yeni parçacık üret
            foreach (var vehicle in gameCore.AutoMotions)
            {
                // Yağmurlu havaya göre ıslaklık/kayganlık durumunu araca bildir
                vehicle.IsWet = isRaining || isSnowing;

                DrawVehicle(vehicle);

                // Motor sesi artık BURADA araç başına tetiklenmiyor: sürekli tek bir ton olarak
                // oyun döngüsünde yönetiliyor (bkz. MainWindow.UpdateEngineAudio). Eskiden her araç
                // için 60 karede bir kısa "bip" çalınıyordu ve tek kanallı çalıcı yüzünden 10 araçlık
                // bir filoda bunların çoğu duyulmadan kesiliyordu.
                if (vehicle.State == "chasing" && gameCore.GameTime % 200 == 0)
                    soundEngine.PlayHorn(vehicle.Type);

                // Parçacık üretimi - sadece gerçek marka görseli olmayan (eski çizim/emoji ile
                // gösterilen) araçlarda; gerçek görselli araçların arkasından duman/iz çıkmasın.
                double speed = Math.Sqrt(vehicle.VelocityX * vehicle.VelocityX + vehicle.VelocityY * vehicle.VelocityY);
                bool hasRealVisual = GetVisualForVehicle(vehicle) != null;
                if (!lowEffectsMode && !hasRealVisual && (vehicle.IsDragged || speed > 0.5 || vehicle.Type == "helicopter"))
                {
                    var vehicleColor = (Color)ColorConverter.ConvertFromString(vehicle.Color);
                    Color pColor = vehicleColor;
                    if (vehicle.Type == "helicopter")
                    {
                        pColor = Color.FromRgb(220, 245, 255);
                    }
                    else if (vehicle.Type == "train")
                    {
                        pColor = Color.FromRgb(150, 150, 150);
                    }

                    double angleRad = vehicle.Angle + Math.PI; // arkaya doğru
                    double offsetX = Math.Cos(angleRad) * (vehicle.Width / 2);
                    double offsetY = Math.Sin(angleRad) * (vehicle.Height / 2);

                    particles.Add(new VehicleParticle
                    {
                        X = vehicle.X + offsetX + (Random.Shared.NextDouble() - 0.5) * 6,
                        Y = vehicle.Y + offsetY + (Random.Shared.NextDouble() - 0.5) * 6,
                        VelocityX = Math.Cos(angleRad) * (speed * 0.15 + 0.3) + (Random.Shared.NextDouble() - 0.5) * 0.8,
                        VelocityY = Math.Sin(angleRad) * (speed * 0.15 + 0.3) + (Random.Shared.NextDouble() - 0.5) * 0.8,
                        Size = Random.Shared.Next(4, 9),
                        Opacity = 1.0,
                        Color = pColor
                    });
                }

                // Egzoz Dumanı: yüksek hızda giden HER araç tipinde (gerçek marka görselli olsa
                // bile) egzozdan hafif, seyrek duman çıkar - gerçek araçların hızlı giderken
                // arkasından çıkan ince duman izine benzer. Motosiklet/bisiklette egzoz yok.
                bool hasExhaust = vehicle.Type != "bike" && vehicle.Type != "helicopter";
                if (!lowEffectsMode && hasExhaust && speed > 3.0 && Random.Shared.NextDouble() < 0.12)
                {
                    double exhaustAngle = vehicle.Angle + Math.PI;
                    double ex = vehicle.X + Math.Cos(exhaustAngle) * (vehicle.Width * 0.45);
                    double ey = vehicle.Y + Math.Sin(exhaustAngle) * (vehicle.Height * 0.2) + vehicle.Height * 0.25;
                    exhaustPuffs.Add(new ExhaustPuff
                    {
                        X = ex,
                        Y = ey,
                        VelX = Math.Cos(exhaustAngle) * 0.4 + (Random.Shared.NextDouble() - 0.5) * 0.3,
                        VelY = -0.15 + (Random.Shared.NextDouble() - 0.5) * 0.2,
                        Size = 3 + Random.Shared.NextDouble() * 2,
                        Opacity = 0.28
                    });
                }

                // Hasar Dumanı: ağır hasarlı araç (HP < %35) motor bölmesinden sürekli koyu duman
                // çıkarır. HP barı yalnızca fare üzerine gelince göründüğünden, hasarın uzaktan da
                // fark edilmesini sağlayan tek görsel ipucu budur - oyuncuya "bu aracı onar" sinyali.
                if (!lowEffectsMode && vehicle.IsSmoking && Random.Shared.NextDouble() < 0.16)
                {
                    // Hasar arttıkça duman yoğunlaşır (HP %35'te ince, %0'a yakınken belirgin).
                    double severity = 1.0 - Math.Clamp(vehicle.HP / (vehicle.MaxHP * 0.35), 0, 1);
                    double frontSign = vehicle.FacingLeft ? -1 : 1; // duman motor (ön) bölmesinden çıkar

                    damageSmoke.Add(new ExhaustPuff
                    {
                        X = vehicle.X + frontSign * vehicle.Width * 0.35 + (Random.Shared.NextDouble() - 0.5) * 4,
                        Y = vehicle.Y - vehicle.Height * 0.35,
                        VelX = (Random.Shared.NextDouble() - 0.5) * 0.35,
                        VelY = -0.45 - Random.Shared.NextDouble() * 0.35,
                        Size = 4 + Random.Shared.NextDouble() * 4 * (0.5 + severity),
                        Opacity = 0.30 + severity * 0.35
                    });
                }
            }

            // Gece Modu Overlay Filtresi (Saat 20:00 - 06:00 arası, veya elle zorlanmışsa)
            bool isNight = IsNightActive();
            if (isNight)
            {
                var nightOverlay = scene.Rent<Rectangle>(ScenePool.NightOverlay);
                nightOverlay.Width = RenderCanvas.ActualWidth;
                nightOverlay.Height = RenderCanvas.ActualHeight;
                nightOverlay.IsHitTestVisible = false;
                // Filtrenin yoğunluğu da sabit değil, karanlıkla birlikte artar (alacakaranlıkta
                // hafif, gece tam) - eskiden eşiği geçer geçmez tam kuvvette beliriyordu.
                byte nightAlpha = (byte)Math.Round(40 * Math.Clamp(GetNightFactor(), 0, 1));
                SetFill(nightOverlay, Color.FromArgb(nightAlpha, 8, 15, 38)); // Yumuşak gece filtresi
            }

            // UI
            DrawUIOverlay();

            // Bu karede yerleştirilmeyen (artık sahnede olmayan) elemanları kırp.
            scene.EndFrame();
        }

        /// <summary>
        /// Havuzdan gelen bir şeklin kontur rengini, YENİ fırça üretmeden günceller: aynı şekil her
        /// karede aynı fırça nesnesini taşır, yalnızca rengi değişir. Eskiden kare başına eleman
        /// başına bir SolidColorBrush ayrılıyordu (144 FPS'te yüzlerce kısa ömürlü nesne).
        /// </summary>
        private static void SetStroke(Shape shape, Color color)
        {
            if (shape.Stroke is SolidColorBrush brush && !brush.IsFrozen) brush.Color = color;
            else shape.Stroke = new SolidColorBrush(color);
        }

        /// <summary>Dolgu rengi için <see cref="SetStroke"/> ile aynı mantık.</summary>
        private static void SetFill(Shape shape, Color color)
        {
            if (shape.Fill is SolidColorBrush brush && !brush.IsFrozen) brush.Color = color;
            else shape.Fill = new SolidColorBrush(color);
        }

        /// <summary>Bir alt kanvastaki (ör. lamba kökü) index'inci elemanı verir, yoksa üretir -
        /// iç içe öğeler de kareler arasında korunsun diye.</summary>
        private static Ellipse LampAt(Canvas root, int index)
        {
            while (root.Children.Count <= index) root.Children.Add(new Ellipse());
            return (Ellipse)root.Children[index];
        }

        /// <summary>Bu karede kullanılmayan alt öğeleri kırpar.</summary>
        private static void TrimChildren(Panel panel, int used)
        {
            while (panel.Children.Count > used) panel.Children.RemoveAt(panel.Children.Count - 1);
        }

        // === Render Diffing (araç gövde/tekerlek görselleri) ===
        // Önceden her kare (144 kez/sn) her araç için sıfırdan yeni Image/Canvas/EllipseGeometry/
        // RotateTransform/BlurEffect nesneleri inşa ediliyordu - bu, projenin en belirgin bilinen
        // mimari performans kısıtıydı (bkz. AutoMotion_Proje_Raporu.md §7). Aynı Vehicle için aynı
        // Canvas/Image/Transform nesneleri araç yaşadığı sürece saklanıp (vehicleBodyCache/
        // trailerBodyCache) sadece mutasyona uğratılıyor (pozisyon/açı/opaklık/blur).
        //
        // İKİNCİ AŞAMA: nesneleri önbelleklemek tek başına yetmiyordu, çünkü eski kod her karede
        // Children.Clear() yapıp hepsini geri ekliyordu - yani nesneler korunsa bile görsel ağaçtan
        // koparılıp yeniden bağlanıyorlardı. Artık sahne katmanı da kareler arasında korunuyor
        // (bkz. Utilities/RetainedScene.cs): kare başına düzen maliyeti ölçümde 1.4-1.8 kat düştü
        // (ayrıntılı sayılar RetainedScene sınıf açıklamasında). Z-sırası ve çizim mantığı birebir aynı.

        private sealed class CachedWheel
        {
            public Canvas Root;
            public RotateTransform Rotate;
            public BlurEffect Blur;
            public double Wr;
        }

        private sealed class CachedVehicleBody
        {
            public Canvas Container;
            public ScaleTransform Squash;
            public ScaleTransform Mirror;
            public RotateTransform Rotate;
            public readonly List<CachedWheel> Wheels = new();
            public string ImagePath;
            public double DisplayWidth, DisplayHeight;
        }

        // Ayrı bir isimle: dosya başında zaten "vehicle görsel dosya yolu -> BitmapImage" önbelleği
        // olan bir vehicleImageCache var (bkz. GetVehicleBitmap) - bunlar "araç örneği -> hazır
        // Canvas görsel ağacı" önbellekleri, farklı bir kavram.
        private readonly Dictionary<Vehicle, CachedVehicleBody> vehicleBodyCache = new();
        private readonly Dictionary<Vehicle, CachedVehicleBody> trailerBodyCache = new();

        /// <summary>Artık gameCore.AutoMotions içinde olmayan (silinen/yok edilen) araçların render
        /// önbelleğini temizler - aksi halde Canvas/Image nesneleri belleğe sızardı.</summary>
        private void PruneVehicleRenderCaches()
        {
            if (vehicleBodyCache.Count <= gameCore.AutoMotions.Count && trailerBodyCache.Count <= gameCore.AutoMotions.Count) return;

            var alive = new HashSet<Vehicle>(gameCore.AutoMotions);
            foreach (var key in vehicleBodyCache.Keys.Where(v => !alive.Contains(v)).ToList())
                vehicleBodyCache.Remove(key);
            foreach (var key in trailerBodyCache.Keys.Where(v => !alive.Contains(v)).ToList())
                trailerBodyCache.Remove(key);
        }

        /// <summary>
        /// Aracın/dorsenin KENDİ görselindeki tekerleği, tam bir daire içinde kesip yerinde döndüren
        /// kalıcı görsel ağacı (Container/Squash/Mirror/Rotate/Wheels) bir kez inşa eder.
        ///
        /// Neden böyle (kırpma yerine tam görsel + dairesel klip): daha önce iki yaklaşım da
        /// bozuktu. (a) Kaynak görselden kare bir bölge kırpıp (CroppedBitmap) daireye klipleme:
        /// kırpma boyutu lastik çapının 1.12 katıyken görüntü tam çap boyunda çizildiğinden lastik
        /// %11 küçülüyor, klip dairesiyle lastik arasında dönen bir "kaporta halkası" kalıyordu.
        /// (b) Sentetik 5 kollu jant: çizilmiş araç görselinin üstüne bindiğinden, WheelSpec yarıçapı
        /// birkaç piksel şaşınca tekerlek belirgin şekilde büyük/küçük görünüyordu. Bu yöntemde hiç
        /// kırpma yok: TAM görsel, tekerlek merkezi dairenin merkezine gelecek şekilde kaydırılıp
        /// aynı merkez etrafında döndürülüyor ve yarıçapı wr olan bir daireyle kırpılıyor.
        /// </summary>
        private CachedVehicleBody BuildVehicleBodyCache(BitmapImage bitmap, WheelSpec[] wheels, string imagePath, double displayWidth, double displayHeight)
        {
            var cache = new CachedVehicleBody { ImagePath = imagePath, DisplayWidth = displayWidth, DisplayHeight = displayHeight };

            var container = new Canvas { Width = displayWidth, Height = displayHeight, RenderTransformOrigin = new Point(0.5, 0.5) };
            var transformGroup = new TransformGroup();
            cache.Squash = new ScaleTransform(1, 1);
            cache.Mirror = new ScaleTransform(-1, 1);
            cache.Rotate = new RotateTransform(0);
            transformGroup.Children.Add(cache.Squash);
            transformGroup.Children.Add(cache.Mirror);
            transformGroup.Children.Add(cache.Rotate);
            container.RenderTransform = transformGroup;

            var image = new Image { Source = bitmap, Width = displayWidth, Height = displayHeight, Stretch = Stretch.Fill };
            container.Children.Add(image);

            foreach (var wheel in wheels)
            {
                double wx = wheel.CxFrac * displayWidth;
                double wy = wheel.CyFrac * displayHeight;
                double wr = Math.Max(3, wheel.RFrac * displayHeight);

                var rotate = new RotateTransform(0, wx, wy);
                var wheelImage = new Image
                {
                    Source = bitmap,
                    Width = displayWidth,
                    Height = displayHeight,
                    Stretch = Stretch.Fill,
                    // Görseli, tekerlek merkezi (wr, wr) noktasına denk gelecek şekilde kaydır
                    RenderTransformOrigin = new Point(0, 0),
                    RenderTransform = rotate
                };
                Canvas.SetLeft(wheelImage, wr - wx);
                Canvas.SetTop(wheelImage, wr - wy);

                var wheelRoot = new Canvas
                {
                    Width = wr * 2,
                    Height = wr * 2,
                    // Dairesel klip: yalnızca tekerleğin kendisi görünür, gövdenin geri kalanı kesilir
                    Clip = new EllipseGeometry(new Point(wr, wr), wr, wr)
                };
                wheelRoot.Children.Add(wheelImage);
                Canvas.SetLeft(wheelRoot, wx - wr);
                Canvas.SetTop(wheelRoot, wy - wr);
                container.Children.Add(wheelRoot);

                cache.Wheels.Add(new CachedWheel { Root = wheelRoot, Rotate = rotate, Wr = wr });
            }

            cache.Container = container;
            return cache;
        }

        /// <summary>Bir karede en fazla kaç tekerleğe hareket bulanıklığı uygulanabileceği.
        ///
        /// NEDEN SINIR VAR (ölçüldü): WPF'te her <c>Effect</c> o elemana ayrı bir render geçişi
        /// kurar. 5 araç × 4 tekerlek = kare başına 20 ayrı geçiş demekti ve profil, süreç CPU'sunun
        /// büyük kısmının burada yandığını gösterdi: aynı sahnede efektler kapatılınca CPU
        /// çekirdeğin %97.2'sinden %35.7'sine iniyordu. Bulanıklık görsel olarak yalnızca EN HIZLI
        /// birkaç araçta fark ediliyor; geri kalanında bedeli ödeyip karşılığını alamıyorduk.</summary>
        private const int MaxBlurredWheelsPerFrame = 6;

        /// <summary>Bu yarıçapın altındaki bulanıklık ekranda ayırt edilemiyor ama tam bir render
        /// geçişine mal oluyor - o yüzden hiç uygulanmıyor.</summary>
        private const double MinVisibleBlurRadius = 1.4;

        private int blurredWheelsThisFrame;
        private double frameMaxTravelSpeed;

        /// <summary>Hava durumunun açılıp kapanma hızı: 60 FPS'te kare başına bu kadar değişir,
        /// yani tam şiddete ~2 saniyede ulaşır. Ani başlayıp bitmesin ama beklemeye de dönüşmesin.</summary>
        private const double WeatherRampPerFrame = 1.0 / 120.0;

        /// <summary>Yağmur/kar yoğunluğu (0 = yok, 1 = tam şiddet). `isRaining`/`isSnowing`
        /// bayrakları hedefi belirler, bu değerler oraya rampayla yaklaşır.</summary>
        private double rainIntensity;
        private double snowIntensity;

        private void UpdateCachedWheel(CachedWheel cw, double wheelTravelDistance, double travelSpeed)
        {
            cw.Rotate.Angle = (wheelTravelDistance / Math.Max(1.0, cw.Wr)) * (180.0 / Math.PI);

            // Hızlanınca hafif hareket bulanıklığı - dönüşün gerçekten hızlandığını hissettirir
            double blurAmount = Math.Clamp((travelSpeed - 1.0) / 3.5, 0, 1.0);
            double radius = Math.Max(0.6, cw.Wr * 0.22) * blurAmount;

            bool amongFastest = frameMaxTravelSpeed <= 0.001 || travelSpeed >= frameMaxTravelSpeed * 0.6;

            bool wantBlur = !lowEffectsMode
                            && blurAmount > 0.01
                            && radius >= MinVisibleBlurRadius
                            && amongFastest
                            && blurredWheelsThisFrame < MaxBlurredWheelsPerFrame;

            if (wantBlur)
            {
                if (cw.Blur == null)
                {
                    cw.Blur = new BlurEffect { KernelType = KernelType.Box };
                    cw.Root.Effect = cw.Blur;
                }
                else if (cw.Root.Effect == null)
                {
                    cw.Root.Effect = cw.Blur;
                }

                cw.Blur.Radius = radius;
                blurredWheelsThisFrame++;
            }
            else if (cw.Root.Effect != null)
            {
                cw.Root.Effect = null;
            }
        }

        /// <summary>
        /// Gerçek marka görselini (Araç Listesi entegrasyonu) hareket yönüne göre aynalayarak,
        /// tekerlek merkezlerinde hıza bağlı dönen bir jant/tekerlek animasyonuyla birlikte çizer.
        /// Görsel ağacı araç yaşadığı sürece yeniden kullanılır (bkz. vehicleBodyCache üstü not).
        /// </summary>
        private void DrawVehicleImage(Vehicle vehicle, VehicleVisual visual, RotateTransform rotateTransform,
                                      double bodyOffsetY, double spawnScale, double spawnOpacity)
        {
            double displayWidth = vehicle.Width * 1.8;
            double displayHeight = displayWidth / visual.AspectRatio;
            var bitmap = GetVehicleBitmap(visual.ImagePath);

            if (!vehicleBodyCache.TryGetValue(vehicle, out var cache)
                || cache.ImagePath != visual.ImagePath
                || Math.Abs(cache.DisplayWidth - displayWidth) > 0.01
                || Math.Abs(cache.DisplayHeight - displayHeight) > 0.01
                || cache.Wheels.Count != visual.Wheels.Length)
            {
                cache = BuildVehicleBodyCache(bitmap, visual.Wheels, visual.ImagePath, displayWidth, displayHeight);
                vehicleBodyCache[vehicle] = cache;
            }

            // Negatif SquashAmount = ESNEME (ince-uzun): yaylanarak toparlanırken araç hedefi aşıp
            // kısa bir an gerilir. Formül işaretten bağımsız çalıştığı için ek bir dal gerekmiyor,
            // yalnızca eşik mutlak değere bakar. Genişleme katsayısı boyuna basılmadan büyük:
            // yumuşak bir top yere çarpınca enine belirgin yayılır, boyuna daha az kısalır.
            if (Math.Abs(vehicle.SquashAmount) > 0.005)
            {
                bool vert = vehicle.SquashAxisIsVertical > 0.5;
                cache.Squash.ScaleX = vert ? 1 + vehicle.SquashAmount * 0.9 : 1 - vehicle.SquashAmount * 0.6;
                cache.Squash.ScaleY = vert ? 1 - vehicle.SquashAmount * 0.6 : 1 + vehicle.SquashAmount * 0.9;
            }
            else
            {
                cache.Squash.ScaleX = 1;
                cache.Squash.ScaleY = 1;
            }

            // Doğuş animasyonu squash ölçeğiyle çarpılır (ikisi de aynı ScaleTransform üzerinden
            // uygulanır - ayrı bir transform eklemek görsel ağacı gereksiz büyütürdü).
            cache.Squash.ScaleX *= spawnScale;
            cache.Squash.ScaleY *= spawnScale;
            cache.Container.Opacity = spawnOpacity;
            // Gerçek marka görselleri (Araç Listesi) kaynak olarak SOLA bakar şekilde kırpılmış.
            // FacingLeft=true zaten sola gittiğini (görsel değişmeden doğru) belirtiyor; asıl
            // aynalama SAĞA giderken (FacingLeft=false) gerekiyor. MirrorScaleX (-1..1) anlık bir
            // sıçrama yerine her karede hedefe akıcı şekilde kayar (bkz. Vehicle.Update) - yön
            // değişince araç aniden "ışınlanıp" ters dönmek yerine kısa/akıcı bir geçişle çevrilir.
            cache.Mirror.ScaleX = vehicle.MirrorScaleX;
            cache.Rotate.Angle = rotateTransform.Angle;

            double travelSpeed = Math.Sqrt(vehicle.VelocityX * vehicle.VelocityX + vehicle.VelocityY * vehicle.VelocityY);
            foreach (var cw in cache.Wheels)
                UpdateCachedWheel(cw, vehicle.WheelTravelDistance, travelSpeed);

            // Alt kenar, araç fizik kutusunun alt kenarına (yerle/görev çubuğuyla temas noktası)
            // hizalanır - görsel oran kayıtlı küçük Height alanından farklı olsa bile araç
            // zeminin altına batmaz.
            Canvas.SetLeft(cache.Container, vehicle.X - displayWidth / 2);
            Canvas.SetTop(cache.Container, vehicle.Y + vehicle.Height / 2 - displayHeight + bodyOffsetY);
            scene.Place(cache.Container);
        }

        /// <summary>
        /// Araca takılı gerçek dorse/römork görselini (Ekipmanlar entegrasyonu) çizer; tekerlekleri
        /// çekici araçla aynı hızda dönen bulanık jant animasyonuna sahiptir. Görsel ağacı araç
        /// yaşadığı sürece yeniden kullanılır (bkz. vehicleBodyCache üstündeki not).
        /// </summary>
        private void DrawTrailerImage(Vehicle vehicle, VehicleVisual visual, double offsetX, bool flip, RotateTransform rotateTransform, double displayWidth, double displayHeight)
        {
            var bitmap = GetVehicleBitmap(visual.ImagePath);

            if (!trailerBodyCache.TryGetValue(vehicle, out var cache)
                || cache.ImagePath != visual.ImagePath
                || Math.Abs(cache.DisplayWidth - displayWidth) > 0.01
                || Math.Abs(cache.DisplayHeight - displayHeight) > 0.01
                || cache.Wheels.Count != visual.Wheels.Length)
            {
                cache = BuildVehicleBodyCache(bitmap, visual.Wheels, visual.ImagePath, displayWidth, displayHeight);
                trailerBodyCache[vehicle] = cache;
            }

            cache.Squash.ScaleX = 1;
            cache.Squash.ScaleY = 1; // dorsede sekme/çarpışma "squash" efekti yok
            cache.Mirror.ScaleX = flip ? -1 : 1;
            cache.Rotate.Angle = rotateTransform.Angle;

            double travelSpeed = Math.Sqrt(vehicle.VelocityX * vehicle.VelocityX + vehicle.VelocityY * vehicle.VelocityY);
            foreach (var cw in cache.Wheels)
                UpdateCachedWheel(cw, vehicle.WheelTravelDistance, travelSpeed);

            Canvas.SetLeft(cache.Container, vehicle.X + offsetX - displayWidth / 2);
            Canvas.SetTop(cache.Container, vehicle.Y + vehicle.Height / 2 - displayHeight);
            scene.Place(cache.Container);
        }

        private void DrawVehicle(Vehicle vehicle)
        {
            // Bozulan Araç Çekicisi: HP sıfırlanan araç anında silinmiyor, kısa bir "çekiliyor"
            // süresi boyunca solarak ve yanında bir çekici ikonuyla ekranda kalıyor.
            if (vehicle.TowCountdown >= 0)
            {
                double towOpacity = Math.Clamp(vehicle.TowCountdown / (double)Vehicle.TowDurationFrames, 0, 1);
                var wreckEmoji = scene.Rent<TextBlock>(ScenePool.WreckEmoji);
                wreckEmoji.Text = vehicle.Emoji;
                wreckEmoji.FontSize = 18;
                wreckEmoji.Opacity = towOpacity * 0.6;
                wreckEmoji.Width = vehicle.Width;
                wreckEmoji.Height = vehicle.Height;
                wreckEmoji.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(wreckEmoji, vehicle.X - vehicle.Width / 2);
                Canvas.SetTop(wreckEmoji, vehicle.Y - vehicle.Height / 2);

                var towTruck = scene.Rent<TextBlock>(ScenePool.TowTruck);
                towTruck.Text = "🚚";
                towTruck.FontSize = 20;
                towTruck.Opacity = 0.9;
                Canvas.SetLeft(towTruck, vehicle.X + vehicle.Width / 2 + 8);
                Canvas.SetTop(towTruck, vehicle.Y - 12);
                return;
            }

            // Dönüş (Rotation) transformasyonu (sürükleme ve tıklama tepkileri için).
            // Araç bir ekran kenarına tırmanmışsa (EdgeSide), o kenar "zeminmiş" gibi
            // görünmesi için gövde ±90° ek olarak döndürülür.
            // Kenar açısı (0/90/-90/180) artık ANINDA sıçramıyor - Vehicle.Update() içinde her karede
            // hedefe doğru yumuşakça kayan WallAngleSmoothed kullanılıyor (köşe dönüşleri akıcı).
            var rotateTransform = new RotateTransform(vehicle.Rotation + vehicle.DriveTilt + vehicle.WallAngleSmoothed);

            // Görsel canlılık ofseti: rölanti titreşimi (motor çalışırken duran aracın küçük
            // titremesi) + süspansiyon çökmesi (inişte yaylanma). İkisi de yalnızca GÖVDEYİ ve ona
            // bağlı ışıkları kaydırır; fizik konumu (vehicle.X/Y) hiç değişmez - gölge, plaka ve
            // rozet gibi "zemine/dünyaya ait" öğeler yerinde kalır, böylece hareket gerçekten
            // aracın kendi süspansiyonundan geliyormuş gibi okunur.
            double bodyOffsetY = vehicle.IdleShakeOffset + vehicle.SuspensionOffset;

            // Doğuş (spawn) animasyonu: araç aniden belirmek yerine hafifçe hedefi aşan bir
            // yaylanmayla (OutBack) ölçeklenip görünür hale gelir.
            double spawnT = Easing.OutBack(vehicle.SpawnProgress);
            double spawnScale = 0.35 + 0.65 * spawnT;
            double spawnOpacity = Easing.Clamp01(vehicle.SpawnProgress * 2.2);

            // 1. Neon Alt Işık / Gölge (Glow) - kullanıcı isteği: artık varsayılan olarak KAPALI,
            // sadece "lights" dekorasyonu (💡 Mağaza) satın alınınca görünür oluyor.
            bool hasLightsDecoration = vehicle.Decorations.Contains("lights");
            if (hasLightsDecoration)
            {
                var glowSize = vehicle.Width * 1.4;
                var underGlow = scene.Rent<Ellipse>(ScenePool.UnderGlow);
                underGlow.Width = glowSize;
                underGlow.Height = 14;
                underGlow.Fill = GetNeonGlowBrush(vehicle.NeonColor, hasLightsDecoration);
                underGlow.Opacity = vehicle.IsDragged ? 0.95 : 0.85;
                underGlow.RenderTransformOrigin = new Point(0.5, 0.5);
                underGlow.RenderTransform = rotateTransform;
                Canvas.SetLeft(underGlow, vehicle.X - glowSize / 2);
                Canvas.SetTop(underGlow, vehicle.Y + vehicle.Height / 2 - 5);
            }

            // 1.5 Gece Farları (Headlights)
            bool isNight = IsNightActive();
            if (isNight && vehicle.Type != "bike")
            {
                var headlightCone = scene.Rent<Polygon>(ScenePool.HeadlightCone);
                headlightCone.Points = new PointCollection
                {
                    new Point(vehicle.Width / 2, 0),
                    new Point(vehicle.Width / 2 + 95, -28),
                    new Point(vehicle.Width / 2 + 95, 28)
                };
                headlightCone.Fill = HeadlightConeBrush;
                // Farlar da karanlıkla birlikte AÇILIR: eskiden 20:00'de tam parlaklıkla bir anda
                // yanıyordu, oysa gece katmanı 19:00'dan itibaren yumuşakça iniyordu.
                headlightCone.Opacity = 0.85 * Math.Clamp(GetNightFactor(), 0, 1);
                headlightCone.RenderTransformOrigin = new Point(0, 0);
                headlightCone.RenderTransform = rotateTransform;
                Canvas.SetLeft(headlightCone, vehicle.X);
                Canvas.SetTop(headlightCone, vehicle.Y);
            }

            // 1.55 Araç Lambaları (fren / geri vites / dönüş sinyali)
            //
            // Farlarla aynı yaklaşım: (0,0) kökenli bir kapsayıcı Canvas, vehicle.X/Y üzerine
            // yerleştirilip aynı rotateTransform ile döndürülüyor - böylece araç dönerken/aynalanırken
            // ışık her zaman doğru konumda kalıyor.
            //
            // rearSign: aracın ARKASININ yerel X ekseninde hangi yönde olduğu. FacingLeft=true ise
            // burun solda, arka sağda (+1). Eskiden fren lambası her durumda -Width/2'ye (sola)
            // çiziliyordu; araç sağa bakarken lamba burnunda yanıyordu.
            double rearSign = vehicle.FacingLeft ? 1.0 : -1.0;
            bool showBrake = vehicle.IsBraking;
            bool showReverse = vehicle.IsReversing;
            bool showSignal = vehicle.SignalLampOn && vehicle.SignalDirection != 0;

            if (showBrake || showReverse || showSignal)
            {
                // Lamba kökü ve içindeki ışıklar da kareler arasında korunur; hangi lambanın yandığı
                // değiştiğinde yalnızca fazlalık kırpılır (bkz. LampAt/TrimChildren).
                var lampRoot = scene.Rent<Canvas>(ScenePool.SignalLamp);
                lampRoot.RenderTransformOrigin = new Point(0, 0);
                lampRoot.RenderTransform = rotateTransform;
                int lampCount = 0;

                if (showBrake)
                {
                    var brakeLight = LampAt(lampRoot, lampCount++);
                    brakeLight.Width = 10; brakeLight.Height = 7;
                    brakeLight.Fill = BrakeLightBrush; brakeLight.Opacity = 0.95;
                    Canvas.SetLeft(brakeLight, rearSign * (vehicle.Width / 2 + 3) - 5);
                    Canvas.SetTop(brakeLight, -3.5);
                }

                // Geri vites lambası: gerçek araçlardaki gibi beyaz ve arkada.
                if (showReverse)
                {
                    var reverseLight = LampAt(lampRoot, lampCount++);
                    reverseLight.Width = 8; reverseLight.Height = 6;
                    reverseLight.Fill = ReverseLightBrush; reverseLight.Opacity = 0.9;
                    Canvas.SetLeft(reverseLight, rearSign * (vehicle.Width / 2 + 3) - 4);
                    Canvas.SetTop(reverseLight, 2.5);
                }

                // Dönüş sinyali: yandan bakışta hem ön hem arka flaşör görünür - ikisi birlikte
                // yanıp sönünce manevra niyeti net okunur.
                if (showSignal)
                {
                    foreach (double endSign in new[] { rearSign, -rearSign })
                    {
                        var signalLamp = LampAt(lampRoot, lampCount++);
                        signalLamp.Width = 7; signalLamp.Height = 6;
                        signalLamp.Fill = SignalLightBrush; signalLamp.Opacity = 0.95;
                        Canvas.SetLeft(signalLamp, endSign * (vehicle.Width / 2 + 3) - 3.5);
                        Canvas.SetTop(signalLamp, -vehicle.Height * 0.22);
                    }
                }

                TrimChildren(lampRoot, lampCount);
                Canvas.SetLeft(lampRoot, vehicle.X);
                Canvas.SetTop(lampRoot, vehicle.Y + bodyOffsetY);
            }

            // 1.6 Neon Yansımalar (Raycast Glow): LED dekorasyonlu araç geceleyin bir duvara
            // tırmanırken, yakınındaki duvara/zemine hafif renkli bir ışık hüzmesi yansıtır.
            if (!lowEffectsMode && isNight && vehicle.EdgeSide != 0 && vehicle.Decorations.Contains("lights"))
            {
                var reflectColor = (Color)ColorConverter.ConvertFromString(vehicle.NeonColor);
                var reflection = scene.Rent<Ellipse>(ScenePool.Reflection);
                reflection.Width = 60;
                reflection.Height = 18;
                reflection.Fill = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(90, reflectColor.R, reflectColor.G, reflectColor.B), 0),
                        new GradientStop(Color.FromArgb(0, reflectColor.R, reflectColor.G, reflectColor.B), 1)
                    }
                };
                reflection.RenderTransform = new RotateTransform(vehicle.EdgeSide == -1 ? 90 : -90);
                double wallX = vehicle.EdgeSide == -1 ? 0 : RenderCanvas.ActualWidth;
                Canvas.SetLeft(reflection, wallX - 30 + (vehicle.EdgeSide == -1 ? 12 : -12));
                Canvas.SetTop(reflection, vehicle.Y - 9);
            }

            // 1.7 Silecekler (Wipers): yağmur yağarken ön camda ileri-geri sallanan basit bir çizgi
            if (!lowEffectsMode && isRaining)
            {
                double wiperAngle = Math.Sin(gameCore.GameTime * 0.25) * 35;
                var wiper = scene.Rent<Line>(ScenePool.Wiper);
                wiper.X1 = 0; wiper.Y1 = 0;
                wiper.X2 = 0; wiper.Y2 = -Math.Min(14, vehicle.Height * 0.4);
                wiper.Stroke = Brushes.Black;
                wiper.StrokeThickness = 1.4;
                wiper.RenderTransformOrigin = new Point(0.5, 1);

                // Açı her karede değiştiği için transform nesnesi de yeniden kullanılır.
                if (wiper.RenderTransform is RotateTransform wiperRotate) wiperRotate.Angle = wiperAngle;
                else wiper.RenderTransform = new RotateTransform(wiperAngle);

                Canvas.SetLeft(wiper, vehicle.X);
                Canvas.SetTop(wiper, vehicle.Y - vehicle.Height * 0.15);
            }

            // 1.8 Zemin Teması Gölgesi (Contact Shadow): araç zeminden ne kadar yükseklerse (zıplama/
            // uçuş modu), gölge o kadar küçülüp saydamlaşır - ekstra bir fizik hesabı olmadan sadece
            // görsel olarak "havada olma" hissini güçlendirir. Yalnızca normal zemin modunda (EdgeSide
            // == 0) çizilir - duvar/tavan tırmanmasında "zemin" kavramı farklı bir eksende olduğundan
            // basit bir alt-gölge o durumlarda anlamsız/yanlış konumlanırdı.
            if (!lowEffectsMode && vehicle.EdgeSide == 0)
            {
                double floorY = multiMonitorMode && monitorFloors.Count > 1 ? FloorYForX(vehicle.X) : RenderCanvas.ActualHeight;
                double heightAboveGround = Math.Max(0, floorY - (vehicle.Y + vehicle.Height / 2.0));
                double shadowScale = Math.Clamp(1.0 - heightAboveGround / 220.0, 0.2, 1.0);
                double shadowOpacity = 0.32 * shadowScale;

                var shadow = scene.Rent<Ellipse>(ScenePool.Shadow);
                shadow.Width = Math.Max(6, vehicle.Width * 0.85 * shadowScale);
                shadow.Height = Math.Max(4, vehicle.Height * 0.22 * shadowScale);
                SetFill(shadow, Color.FromArgb((byte)(shadowOpacity * 255), 0, 0, 0));
                Canvas.SetLeft(shadow, vehicle.X - shadow.Width / 2);
                Canvas.SetTop(shadow, floorY - shadow.Height / 2 - 2);
            }

            // 2. Araç Gövdesi: Gerçek marka görseli varsa onu, yoksa eski gradyan dikdörtgen+emoji çizimini kullan
            // Not: "paint" dekorasyonunun eskiden çizdiği parlak sarı kontur, kullanıcı isteğiyle
            // kaldırıldı - araç etrafında artık hiçbir dekorasyon sarı bir dikdörtgen çizmiyor.
            bool hasPaintDecoration = vehicle.Decorations.Contains("paint");
            var visual = GetVisualForVehicle(vehicle);

            if (visual != null && GetVehicleBitmap(visual.ImagePath) != null)
            {
                DrawVehicleImage(vehicle, visual, rotateTransform, bodyOffsetY, spawnScale, spawnOpacity);
            }
            else
            {
                var baseColor = (Color)ColorConverter.ConvertFromString(vehicle.Color);
                var bodyBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(baseColor, 0),
                        new GradientStop(Color.FromRgb((byte)(baseColor.R * 0.55), (byte)(baseColor.G * 0.55), (byte)(baseColor.B * 0.55)), 1)
                    }
                };

                // Doğuş animasyonu bu çizim yolunda gövde ve emoji için ortak bir dönüşüm grubuyla
                // uygulanır (döndürme + doğuş ölçeği aynı merkezden).
                var bodyTransform = new TransformGroup();
                bodyTransform.Children.Add(new ScaleTransform(spawnScale, spawnScale));
                bodyTransform.Children.Add(rotateTransform);

                var rect = scene.Rent<Rectangle>(ScenePool.VehicleRect);
                rect.Width = vehicle.Width;
                rect.Height = vehicle.Height;
                rect.Fill = bodyBrush;
                SetStroke(rect, Colors.Black);
                rect.StrokeThickness = 1.8;
                rect.RadiusX = 4;
                rect.RadiusY = 4;
                rect.Opacity = spawnOpacity;
                rect.RenderTransformOrigin = new Point(0.5, 0.5);
                rect.RenderTransform = bodyTransform;

                Canvas.SetLeft(rect, vehicle.X - vehicle.Width / 2);
                Canvas.SetTop(rect, vehicle.Y - vehicle.Height / 2 + bodyOffsetY);

                // 3. Emoji
                var emoji = scene.Rent<TextBlock>(ScenePool.VehicleEmoji);
                emoji.Text = vehicle.Emoji;
                emoji.FontSize = 18;
                emoji.Foreground = Brushes.Black;
                emoji.TextAlignment = TextAlignment.Center;
                emoji.Width = vehicle.Width;
                emoji.Height = vehicle.Height;
                emoji.VerticalAlignment = VerticalAlignment.Center;
                emoji.HorizontalAlignment = HorizontalAlignment.Center;
                emoji.Opacity = spawnOpacity;
                emoji.RenderTransformOrigin = new Point(0.5, 0.5);
                emoji.RenderTransform = bodyTransform;

                Canvas.SetLeft(emoji, vehicle.X - vehicle.Width / 2);
                Canvas.SetTop(emoji, vehicle.Y - vehicle.Height / 2 + bodyOffsetY);
            }

            // 2.5 "spoiler" dekorasyon rozeti ("sticker" parıltı ikonu kullanıcı isteğiyle kaldırıldı)
            if (vehicle.Decorations.Contains("spoiler"))
            {
                var spoiler = scene.Rent<Rectangle>(ScenePool.Spoiler);
                spoiler.Width = vehicle.Width * 0.5;
                spoiler.Height = 4;
                spoiler.Fill = Brushes.Black;
                spoiler.RenderTransformOrigin = new Point(0.5, 0.5);
                spoiler.RenderTransform = rotateTransform;
                Canvas.SetLeft(spoiler, vehicle.X - vehicle.Width * 0.25);
                Canvas.SetTop(spoiler, vehicle.Y - vehicle.Height / 2 - 6);
            }

            // 3.5 Dorse / Ekipman (Trailer/Equipment Attachment) - gerçek görsel varsa onu kullan
            if (!string.IsNullOrEmpty(vehicle.TrailerType) && Vehicle.TrailerCatalog.TryGetValue(vehicle.TrailerType, out var trailerInfo))
            {
                double dirSign = Math.Cos(vehicle.Angle) >= 0 ? -1 : 1; // aracın arkasında kalacak yön
                var trailerVisual = TrailerVisuals.Get(vehicle.TrailerType);
                var towingVisual = GetVisualForVehicle(vehicle);

                if (trailerVisual != null && GetVehicleBitmap(trailerVisual.ImagePath) != null)
                {
                    // Çekicinin GERÇEK render boyutu (gerçek marka görselli araçlar 1.8x büyütülerek
                    // çiziliyor - bkz. DrawVehicleImage; bunu hesaba katmadan eski kod dorseyi çekicinin
                    // yarısına kadar içine gömüyordu). Dorsenin yüksekliği çekicinin GERÇEK gövde
                    // yüksekliğiyle eşleştirilir (gerçek bir tır/traktör+dorse ikilisinde ikisi de aynı
                    // şasi/zemin hizasında durur); genişlik ise dorsenin KENDİ gerçek en/boy oranından
                    // türetilir - böylece dorse görseli esnetilmeden/sıkıştırılmadan doğru oranda ve
                    // çekiciyle orantılı büyüklükte görünür (tır için daha büyük, traktör için daha küçük).
                    double towingRealHalfWidth;
                    double towingRealHeight;
                    if (towingVisual != null)
                    {
                        double towingDisplayWidth = vehicle.Width * 1.8;
                        towingRealHalfWidth = towingDisplayWidth / 2;
                        towingRealHeight = towingDisplayWidth / towingVisual.AspectRatio;
                    }
                    else
                    {
                        towingRealHalfWidth = vehicle.Width / 2;
                        towingRealHeight = vehicle.Height;
                    }

                    // Dorse uzunluğu da araçlarla AYNI gerçek-dünya ölçeğine oturur: 13.6 m'lik bir
                    // açık dorse ile 7 m'lik bir kasalı römork artık aynı boyda çizilmiyor. Ekrandaki
                    // metre karşılığı, çekicinin çizim ölçeğiyle birebir aynı tutulur (araç görselleri
                    // Width'in 1.8 katıyla çiziliyor, Width de Scale'i içeriyor) - böylece dorse
                    // çekiciyle tutarlı orantıda kalır. Yükseklik görselin kendi en/boy oranından
                    // türetilir, yani görsel esnetilmez.
                    double onScreenPxPerMetre = Vehicle.PixelsPerMetre * vehicle.Scale * 1.8;
                    double trailerDisplayWidth, trailerDisplayHeight;

                    if (Vehicle.TrailerRealLengthMetres.TryGetValue(vehicle.TrailerType, out double trailerMetres)
                        && trailerVisual.AspectRatio > 0)
                    {
                        trailerDisplayWidth = trailerMetres * onScreenPxPerMetre;
                        trailerDisplayHeight = trailerDisplayWidth / trailerVisual.AspectRatio;
                    }
                    else
                    {
                        // Tabloda yoksa eski davranış: çekicinin gövde yüksekliğine oranla.
                        trailerDisplayHeight = towingRealHeight * 0.95;
                        trailerDisplayWidth = trailerDisplayHeight * trailerVisual.AspectRatio;
                    }

                    // Çeki oku çekicinin arkasına hafif girecek şekilde küçük bir örtüşme payı (-3) -
                    // gerçek bir tırın beşinci teker/çeki demiri bağlantısında olduğu gibi tam yapışık,
                    // aralıksız bir görünüm (önceki kodda boşluk yerine ciddi bir iç içe geçme vardı).
                    double offsetX = dirSign * (towingRealHalfWidth + trailerDisplayWidth / 2 - 3);

                    // Görsellerde çeki oku sol tarafta: araç sağa bakıyorsa (dirSign<0, dorse solda kalır)
                    // çeki okunun araca dönük olması için görsel aynalanır.
                    bool flip = trailerVisual.HitchOnRight ? dirSign >= 0 : dirSign < 0;
                    DrawTrailerImage(vehicle, trailerVisual, offsetX, flip, rotateTransform, trailerDisplayWidth, trailerDisplayHeight);
                }
                else
                {
                    double trailerWidth = vehicle.Width * 0.9;
                    double trailerHeight = vehicle.Height * 0.75;
                    double offsetX = dirSign * (vehicle.Width / 2 + trailerWidth / 2 + 3);
                    var trailerRect = scene.Rent<Rectangle>(ScenePool.TrailerRect);
                    trailerRect.Width = trailerWidth;
                    trailerRect.Height = trailerHeight;
                    SetFill(trailerRect, Color.FromRgb(0xfb, 0xca, 0x1f));
                    trailerRect.Stroke = Brushes.Black;
                    trailerRect.StrokeThickness = 2;
                    trailerRect.RadiusX = 2;
                    trailerRect.RadiusY = 2;
                    trailerRect.RenderTransformOrigin = new Point(0.5, 0.5);
                    trailerRect.RenderTransform = rotateTransform;
                    Canvas.SetLeft(trailerRect, vehicle.X + offsetX - trailerWidth / 2);
                    Canvas.SetTop(trailerRect, vehicle.Y - trailerHeight / 2);

                    var trailerEmoji = scene.Rent<TextBlock>(ScenePool.TrailerEmoji);
                    trailerEmoji.Text = trailerInfo.emoji;
                    trailerEmoji.FontSize = 14;
                    trailerEmoji.Width = trailerWidth;
                    trailerEmoji.Height = trailerHeight;
                    trailerEmoji.TextAlignment = TextAlignment.Center;
                    trailerEmoji.VerticalAlignment = VerticalAlignment.Center;
                    trailerEmoji.HorizontalAlignment = HorizontalAlignment.Center;
                    trailerEmoji.RenderTransformOrigin = new Point(0.5, 0.5);
                    trailerEmoji.RenderTransform = rotateTransform;
                    Canvas.SetLeft(trailerEmoji, vehicle.X + offsetX - trailerWidth / 2);
                    Canvas.SetTop(trailerEmoji, vehicle.Y - trailerHeight / 2);
                }
            }

            // 3.6 Detaylı Yıkama: kirlilik arttıkça araç üzerinde belirginleşen çamur lekeleri
            if (dirtEffectEnabled && vehicle.Dirtiness > 0.15)
            {
                double dirtOpacity = Math.Min(0.55, vehicle.Dirtiness * 0.55);
                var mudColor = Color.FromArgb((byte)(dirtOpacity * 255), 90, 65, 40);
                (double ox, double oy, double size)[] spots = { (-0.28, 0.22, 0.22), (0.15, -0.18, 0.16), (0.32, 0.25, 0.18), (-0.1, -0.05, 0.13) };
                foreach (var (ox, oy, size) in spots)
                {
                    var mud = scene.Rent<Ellipse>(ScenePool.MudSplatter);
                    mud.Width = vehicle.Width * size;
                    mud.Height = vehicle.Height * size;
                    SetFill(mud, mudColor);
                    Canvas.SetLeft(mud, vehicle.X + vehicle.Width * ox - mud.Width / 2);
                    Canvas.SetTop(mud, vehicle.Y + vehicle.Height * oy - mud.Height / 2);
                }
            }

            // 3.7 Kaporta Hasar İzleri: can azaldıkça gövdede koyu çizikler belirir. Konumlar araç
            // ömrü boyunca SABİT (kimliğinden türetiliyor) - her karede rastgele seçilseydi izler
            // titrer, hasar yerine parazit gibi görünürdü. Gövdeyle birlikte döner.
            int damageMarks = VehicleDamage.MarkCount(vehicle.MaxHP > 0 ? vehicle.HP / (double)vehicle.MaxHP : 1.0);
            if (!lowEffectsMode && damageMarks > 0)
            {
                int seed = vehicle.GetHashCode();
                for (int i = 0; i < damageMarks; i++)
                {
                    // Deterministik dağılım: aynı araçta aynı iz hep aynı yerde.
                    double fx = ((seed >> (i * 3)) & 7) / 7.0 - 0.5;   // -0.5 .. 0.5
                    double fy = ((seed >> (i * 3 + 9)) & 7) / 7.0 - 0.5;
                    double len = vehicle.Width * (0.16 + (i % 3) * 0.05);
                    double angle = (((seed >> (i * 5)) & 3) - 1.5) * 0.7; // hafif eğik

                    var scratch = scene.Rent<Line>(ScenePool.DamageMark);
                    double cx = vehicle.X + vehicle.Width * fx * 0.6;
                    double cy = vehicle.Y + vehicle.Height * fy * 0.5;
                    scratch.X1 = cx - Math.Cos(angle) * len / 2;
                    scratch.Y1 = cy - Math.Sin(angle) * len / 2;
                    scratch.X2 = cx + Math.Cos(angle) * len / 2;
                    scratch.Y2 = cy + Math.Sin(angle) * len / 2;
                    scratch.StrokeThickness = 1.6;
                    scratch.StrokeStartLineCap = PenLineCap.Round;
                    scratch.StrokeEndLineCap = PenLineCap.Round;
                    scratch.RenderTransformOrigin = new Point(0.5, 0.5);
                    scratch.RenderTransform = rotateTransform;
                    SetStroke(scratch, Color.FromArgb(150, 25, 20, 18));
                }
            }

            // 4. HP Barı - varsayılan olarak gizli, sadece fare aracın üzerine gelince görünür
            // (kullanıcı isteği: sürekli görünen can barı gereksiz görsel gürültü yaratıyordu).
            bool isHoveredForHp = vehicle.HP < vehicle.MaxHP && vehicle.IsPointInside(lastMousePos.X, lastMousePos.Y);
            if (isHoveredForHp)
            {
                var hpBarWidth = vehicle.Width * (double)vehicle.HP / vehicle.MaxHP;
                var hpBar = scene.Rent<Rectangle>(ScenePool.HpBar);
                hpBar.Width = hpBarWidth;
                hpBar.Height = 3.5;
                SetFill(hpBar, vehicle.HP > vehicle.MaxHP * 0.5 ? Colors.LimeGreen : Colors.Red);

                Canvas.SetLeft(hpBar, vehicle.X - vehicle.Width / 2);
                Canvas.SetTop(hpBar, vehicle.Y - vehicle.Height / 2 - 8);
            }

            // Uçuş / Kilit / Sürüş rozetleri (seçim çerçevesi kaldırıldığı için artık her zaman gösterilir)
            if (vehicle.IsFlying || vehicle.IsLocked || vehicle.IsDriven)
            {
                var badge = scene.Rent<TextBlock>(ScenePool.Badge);
                badge.Text = vehicle.IsDriven ? "🎮" : (vehicle.IsFlying ? "🛫" : "🔒");
                badge.FontSize = 14;
                Canvas.SetLeft(badge, vehicle.X + vehicle.Width / 2 + 3);
                Canvas.SetTop(badge, vehicle.Y - vehicle.Height / 2 - 12);
            }

            // Rota çizgilerini çiz
            if (vehicle == selectedVehicle && vehicle.CustomPath.Count > 0)
            {
                var routeLine = scene.Rent<Polyline>(ScenePool.RouteLine);
                SetStroke(routeLine, Color.FromArgb(160, 255, 0, 127));
                routeLine.StrokeThickness = 2.0;
                routeLine.StrokeDashArray ??= new DoubleCollection { 5, 4 };

                routeLine.Points.Clear();
                routeLine.Points.Add(new Point(vehicle.X, vehicle.Y));
                foreach (var pt in vehicle.CustomPath)
                {
                    routeLine.Points.Add(pt);
                }

                var targetPt = vehicle.CustomPath.Last();
                var flag = scene.Rent<TextBlock>(ScenePool.RouteFlag);
                flag.Text = "📍";
                flag.FontSize = 18;
                flag.Margin = new Thickness(-9, -18, 0, 0);
                Canvas.SetLeft(flag, targetPt.X);
                Canvas.SetTop(flag, targetPt.Y);
            }

            // 5.15 Duygu Durumu Görsel Geri Bildirimi (Emotion Feedback) - her araçta sürekli
            // görünmesi dağınık/rahatsız edici olduğu için artık sadece fare aracın üzerindeyken gösteriliyor.
            {
                double hoverDx = lastMousePos.X - vehicle.X;
                double hoverDy = lastMousePos.Y - vehicle.Y;
                bool isMouseOverVehicle = Math.Sqrt(hoverDx * hoverDx + hoverDy * hoverDy) < Math.Max(vehicle.Width, vehicle.Height) * 0.9;

                // Rozet iki durumda görünür: (a) fare aracın üzerindeyken (eskiden tek yol buydu),
                // (b) durum YENİ DEĞİŞTİYSE kısa bir süre - böylece araç yakıtsız kaldığı ya da
                // ağır hasar aldığı AN fark edilir, ama rozet kalıcı gürültüye dönüşmez (3.4 / A-6).
                string moodEmoji = Mood.Emoji(vehicle.CurrentMood);
                bool showFromPulse = vehicle.MoodPulse > 0.01;

                if (moodEmoji != null && (isMouseOverVehicle || showFromPulse))
                {
                    var moodTag = scene.Rent<TextBlock>(ScenePool.MoodTag);
                    moodTag.Text = moodEmoji;
                    moodTag.FontSize = 13;
                    // Fareyle bakılırken tam görünür; kendiliğinden çıkan rozet solarak kaybolur.
                    moodTag.Opacity = isMouseOverVehicle ? 1.0 : Math.Clamp(vehicle.MoodPulse * 1.5, 0, 1);
                    Canvas.SetLeft(moodTag, vehicle.X + vehicle.Width / 2 - 4);
                    Canvas.SetTop(moodTag, vehicle.Y - vehicle.Height / 2 - 14);
                }
            }

            // 5.2 Kişiselleştirilmiş Plaka - yalnızca kullanıcı özel bir isim verdiyse gösterilir
            // (varsayılan "CAR"/"TRUCK" gibi tip etiketleri araçların altında görünmesin).
            // Düz metin yerine gerçek bir araç plakası gibi (beyaz zemin, siyah çerçeve, kısa metin) çizilir.
            if (!string.IsNullOrEmpty(vehicle.Name))
            {
                string plateText = vehicle.Name.Length > 10 ? vehicle.Name.Substring(0, 10).ToUpper() : vehicle.Name.ToUpper();
                var plate = scene.Rent<Border>(ScenePool.Plate);
                plate.Background = Brushes.White;
                plate.BorderBrush = Brushes.Black;
                plate.BorderThickness = new Thickness(1.5);
                plate.CornerRadius = new CornerRadius(2);
                plate.Padding = new Thickness(4, 1, 4, 1);

                if (plate.Child is not TextBlock plateTxt)
                {
                    plateTxt = new TextBlock
                    {
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black,
                        FontFamily = new FontFamily("Consolas, Oswald")
                    };
                    plate.Child = plateTxt;
                }
                plateTxt.Text = plateText;

                plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(plate, vehicle.X - plate.DesiredSize.Width / 2);
                Canvas.SetTop(plate, vehicle.Y + vehicle.Height / 2 + 4);
            }

            // 5.3 Taşıdığı Kargo Görseli
            if (vehicle.HasCargo)
            {
                var cargoBox = scene.Rent<TextBlock>(ScenePool.CargoBox);
                cargoBox.Text = "📦";
                cargoBox.FontSize = 14;
                cargoBox.RenderTransformOrigin = new Point(0.5, 0.5);
                cargoBox.RenderTransform = rotateTransform;
                Canvas.SetLeft(cargoBox, vehicle.X - 7);
                Canvas.SetTop(cargoBox, vehicle.Y - vehicle.Height / 2 - 20);
            }
        }

        private void DrawUIOverlay()
        {
            // İstatistikler (Sol Üst) - ZIndex high
            // Button handler'larda güncelleniyor
        }
    }
}
