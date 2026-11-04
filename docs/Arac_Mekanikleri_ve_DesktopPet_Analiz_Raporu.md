# AutoMotion — Araç Mekanikleri ve Desktop Pet Gereksinim/Güzelleştirme Analiz Raporu

> **Tarih:** 2026-07-30
> **Kapsam:** Bu rapor iki eksende odaklanır: **(A) Araç mekanikleri** (fizik, sürüş hissi, tekerlek/
> süspansiyon/görsel detay) ve **(B) Desktop pet uygulaması gereksinimleri** (VPet-Simulator, Shimeji,
> Desktop Goose gibi masaüstü arkadaş yazılımlarının sektör standartları). `REQUIREMENTS.md` genel
> özellik listesini kapsıyordu; bu doküman daha **derinlemesine, "detay ve his" odaklı** bir analizdir ve
> bu oturumda yapılan vites/tekerlek/sürüş iyileştirmelerinin üzerine inşa edilmiştir.

---

## A. Araç Mekanikleri Analizi

### A.1 Mevcut Sistem Özeti

| Katman | Mevcut Durum |
|---|---|
| Hareket fiziği | Yerçekimi + sekme, sürükle-fırlatma, duvar/kenar tırmanma, rota takibi (`CustomPath`) |
| Sürüş modu | 1:1 WASD kontrolü, gerçek vites sistemi (R/N/1-4), vitese göre ivme/tepe hız, tork eğrisi (hıza yaklaştıkça yumuşayan ivme) |
| Vites arayüzü | H-pattern vites kolu (sürüklenebilir topuz, gerçekçi kulvar geçişi) |
| Tekerlek | Yarıçapa göre doğru açısal hız (kaymadan yuvarlanma fiziği), hızla artan hareket bulanıklığı (motion blur), 6 dilimli krom jant deseni |
| Sürüş hissi | Yumuşatılmış gaz/fren girdisi (ani tepki yerine kademeli), vitese bağlı motor freni (düşük vites daha güçlü yavaşlatır), gaz/fren lastik izi (wheelspin + fren kilitlenmesi) |
| Hasar/Bakım | HP, kaza hasarı, tamir kiti, "bozulma" ve çekici (tow) süreci |
| Yakıt | Tüketim, manuel/istasyon doldurma, yakıt biterken sürüşten otomatik çıkış |
| Ekonomi | Yükseltmeler (hız/ivme/verimlilik/dayanıklılık), dekorasyonlar (boya/spoiler/led) |
| Görsel | Procedural Canvas çizimi + gerçek marka görselleri (Araç Listesi), squash/stretch sekme deformasyonu |

**Değerlendirme:** Çekirdek sürüş hissi bu oturumda (vites/tekerlek/motor freni/patinaj) belirgin şekilde
olgunlaştı. Kalan boşluklar çoğunlukla **"ince detay" ve "görsel zenginlik"** katmanında — yani
mekanik iskelet sağlam, üstüne konacak katmanlar eksik.

---

### A.2 Eksik/Geliştirilebilir Mekanikler (Öncelik Sıralı)

