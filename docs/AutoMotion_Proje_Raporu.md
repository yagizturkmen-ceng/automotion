# AutoMotion — Teknik Proje Raporu

> Bu doküman, `C:\Users\yagiz\Documents\Claude\AutoMotion` klasöründeki (ve kök `AutoMotion.sln`
> solüsyonundaki) **AutoMotion** projesinin kod tabanı taranarak hazırlanmıştır. Amaç, bu projeyi
> hiç görmemiş bir yapay zekâya (ör. Gemini) tam bağlam sağlamaktır. Rapor Türkçedir.

---

## 1. Projenin Genel Amacı / Ne Yaptığı

**AutoMotion**, Windows masaüstünde çalışan, **şeffaf pencereli bir "masaüstü sanal araç simülatörü"**
uygulamasıdır. Kategori olarak *desktop pet / desktop companion* (masaüstü sanal evcil hayvan)
yazılımları ailesine girer (VPet-Simulator, Desktop Goose, Shimeji-ee gibi uygulamalara benzer), ancak
"evcil hayvan" yerine **araçları** (araba, kamyon, otobüs, bisiklet, motosiklet, tren, helikopter,
traktör) simüle eder.

Kullanıcı, ekranına serbestçe gezinen, yerçekimine tabi, zıplayıp sekebilen, sürüklenip
fırlatılabilen minyatür araçlar ekler. Bu araçlar:
- Otonom olarak dolaşır (gezinme/kovalama/kaçma yapay zekâsı),
- Yakıt tüketir, hasar alır, tamir edilebilir,
- Ekonomi sistemiyle para kazanır/harcar (yükseltme, dekorasyon, dorse satın alma),
- Kargo teslimat görevleri yapabilir,
- Ekran kenarlarına ve aktif pencerelere göre fiziksel olarak "tırmanabilir",
- Kaydedilip daha sonra yüklenebilir.

Uygulama, VPet-Simulator projesinden esinlenilerek geliştirilmiştir ve `REQUIREMENTS.md` dosyasında bu
esinlenme açıkça belgelenmiştir. Görsel dil ise "Beytrium" adlı bir web arayüzü tasarım dilinden
(neo-brutalist: kalın siyah kenarlıklar, sert/blursuz gölgeler, Oswald fontu, buton "basma" efekti)
alınmıştır.

---

## 2. Genel Mimari Yapısı

