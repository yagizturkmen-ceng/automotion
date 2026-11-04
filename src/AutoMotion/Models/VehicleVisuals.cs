using System.Collections.Generic;

namespace AutoMotion.Models
{
    /// <summary>Bir tekerleğin görsel üzerindeki konumu (resmin genişlik/yükseklik oranına göre kesir)</summary>
    public readonly struct WheelSpec
    {
        public double CxFrac { get; }
        public double CyFrac { get; }
        public double RFrac { get; }

        public WheelSpec(double cxFrac, double cyFrac, double rFrac)
        {
            CxFrac = cxFrac;
            CyFrac = cyFrac;
            RFrac = rFrac;
        }
    }

    /// <summary>Gerçek marka görseliyle çizilen bir araç varyantının tüm görsel meta verisi</summary>
    public class VehicleVisual
    {
        public string Type { get; init; } = "";
        public string Brand { get; init; } = "";
        public string Label { get; init; } = "";
        public string ImagePath { get; init; } = "";
        public double AspectRatio { get; init; } // genişlik / yükseklik
        public WheelSpec[] Wheels { get; init; } = System.Array.Empty<WheelSpec>();

        /// <summary>Sadece dorse/römork görselleri için: normalde çeki oku görselin SOL tarafındadır
        /// (bkz. Register). Bazı kaynak fotoğraflar ayna metnini okunaklı tutmak için ters (sağdan
        /// sola değil soldan sağa) alınmış olabilir - bu durumda çeki oku görselin SAĞ tarafına
        /// düşer; bu bayrak MainWindow'daki aynalama yönünü buna göre tersine çevirir.</summary>
        public bool HitchOnRight { get; init; }
    }

    /// <summary>
    /// "Araç Listesi" klasöründen entegre edilen gerçek marka görsellerinin kataloğu.
    /// Her görsel arkaplanı temizlenmiş, sıkı kırpılmış PNG olarak Assets/Vehicles altında saklanır.
    /// </summary>
    public static class VehicleVisuals
    {
        private static readonly Dictionary<string, VehicleVisual> _catalog = new();
        private static readonly Dictionary<string, List<string>> _brandsByType = new();

        static VehicleVisuals()
        {
            // Tekerlek konumları görsellerden ölçülerek doğrulandı. Temel kısıt: HER tekerlek yere
            // basar, yani merkezinin dikey konumu her zaman "görselin zemin çizgisi - yarıçap"tır
            // (CyFrac = zeminKesri - RFrac). Önceden bazı araçlarda bu tutmuyordu (ör. yarış
            // motosikletinin ARKA tekerleği ön tekerlekten ~36px yukarıda tanımlıydı) - tekerlek
            // görselin gerçek lastiğinin üstüne oturmayınca dönerken yamuk/kaymış görünüyordu.
            // NOT: Her araçta ÖN/ARKA tekerlek aynı çapta ve aynı zemin hizasında olmalı (gerçekte
            // ikisi de aynı boyutta lastik ve aynı zemine basar) - tek tek ölçülen Cy/R değerleri
            // arasındaki küçük farklar (tekerlek gölgesi/çamurluk gölgesi gibi ölçüm gürültüsünden)
            // dönerken aracın "yamuk" durur gibi görünmesine sebep oluyordu. Bu yüzden çift başına
            // R = iki ölçümün ortalaması, Cy = ortak zemin çizgisinden (Cy+R ortalaması) geri
            // hesaplanır. İSTİSNA: traktörün ön/arka tekerlekleri GERÇEKTE de farklı
            // boyuttadır (traktör lastiği), bu yüzden orada radius kasıtlı olarak farklı bırakıldı.
            Register("car", "sedan", "Sedan", 380.0 / 134.0,                        // zemin 0.970
                new WheelSpec(0.190, 0.752, 0.212), new WheelSpec(0.785, 0.752, 0.212));
            Register("car", "sport", "Spor Sedan", 401.0 / 141.0,                   // zemin 0.972
                new WheelSpec(0.173, 0.757, 0.209), new WheelSpec(0.774, 0.757, 0.209));
            Register("car", "luxury", "Lüks Sedan", 394.0 / 145.0,                  // zemin 0.972
                new WheelSpec(0.176, 0.753, 0.210), new WheelSpec(0.777, 0.753, 0.210));

            // Kamyonlar (zemin 0.984)
            Register("truck", "hauler", "Uzun Yol Çekicisi", 365.0 / 248.0,
                new WheelSpec(0.247, 0.849, 0.129), new WheelSpec(0.842, 0.849, 0.129));
            Register("truck", "rig", "Ağır Vasıta Çekici", 347.0 / 246.0,
                new WheelSpec(0.254, 0.858, 0.129), new WheelSpec(0.856, 0.858, 0.129));
            Register("truck", "freight", "Yük Çekicisi", 343.0 / 250.0,
                new WheelSpec(0.243, 0.856, 0.126), new WheelSpec(0.852, 0.856, 0.126));

            // Motosikletler (zemin 0.984 - iki tekerlek de aynı zemin çizgisinde)
            Register("motorcycle", "sport", "Spor Motosiklet", 314.0 / 247.0,
                new WheelSpec(0.154, 0.788, 0.188), new WheelSpec(0.825, 0.788, 0.188));
            Register("motorcycle", "race", "Yarış Motosikleti", 326.0 / 257.0,
                new WheelSpec(0.151, 0.793, 0.197), new WheelSpec(0.835, 0.793, 0.197));

            // Otobüsler
            Register("bus", "coach", "Tur Otobüsü", 410.0 / 179.0,                  // zemin 0.978
                new WheelSpec(0.298, 0.841, 0.131), new WheelSpec(0.725, 0.841, 0.131));
            Register("bus", "intercity", "Şehirlerarası Otobüs", 403.0 / 170.0,     // zemin 0.976
                new WheelSpec(0.278, 0.845, 0.129), new WheelSpec(0.728, 0.845, 0.129));
            Register("bus", "midi", "Midibüs", 404.0 / 157.0,                       // zemin 0.975
                new WheelSpec(0.241, 0.825, 0.131), new WheelSpec(0.726, 0.825, 0.131));

            // Traktör (zemin 0.982): ön tekerlek küçük, arka dev - GERÇEKTE de farklı boyutta
            // (traktörlerde standart), bu yüzden istisnai olarak radius eşitlenmedi; sadece ortak
            // zemin çizgisi (Cy+R) korunuyor.
            Register("tractor", "farm", "Tarım Traktörü", 348.0 / 218.0,
                new WheelSpec(0.222, 0.760, 0.229), new WheelSpec(0.762, 0.695, 0.298));
        }