| # | Konu | Neden Önemli | Öneri | Öncelik |
|---|---|---|---|---|
| M-01 | **Direksiyon/yatay kontrol (A/D) zeminde işlevsiz** | `ApplyDriveInput`'ta `strafe` parametresi yalnızca helikopter/uçuş modunda kullanılıyor; zeminde A/D tuşları sadece motor sesi tetikliyor, hiçbir fiziksel etkisi yok. Oyuncu "neden basıyorum da bir şey olmuyor" hisseder. | Zeminde A/D'ye küçük bir **dikey sekme/sıçrama** (ör. hız çizgisinde hafif yön değişimi, EdgeSide geçişine yardım) veya en azından görsel bir "direksiyon kırma" tepkisi (gövde hafif eğilme) bağlanmalı. | **P0** |
| M-02 | **Süspansiyon/ağırlık aktarımı (weight transfer) yok** | Gerçek araçlarda hızlanınca arka çöker/ön kalkar, frende ön çöker/arka kalkar (dive/squat). Şu an sadece çarpışma anında `SquashAmount` tetikleniyor; sürüş sırasında ivme/fren asla gövdeyi etkilemiyor. | `ApplyDriveInput` içinde kare-kare ivme farkına (Δhız) bağlı küçük bir gövde eğimi/squash (±3-4°) eklenmesi — "gerçekçilik" hissinin en büyük tek katkısı muhtemelen bu olur. | **P0** |
| M-03 | **Tüm araçlar aynı "sürüş kişiliği"nde** | Vites çarpanları (`GearAccelMult`/`GearMaxSpeedMult`) tüm araç tiplerinde ortak; bir bisiklet ile bir kamyonun sürüş hissi (ivmelenme eğrisi, vites geçiş sertliği) birebir aynı. | Araç tipine göre çarpan setleri (kamyon: yavaş ivme/geç vites geçişi + güçlü motor freni; motosiklet: çok hızlı ivme/dar tepe hız aralığı; bisiklet: vites yok, sabit tempo). | **P1** |
| M-04 | **Direksiyon/dönüş yumuşaklığı yok** (önceki iterasyonda kullanıcı bunu opsiyonel bıraktı) | Yön değişimi anlık (`FacingLeft` bool, ara kare yok) — gerçek bir aracın dönüşte kademeli yön değiştirmesi hissi eksik. | `Angle`/`FacingLeft` arasında kısa bir geçiş (lerp) eklenmesi; ani 180° "teleport" dönüş yerine yumuşak çevrilme. | **P1** |
| M-05 | **Fren lambası / sinyal / far görsel geri bildirimi eksik** | Fren yapınca (`vertical<0`) hiçbir görsel ışık tepkisi yok; sadece lastik izi var. Gerçek araç hissi için en ucuz/etkili detaylardan biri budur. | Fren anında araç arkasında kısa süreli kırmızı "brake light" parıltısı; gece modunda faroların önden huzme koni efekti zaten var (`headlightCone`) — fren lambası eklemek simetrik ve ucuz bir iyileştirme. | **P1** |
| M-06 | **Egzoz dumanı sadece "backfire" ile sınırlı** | Şu an yalnızca maksimum upgrade'li araçta ani gazda nadir bir "patlama" efekti var (`SpawnBackfire`). Sürekli hafif egzoz dumanı (özellikle soğuk havada/yüksek hızda) yok. | Düşük yoğunlukta, hızla orantılı, kısa ömürlü egzoz partikülü — "Efekt Yoğunluğu" ayarına (`EffectsDensityBtn`) bağlanarak performans kontrolü sağlanabilir. | **P2** |
| M-07 | **Kaymadan yuvarlanma fiziği tekerlek görselinde var ama zemin sürtünmesi tekil** | Bu oturumda tekerlek açısı yarıçapa göre düzeltildi (✅), ama zemin türüne göre sürtünme/tutuş (asfalt/ıslak/toprak) farkı yok — `IsWet` sadece genel sürtünme çarpanını (0.98→0.994) etkiliyor, tekerlek görselinde kayma/patinaj farkı görünmüyor. | Islak zeminde patinaj mesafesi/ihtimali artırılabilir (gerçekçi "ıslak zeminde tutuş azalır" hissi). | **P2** |
| M-08 | **Vites geçişi sırasında güç kesintisi yok** | Gerçek bir araçta vites değiştirirken (özellikle manuel) çok kısa bir güç kesintisi/tekleme hissedilir. Şu an vites değişimi anlık ve kesintisiz uygulanıyor. | `SetGear()` çağrıldığında ~150-250ms süreyle `appliedAccel`'i geçici olarak sıfıra yakın tutan bir "clutch" katsayısı. | **P2** |
| M-09 | **Sabit tepe hız duvarı, yumuşak limitleyici yok** | `gearMaxSpeed` aşılınca hız vektörü doğrudan kırpılıyor (`speed>gearMaxSpeed` → clamp) — sert bir "duvara çarpma" hissi verebilir. | Tepe hıza yaklaşırken de (torqueCurve zaten var) son %5'lik dilimde yumuşak bir sönümleme eklenmesi. | **P3** |
| M-10 | **Çekiş/dorse fiziği basit** | Dorse takılı araçlarda (kamyon/traktör) ek kütle/ivme cezası var mı kontrol edilmeli; gerçekçilik için yüklü dorse ivmeyi ve fren mesafesini belirgin şekilde artırmalı (zaten `HasCargo` için `EffectiveAcceleration *0.55` var, dorse için ayrı bir çarpan yok gibi görünüyor). | `TrailerType` dolu olduğunda ek `EffectiveAcceleration` cezası ve fren mesafesi artışı. | **P3** |