Proje **tek bir WPF (.NET) masaüstü uygulaması** projesidir — çözüm (solution) düzeyinde tek proje
içerir (`AutoMotion.csproj`), ayrı bir Core/Windows kütüphane ayrımı (VPet'in aksine) yoktur; her şey
tek proje içinde katmanlanmıştır:

```
AutoMotion.sln
└── AutoMotion/ (WPF proje, WinExe, net9.0-windows)
    ├── App.xaml / App.xaml.cs           → Uygulama giriş noktası, global hata yakalama
    ├── MainWindow.xaml / .xaml.cs       → Tek ana pencere: UI + oyun döngüsü + tüm event handler'lar (~2500 satır)
    ├── Models/                          → Oyun durumu ve iş mantığı (motor katmanı)
    │   ├── GameCore.cs                  → Merkezi oyun durumu yöneticisi (VPet'ten esinli)
    │   ├── AutoMotion.cs                → Vehicle sınıfı = "araç" varlığı (fizik + AI + seri hale getirme)
    │   ├── Item.cs                      → Item hiyerarşisi: Fuel, RepairKit, Upgrade, Decoration + ItemFactory
    │   ├── Obstacle.cs                  → Trafik ışığı / kargo paketi / teslimat noktası nesneleri
    │   ├── TouchArea.cs                 → Dokunma/etkileşim bölgesi soyutlaması + GameSaveData DTO
    │   ├── Expansion.cs                 → Eklenti (mod) sistemi taban sınıfı
    │   └── VehicleVisuals.cs            → Gerçek marka görselleri kataloğu (Audi, BMW, Volvo vb.) + tekerlek konumlandırma meta verisi
    ├── Services/
    │   ├── SaveManager.cs               → JSON tabanlı kaydetme/yükleme, autosave, kayıt geçmişi
    │   └── ExpansionManager.cs          → Eklentileri kayıt altına alma, açma/kapatma, her karede güncelleme
    ├── Expansions/                      → Expansion soyut sınıfından türeyen somut eklentiler
    │   ├── TrafficExpansion.cs          → Periyodik otomatik trafik (araç) üretimi
    │   └── EconomyExpansion.cs          → Periyodik pasif gelir
    ├── Utilities/
    │   └── ImageBackgroundRemover.cs    → Runtime'da beyaz arka plan temizleme (flood-fill/hysteresis) + sıkı kırpma
    ├── Views/
    │   └── MiniHudWindow.xaml / .xaml.cs → Rainmeter tarzı bağımsız, sürüklenebilir mini istatistik penceresi
    ├── Styles.xaml                      → "Beytrium" tasarım dili: ModernButton, IconButton, AnimatedPanel, BeytriumScrollBar vb.
    ├── Assets/Vehicles/                 → Marka bazlı araç PNG'leri (car/bus/truck/motorcycle/tractor/trailer alt klasörleri)
    ├── REQUIREMENTS.md                  → Detaylı gereklilik/rekabet analizi raporu (proje içi belge)
    ├── CALISMA_RAPORU.md, GUNLUK_RAPOR_*.md → Geliştirme oturumu günlükleri
    └── cs/, de/, es/, fr/, it/, ja/, ko/, pl/, pt-BR/, ru/, tr/, zh-Hans/, zh-Hant/ → .NET SDK'nın otomatik oluşturduğu kültür/lokalizasyon kaynak klasörleri (proje koduna ait değil, derleme çıktısı yan ürünüdür)
```

### Mimari Katmanlar ve Sorumluluk Ayrımı

1. **Model / Domain katmanı** (`Models/`): Saf iş mantığı, WPF'ye bağımlı değil (TouchArea içindeki
   `Point` hariç). `GameCore` merkezi durumu (`ObservableCollection<Vehicle> AutoMotions`,
   `List<Item> Items`, `ObservableCollection<Obstacle> Obstacles`, `GameData`) tutar ve her karede
   `Update()` metoduyla ilerletilir.
2. **Servis katmanı** (`Services/`): Kalıcılık (`SaveManager`) ve eklenti orkestrasyonu
   (`ExpansionManager`) — `GameCore`'u kullanır ama UI'dan bağımsızdır.
3. **Eklenti katmanı** (`Expansions/`): `Expansion` soyut sınıfından türeyen, açılıp kapatılabilen,
   `GameCore` üzerinde yan etkili (araç ekleme, para verme) davranışlar. VPet'in Steam Workshop/MOD
   sistemine benzetilerek tasarlanmıştır.
4. **Sunum katmanı** (`MainWindow.xaml.cs`, ~2500 satır — tek dev dosya): Tüm UI olay işleyicileri,
   `DispatcherTimer` tabanlı oyun döngüsü (`GameLoop_Tick`, ~60 FPS / 16 ms), procedural
   `Canvas`-tabanlı çizim (`DrawVehicle`, `DrawVehicleImage`), Win32 API ile aktif pencere takibi
   (window clambering), sistem tepsisi (tray icon) entegrasyonu, `SoundEngine` ve `ThemeEngine` iç
   sınıfları da bu dosyanın içinde tanımlıdır.