        /// <summary>
        /// Marka adlı ESKİ varyant anahtarlarının marka-nötr YENİ anahtarlara eşlemesi.
        ///
        /// Neden: görseller kullanıcının kendi özgün çizimleri, ancak dosya adları ve arayüz
        /// etiketleri gerçek üretici markalarına atıfta bulunuyordu ("car_bmw.png", "Mercedes
        /// C-Serisi"). Araç üreticileri ticari markalarını agresif korur; Steam'de bu, kaldırılma
        /// (DMCA) sebebidir. Görsellerin kendisi değişmedi - yalnızca adlandırma nötrleştirildi.
        ///
        /// `Vehicle.Brand` kayıt dosyalarında saklandığı için (bkz. AutoMotionData.Brand) eski
        /// kayıtlar hâlâ eski anahtarları taşır; bunlar yüklenirken buradan geçirilir, aksi halde
        /// araçlar görselini kaybedip jenerik emoji çizimine düşerdi.
        /// </summary>
        private static readonly Dictionary<string, string> LegacyBrandKeys = new()
        {
            { "car:audi", "sedan" },
            { "car:bmw", "sport" },
            { "car:mercedes", "luxury" },
            { "truck:man", "hauler" },
            { "truck:scania", "rig" },
            { "truck:volvo", "freight" },
            { "motorcycle:honda", "sport" },
            { "motorcycle:kawasaki", "race" },
            { "bus:mercedes", "coach" },
            { "bus:neoplan", "intercity" },
            { "bus:iveco", "midi" },
            { "tractor:newholland", "farm" },
        };

        /// <summary>Eski (marka adlı) bir varyant anahtarını güncel karşılığına çevirir; zaten güncel
        /// veya bilinmeyen bir anahtar olduğu gibi döner. Tip duyarlıdır: "mercedes" otomobilde
        /// "luxury", otobüste "coach" olur.</summary>
        public static string MigrateBrandKey(string type, string brand)
        {
            if (string.IsNullOrEmpty(brand)) return brand;
            return LegacyBrandKeys.TryGetValue(Key(type, brand), out var migrated) ? migrated : brand;
        }

        private static void Register(string type, string brand, string label, double aspectRatio, WheelSpec wheel1, WheelSpec wheel2)
        {
            var visual = new VehicleVisual
            {
                Type = type,
                Brand = brand,
                Label = label,
                ImagePath = $"/Assets/Vehicles/{type}/{type}_{brand}.png",
                AspectRatio = aspectRatio,
                Wheels = new[] { wheel1, wheel2 }
            };

            _catalog[Key(type, brand)] = visual;

            if (!_brandsByType.TryGetValue(type, out var brands))
            {
                brands = new List<string>();
                _brandsByType[type] = brands;
            }
            brands.Add(brand);
        }

