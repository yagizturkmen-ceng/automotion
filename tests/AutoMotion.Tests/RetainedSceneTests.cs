using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using AutoMotion.Utilities;

namespace AutoMotion.Tests;

/// <summary>
/// Sahne katmanının kareler arasında korunması (bkz. Utilities/RetainedScene.cs).
///
/// Buradaki testler zamana DEĞİL, görsel ağaca yapılan işlem SAYISINA bakar - performans iddiası
/// "şu kadar milisaniye" değil, "kararlı sahnede ağaca hiç dokunulmuyor" olarak deterministik
/// biçimde doğrulanır (zaman ölçümü makine yüküne göre dalgalanır, bu sayım dalgalanmaz).
/// Kazancın büyüklüğü için bkz. RetainedScene sınıf açıklamasındaki ölçüm tablosu.
/// </summary>
public class RetainedSceneTests
{
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>Sabit sayıda parçacığın çizildiği tipik bir kareyi taklit eder.</summary>
    private static void DrawFrame(RetainedScene scene, int rainDrops, int particles)
    {
        scene.BeginFrame();
        for (int i = 0; i < rainDrops; i++)
        {
            var line = scene.Rent<Line>(ScenePool.RainDrop);
            line.X1 = i; line.Y1 = i; line.X2 = i + 1; line.Y2 = i + 20;
        }
        for (int i = 0; i < particles; i++)
        {
            var dot = scene.Rent<Ellipse>(ScenePool.Particle);
            dot.Width = dot.Height = 4;
        }
        scene.EndFrame();
    }

    [Fact]
    public void StableScene_DoesNotTouchTheVisualTree_AfterTheFirstFrame()
    {
        OnSta(() =>
        {
            var scene = new RetainedScene(new Canvas());

            DrawFrame(scene, rainDrops: 40, particles: 12);
            int firstFrameAdds = scene.AddedThisFrame;

            for (int frame = 0; frame < 30; frame++)
            {
                DrawFrame(scene, rainDrops: 40, particles: 12);

                Assert.Equal(0, scene.AddedThisFrame);
                Assert.Equal(0, scene.RemovedThisFrame);
            }

            Assert.Equal(52, firstFrameAdds);  // ilk karede elemanlar bir kez kurulur
            Assert.Equal(52, scene.Count);
        });
    }

