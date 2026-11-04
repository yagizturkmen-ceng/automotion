using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AutoMotion.Views
{
    /// <summary>
    /// Açılış ekranı: AutoMotion logosu, sonsuzluk sembolünün üstüne soldan sağa akan altın bir
    /// "dolum" (fill) animasyonuyla gösterilir. logo_infinity_fill.png, logo.png'deki sonsuzluk
    /// sembolünün konumunu birebir eşleyen (renk seçimiyle çıkarılmış), sadece o şekli içeren ayrı
    /// bir katmandır - Image.Clip üzerinde animasyonla soldan sağa açığa çıkarılır.
    /// </summary>
    public partial class SplashWindow : Window
    {
        /// <summary>Dolum animasyonu (ve ardından kısa bir parıltı) tamamlandığında tetiklenir -
        /// App.xaml.cs bunu dinleyip asıl MainWindow'u bu noktada oluşturup gösterir.</summary>
        public event EventHandler LoadingCompleted;

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => StartFillAnimation();
        }

        private void StartFillAnimation()
        {
            // Bitiş genişliği 320 yerine bilerek 340 (küçük bir pay) - animasyonun tam son karesinde
            // bir yuvarlama/zamanlama sapması yüzünden en sağ kenarın 1-2 piksel kırpılı kalması
            // ihtimaline karşı güvenli bir marj. Grid zaten 320x320 ile sınırlı olduğundan görünen
            // alanın dışına taşma riski yok.
            var fillAnim = new RectAnimation(
                new Rect(0, 0, 0, 320),
                new Rect(0, 0, 340, 320),
                new Duration(TimeSpan.FromSeconds(1.4)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            fillAnim.Completed += (s, e) =>
            {
                // Animasyonun son değerini elle de sabitle - BeginAnimation'ın tuttuğu animasyon
                // değeri ile bağımsız olarak, Clip'in kesin olarak tam açık kalmasını garanti eder.
                FillClip.Rect = new Rect(0, 0, 340, 320);
                PlayCompletionGlow();
            };

            FillClip.BeginAnimation(RectangleGeometry.RectProperty, fillAnim);
        }

        /// <summary>Dolum tamamlanınca kısa bir altın parıltı (glow) darbesi - "hazır" hissi veren
        /// küçük bir vurgu, sonunda LoadingCompleted'ı tetikler.</summary>
        private void PlayCompletionGlow()
        {
            StatusText.Text = AutoMotion.Services.Loc.T("ui.splash.ready");

            var opacityPulse = new DoubleAnimationUsingKeyFrames();
            opacityPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacityPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            opacityPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));
            opacityPulse.Completed += (s2, e2) => LoadingCompleted?.Invoke(this, EventArgs.Empty);

            var blurPulse = new DoubleAnimationUsingKeyFrames();
            blurPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blurPulse.KeyFrames.Add(new EasingDoubleKeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            blurPulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));

            FillGlow.BeginAnimation(DropShadowEffect.OpacityProperty, opacityPulse);
            FillGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurPulse);
        }
    }
}