        private static string Key(string type, string brand) => $"{type}:{brand}";

        public static VehicleVisual Get(string type, string brand)
        {
            if (string.IsNullOrEmpty(brand)) return null;
            // Eski kayıtlardan gelen marka adlı anahtarlar da çözümlensin (bkz. MigrateBrandKey).
            return _catalog.TryGetValue(Key(type, MigrateBrandKey(type, brand)), out var v) ? v : null;
        }

        public static IReadOnlyList<string> BrandsFor(string type)
        {
            return _brandsByType.TryGetValue(type, out var brands) ? brands : System.Array.Empty<string>();
        }

        public static bool HasVisuals(string type) => _brandsByType.ContainsKey(type);
    }

    /// <summary>
    /// "Araç Listesi/Ekipmanlar" klasöründen entegre edilen gerçek dorse/römork görselleri.
    /// Vehicle.TrailerType değerine göre (flatbed/tanker/equipment/wagon) eşleşir.
    /// </summary>
    public static class TrailerVisuals
    {
        private static readonly Dictionary<string, VehicleVisual> _catalog = new();

        static TrailerVisuals()
        {
            // Dorse tekerlekleri de görsellerden ölçüldü. Önceki değerler belirgin şekilde kaymıştı:
            // flatbed'in 3 dingilli bojisi 0.63-0.83 arasındayken tanımlar 0.68/0.88'i (yani birinin
            // arası ve bojinin tamamen dışı) gösteriyordu; equipment/wagon'da ise yarıçap gerçek
            // lastiğin yaklaşık yarısı kadar (0.11-0.12 yerine ~0.17) tanımlıydı.
            // flatbed/tanker: 3 dingilli boji - üçüncü tekerlek görselden ölçülerek eklendi (aynı
            // eksende, ilk ikisiyle aynı Y/yarıçapta, dingil aralığına göre X uzatıldı).
            Register("flatbed", "/Assets/Vehicles/trailer/trailer_flatbed.png", 579.0 / 180.0,   // zemin 0.978
                new WheelSpec(0.631, 0.798, 0.205), new WheelSpec(0.737, 0.798, 0.205), new WheelSpec(0.841, 0.798, 0.205));
            Register("tanker", "/Assets/Vehicles/trailer/trailer_tanker.png", 583.0 / 203.0,     // zemin 0.980
                new WheelSpec(0.614, 0.840, 0.138), new WheelSpec(0.728, 0.840, 0.138), new WheelSpec(0.842, 0.840, 0.138));
            Register("equipment", "/Assets/Vehicles/trailer/trailer_equipment.png", 497.0 / 174.0, // zemin 0.977
                new WheelSpec(0.616, 0.791, 0.181), new WheelSpec(0.769, 0.791, 0.181));
            // wagon: kaynak fotoğrafın yazıları okunaklı kalması için görsel yatayda çevrilmiş
            // (bkz. Assets/Vehicles/trailer/trailer_wagon.png) - bu yüzden çeki oku SAĞ tarafta ve
            // tekerlek X kesirleri de aynalanmış (1-x) durumda; hitchOnRight=true bunu telafi eder.
            Register("wagon", "/Assets/Vehicles/trailer/trailer_wagon.png", 479.0 / 228.0,       // zemin 0.982
                new[] { new WheelSpec(0.182, 0.794, 0.193), new WheelSpec(0.395, 0.794, 0.193) }, hitchOnRight: true);
        }

        private static void Register(string trailerType, string imagePath, double aspectRatio, params WheelSpec[] wheels)
        {
            Register(trailerType, imagePath, aspectRatio, wheels, hitchOnRight: false);
        }

        private static void Register(string trailerType, string imagePath, double aspectRatio, WheelSpec[] wheels, bool hitchOnRight)
        {
            _catalog[trailerType] = new VehicleVisual
            {
                Type = "trailer",
                Brand = trailerType,
                Label = trailerType,
                ImagePath = imagePath,
                AspectRatio = aspectRatio,
                Wheels = wheels,
                HitchOnRight = hitchOnRight
            };
        }

        public static VehicleVisual Get(string trailerType)
        {
            if (string.IsNullOrEmpty(trailerType)) return null;
            return _catalog.TryGetValue(trailerType, out var v) ? v : null;
        }
    }
}