---

### A.3 Görsel Güzelleştirme (Polish) Önerileri — Düşük Riskli, Yüksek Etkili

Bunlar mekanik değişiklik gerektirmez, sadece `DrawVehicle`/`BuildWheelVisual` çizim katmanına ek
detaylardır — mevcut mimariyi bozma riski düşüktür:

1. **Fren lambası parıltısı** (bkz. M-05) — en ucuz, en fark edilir iyileştirme.
2. **Süspansiyon görsel seyri** — tekerlek merkezinin gövdeye göre çok küçük (1-2px) dikey oynaması,
   büyük çukur/rampa geçişlerinde belirginleşerek "esneyen süspansiyon" hissi.
3. **Kir/toz birikimi tekerlekte de gösterilsin** — şu an `Dirtiness` sadece gövdede işleniyor;
   gerçekte tekerlek/jant kiri gövdeden çok daha hızlı ve görünür birikir.
4. **Plaka/detay çıkartmaları** — küçük bir plaka dikdörtgeni (opsiyonel, marka görsellerinde zaten
   olabilir, procedural çizimde yok).
5. **Cam yansıması** — özellikle gece/gündüz temasına göre ön cam üzerinde ince bir gradyan parıltısı.
6. **Kış/kar teması için kar zinciri veya buğulu egzoz** — mevcut `isRaining` sistemine simetrik bir
   "kar" durumu eklenirse (Ortam & Olaylar panelinde zaten hava durumu var), tekerlek izi kardan
   farklı (beyaz iz) çizilebilir.

---

## B. Desktop Pet Uygulaması Gereksinim Analizi

### B.1 Mevcut Güçlü Yönler

AutoMotion, kategori standartlarına göre zaten **olgun** sayılabilecek özelliklere sahip:

- Şeffaf, `Topmost`, sistem tepsisi (tray) simgesi + sağ tık menüsü
- Yakıt/HP/Mutluluk gibi "bakım gerektiren canlı varlık" mekanikleri (VPet'in açlık/mutluluk döngüsüne
  paralel)
- Aktif pencere üzerinde "tırmanma" (window clambering) — Shimeji/VPet'in imza özelliği
- **Müzik-senkronize süspansiyon sekmesi** (`UpdateMusicSync`) — sistem sesine tepki veren araçlar,
  incelenen referans uygulamalarda (VPet, Shimeji, Desktop Goose) **rastlanmayan özgün bir özellik**,
  öne çıkarılmaya değer bir farklılaştırıcı.
- Kaydetme/yükleme, çoklu kayıt listesi, achievement sistemi
- Panel bazlı, sürüklenebilir/sabitlenebilir, katlanabilir arayüz (Rainmeter tarzı esneklik)

### B.2 Eksik/Geliştirilebilir Sektör Standartları

