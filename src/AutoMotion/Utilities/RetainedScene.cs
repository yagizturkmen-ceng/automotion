using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Sahne katmanının görsel ağacını kareler arasında KORUR (retained mode).
    ///
    /// SORUN: render döngüsü her karede <c>RenderCanvas.Children.Clear()</c> yapıp sahnenin tamamını
    /// yeniden ekliyordu. Araç gövdeleri NESNE olarak zaten önbelleklenmişti (bkz. vehicleBodyCache),
    /// ama her kare Children'dan çıkarılıp geri eklendikleri için görsel ağaçtan koparılıp yeniden
    /// bağlanıyorlardı - WPF her bağlanmada o alt ağacın kompozisyon düğümünü yeniden kuruyor.
    ///
    /// ÖLÇÜM (GERÇEK uygulamada, 5 araç, 20 saniyelik koşu, aynı binary'de A/B):
    ///   RenderGame kare başına : 2.038 ms -> 0.557 ms   (3.66x)
    ///   Süreç CPU'su           : çekirdeğin %101.9 -> %86.1
    ///   RenderGame'in CPU payı : %10.3 -> %3.4
    ///
    /// ÖNEMLİ BULGU: bu değişiklikten sonra CPU'nun **%94'ü uygulama kodunun DIŞINDA** (WPF
    /// kompozisyonu/GPU/GC) harcanıyor; tüm GameLoop_Tick yalnızca %5.8. Yani sahne kurulumu artık
    /// darboğaz değil - kalan maliyet tam ekran saydam/katmanlı pencerenin kompozisyonunda.
    /// Sahne çizimini daha da optimize etmek (DrawingVisual/WriteableBitmap) %3.4'lük bir dilimi
    /// hedeflemek olurdu; sıradaki iş o değil, pencere/kompozisyon mimarisi.
    ///
    /// ÇÖZÜM: kare boyunca elemanlar aynı sırayla "yerleştirilir"; sıra bir önceki kareyle aynıysa
    /// görsel ağaca HİÇ dokunulmaz - yalnızca çağıranın güncellediği özellikler (konum, transform,
    /// renk) değişir. Kare sonunda artakalan elemanlar kırpılır.
    ///
    /// GEÇİCİ ELEMANLAR (yağmur damlası, parçacık, iz): her karede yeniden üretilmek yerine
    /// <see cref="Rent{T}"/> ile havuzdan alınır. Havuzlar çağrı noktası başına ayrıdır: aynı çağrı
    /// noktası her karede aynı özellik kümesini yazdığı için havuzdan dönen eleman üzerinde bayat
    /// (bir önceki kullanımdan kalan) özellik kalmaz.
    ///
    /// SAHNE DIŞI ELEMANLAR: bu sınıf yalnızca kendisine verilen katmanı yönetir. Seçim kutusu,
    /// kalp animasyonu gibi sahne döngüsü dışında eklenen öğeler RenderCanvas'ın doğrudan çocuğu
    /// olarak kalır ve kare kırpmasından etkilenmez (eskiden her karedeki Clear() onları da
    /// siliyordu).
    /// </summary>
    public sealed class RetainedScene
    {
        private readonly Panel layer;
        private readonly Dictionary<int, PoolEntry> pools = new();
        private int cursor;

        public RetainedScene(Panel layer)
        {
            this.layer = layer ?? throw new ArgumentNullException(nameof(layer));
        }

        private sealed class PoolEntry
        {
            public readonly List<UIElement> Items = new();
            public int Used;
        }

        /// <summary>Bu karede görsel ağaca yapılan ekleme sayısı (0 = ağaç hiç kirlenmedi).
        /// Testler ve teşhis için; sahne kararlıyken sıfır olmalı.</summary>
        public int AddedThisFrame { get; private set; }

        /// <summary>Bu karede görsel ağaçtan çıkarılan eleman sayısı.</summary>
        public int RemovedThisFrame { get; private set; }

        /// <summary>Katmandaki eleman sayısı.</summary>
        public int Count => layer.Children.Count;

        public void BeginFrame()
        {
            cursor = 0;
            AddedThisFrame = 0;
            RemovedThisFrame = 0;

            foreach (var pool in pools.Values)
                pool.Used = 0;
        }

        /// <summary>
        /// Verilen çağrı noktasının havuzundan bir sonraki elemanı alır (yoksa üretir) ve sahnedeki
        /// sırasına yerleştirir. Dönen eleman bir önceki kareden kalma olabilir - çağıran, o çağrı
        /// noktasının yazdığı TÜM özellikleri yeniden atamalıdır.
        /// </summary>
        public T Rent<T>(ScenePool poolId) where T : UIElement, new()
        {
            int key = (int)poolId;
            if (!pools.TryGetValue(key, out var pool))
            {
                pool = new PoolEntry();
                pools[key] = pool;
            }

            T element;
            if (pool.Used < pool.Items.Count)
            {
                // Havuzdaki nesne tipi çağrı noktasıyla sabittir; yine de savunmacı davranılır.
                element = pool.Items[pool.Used] as T;
                if (element == null)
                {
                    element = new T();
                    pool.Items[pool.Used] = element;
                }
            }
            else
            {
                element = new T();
                pool.Items.Add(element);
            }

            pool.Used++;
            Place(element);
            return element;
        }

        /// <summary>
        /// Dışarıda tutulan (havuza ait olmayan) bir elemanı sahnedeki sırasına yerleştirir -
        /// ör. araç başına önbelleklenmiş gövde ağacı. Eleman zaten doğru sıradaysa görsel ağaca
        /// dokunulmaz.
        /// </summary>
        public void Place(UIElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var children = layer.Children;

            // Hızlı yol: sahne bir önceki kareyle aynı sırada kuruluyorsa hiçbir şey yapılmaz.
            if (cursor < children.Count && ReferenceEquals(children[cursor], element))
            {
                cursor++;
                return;
            }

            // Eleman başka bir sıradaysa (kompozisyon değişmiş) önce oradan çıkarılır.
            int existing = IndexOf(children, element, cursor);
            if (existing >= 0)
            {
                children.RemoveAt(existing);
                RemovedThisFrame++;
            }

            if (cursor < children.Count) children.Insert(cursor, element);
            else children.Add(element);

            AddedThisFrame++;
            cursor++;
        }

        /// <summary>Bu karede yerleştirilmeyen artık elemanları katmandan çıkarır.</summary>
        public void EndFrame()
        {
            var children = layer.Children;
            while (children.Count > cursor)
            {
                children.RemoveAt(children.Count - 1);
                RemovedThisFrame++;
            }

            // Kullanılmayan havuz nesneleri listede kalır (bir sonraki karede yeniden kullanılmak
            // üzere) - havuzun kendisi sahne kırpmasından bağımsızdır. Sınırsız büyümesini
            // engellemek için bkz. TrimPools.
            TrimPools();
        }

        /// <summary>
        /// Bir tepe anında (ör. sağanak yağmur) şişen havuzları kalıcı olarak taşımamak için,
        /// kullanılmayan fazlalığı kademeli olarak kırpar. Kademeli olması bilinçli: eleman sayısı
        /// kare kare dalgalandığında sürekli üret/at döngüsüne girilmez.
        /// </summary>
        private void TrimPools()
        {
            foreach (var pool in pools.Values)
            {
                int slack = pool.Items.Count - pool.Used;
                if (slack <= PoolSlackTolerance) continue;

                int removeCount = Math.Max(1, slack / 4);
                pool.Items.RemoveRange(pool.Items.Count - removeCount, removeCount);
            }
        }

        /// <summary>Havuzda tutulmasına izin verilen kullanılmayan eleman sayısı. Küçük dalgalanmalar
        /// (birkaç parçacık) yeniden üretime yol açmasın diye sıfır değil.</summary>
        private const int PoolSlackTolerance = 16;

        /// <summary>Sahneyi tamamen boşaltır (render döngüsü askıya alınırken donmuş kare kalmasın).
        /// Havuzlar korunur - döngü uyandığında nesneler yeniden kullanılır.</summary>
        public void Clear()
        {
            layer.Children.Clear();
            cursor = 0;
            foreach (var pool in pools.Values)
                pool.Used = 0;
        }

        private static int IndexOf(UIElementCollection children, UIElement element, int from)
        {
            for (int i = from; i < children.Count; i++)
                if (ReferenceEquals(children[i], element)) return i;
            return -1;
        }
    }

    /// <summary>
    /// Sahnedeki her geçici eleman türü/çağrı noktası için ayrı bir havuz kimliği. Çağrı noktası
    /// başına ayrı olması ŞART: havuzdan dönen eleman bir önceki kareden kalma özellikler taşır,
    /// aynı havuzu iki farklı çağrı noktası paylaşırsa bayat özellik sızar.
    /// </summary>
    public enum ScenePool
    {
        PhysicsRamp,
        FuelCanCursor,
        StopSignRing,
        SkidMark,
        ExhaustPuff,
        DamageSmoke,
        GreetingPop,
        SpeedLine,
        RainDrop,
        RainSplash,
        SnowFlake,
        ObstacleBox,
        Particle,
        NightOverlay,
        WreckEmoji,
        TowTruck,
        UnderGlow,
        HeadlightCone,
        SignalLamp,
        Reflection,
        Wiper,
        Shadow,
        VehicleRect,
        VehicleEmoji,
        Spoiler,
        TrailerRect,
        TrailerEmoji,
        MudSplatter,
        DamageMark,
        HpBar,
        Badge,
        RouteLine,
        RouteFlag,
        MoodTag,
        Plate,
        CargoBox
    }
}
