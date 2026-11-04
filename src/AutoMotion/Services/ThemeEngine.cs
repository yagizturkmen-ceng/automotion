using System;
using System.Windows;
using System.Windows.Media;

namespace AutoMotion.Services
{
    // Tema Sistemi - Varsayılan (0) TAMAMEN ŞEFFAF, masaüstü pet gibi görünür
    public class ThemeEngine
    {
        private int currentTheme = 0;
        // Tema galerisi genişletildi (8 → 11): Yağmurlu/Fırtınalı, Kış ve Sentetik Gece (Synthwave)
        // eklendi - REQUIREMENTS.md'de "tema galerisi sınırlı" olarak not edilmiş, önceki oturumlarda
        // planlanıp ertelenmiş bir madde (D-09).
        public int ThemeCount => 11;
        private static bool IsNightNow => DateTime.Now.Hour >= 20 || DateTime.Now.Hour < 6;

        public string ThemeName => currentTheme switch
        {
            0 => "Şeffaf (Masaüstü)",
            1 => "Açık",
            2 => "Koyu",
            3 => "Gün Batımı",
            4 => "Okyanus",
            5 => "Cam (Glassmorphism)",
            6 => "Tam Şeffaf (Çizgisiz)",
            7 => IsNightNow ? "Otomatik (şu an: Gece)" : "Otomatik (şu an: Gündüz)",
            8 => "Yağmurlu / Fırtınalı",
            9 => "Kış",
            10 => "Sentetik Gece (Synthwave)",
            _ => "Şeffaf"
        };

        public Brush GetBackgroundBrush()
        {
            return currentTheme switch
            {
                0 => Brushes.Transparent,
                1 => new SolidColorBrush(Color.FromArgb(240, 255, 255, 204)), // #FFFFCC Light Mode
                2 => new SolidColorBrush(Color.FromArgb(240, 21, 32, 43)),   // #15202b Dark Mode
                3 => new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(180, 255, 107, 107), 0),
                        new GradientStop(Color.FromArgb(180, 254, 202, 87), 1)
                    }
                },
                4 => new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(180, 0, 31, 91), 0),      // #001F5B Beykent Navy
                        new GradientStop(Color.FromArgb(180, 26, 188, 170), 1)    // #1ABCAA Beykent Blue
                    }
                },
                5 => new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                6 => Brushes.Transparent,
                7 => IsNightNow
                    ? new SolidColorBrush(Color.FromArgb(240, 21, 32, 43))
                    : new SolidColorBrush(Color.FromArgb(240, 255, 255, 204)),
                8 => new LinearGradientBrush // Yağmurlu/Fırtınalı: koyu kurşuni-mavi bulut tonları
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(210, 46, 54, 64), 0),
                        new GradientStop(Color.FromArgb(210, 70, 82, 94), 1)
                    }
                },
                9 => new LinearGradientBrush // Kış: soluk buz mavisi/beyaz gradyan
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(200, 226, 240, 250), 0),
                        new GradientStop(Color.FromArgb(200, 186, 214, 235), 1)
                    }
                },
                10 => new LinearGradientBrush // Sentetik Gece: mor-pembe neon retro (synthwave) gökyüzü
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(220, 30, 12, 60), 0),
                        new GradientStop(Color.FromArgb(220, 120, 30, 110), 0.6),
                        new GradientStop(Color.FromArgb(220, 255, 90, 130), 1)
                    }
                },
                _ => Brushes.Transparent
            };
        }

        public Brush GetGridColor()
        {
            return currentTheme switch
            {
                0 => new SolidColorBrush(Color.FromArgb(35, 150, 150, 150)),
                2 => new SolidColorBrush(Color.FromArgb(60, 100, 100, 120)),
                6 => Brushes.Transparent, // Tam şeffaf mod: ızgara çizgisi yok
                7 => IsNightNow
                    ? new SolidColorBrush(Color.FromArgb(60, 100, 100, 120))
                    : new SolidColorBrush(Color.FromArgb(35, 150, 150, 150)),
                8 => new SolidColorBrush(Color.FromArgb(55, 210, 220, 230)),
                9 => new SolidColorBrush(Color.FromArgb(45, 120, 150, 180)),
                10 => new SolidColorBrush(Color.FromArgb(70, 255, 120, 220)),
                _ => new SolidColorBrush(Color.FromArgb(50, 200, 200, 200))
            };
        }

        // Arka plan/ızgara fırçaları her karede (144 kez/sn) yeniden oluşturulup WPF'e yeni bir nesne
        // olarak veriliyordu; bu hem gereksiz tahsisat hem de her karede arka planın "değişti" kabul
        // edilip yeniden boyanmasına yol açıyordu. Aynı tema+gece durumu için bir kez üretilip
        // donduruluyor (Freeze), sonraki karelerde aynı örnek geri veriliyor.
        private Brush cachedBackground;
        private Brush cachedGrid;
        private int cachedThemeKey = int.MinValue;

        private int ThemeCacheKey => currentTheme * 2 + (currentTheme == 7 && IsNightNow ? 1 : 0);

        public Brush GetBackgroundBrushCached()
        {
            EnsureThemeCache();
            return cachedBackground;
        }

        public Brush GetGridColorCached()
        {
            EnsureThemeCache();
            return cachedGrid;
        }

        private void EnsureThemeCache()
        {
            int key = ThemeCacheKey;
            if (key == cachedThemeKey && cachedBackground != null) return;

            cachedThemeKey = key;
            cachedBackground = GetBackgroundBrush();
            if (cachedBackground.CanFreeze) cachedBackground.Freeze();
            cachedGrid = GetGridColor();
            if (cachedGrid.CanFreeze) cachedGrid.Freeze();
        }

        public void SetTheme(int theme)
        {
            currentTheme = theme;
            cachedThemeKey = int.MinValue; // önbelleği geçersiz kıl
        }
    }
}