| # | Konu | Referans | Mevcut Durum | Öneri | Öncelik |
|---|---|---|---|---|---|
| D-01 | **Gerçek zamanlı (uygulama kapalıyken de işleyen) ihtiyaç döngüsü** | VPet'in açlık/enerji sistemi, uygulama kapalıyken bile gerçek saate göre azalır; tekrar açılınca "beni özledin mi" hissi verir. | `Happiness`/`Fuel` sadece `GameCore.Update()` her karede (uygulama açıkken) azalıyor; kapalıyken zaman donuyor. | Kaydetme zaman damgası (`GameData.LastSave`) zaten var — yüklerken geçen gerçek süreye göre yakıt/mutluluk "yokluk cezası" uygulanabilir (VPet'in imza mekaniği). | **P0** |
| D-02 | **İlk çalıştırma / onboarding akışı yok** | Çoğu masaüstü pet uygulaması ilk açılışta kısa bir tanıtım/kısayol listesi gösterir. | Kısayol listesi sadece Vites panelinde (sürüş modunda) görünüyor; genel kısayollar (T, M, R, C, S, L, E, V, K, A, G) hiçbir yerde kalıcı gösterilmiyor — kullanıcı keşfedene kadar bilmiyor. | İlk açılışta kapatılabilir bir "Hoş Geldin" kartı + `?` yardım butonu ile her an açılabilen kısayol referansı. | **P0** |
| D-03 | **"Uyku modu" / düşük CPU kullanımı yok** | VPet gece geç saatte pet'i "uyutur", performans ayarı sunar; Desktop Goose da uzun süre etkileşimsizlikte pasifleşir. | Gece modu (`NightModeBtn`) sadece görsel tema değiştiriyor, oyun döngüsü hızı/CPU kullanımı sabit. | Uzun süre (ör. 10dk) fare etkileşimi yokken kare hızını düşürüp CPU/pil tüketimini azaltan bir "boşta" modu. | **P1** |
| D-04 | **Windows ile otomatik başlatma seçeneği yok** | Sektör standardı (VPet, Rainmeter dahil hemen hemen tüm masaüstü araçları) bir "başlangıçta çalıştır" seçeneği sunar. | Uygulama içinde bu ayar bulunmuyor (araştırılmadı ama koşullu — kontrol edilmeli). | Ayarlara `HKCU\...\Run` kaydı ile aç/kapa seçeneği. | **P1** |
| D-05 | **Konuşma balonu / bildirim sistemi kısmi** | VPet'in `SayInfo` sistemi zengin, bağlama duyarlı diyaloglar sunar. | `ShowToast` genel bildirimler için var, ama araca özgü "konuşma balonu" (ör. düşük yakıtta aracın kendisi "acıktım" der gibi) sınırlı/rastgele. | Araç tipine özgü, mizahi, zamanlamalı diyalog havuzu (VPet'in imza deneyimi). | **P1** |
| D-06 | **Çoklu monitör "geçiş" var ama monitör-özel davranış yok** | `MultiMonitorBtn` (Çoklu Monitör Gezinme) mevcut, ama her monitörün kendi taban çizgisi/görev çubuğu farkına göre davranış ayarı belirsiz. | Kısmen mevcut, derinlik test edilmeli. | Her monitör için ayrı "zemin" tanımı + monitörler arası yürüyerek geçiş animasyonu. | **P2** |
| D-07 | **Erişilebilirlik (screen reader / klavye-only) düşünülmemiş** | Modern masaüstü uygulamalarında (özellikle Windows mağazası standartları) `AutomationProperties.Name` gibi etiketler beklenir. | İkon butonlarda sadece emoji + tooltip var, ekran okuyucu için `AutomationProperties.Name` eksik olabilir. | Kritik butonlara `AutomationProperties.Name` eklenmesi (düşük efor, erişilebilirlik puanı yüksek). | **P2** |
| D-08 | **Paylaşım/topluluk özelliği yok** | VPet'in Steam Workshop MOD paylaşımı; Desktop Goose'un GIF/ekran görüntüsü paylaşım kültürü. | `ExpansionManager` altyapısı var ama harici mod indirme/paylaşma arayüzü yok. | "Ekran görüntüsü al" veya "GIF kaydet" butonu (basit, dış bağımlılık gerektirmez) ilk adım olabilir. | **P3** |
| D-09 | **Tema galerisi sınırlı** | `REQUIREMENTS.md` zaten bunu not etmiş (madde 3) — VPet'in geniş tema/skin galerisine kıyasla AutoMotion'da az sayıda hazır tema var. | Mevcut. | 3-4 hazır tema (gündüz/gece/yağmurlu/kış) — zaten planlanmış, önceliklendirilebilir. | **P2** |

