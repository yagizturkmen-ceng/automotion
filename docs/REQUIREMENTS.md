# AutoMotion — Detaylı Gereklilik ve Rekabet Analizi Raporu

**Doküman amacı:** AutoMotion (masaüstü şeffaf araç simülatörü) için, benzer kategorideki uygulamalar
incelenerek çıkarılmış, uygulanabilir ve önceliklendirilmiş bir gereklilik listesi sunmak. Bu belge hem
mevcut kod tabanında (bkz. `MainWindow.xaml`, `MainWindow.xaml.cs`, `Models/AutoMotion.cs`,
`Models/GameCore.cs`, `Services/`, `Expansions/`) zaten karşılanmış maddeleri hem de eksik/önerilen
maddeleri işaretler.

---

## 1. Kapsam ve Yöntem

AutoMotion, kategorik olarak **"masaüstü sanal evcil hayvan / masaüstü arkadaş" (desktop pet / desktop
companion)** yazılımları ailesine girer, ancak "evcil hayvan" yerine "araç" (araba, kamyon, otobüs,
traktör, helikopter vb.) simüle eder. Bu analiz için incelenen referans uygulamalar:

| Uygulama | Kategori | İncelenen Kaynak | Bu projeyle ortak yön |
|---|---|---|---|
| **VPet-Simulator** (`VPet-main/`, bu makinede yerel kopyası mevcut) | Açık kaynak masaüstü evcil hayvan motoru (WPF, C#) | Yerel repo: `VPet-Simulator.Core`, `VPet-Simulator.Windows`, `README.md` | Şeffaf pencere, yerçekimi/sürükleme fiziği, envanter/eşya sistemi, kaydetme sistemi, MOD/Expansion mimarisi, konuşma balonu (SayInfo), iş/görev sistemi (WorkTimer) |
| **Desktop Goose** | Şakacı masaüstü ajanı (Windows) | Genel bilgi | Ekranda serbestçe dolaşan, pencere kenarlarıyla etkileşen nesne |
| **Shimeji-ee / Bongo Cat** | Sprite tabanlı masaüstü maskotu | Genel bilgi | Sürükle-bırak, çoklu nesne, ekran kenarına tırmanma |
| **Rainmeter (widget iskeletleri)** | Masaüstü widget/HUD çerçevesi | Genel bilgi | Özelleştirilebilir panel/skin sistemi, sabitleme (pin) |
| **Beytrium (web uygulaması)** | Bu projenin görsel dil kaynağı | `Beytrium_Arayuz_Tasarim_Raporu.docx` | Neo-brutalist UI dili: kalın siyah kenarlık, sert gölge, buton bas efekti, Oswald tipografi, Beykent sarı/lacivert + neo mavi palet |

**Yöntem:** Her referans uygulamanın çekirdek döngüsü (game loop), fizik modeli, envanter/ekonomi
sistemi, UI/UX yaklaşımı ve genişletilebilirlik mimarisi incelenip AutoMotion'ın mevcut
`Models/AutoMotion.cs` (`Vehicle.Update()`), `Models/GameCore.cs` ve `MainWindow.xaml.cs` dosyalarındaki
karşılıklarıyla eşleştirildi. Halihazırda VPet esintili birçok mekanik zaten kodda **"VPet gibi"** yorum
etiketiyle işaretli durumda (bkz. `Update()` içindeki yerçekimi/sekme, ekran kenarı gizlenme, sürükleme
rotasyonu).

---

## 2. Fonksiyonel Gereklilikler

### 2.1. Çekirdek Simülasyon / Fizik (Durum: **Büyük ölçüde tamamlandı**)

| # | Gereklilik | Kaynak / Gerekçe | Durum |
|---|---|---|---|
| F-01 | Yerçekimi + zemine sekme fiziği (gravity/bounce) | VPet: nesne bırakıldığında serbest düşüş ve zeminde sönümlü sekme | ✅ `Vehicle.Update()` — `gravity=0.4`, `bounce=-0.45` |
| F-02 | Sürükle-bırak ile fırlatma (drag & throw) | VPet, Desktop Goose | ✅ `Canvas_MouseUp` — hız `Clamp(-25,25)` ile serbest bırakılıyor |
| F-03 | Ekran kenarına çarpıp sekme / sınır içinde kalma | VPet, Shimeji | ✅ Duvar çarpışması bloğu (`VelocityX *= -0.7`) |
| F-04 | Ekran kenarında "gizlenme" (side-hide) davranışı | VPet'in ekran dışına kısmen çıkıp saklanma davranışı | ✅ `IsSideHidden` mantığı |
| F-05 | Aktif pencere üzerinde "sürüş" (window clambering) | VPet'in masaüstü pencere üstünde yürüme özelliği | ✅ `activeWinTop/Left/Right` parametreleri ile taklit ediliyor |
| F-06 | Uçabilen araç tipi (helikopter sabit, diğerleri opsiyonel) | Kullanıcı talebi: *"araçlar isterse uçabilsin"* | ✅ **Bu iterasyonda eklendi** — `Vehicle.IsFlying`, `V` kısayolu, tüm tipler için yerçekiminden muafiyet + VPet tarzı sinüs salınımlı süzülme |
| F-07 | Araç kilitleme (AI/hareketi durdurma) | Kullanılabilirlik iyileştirmesi | ✅ **Bu iterasyonda eklendi** — `Vehicle.IsLocked`, `K` kısayolu |
| F-08 | Rastgele gezinme / kovalama / kaçma AI durumları | VPet'in mod sistemi, basit davranış ağaçları | ✅ `UpdateAI()` — `wandering/chasing/fleeing` |
| F-09 | Özel rota çizme (sağ tık + sürükle ile path belirleme) | RTS oyunlarından esinlenme, kullanılabilirlik | ✅ `CustomPath` listesi |
| F-10 | Çoklu seçim (dikdörtgen alan ile) | Genel masaüstü UX standardı | ✅ `isDragSelecting` |
| F-11 | Trafik ışığı / engel etkileşimi | Simülasyon derinliği | ✅ `Obstacle`, kırmızı/sarı ışıkta yavaşlama |
| F-12 | **Dorse / Ekipman takma sistemi** (kamyon: açık kasa & tanker, traktör: tarım ekipmanı) | Kullanıcı talebi (Gemini mockup referansı) | ✅ **Bu iterasyonda eklendi** — `Vehicle.TrailerType`, `TrailerCatalog`, görsel çizim + UI paneli |
| F-13 | Yağmur / hava durumu sistemi | Ortam çeşitliliği, VPet'in "duygu durumuna göre görsel değişim" fikri | ✅ `isRaining`, `RainDrop` |
| F-14 | Kargo görevi (teslimat sistemi) | Basit ekonomi/oyunlaştırma döngüsü | ✅ `HasCargo`, `TargetX/TargetY` |

### 2.2. Envanter / Ekonomi (Durum: **Kısmen tamamlandı**)

| # | Gereklilik | Kaynak / Gerekçe | Durum |
|---|---|---|---|
| F-15 | Yakıt/tamir kiti gibi tüketilebilir eşyalar | VPet'in yemek/içecek (Food/IFood) sistemi | ✅ `Item`, `Fuel`, `RepairKit` |
| F-16 | Para/ekonomi sistemi | VPet'in mağaza (winBetterBuy) sistemi | ✅ **Bu iterasyonda tamamlandı** — kargo teslimatı zaten ödül veriyordu (`+250`); `TryPurchase()` ile Yakıt/Onarım/Yükseltme/Dekorasyon artık gerçek parayla satın alınıyor, yetersiz bakiyede satın alma reddediliyor |
| F-17 | Yükseltme (upgrade) sistemi | RPG tarzı ilerleme | ✅ **Bu iterasyonda tamamlandı** — `BuyUpgradeBtn_Click` ile Hız yükseltmesi mağazadan satın alınabiliyor (diğer upgrade tipleri `Upgrade` sınıfında hazır, ek buton eklemek yeterli) |
| F-18 | Dekorasyon sistemi | Kişiselleştirme | ✅ **Bu iterasyonda tamamlandı** — `BuyDecorationBtn_Click` ile Boya dekorasyonu satın alınabiliyor |

### 2.3. Kaydetme / Kalıcılık (Durum: **Tamamlandı**)

| # | Gereklilik | Kaynak | Durum |
|---|---|---|---|
| F-19 | Oyun durumunu kaydet/yükle | VPet'in `GameSave` sistemi | ✅ `SaveManager`, `GameCore.Save()/Load()`. **Bu iterasyonda düzeltildi:** autosave artık en fazla 5 kayıt tutuyor (`PruneOldAutoSaves`), "Yükle" butonu artık sadece en son kaydı değil, tüm kayıtları listeleyip seçtiren bir **Kayıt Listesi modalı** (`SaveListModal`) açıyor |
| F-20 | Yeni eklenen alanların (uçuş, kilit, dorse, isim) kaydı | Veri bütünlüğü | ✅ **Bu iterasyonda eklendi** — `AutoMotionData.IsFlying/IsLocked/TrailerType/Name` |

### 2.4. Genişletilebilirlik (Durum: **Temel altyapı var, içerik az**)

| # | Gereklilik | Kaynak / Gerekçe | Durum |
|---|---|---|---|
| F-21 | Eklenti (Expansion) mimarisi | VPet'in MOD sistemi (Steam Workshop) | ✅ `ExpansionManager`, `Expansions/TrafficExpansion.cs` |
| F-22 | Birden fazla hazır genişletme (hava durumu, ekonomi, görev) | VPet'in zengin Workshop içeriği | ⚠️ **Kısmen tamamlandı** — `EconomyExpansion` (💰 Pasif Ekonomi, bu iterasyonda eklendi) `TrafficExpansion`'a katıldı, toplam 2 genişletme. Öneri: Hava Durumu ve Görev/Kargo genişletmeleri de ayrı `Expansion` sınıflarına taşınabilir (şu an doğrudan `MainWindow.xaml.cs` içinde) |

---

## 3. UI / UX Gereklilikleri (Beytrium Tasarım Dili Uyumu)

`Beytrium_Arayuz_Tasarim_Raporu.docx` referans alınarak:

| # | Gereklilik | Durum |
|---|---|---|
| U-01 | 4px kalın siyah kenarlıklar (`border-4 border-black`) | ✅ `Styles.xaml` — `ModernButton`, `AnimatedPanel` |
| U-02 | Sert (blursuz), 4-8px offset kutu gölgeleri | ✅ `DropShadowEffect BlurRadius=0` her yerde |
| U-03 | Buton "basma" etkileşim animasyonu (hover/active translate) | ✅ `ModernButton`/`IconButton` şablonlarında `TranslateTransform` |
| U-04 | Özel scrollbar (16px, sarı thumb, siyah kenarlık, hover mavi) | ✅ **Önceki iterasyonda eklendi** — `BeytriumScrollBar` |
| U-05 | Oswald tipografisi tüm arayüzde tutarlı | ✅ **Bu iterasyonda tamamlandı** — `PixelFont` kaynağı Oswald'a çevrildi, pencere kökünde `TextElement.FontFamily` mirası eklendi, `BodyText` stili güncellendi |
| U-06 | Sorgu/mesaj popup'ları Beytrium stilinde | ✅ `CustomDialogOverlay` (sarı başlık şeridi, kalın siyah çerçeve) |
| U-07 | Seçim göstergesi net/blursuz olmalı (kullanıcı talebi: *"bulanık değil başka şekilde belli olsun"*) | ✅ **Bu iterasyonda eklendi** — blur'lu dönen halka kaldırıldı, yerine siyah+sarı kalın köşeli çerçeve + köşe işaretleri + blursuz nabız animasyonu |
| U-08 | Tek tek araç silme (liste + panel + klavye) | ✅ **Bu iterasyonda eklendi** — liste satırı 🗑 butonu, `DeleteSelectedBtn`, `Delete` tuşu |
| U-09 | Sol/sağ panellerin sürüklenebilir ve sabitlenebilir (pin) olması | ✅ `PanelHeader_MouseLeftButtonDown`, `pinnedPanels` |
| U-10 | Mobil/alt gezinme çubuğu (rapor bölüm 5) | ⚪️ **Uygulanamaz** — AutoMotion masaüstü-only WPF uygulaması, mobil düzen kavramı yok. Bilgi amaçlı not düşüldü. |
| U-11 | Arka plan hareketli "blob" / gradyan animasyonu (rapor bölüm 6) | ⚪️ Düşük öncelik — şeffaf masaüstü overlay olduğundan arka plan animasyonu görsel gürültü yaratabilir; **öneri: uygulanmasın**, sadece panel/buton animasyonları yeterli |

---

## 4. Rakip/Benzer Uygulama Kıyaslaması — Öne Çıkan Eksik Fikirler

Bu bölüm, incelenen uygulamalarda bulunup AutoMotion'da **henüz olmayan** ama değer katabilecek
fikirleri listeler (öncelik sırasına göre):

1. ✅ **Duygu durumuna göre görsel geri bildirim — bu iterasyonda tamamlandı.**
   `DrawVehicle` içine mutluluk/yakıt/HP durumuna göre 😢/😰/🤕 rozetleri eklendi (konuşma balonu
   yokken gösteriliyor).
2. **Zamanlanmış görevler / hatırlatıcılar (VPet'in `ScheduleTask.cs`).**
   Kullanıcıya belirli saatte konuşma balonu ile hatırlatma (ör. "Yakıtım bitiyor, 10 dakikaya doldur").
   *(Henüz uygulanmadı.)*
3. **Çoklu tema desteğinin genişletilmesi.**
   `ThemeEngine` ve `ThemeBtn` mevcut ama tema sayısı sınırlı — VPet'in geniş tema/skin galerisine
   benzer şekilde 3-4 hazır tema (gündüz/gece/yağmurlu/kış) önerilir. *(Henüz uygulanmadı.)*
4. ✅ **İstatistik/başarım (achievement) sistemi — kısmen tamamlandı.**
   `GameData.TotalMileage` artık tüm araçlardan biriken mesafeyi topluyor, İstatistikler panelinde
   "🏆 Toplam Mesafe" olarak gösteriliyor, her 100 km'de bir rastgele bir araç konuşma balonuyla
   kutluyor (`GameCore.Update()` milestone kontrolü).
5. **Çoklu pencere / çoklu monitör desteği.**
   VPet ve Desktop Goose'un ekranlar arası geçiş desteği; AutoMotion şu an tek `Window` örneği.
   *(Henüz uygulanmadı.)*

---

## 5. Öncelik Matrisi (Bu iterasyonda tamamlanan vs. sonraki adım önerisi)

| Öncelik | Bu iterasyonda tamamlanan | Sonraki iterasyon önerisi |
|---|---|---|
| **P0 (Kritik UX)** | Font tutarlılığı, blursuz seçim göstergesi, tek tek silme, uçuş/kilit modu, dorse/ekipman sistemi | — |
| **P1 (Ekonomi derinliği)** | — | Para kazanma/harcama akışını UI'a bağlama, yükseltme mağazası |
| **P2 (İçerik genişliği)** | — | 2-3 yeni Expansion (Ekonomi, Mevsimsel Tema, Görev Sistemi) |
| **P3 (Cila)** | — | Duygu durumuna bağlı görsel geri bildirim, başarım sistemi |

---

## 6. Mimari Notlar

- Proje **self-contained WPF (.NET 9, win-x64)** olarak `dotnet publish` ile tek klasöre (`AutoMotion/`
  kökü, `AutoMotion.exe` + native DLL'ler) yayınlanıyor. Single-file paketleme (`PublishSingleFile=true`)
  bu ortamda `DllNotFoundException` verdiği için **klasör bazlı self-contained yayın** tercih edildi —
  bu karar korunmalı.
- Görsel katman tamamen **procedural Canvas çizimi** (`DrawVehicle`), VPet'in sprite/PNG kare animasyon
  sistemine (`PNGAnimation.cs`) birebir port edilemez; bu nedenle "VPet'ten birebir" talebi, VPet'in
  *davranış kurallarının* (fizik, sekme, gizlenme, sürükleme) birebir taklidi olarak yorumlanmış ve
  uygulanmıştır — sprite motorunun kendisi değil.
- Gerçekçi marka görselleri (Mercedes, Volvo vb. fotoğraflar) **kasıtlı olarak dahil edilmedi**: (a) telif
  hakkı riski, (b) harici dosya indirme için kullanıcı onayı gerekliliği. Bunun yerine mevcut
  emoji+vektör çizim yaklaşımı korunarak dorse/ekipman sistemi bu dille genişletildi.

---

## 7. Sonraki Denetimde Bulunan ve Giderilen Ek Sorunlar

Kod tabanının uçtan uca tekrar taranması sonucunda bulunan ve bu iterasyonda giderilen maddeler:

| # | Sorun | Çözüm |
|---|---|---|
| B-01 | Autosave dosyaları sınırsız birikiyordu (`SaveManager.AutoSave`, hiç silme yok) | `PruneOldAutoSaves()` eklendi, en fazla 5 autosave tutuluyor |
| B-02 | "Yükle" butonu her zaman `GetLatestSave()` ile en son kaydı açıyordu, kullanıcı hangi kaydı yükleyeceğini seçemiyordu | `SaveListModal` eklendi — tüm kayıtlar tarih/araç sayısıyla listeleniyor, satır bazlı Yükle/Sil butonları var |
| B-03 | Satın alınan dekorasyonlar (`Decorations` listesi) `DrawVehicle()` içinde hiç okunmuyordu, görsel etkisi yoktu | `paint` → parlak sarı kontur, `sticker` → ✨ rozeti, `spoiler` → arka spoiler çizgisi, `lights` → güçlendirilmiş neon glow |
| B-04 | Mağazada yalnızca 1 yükseltme (Hız) ve 1 dekorasyon (Boya) satın alınabiliyordu, model tarafında hazır olan `acceleration/efficiency/durability` ve `sticker/spoiler/lights` UI'a bağlanmamıştı | Hızlı İşlemler paneline 4 yükseltme + 4 dekorasyon butonu eklendi |
| B-05 | Çoklu seçimde (birden fazla araç) Shift+tık ile grup rota/hedef komutu verilemiyordu, sadece tekil `selectedVehicle` rota alabiliyordu | Shift+tık artık `selectedVehicles.Count > 1` durumunda tüm seçili araçlara aynı hedef noktasını atıyor (grup komutu) |
| B-06 | `SoundEngine.PlayCrash()`/`PlayDrift()` hiç çağrılmıyordu (ölü kod) | `PlayCrash` artık `Vehicle.OnWallBounce` event'ine bağlı (duvara sert çarpınca çalıyor), `PlayDrift` grup rota komutunda çalıyor |
| B-07 | `MainWindow.pathVisual` ve `Vehicle.lastStateChange` hiç kullanılmayan ölü alanlardı | Kaldırıldı |
| B-08 | `Resources/`, `Utilities/`, `Views/` klasörleri projede duruyordu ama tamamen boştu | Kaldırıldı |
| B-09 | `Decoration.Use()` aynı dekorasyonu sınırsız kez ekleyebiliyordu (Upgrade'in aksine tekrar kontrolü yoktu) | `Contains` kontrolü eklendi |

**Bilinçli olarak ertelenen maddeler** (gerekçesiyle):
- **Gerçek ses dosyaları** (`SoundEngine` şu an `SystemSounds`/`Console.Beep` kullanıyor) — telifsiz `.wav` asset'i harici olarak indirilmesi gerekir, kullanıcı onayı ve kaynak seçimi gerektirir.
- **Render performans optimizasyonu** (her karede `RenderCanvas.Children.Clear()` + yeniden çizim) — çalışıyor, ama 20+ araçta performans sorunu yaratabilir. Diffing tabanlı bir render motoruna geçmek büyük bir mimari değişiklik olduğundan, mevcut kararlılığı riske atmamak için bu iterasyona alınmadı.

---

*Bu doküman `AutoMotion/REQUIREMENTS.md` olarak saklanmaktadır; yeni özellik eklendikçe ilgili tablo
satırının "Durum" sütunu güncellenmelidir.*