5. **Views/** klasöründeki `MiniHudWindow`, ana pencereden bağımsız ikinci bir `Window`'dur
   (çoklu monitör / her zaman üstte mini istatistik göstergesi).

### Sınıflar Arası Etkileşim (özet akış)

```
App (giriş noktası)
 └─ MainWindow (Window_Loaded)
     ├─ new GameCore()          → merkezi durum
     ├─ new SaveManager()       → kalıcılık
     ├─ new SoundEngine()       → ses (Console.Beep / SystemSounds tabanlı, iç sınıf)
     ├─ new ThemeEngine()       → 8 temalı arka plan/ızgara rengi yönetimi (iç sınıf)
     ├─ new ExpansionManager()  → TrafficExpansion + EconomyExpansion kaydı
     ├─ DispatcherTimer (16ms) → GameLoop_Tick
     │    ├─ gameCore.Update(canvasWidth, canvasHeight, activeWinTop/Left/Right)
     │    │    └─ her Vehicle.Update(...) çağrılır (fizik + AI + yakıt + hasar)
     │    │    └─ her Obstacle.Update() çağrılır (trafik ışığı zamanlayıcısı)
     │    ├─ expansionManager.UpdateAll(...)
     │    ├─ UpdateCargoMissions() (kargo teslimat mantığı, MainWindow içinde)
     │    └─ RenderCanvas.Children.Clear() + her araç için DrawVehicle(vehicle) (yeniden çizim)
     └─ Kullanıcı etkileşimleri (Canvas_MouseDown/Move/Up, Window_KeyDown, çok sayıda *_Click)
          → doğrudan Vehicle/GameCore/Item durumunu değiştirir
```

---

## 3. Kullanılan Teknolojiler, Diller, Framework'ler

| Kategori | Teknoloji |
|---|---|
| Dil | C# (nullable enable, implicit usings açık) |
| UI Framework | **WPF** (Windows Presentation Foundation) |
| Hedef Framework | **.NET 9** (`net9.0-windows`), `OutputType=WinExe`, `UseWPF=true` |
| Ek Framework Referansı | `Microsoft.WindowsDesktop.App.WindowsForms` (yalnızca `NotifyIcon`/sistem tepsisi için; bilinçli olarak `UseWindowsForms=true` **kullanılmıyor** çünkü WPF tipleriyle isim çakışması — `Point`, `Size`, `Color`, `Brush` vb. — CS0104 hatası yaratıyordu) |
| Seri hale getirme | `System.Text.Json` (kayıt dosyaları JSON formatında) |
| Görsel işleme | `System.Windows.Media.Imaging` (BitmapImage, WriteableBitmap) — manuel arka plan temizleme algoritması |
| Sistem entegrasyonu | Win32 API p/invoke (`user32.dll` — `GetForegroundWindow`, `GetWindowRect`) aktif pencere takibi için |
| Ses | `System.Media.SystemSounds` ve `Console.Beep` (gerçek ses dosyası **yok**, bilinçli olarak ertelenmiş) |
| Paketleme | Self-contained, **klasör bazlı** `dotnet publish` (win-x64) — `PublishSingleFile=true` denenmiş ama WPF native DLL'lerinin (`wpfgfx_cor3.dll` vb.) extract sorunundan dolayı terk edilmiş |
| Görsel varlıklar | PNG (araç/dorse görselleri, `Assets/Vehicles/**/*.png`, `.csproj` içinde `<Resource>` olarak gömülü) |
| Proje/Çözüm | Visual Studio `.sln` (VS2022 / format 12.00), tek proje |

Dış NuGet paket bağımlılığı yok — proje tamamen .NET SDK ve WPF/WinForms framework referanslarıyla
çalışıyor.

---

## 4. Ana İş Akışları / Mantık Akışı

### 4.1. Uygulama açılışı
1. `App` başlar, global `DispatcherUnhandledException`/`UnhandledException` yakalayıcıları kurulur
   (hata `%AppData%\AutoMotion\error.log` dosyasına yazılır, uygulama çökmez).
2. `MainWindow` constructor'ı ekran çalışma alanını (`SystemParameters.WorkArea`) tam kaplayacak
   şekilde pencereyi konumlandırır; pencere **şeffaf, çerçevesiz, her zaman üstte**
   (`WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`) — klasik "masaüstü overlay" deseni.
3. Sistem tepsisi ikonu (`NotifyIcon`) kurulur: göster/gizle, araç ekle, çıkış menüleri.
4. `Window_Loaded`: `GameCore`, `SaveManager`, `SoundEngine`, `ThemeEngine`, `ExpansionManager`
   örneklenir; `TrafficExpansion` ve `EconomyExpansion` kaydedilir; oyun **boş** başlar (kullanıcı
   "Araç Ekle" ile kendi seçtiği tip/markayı ekler); 16 ms'lik `DispatcherTimer` (≈60 FPS) başlatılır.

### 4.2. Oyun döngüsü (her karede, `GameLoop_Tick`)
1. Win32 API ile o an odaktaki (foreground) pencerenin sınırları okunur (araçların o pencerenin
   üstünde "yürüyebilmesi" — window clambering — için).
2. `gameCore.Update(canvasWidth, canvasHeight, activeWinTop, activeWinLeft, activeWinRight)` çağrılır:
   - Her `Obstacle.Update()` (trafik ışığı renk döngüsü: kırmızı→yeşil→sarı→kırmızı).
   - Her `Vehicle.Update(...)`: sürükleniyor mu / kilitli mi / uçuyor mu kontrolleri; yakıt tüketimi;
     rota takibi (`CustomPath`) veya AI (`UpdateAI`: wandering/chasing/fleeing); trafik ışığında
     yavaşlama; yerçekimi + sekme fiziği (duvar/zemin/aktif pencere üstü); sekme sırasında
     squash/stretch deformasyonu; tekerlek dönüş açısı; yönü (sağa/sola bakma).
   - Toplam kilometre (`GameData.TotalMileage`) biriktirilir, 100 km'lik kilometre taşlarında
     başarım kontrolü tetiklenir.
   - `IsDestroyed` (HP≤0) olan araçlar listeden kaldırılır.
   - `CheckAchievements()` — `GameCore.AchievementCatalog`'daki 8 başarım koşulu kontrol edilir,
     yeni kazanılan varsa `AchievementUnlocked` event'i tetiklenir → UI'da toast bildirimi + ses.
3. `expansionManager.UpdateAll(...)`: açık eklentiler tick'lenir (ör. `TrafficExpansion` her ~5
   saniyede ekran kenarından yeni araç doğurur, en fazla 6 tane; `EconomyExpansion` her ~10 saniyede
   araç başına 15 birim pasif gelir verir).
4. `UpdateCargoMissions()`: kargo paketine ulaşan araç `HasCargo=true` olur ve hedefi teslimat
   noktasına döner; teslimat noktasına ulaşınca ödül verilir (`AddMoney`) ve yeni görev başlar.
5. Yağmur açıksa (`isRaining`) yağmur damlaları güncellenir/çizilir.
6. `RenderCanvas.Children.Clear()` + tüm araçlar/engeller/parçacıklar yeniden çizilir
   (`DrawVehicle`/`DrawVehicleImage`) — **diffing yok, her karede tam yeniden çizim** (bilinçli olarak
   ertelenmiş bir performans optimizasyonu notu `REQUIREMENTS.md`'de mevcut).

### 4.3. Kullanıcı etkileşimi akışları
- **Sürükle-fırlat**: `Canvas_MouseDown` → araç seçilir/sürüklenmeye başlanır → `Canvas_MouseMove` ile
  pozisyon güncellenir → `Canvas_MouseUp`'ta bırakılan hız `Clamp(-25,25)` ile serbest bırakılır ve
  fizik motoru devralır.
- **Sağ tık + sürükle**: özel rota (`CustomPath`) noktaları eklenir; araç bu rotayı otonom takip eder.
- **Dikdörtgen çoklu seçim**: `isDragSelecting` ile alan seçimi; Shift+tık ile birden fazla araca aynı
  anda hedef/rota komutu verilebilir (grup komutu, `PlayDrift` sesi eşliğinde).
- **Mağaza / satın alma**: `TryPurchase(Item item)` → `GameData.Money` yeterliyse `item.Use(vehicle)`
  çağrılır (Fuel doldurur, RepairKit HP onarır, Upgrade kalıcı istatistik artışı uygular — hız/
  hızlanma/verimlilik/dayanıklılık —, Decoration görsel özellik ekler); yetersiz bakiyede reddedilir.
- **Kaydet/Yükle**: `SaveBtn_Click` → `gameCore.Save()` → `SaveManager.Save(name, GameSaveData)` JSON
  dosyasına yazılır (`%AppData%\AutoMotion\Saves\*.automotion` + `saves.json` indeks dosyası).
  `LoadBtn_Click` → `SaveListModal` ile tüm kayıtlar listelenir, kullanıcı seçtiğini yükler
  (`gameCore.Load(saveData)` → tüm `Vehicle`/`Item` nesneleri yeniden oluşturulup deserialize edilir).
  Otomatik kayıt (`AutoSave`) her 60 saniyede bir yapılır, en fazla 5 autosave saklanır
  (`PruneOldAutoSaves`). Pencere kapanırken de bir "AutoSave" kaydı alınır (`Window_Closed`).
- **Klavye kısayolları** (`Window_KeyDown`): T=Tema değiştir, M=Ses aç/kapa, R=Araç ekle, C=Temizle,
  S=Kaydet, L=Yükle, F=Yakıt al, H=Onarım, E=Eklentiler paneli, Delete=Seçili aracı sil, V=Uçuş modu,
  K=Kilit modu, G=Tüm arayüzü gizle/göster, A=Otomatik hareketi aç/kapa.
- **Dorse/ekipman takma**: `AttachTrailer_Click` → `Vehicle.TrailerType` set edilir (flatbed/tanker/
  equipment/wagon), `TrailerVisuals` katalogundan görsel çekilip aracın arkasına render edilir.
- **Özel araç görseli içe aktarma**: `ImportCustomVehicleBtn_Click` → kullanıcı kendi PNG'sini seçer →
  `ImageBackgroundRemover.Process()` ile beyaz arka plan temizlenip sıkı kırpılır → `customVisuals`
  sözlüğüne eklenir.

---

## 5. Önemli Dosyalar ve Klasör Yapısı

| Yol | Açıklama |
|---|---|
| `AutoMotion.sln` (kök) | Tek projeli Visual Studio çözüm dosyası |
| `AutoMotion/AutoMotion.csproj` | Proje tanımı: net9.0-windows, WinExe, UseWPF, WindowsForms framework referansı, `Assets\Vehicles\**\*.png` kaynak dahil etme |
| `AutoMotion/App.xaml.cs` | Giriş noktası; global istisna yakalama ve loglama |
| `AutoMotion/MainWindow.xaml` | Ana pencere XAML: şeffaf/çerçevesiz `Window`, `Canvas` (RenderCanvas), yüzer dock, paneller, modallar, `Styles.xaml` kaynaklarını kullanan tüm UI |
| `AutoMotion/MainWindow.xaml.cs` | ~2486 satır: oyun döngüsü, çizim, tüm buton/klavye event handler'ları, `SoundEngine` ve `ThemeEngine` sınıfları burada iç içe tanımlı |
| `AutoMotion/Models/GameCore.cs` | Merkezi oyun durumu: araç/eşya/engel koleksiyonları, `GameData` (para, istatistikler), `Achievement`/`AchievementCatalog` (8 başarım), Save/Load/Reset |
| `AutoMotion/Models/AutoMotion.cs` | `Vehicle` sınıfı: pozisyon/hız/fizik, 8 araç tipi istatistik tablosu (car/truck/bus/bike/motorcycle/train/helicopter/tractor), yerçekimi+sekme+duvar tırmanma fiziği, AI (`UpdateAI`), dorse sistemi, seri hale getirme (`AutoMotionData`) |
| `AutoMotion/Models/Item.cs` | `Item` soyut sınıfı ve alt sınıflar: `Fuel` (regular/premium/turbo), `RepairKit` (standard/advanced/full), `Upgrade` (speed/acceleration/efficiency/durability), `Decoration` (paint/sticker/spoiler/lights); `ItemFactory` |
| `AutoMotion/Models/Obstacle.cs` | Trafik ışığı (zamanlayıcılı renk döngüsü), kargo paketi, teslimat noktası |
| `AutoMotion/Models/TouchArea.cs` | Basit dokunma bölgesi soyutlaması + `GameSaveData` DTO (kayıt dosyası şeması) |
| `AutoMotion/Models/Expansion.cs` | Eklenti taban sınıfı: `Enable/Disable/Tick`, `OnEnable/OnDisable/OnUpdate` template method deseni |
| `AutoMotion/Models/VehicleVisuals.cs` | Gerçek marka görselleri kataloğu (`VehicleVisuals`, `TrailerVisuals`): Audi/BMW/Mercedes/MAN/Scania/Volvo/Honda/Kawasaki/Neoplan/Iveco/New Holland — her biri için görsel yolu, en-boy oranı, tekerlek konum/çap yüzdeleri (`WheelSpec`) |
| `AutoMotion/Services/SaveManager.cs` | JSON dosya tabanlı kayıt sistemi: kaydet/yükle/sil/tümünü temizle, kayıt geçmişi indeksi, autosave (60 sn, maks. 5 kayıt tutulur) |
| `AutoMotion/Services/ExpansionManager.cs` | Eklenti kayıt/aç-kapa/tick orkestrasyonu |
| `AutoMotion/Expansions/TrafficExpansion.cs` | Açıkken ekran kenarlarından periyodik (~5 sn) otomatik araç üretir, en fazla 6 adet; kapanınca kendi ürettiklerini temizler |
| `AutoMotion/Expansions/EconomyExpansion.cs` | Açıkken her ~10 saniyede araç başına 15 birim pasif gelir üretir |
| `AutoMotion/Utilities/ImageBackgroundRemover.cs` | Kullanıcının içe aktardığı özel araç görseli için beyaz arka plan temizleme (hysteresis flood-fill algoritması) ve sıkı kırpma — Python/Pillow ile yapılan ön işlemin C#/WPF runtime karşılığı |
| `AutoMotion/Views/MiniHudWindow.xaml(.cs)` | Ana pencereden bağımsız, sürüklenebilir, her zaman üstte kalan mini istatistik penceresi (araç sayısı, para, mesafe) |
| `AutoMotion/Styles.xaml` | "Beytrium" tasarım dili kaynak sözlüğü: `ModernButton`, `IconButton`, `AnimatedPanel`, `BeytriumScrollBar`, `CustomDialogOverlay`, renk paleti (Beykent sarı/lacivert, neo mavi) |
| `AutoMotion/Assets/Vehicles/{car,truck,bus,motorcycle,tractor,trailer}/` | Arka planı temizlenmiş marka PNG'leri |
| `AutoMotion/REQUIREMENTS.md` | Detaylı gereklilik ve rekabet analizi — VPet-Simulator, Desktop Goose, Shimeji-ee, Rainmeter ve "Beytrium" ile karşılaştırma; tamamlanan/eksik özellik tabloları; mimari notlar; bilinçli ertelenen maddeler |
| `AutoMotion/CALISMA_RAPORU.md`, `GUNLUK_RAPOR_2026-07-28-1.md` | Geliştirme oturumu değişiklik günlükleri (Türkçe) |
| `AutoMotion/cs/`, `de/`, `es/`, `fr/`, `it/`, `ja/`, `ko/`, `pl/`, `pt-BR/`, `ru/`, `tr/`, `zh-Hans/`, `zh-Hant/` | .NET SDK derleme çıktısının otomatik oluşturduğu kültür-özel kaynak (satellite resource) klasörleri; proje kaynak kodunun parçası değildir |
| `AutoMotion/bin/`, `AutoMotion/obj/` | Derleme çıktı/ara dosya klasörleri (kaynak kontrolüne dahil edilmemesi gereken standart .NET klasörleri) |

---

## 6. Mevcut Özellikler Listesi

**Çekirdek simülasyon/fizik**
- Yerçekimi + zeminde sönümlü sekme fiziği
- Sürükle-bırak ile fırlatma (hız clamp'li)
- Ekran kenarına çarpma/sekme, kenara "tırmanma" (duvar zemin gibi davranır)
- Aktif (odaktaki) pencere üzerinde "yürüme" (Win32 pencere sınırı takibi)
- Uçuş modu (tüm araç tiplerinde açılabilir, helikopterde varsayılan, sinüzoidal süzülme)
- Araç kilitleme (AI/hareketi tamamen durdurma)
- Rastgele gezinme / kovalama / kaçma yapay zekâ durumları
- Sağ tık ile özel rota çizme (`CustomPath`)
- Dikdörtgen alan ile çoklu seçim + grup rota komutu (Shift+tık)
- Trafik ışığı etkileşimi (kırmızı/sarıda yavaşlama-durma)
- Dorse/ekipman takma sistemi (kamyon: açık kasa/tanker; traktör: 2 tarım ekipmanı)
- Yağmur/hava durumu sistemi (görsel yağmur damlaları, sürtünme değişimi)
- Kargo teslimat görev döngüsü (paket topla → teslimat noktasına götür → ödül)

**Envanter/Ekonomi**
- Tüketilebilir eşyalar: 3 yakıt tipi, 3 onarım kiti tipi
- Para/ekonomi sistemi (`GameData.Money`, kargo ödülü, pasif gelir eklentisi)
- 4 yükseltme tipi (hız/hızlanma/verimlilik/dayanıklılık) — kalıcı istatistik artışı, tekrar satın alınamaz
- 4 dekorasyon tipi (boya/sticker/spoiler/LED) — görsel etkili (kontur rengi, rozet, spoiler çizgisi, glow)

**Kalıcılık**
- JSON tabanlı kaydetme/yükleme (`%AppData%\AutoMotion\Saves`)
- Otomatik kayıt (60 sn aralıklı, en fazla 5 autosave tutulur)
- Kayıt Listesi modalı (tüm kayıtları listeleyip seçerek yükleme/silme)
- Kapanışta otomatik "AutoSave" kaydı

**Genişletilebilirlik**
- `Expansion` taban sınıfı üzerinden mod/eklenti mimarisi
- 2 hazır eklenti: Otomatik Trafik, Pasif Ekonomi (açılıp kapatılabilir, panelden yönetilir)

**Kişiselleştirme/Görsel**
- Gerçek marka görselleri kataloğu (Audi, BMW, Mercedes, MAN, Scania, Volvo, Honda, Kawasaki, Neoplan, Iveco, New Holland) — tekerlek konumu piksel-hassas eşlenmiş
- Kullanıcının kendi araç görselini içe aktarması (otomatik arka plan temizleme + kırpma)
- 8 tema (Şeffaf, Açık, Koyu, Gün Batımı, Okyanus, Cam/Glassmorphism, Tam Şeffaf, Otomatik Gündüz/Gece)
- Araç büyüt/küçült (ölçekleme), isim verme
- Duygu durumuna göre görsel rozet (yakıt/mutluluk/HP düşükse 😢/😰/🤕)

**İstatistik/Başarım**
- 8 başarımlık katalog (ilk araç, filo büyüklüğü, kazanç eşikleri, mesafe eşikleri, koleksiyoncu)
- Toplam mesafe (`TotalMileage`) takibi, her 100 km'de konuşma balonu kutlaması
- Mini HUD penceresi (araç sayısı, para, mesafe — Rainmeter tarzı bağımsız widget)

**Kullanılabilirlik/UI**
- Sistem tepsisi (tray) entegrasyonu — göster/gizle, hızlı araç ekle, çıkış
- Şeffaf, çerçevesiz, her zaman üstte pencere (gerçek masaüstü overlay)
- Sürüklenebilir/sabitlenebilir (pin) yan paneller
- Tek tek araç silme (liste satırı, panel butonu, Delete tuşu)
- 13 klavye kısayolu
- Özel (Beytrium tarzı) diyalog/modal ve scrollbar bileşenleri
- Parçacık efekt sistemi (neon iz/trail)

---

## 7. TODO / Eksikler / Geliştirmeye Açık Noktalar

`REQUIREMENTS.md` dosyasında ayrıntılı biçimde belgelenmiş, henüz uygulanmamış veya kısmen uygulanmış
maddeler:

**Henüz uygulanmamış öneriler**
1. **Zamanlanmış görevler/hatırlatıcılar** — VPet'in `ScheduleTask.cs` benzeri, belirli saatte
   konuşma balonuyla hatırlatma (ör. "yakıtım azalıyor, 10 dakikaya doldur").
2. **Çoklu tema desteğinin genişletilmesi** — `ThemeEngine` mevcut (8 tema var) ama daha fazla
   hazır gündüz/gece/yağmurlu/kış teması önerilir.
3. **Çoklu pencere / çoklu monitör desteği** — uygulama şu an tek `Window` örneği; VPet ve Desktop
   Goose'daki ekranlar arası geçiş desteği yok (not: `ToggleMultiMonitor_Click` adlı bir handler var,
   ama kapsamı sınırlı olabilir — kod incelemesinde tam çoklu-monitör mimarisi doğrulanmadı).

**Bilinçli olarak ertelenmiş maddeler** (gerekçesiyle, `REQUIREMENTS.md` §7'de belgelenmiş)
- **Gerçek ses dosyaları**: `SoundEngine` şu an yalnızca `SystemSounds`/`Console.Beep` kullanıyor;
  telifsiz `.wav` asset'i indirmek harici kaynak seçimi ve kullanıcı onayı gerektirdiği için
  ertelenmiş.
- **Render performans optimizasyonu**: her karede `RenderCanvas.Children.Clear()` + tam yeniden çizim
  yapılıyor; 20+ araçta performans sorunu yaratabilir. Diffing tabanlı render motoruna geçiş büyük bir
  mimari değişiklik olduğundan ertelenmiş — **bu, projenin en belirgin bilinen mimari kısıtıdır**.
- **Gerçek marka logoları/fotoğrafları**: telif riski + harici indirme onay gerekliliği nedeniyle
  kasıtlı olarak dahil edilmemiş; bunun yerine emoji+vektör çizim ve elle kırpılmış/arka planı
  temizlenmiş jenerik marka görselleri kullanılıyor.

**Kod tabanı taramasında daha önce bulunup giderilen sorunlar** (tarihsel referans amaçlı,
`REQUIREMENTS.md` §7 tablosu):
- Autosave'lerin sınırsız birikmesi → 5 kayıtla sınırlandı.
- "Yükle" butonunun her zaman sadece en son kaydı açması → kayıt listesi modalı eklendi.
- Satın alınan dekorasyonların görsel etkisinin olmaması → çizim koduna bağlandı.
- Mağazada sınırlı sayıda yükseltme/dekorasyon seçeneği → tüm tipler UI'a bağlandı.
- Çoklu seçimde grup komutu verilememesi → Shift+tık ile grup hedefleme eklendi.
- Ölü kod (`SoundEngine.PlayCrash`/`PlayDrift` hiç çağrılmıyordu, kullanılmayan alanlar, boş
  `Resources/Utilities/Views` klasörleri) → temizlendi (not: `Utilities/` ve `Views/` klasörleri
  şu an tekrar dolu — `ImageBackgroundRemover.cs` ve `MiniHudWindow` içeriyor; bu klasörler daha
  sonraki bir iterasyonda yeniden kullanılmaya başlanmış olmalı).

**Yapılandırma/Bağımlılık notları**
- Dış NuGet paketi yok; sadece .NET 9 SDK + WPF + `Microsoft.WindowsDesktop.App.WindowsForms`
  framework referansı.
- `PublishSingleFile=true` **kullanılmamalı** — WPF native DLL extraction sorunu nedeniyle
  `DllNotFoundException` veriyor; self-contained **klasör** publish kararı korunmalı (mimari not,
  `REQUIREMENTS.md` §6).
- Kayıt dosyaları ve hata logları `%AppData%\AutoMotion\` altında tutulur (`Saves\`, `error.log`).
- Projede otomatik test (unit test projesi) **yok** — `.sln` içinde tek proje var, test altyapısı
  eklenmemiş; bu da geliştirmeye açık bir alan olarak not edilebilir.

---

*Bu rapor, kod tabanının doğrudan okunmasıyla (MainWindow.xaml.cs, tüm Models/Services/Expansions/
Utilities/Views dosyaları, .csproj, .sln, REQUIREMENTS.md, CALISMA_RAPORU.md) 2026-07-29 tarihinde
hazırlanmıştır.*