### B.3 Güzelleştirme (Polish) Önerileri — Desktop Pet Katmanı

1. **Kısayol referans paneli** (D-02) — tek bir `?` butonuyla her an açılabilen, 8-bit fontla
   tutarlı, katlanabilir bir "Kısayollar" paneli; bu oturumda font/panel sistemleri zaten olgunlaştı,
   üzerine eklemek düşük maliyetli.
2. **Gerçek zaman "özledim" mekaniği** (D-01) — tek bir hesaplama (`DateTime.Now - LastSave`) ile
   büyük bir "yaşıyor" hissi katar; VPet'in en çok konuşulan özelliklerinden biridir.
3. **Fren lambası + süspansiyon** (A bölümü M-02/M-05) — iki küçük görsel detay, sürüş hissini
   orantısız derecede iyileştirir; bu oturumdaki vites/tekerlek işinin doğal devamı.
4. **Müzik-senkron özelliğinin öne çıkarılması** — bu, rakip uygulamalarda bulunmayan bir özellik;
   pazarlama/tanıtım açısından (ör. `AutoMotion_Proje_Raporu.md`'de) vurgulanmalı, belki ayarlarda
   hassasiyet kaydırıcısı eklenerek derinleştirilmeli.

---

## C. Öncelik Matrisi (Özet)

| Öncelik | Araç Mekanikleri | Desktop Pet |
|---|---|---|
| **P0 (Sıradaki iterasyon)** | Ağırlık aktarımı (dive/squat), A/D zeminde işlevsiz sorunu | Gerçek-zaman ihtiyaç döngüsü, kısayol/onboarding paneli |
| **P1** | Araç tipine özgü sürüş kişiliği, dönüş yumuşaklığı, fren lambası | Uyku modu, Windows'la başlatma, zengin konuşma balonları |
| **P2** | Egzoz dumanı, zemin türüne göre tutuş farkı, vites geçiş kesintisi | Monitör-özel davranış, erişilebilirlik etiketleri, tema galerisi |
| **P3** | Yumuşak tepe hız limiti, dorse ağırlık cezası | Paylaşım/GIF özelliği |

---

## D. Kapanış

Bu oturumda **araç mekanikleri** tarafında sürüş hissinin temel iskeleti (vites, tekerlek fiziği, motor
freni, patinaj) büyük ölçüde olgunlaştırıldı — kalan iş büyük ölçüde **"his katmanı"** (ağırlık
aktarımı, fren lambası, direksiyon yumuşaklığı) ve araç-tipi farklılaşmasıdır. **Desktop pet**
tarafında ise iskelet (şeffaf pencere, tray, kaydetme, panel sistemi, bakım mekanikleri) sağlam;
eksik olan, sektörün "imza" sayılan birkaç özelliği (gerçek-zaman özlem döngüsü, onboarding, uyku
modu) ve küçük ama etkili cila detaylarıdır. Her iki eksende de **P0 maddeleri düşük efor/yüksek
etki** oranına sahiptir ve bir sonraki iterasyon için doğal aday listesidir.

*Bu doküman `AutoMotion/Arac_Mekanikleri_ve_DesktopPet_Analiz_Raporu.md` olarak saklanmaktadır.*

---

## E. Uygulama Durumu (2026-07-30 — "hepsine bir el at" iterasyonu)

Bu raporda listelenen maddelerin büyük çoğunluğu bu iterasyonda koda işlendi:

| # | Madde | Durum |
|---|---|---|
| M-01 | Zeminde A/D artık işlevsiz değil | ✅ Hafif viraj/lean efekti eklendi (`ApplyDriveInput` → `steerLean`) |
| M-02 | Ağırlık aktarımı (dive/squat) | ✅ `DriveTilt` alanı, hız değişimine göre ±4.5° gövde eğimi |
| M-03 | Araç tipine göre sürüş kişiliği | ✅ Ağır (kamyon/otobüs/traktör/tren) ve sportif (motosiklet/bisiklet) profilleri ayrıştırıldı |
| M-04 | Dönüşte yumuşak geçiş | ⚪️ **Bilinçli olarak ertelendi** — 2D aynalanmış sprite'ı "yarı çevirmek" gerçekçi bir 3D ara kare olmadan görsel bozulmaya (gerilme/deformasyon) yol açar; mevcut eşik tabanlı ani çevirme (`VelocityX` ±0.3 histerezis) korunuyor |
| M-05 | Fren lambası | ✅ Sert frende kırmızı parıltı eklendi |
| M-06 | Egzoz dumanı | ✅ Hızlı giden tüm araçlarda (motosiklet/bisiklet hariç) seyrek duman parçacığı |
| M-07 | Islak zeminde tutuş | ✅ Islak zeminde patinaj eşiği düşürüldü (daha kolay kayma) |
| M-08 | Vites geçişinde güç kesintisi | ✅ `GearShiftCooldown` (~14 kare) eklendi |
| M-09 | Yumuşak tepe hız limiti | ✅ Sert kırpma yerine kademeli (lerp) sınırlama |
| M-10 | Dorse ağırlık cezası | ✅ Dorse takılıyken ivme %35 azalıyor |
| D-01 | Gerçek-zaman özlem döngüsü | ✅ `GameCore.Load` artık geçen gerçek süreye göre yakıt/mutluluk cezası uyguluyor + yükleme sonrası bildirim |
| D-02 | Onboarding/kısayol paneli | ✅ Üst barda ❓ butonu + kalıcı Kısayollar modalı + ilk çalıştırmada tek seferlik karşılama bildirimi |
| D-03 | Boşta düşük CPU modu | ✅ 5dk hareketsizlikte kare hızı 60→~15fps'e düşüyor, herhangi bir etkileşimde anında normale dönüyor |
| D-04 | Windows ile başlatma | ✅ Tepsi menüsüne "Windows ile Başlat" onay kutusu eklendi (Registry `Run` anahtarı) |
| D-05 | Zengin konuşma balonları | ⚪️ Ertelendi — mevcut `ShowToast`/başarım bildirimleri korunuyor, araç-tipine-özgü diyalog havuzu ayrı bir içerik-yoğun iterasyon gerektiriyor |
| D-06 | Monitör-özel davranış | ⚪️ Ertelendi — mevcut `MultiMonitorBtn` altyapısı test edilmeden derinleştirmek riskli |
| D-07 | Erişilebilirlik etiketleri | ✅ Tüm ikon butonlara (`IconButton`/`IconButtonClose`) `AutomationProperties.Name` eklendi (49 buton) |
| D-08 | Paylaşım/GIF | ⚪️ Ertelendi — kapsam dışı, ayrı bir özellik olarak ele alınmalı |
| D-09 | Tema galerisi genişletme | ⚪️ Ertelendi — içerik üretimi (yeni tema paletleri) ayrı bir iterasyon |

**Not:** Bu iterasyonda ayrıca, önceki oturumda kaldırılan "sticker" (✨ parıltı) görsel efekti nedeniyle
işlevsiz kalmış olan **başarım ödül dekorasyonu** fark edildi ve "paint" (parlak kontur) ile
değiştirilerek düzeltildi — başarım kazanmak artık yine görsel bir karşılık veriyor.

Derleme hatasız geçti, uygulama test edildi ve kararlı çalıştı; kök `AutoMotion.exe` güncellendi.