    [Fact]
    public void PooledElements_AreReusedAcrossFrames()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);

            DrawFrame(scene, rainDrops: 3, particles: 0);
            var firstFrameElements = canvas.Children.Cast<UIElement>().ToList();

            DrawFrame(scene, rainDrops: 3, particles: 0);
            var secondFrameElements = canvas.Children.Cast<UIElement>().ToList();

            // Aynı NESNELER geri gelmeli - yeniden üretim yok, dolayısıyla GC baskısı da yok.
            Assert.Equal(firstFrameElements.Count, secondFrameElements.Count);
            for (int i = 0; i < firstFrameElements.Count; i++)
                Assert.Same(firstFrameElements[i], secondFrameElements[i]);
        });
    }

    [Fact]
    public void ShrinkingScene_TrimsLeftoverElements()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);

            DrawFrame(scene, rainDrops: 10, particles: 5);
            Assert.Equal(15, canvas.Children.Count);

            DrawFrame(scene, rainDrops: 4, particles: 1);

            Assert.Equal(5, canvas.Children.Count);
            Assert.True(scene.RemovedThisFrame > 0);
        });
    }

    [Fact]
    public void GrowingScene_AddsOnlyTheNewElements()
    {
        OnSta(() =>
        {
            var scene = new RetainedScene(new Canvas());

            DrawFrame(scene, rainDrops: 10, particles: 0);
            DrawFrame(scene, rainDrops: 13, particles: 0);

            // Yalnızca 3 yeni damla eklenmeli; mevcut 10 eleman yerinde kalmalı.
            Assert.Equal(3, scene.AddedThisFrame);
            Assert.Equal(0, scene.RemovedThisFrame);
        });
    }

    /// <summary>Sahne sırası korunmalı: z-sırası Canvas'ta çocuk sırasıyla belirlenir, bir eleman
    /// yanlış sıraya düşerse (ör. araç yağmurun altında kalırsa) görsel olarak bozulur.</summary>
    [Fact]
    public void PlacementOrder_DefinesChildOrder()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);
            var vehicle = new Rectangle();

            scene.BeginFrame();
            var rain = scene.Rent<Line>(ScenePool.RainDrop);
            scene.Place(vehicle);
            var hud = scene.Rent<Rectangle>(ScenePool.HpBar);
            scene.EndFrame();

            Assert.Same(rain, canvas.Children[0]);
            Assert.Same(vehicle, canvas.Children[1]);
            Assert.Same(hud, canvas.Children[2]);
        });
    }

    /// <summary>Araç gövdeleri havuza değil, araç başına önbelleğe ait (bkz. vehicleBodyCache);
    /// sahne bunları yalnızca yerine koyar ve kararlı sahnede ağaca dokunmaz.</summary>
    [Fact]
    public void ExternallyOwnedElements_StayInPlaceAcrossFrames()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);
            var body = new Canvas();

            for (int frame = 0; frame < 5; frame++)
            {
                scene.BeginFrame();
                scene.Rent<Line>(ScenePool.RainDrop);
                scene.Place(body);
                scene.EndFrame();

                if (frame > 0)
                {
                    Assert.Equal(0, scene.AddedThisFrame);
                    Assert.Equal(0, scene.RemovedThisFrame);
                }
            }

            Assert.Same(body, canvas.Children[1]);
        });
    }

    /// <summary>Bir bölümün eleman sayısı değişince ondan SONRAKİ elemanlar da kaymalı ama
    /// kaybolmamalı - sıra korunarak yeniden hizalanır.</summary>
    [Fact]
    public void ElementsAfterAChangedSection_KeepTheirOrder()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);
            var body = new Canvas();

            scene.BeginFrame();
            for (int i = 0; i < 5; i++) scene.Rent<Line>(ScenePool.RainDrop);
            scene.Place(body);
            scene.EndFrame();

            // Yağmur azaldı: araç gövdesi bir sıra öne kaymalı, ağaçta kalmalı.
            scene.BeginFrame();
            for (int i = 0; i < 3; i++) scene.Rent<Line>(ScenePool.RainDrop);
            scene.Place(body);
            scene.EndFrame();

            Assert.Equal(4, canvas.Children.Count);
            Assert.Same(body, canvas.Children[3]);
        });
    }

    [Fact]
    public void Clear_EmptiesTheLayerButKeepsPoolsUsable()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);

            DrawFrame(scene, rainDrops: 6, particles: 2);
            scene.Clear();
            Assert.Empty(canvas.Children);

            DrawFrame(scene, rainDrops: 6, particles: 2);
            Assert.Equal(8, canvas.Children.Count);
        });
    }

    /// <summary>Tepe anından sonra havuz sonsuza kadar şişmemeli (sağanak bitince bellek geri
    /// verilmeli) ama küçük dalgalanmalarda da sürekli üret/at döngüsüne girilmemeli.</summary>
    [Fact]
    public void Pools_ShrinkGraduallyAfterASpike()
    {
        OnSta(() =>
        {
            var canvas = new Canvas();
            var scene = new RetainedScene(canvas);

            DrawFrame(scene, rainDrops: 400, particles: 0);
            Assert.Equal(400, canvas.Children.Count);

            for (int frame = 0; frame < 60; frame++)
                DrawFrame(scene, rainDrops: 5, particles: 0);

            Assert.Equal(5, canvas.Children.Count);

            // Havuz kademeli kırpıldığı için sahne kararlıyken artık ağaca dokunulmuyor olmalı.
            DrawFrame(scene, rainDrops: 5, particles: 0);
            Assert.Equal(0, scene.AddedThisFrame);
            Assert.Equal(0, scene.RemovedThisFrame);
        });
    }

    /// <summary>
    /// REGRESYON KORUMASI: her havuz kimliği çizim kodunda TAM BİR KEZ kullanılmalı.
    ///
    /// İki farklı çağrı noktası aynı havuzu paylaşırsa, havuzdan dönen eleman diğer çağrı noktasının
    /// bıraktığı özellikleri taşır (ör. bir karede yağmur damlası, diğerinde fren lambası olan bir
    /// elips) - bu, derleme hatası vermeyen ama ekranda yanlış görüntü üreten bir hata olurdu.
    /// Hiç kullanılmayan kimlik ise ölü koddur.
    /// </summary>
    [Fact]
    public void EveryScenePool_IsUsedExactlyOnceInTheRenderCode()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "AutoMotion", "Assets", "Lang")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(dir!.FullName, "AutoMotion", "MainWindow.Rendering.cs"));

        var offenders = Enum.GetNames<ScenePool>()
            .Select(name => (name, count: System.Text.RegularExpressions.Regex
                .Matches(source, $@"ScenePool\.{name}\b").Count))
            .Where(x => x.count != 1)
            .Select(x => $"{x.name} = {x.count} kez")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Her havuz tam bir çağrı noktasına ait olmalı:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void SeparatePools_DoNotShareElements()
    {
        OnSta(() =>
        {
            var scene = new RetainedScene(new Canvas());

            scene.BeginFrame();
            var skid = scene.Rent<Line>(ScenePool.SkidMark);
            var rain = scene.Rent<Line>(ScenePool.RainDrop);
            scene.EndFrame();

            // Aynı tip ama farklı çağrı noktası -> ayrı nesneler (bayat özellik sızmasın).
            Assert.NotSame(skid, rain);
        });
    }
}
