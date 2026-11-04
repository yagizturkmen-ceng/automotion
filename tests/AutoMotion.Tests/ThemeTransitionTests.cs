using System;
using System.Threading;
using System.Windows.Media;
using AutoMotion.Services;

namespace AutoMotion.Tests;

/// <summary>
/// Tema çapraz geçişinin dayandığı sözleşme (kalan işler 3.4 / B-3).
///
/// Geçiş, arka plan fırçasının NESNE KİMLİĞİNE bakarak tema değişimini fark ediyor
/// (bkz. MainWindow.UpdateThemeTransition): fırça referansı değişmişse eski arka plan üstte
/// bırakılıp soldurulur. Önbellek bir gün "hep aynı nesneyi döndür" haline gelirse geçiş
/// sessizce çalışmaz - hata vermez, sadece eski ani sıçrama geri gelir. Bu testler onu yakalar.
/// </summary>
public class ThemeTransitionTests
{
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void SameTheme_ReturnsTheSameBrushInstance()
    {
        OnSta(() =>
        {
            var engine = new ThemeEngine();
            engine.SetTheme(2);

            Brush first = engine.GetBackgroundBrushCached();
            Brush second = engine.GetBackgroundBrushCached();

            // Aynı tema her karede yeni fırça üretmemeli (performans) ...
            Assert.Same(first, second);
        });
    }

    [Fact]
    public void ChangingTheme_ReturnsADifferentBrushInstance()
    {
        OnSta(() =>
        {
            var engine = new ThemeEngine();

            engine.SetTheme(1);
            Brush light = engine.GetBackgroundBrushCached();

            engine.SetTheme(2);
            Brush dark = engine.GetBackgroundBrushCached();

            // ... ama tema DEĞİŞTİĞİNDE farklı nesne dönmeli, yoksa geçiş tetiklenmez.
            Assert.NotSame(light, dark);
        });
    }

    /// <summary>Birden çok tema arasında gezinirken her adımda değişim algılanabilmeli.</summary>
    [Fact]
    public void EveryThemeSwitch_IsDetectableByReference()
    {
        OnSta(() =>
        {
            var engine = new ThemeEngine();
            Brush? previous = null;
            int detected = 0;

            for (int theme = 1; theme <= 5; theme++)
            {
                engine.SetTheme(theme);
                Brush current = engine.GetBackgroundBrushCached();

                if (previous != null && !ReferenceEquals(previous, current)) detected++;
                previous = current;
            }

            Assert.Equal(4, detected); // 1→2, 2→3, 3→4, 4→5
        });
    }
}
