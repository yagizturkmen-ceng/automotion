using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AutoMotion.Utilities;

namespace AutoMotion.Models
{
    /// <summary>
    /// Vehicle - Sanal araç (VPet'deki Pet yerine)
    /// </summary>
    public class Vehicle
    {
        // Pozisyon ve hareket
        public double X { get; set; }
        public double Y { get; set; }
        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public double Angle { get; set; }

        // Araç özellikleri
        public string Type { get; set; }
        public string Emoji { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Color { get; set; }
        public double MaxSpeed { get; set; }
        public double Acceleration { get; set; }

        /// <summary>Kargo taşırken ivmelenme düşer - yük hissi verir (Kargo Ağırlık Fiziği).</summary>
        public double EffectiveAcceleration => HasCargo ? Acceleration * 0.55 : Acceleration;

        // Ağırlık (kütle) - gerçekçi fizik hissi için: sekme sönümlemesi, çarpışma tepkisi ve
        // fırlatma/atma davranışı bu değere göre ölçeklenir (ağır araç daha az zıplar, daha az fırlar,
        // çarpışmada hafif aracı iter, kendisi neredeyse hiç itilmez).
        public double Mass { get; set; } = 1.0;

        // Gerçek marka görseli (Araç Listesi entegrasyonu) ve tekerlek dönüş animasyonu
        public string Brand { get; set; } = "";

        /// <summary>Kümülatif kat edilen mesafe (piksel, işaretli) - render sırasında her tekerleğin
        /// KENDİ yarıçapına bölünerek gerçek "kaymadan yuvarlanma" (rolling without slipping) açısı
        /// hesaplanır. Böylece büyük tekerlekler (kamyon) aynı gövde hızında küçük tekerleklerden
        /// (motosiklet) daha yavaş döner - tek bir sabit çarpanla tüm tekerlekleri aynı hızda
        /// döndürmek yerine fiziksel olarak doğru bir his verir.</summary>
        public double WheelTravelDistance { get; set; }
        public bool FacingLeft { get; set; }

        /// <summary>FacingLeft'in render'daki karşılığı olan yatay ayna ölçeği (-1..1) - anlık bir
        /// -1/1 sıçraması yerine her karede hedefe doğru yumuşakça kayar. Böylece yön değişince araç
        /// aniden "ışınlanıp" ters dönmüyor, kısa ama akıcı bir geçişle çevriliyor.</summary>
        public double MirrorScaleX { get; set; } = -1;

        /// <summary>EdgeSide'a karşılık gelen gövde açısının (0/90/-90/180) yumuşatılmış hali - köşe
        /// dönüşlerinde (zemin→duvar→tavan) ani 90°'lik sıçrama yerine akıcı bir çevrilme sağlar.</summary>
        public double WallAngleSmoothed { get; set; }

        /// <summary>0 = zemin/tavan gibi (yatay) yönelim, 1 = duvar gibi (dikey) yönelim. WallAngleSmoothed
        /// ile aynı hızda hedefe kayar; köşe geçişinde HalfX/HalfY'nin ANINDA (Width/2 ↔ Height/2) yer
        /// değiştirmesi yerine bu değere göre kademeli karışır. Eskiden konum köşeye anlık "oturuyordu" -
        /// gövde 90° dönerken pozisyon zaten yeni kenarın (daha dar) yarı-genişliğine sıçrıyor, bu da
        /// aracın ekranın içine doğru ani bir adım atmış gibi görünmesine sebep oluyordu.</summary>
        public double CornerBlendSmoothed { get; set; } = -1;

        /// <summary>Sürüş sırasındaki gövde eğimi (derece) - hızlanma/frenleme kaynaklı "ağırlık
        /// aktarımı" (dive/squat) ve zeminde A/D ile viraj alırken oluşan hafif yatık duruşun
        /// toplamıdır. Sadece render katmanında `Rotation`'a eklenir, fizik/hıza dokunmaz.</summary>
        public double DriveTilt { get; set; }
        private double prevSpeedForTilt;

        /// <summary>Vites değiştirildiğinde MainWindow tarafından kısa bir süreliğine (kare sayısı)
        /// ayarlanır - bu süre boyunca motor gücü neredeyse kesilir (gerçek bir manuel şanzımanda
        /// debriyaj/vites değişimi sırasında hissedilen kısa güç kesintisi).</summary>
        public double GearShiftCooldown { get; set; }

        // Ekran kenarına tırmanma (kenar "yermiş" gibi davranır): 0=zemin, -1=sol duvar, 1=sağ duvar
        public int EdgeSide { get; set; } = 0;
        private double wanderTimer;
        private int wanderDir = 1;

        /// <summary>Bir duvar/tavan köşesinde ne kadar süredir (saniye) neredeyse hareketsiz kaldığını
        /// sayar - kenar geçiş eşiklerinin (ör. büyük/uzun araçlarda) hiç tutmayıp aracın köşede sonsuza
        /// kadar sıkışıp kalmasına karşı bir güvenlik ağı. Zemindeyken (EdgeSide==0) sıkışma sayılmaz,
        /// orada zaten AI serbestçe hareket edebiliyor.</summary>
        private double cornerStuckTimer;

        /// <summary>Bir EdgeSide (zemin/duvar/tavan) geçişinden sonra kısa bir süre yeni bir geçişi
        /// engeller. Eşiklerin hemen ötesinde (ör. tam köşede, iki araç birbirini itiştirirken veya
        /// yüksek FPS'te tek karede eşiği aşıp geri dönen büyük bir dt ile) EdgeSide her karede 0↔-1
        /// gibi ileri geri "flip-flop" yapabiliyordu - bu da WallAngleSmoothed hedefinin her karede
        /// 0°↔90° arasında zıplamasına, dolayısıyla aracın köşede hızlıca "dönüyor/takla atıyormuş"
        /// gibi görünmesine sebep oluyordu.</summary>
        private double edgeTransitionCooldown;

        /// <summary>Bir kenar geçişi başladığı anki WallAngleSmoothed değeri ve hedefe olan (en kısa
        /// yoldan hesaplanmış) toplam açı farkı - gövdenin dönüşü artık kendi ayrı zaman sabitiyle
        /// lerp'lenmiyor, doğrudan CornerBlendSmoothed'in (aracın gövdesinin ne kadarının diğer
        /// kenara "geçtiğinin" ölçüsü - ör. Width/2↔Height/2 boyut karışımında da kullanılan aynı
        /// değer) ilerleme yüzdesine bağlanıyor: gövde %70 diğer kenara geçtiyse ön taraf da tam
        /// olarak %70 oranında o kenarın açısına dönmüş olur.</summary>
        private double wallAngleFrom;
        private double wallAngleDeltaTotal;

        // Boyut ölçeği (büyüt/küçült özelliği) - değiştirildiğinde Width/Height'ı temel
        // boyutlardan yeniden hesaplar, böylece fizik/çizim kodunun geri kalanı hiç
        // değişmeden ölçeklenmiş boyutları kullanmış olur.
        private int baseWidth;
        private int baseHeight;
        /// <summary>Varsayılan doğuş büyüklüğü artık %200 - eskiden %100'de başlayan araçlar
        /// masaüstünde çok küçük/fark edilmez kalıyordu.</summary>
        public const double DefaultScale = 2.0;
        private double _scale = DefaultScale;
        public double Scale
        {
            get => _scale;
            set
            {
                _scale = Math.Clamp(value, 0.2, 4.0);
                if (baseWidth > 0)
                {
                    Width = Math.Max(6, (int)Math.Round(baseWidth * _scale));
                    Height = Math.Max(6, (int)Math.Round(baseHeight * _scale));
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // EZİLME / ESNEME (squash & stretch)
        //
        // Araç yere değdiği an yayvanlaşır (enine genişler, boyuna basılır), sonra YAYLANARAK
        // toparlanır - yumuşak bir top ya da oyun hamuru gibi. Toparlanırken hedefi bir miktar aşar,
        // yani kısa bir an ince-uzun kalır; "canlı" hissini veren asıl ayrıntı bu aşma.
        //
        // Pozitif = ezilmiş (yayvan), NEGATİF = gerilmiş (ince-uzun). Eskiden değer yalnızca
        // pozitifti ve üstel sönümle sıfıra iniyordu: araç eziliyor ama geri yaylanmıyordu.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Çarpma anında ulaşılabilecek en yüksek ezilme. Yüksek tutuluyor: kullanıcı
        /// isteği "yumuşak top gibi belirgin genişlesin".</summary>
        private const double MaxSquash = 0.85;

        /// <summary>Bu hızın üstündeki çarpmalar tam ezilme üretir (piksel/kare).</summary>
        private const double FullSquashSpeed = 9.0;

        /// <summary>Yayın sertliği - toparlanma süresini belirler (~0.5 sn).</summary>
        private const double SquashStiffness = 0.06;

        public double SquashAmount { get; set; } = 0;

        private double squashVelocity;

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // DURUM DEĞİŞİMİ GERİ BİLDİRİMİ (3.4 / A-6)
        //
        // Duygu rozeti (😢/😰/🤕) yalnızca fare aracın üzerindeyken görünüyordu; yani araç
        // yakıtsız kaldığı ya da ağır hasar aldığı AN fark edilmiyordu - kullanıcı ancak tesadüfen
        // üzerine gelirse görüyordu. Rozeti kalıcı yapmak da istenmiyor (her araçta sürekli duran
        // rozet ekranı dağıtıyor, bilinçli olarak kaldırılmıştı).
        //
        // Çözüm ikisinin arası: durum DEĞİŞTİĞİ anda rozet kısa süre belirip solar. Böylece geçiş
        // fark edilir ama kalıcı gürültü olmaz.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Durum değişiminden sonra rozetin görünürlüğü (1 = yeni değişti, 0 = solmuş).</summary>
        public double MoodPulse { get; private set; }

        /// <summary>Aracın en son bilinen hâli - değişimi yakalamak için.</summary>
        public MoodState CurrentMood { get; private set; } = MoodState.Normal;

        /// <summary>Rozetin görünür kalma süresi (60 FPS'te kare). ~2.5 saniye: fark edilecek
        /// kadar uzun, rahatsız edecek kadar değil.</summary>
        private const double MoodPulseFrames = 150;

        private void UpdateMood(double dt)
        {
            var mood = Mood.Classify(Happiness, Fuel, MaxFuel, HP, MaxHP);

            if (mood != CurrentMood)
            {
                CurrentMood = mood;
                // Yalnızca KÖTÜ bir duruma geçince rozet çıkar; düzelme sessizce olur (araç
                // iyileştiğinde ekranda rozet patlaması olması gereksiz gürültü olurdu).
                MoodPulse = mood == MoodState.Normal ? 0 : 1;
            }
            else if (MoodPulse > 0)
            {
                MoodPulse = Math.Max(0, MoodPulse - dt / MoodPulseFrames);
            }
        }

        /// <summary>Zemine/duvara çarpma anında ezilmeyi başlatır. Çarpma hızı ne kadar yüksekse
        /// araç o kadar yayvanlaşır.</summary>
        private void ApplyGroundSquash(double impactSpeed)
        {
            double target = Math.Clamp(impactSpeed / FullSquashSpeed, 0, 1) * MaxSquash;

            // Zaten eziliyorsa daha güçlü olanı kazanır (arka arkaya sekmelerde titremesin).
            if (target > SquashAmount)
            {
                SquashAmount = target;
                squashVelocity = 0;
            }
        }

        /// <summary>Ezilmeyi yay-sönümleyici ile normale döndürür: sıfıra doğru çekilir, hedefi
        /// bir miktar aşar (kısa bir esneme) ve sönerek durur. Kargo taşıyan araç daha ağır/hantal
        /// toparlanır.</summary>
        private void UpdateSquashSpring(double dt)
        {
            if (SquashAmount == 0 && squashVelocity == 0) return;

            squashVelocity += -SquashStiffness * SquashAmount * dt;
            squashVelocity *= Damp(HasCargo ? 0.90 : 0.85, dt);
            SquashAmount += squashVelocity * dt;

            // Yeterince sakinleştiyse tam olarak sıfıra otur (sonsuza kadar titremesin).
            if (Math.Abs(SquashAmount) < 0.005 && Math.Abs(squashVelocity) < 0.005)
            {
                SquashAmount = 0;
                squashVelocity = 0;
            }
        }
        public double SquashAxisIsVertical { get; set; } = 1; // 1: dikey eksende sekme (zemin), 0: yatay eksende (duvar)

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // GÖRSEL CANLILIK ANİMASYONLARI
        //
        // Hepsi SADECE render katmanını etkiler - fizik/hız/çarpışma hesaplarına hiç dokunmazlar.
        // Bu ayrım bilinçli: bir animasyonun yanlış ayarlanması oyunun oynanışını asla bozmamalı.
        // Tümü kare hızından bağımsız ilerler (bkz. Utilities/Easing.cs).
        // ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Bu araç tipinin motoru var mı? (bisiklet hariç hepsi) - rölanti titreşimi,
        /// egzoz ve motor sesi bu bayrağa bakar.</summary>
        public bool HasEngine => Type != "bike";

        /// <summary>Rölanti titreşimi: motor çalışırken duran aracın gövdesinde hissedilen küçük
        /// dikey titreme (piksel). Duran araç eskiden TAMAMEN hareketsizdi ve bu onu "ölü bir resim"
        /// gibi gösteriyordu - masaüstü pet türünde canlılık hissinin en büyük tek kaynağı budur.
        /// Ağır dizel araçlarda (kamyon/otobüs/traktör/tren) daha yavaş ve daha belirgin,
        /// otomobil/motosikletde daha hızlı ve ince titrer.</summary>
        public double IdleShakeOffset { get; private set; }
        private double idlePhase;
        private double idleIntensity;

        /// <summary>Süspansiyon sıkışması (piksel, pozitif = gövde aşağı çökmüş). Zemine inişte
        /// darbeyle sıkışır, yay-sönümleyici ile geri açılır. SquashAmount tüm gövdeyi ezerken bu
        /// yalnızca gövdeyi tekerlekler üzerinde alçaltıp yükseltir - gerçek bir süspansiyon gibi.</summary>
        public double SuspensionOffset { get; private set; }
        private double suspensionVelocity;

        /// <summary>Doğuş (spawn) animasyonu ilerlemesi 0→1. Araç eskiden aniden beliriyordu;
        /// artık kısa bir ölçek+opaklık girişiyle "yaylanarak" sahneye giriyor.</summary>
        public double SpawnProgress { get; private set; }
        private const double SpawnDurationFrames = 22; // ~0.37 sn @60 FPS

        /// <summary>Kayıttan yüklenen araçlarda doğuş animasyonunu atlamak için (oyun zaten
        /// devam eden bir duruma geri dönüyor, her araç yeniden "doğmuş" gibi görünmemeli).</summary>
        public void SkipSpawnAnimation() => SpawnProgress = 1.0;

        /// <summary>Dönüş sinyali: -1 sol, 0 kapalı, +1 sağ. Sürüşte direksiyon girdisinden,
        /// AI'da yön değişiminden set edilir.</summary>
        public int SignalDirection { get; private set; }
        private double signalHoldTimer;
        private double signalBlinkPhase;

        /// <summary>Sinyal lambasının o anki yanık/sönük durumu (yanıp sönme fazı).</summary>
        public bool SignalLampOn { get; private set; }

        /// <summary>Geri vites lambası (beyaz) - gerçek araçlarda olduğu gibi yalnızca geri viteste
        /// ve gerçekten geri hareket ederken yanar.</summary>
        public bool IsReversing => IsDriven && Gear == -1 && !IsDragged;

        /// <summary>Ağır hasarlı araç motor bölmesinden duman çıkarır - HP barı yalnızca fare
        /// üzerine gelince göründüğü için, hasarın uzaktan da fark edilmesini sağlayan görsel ipucu.</summary>
        public bool IsSmoking => !IsDestroyed && HP > 0 && HP < MaxHP * 0.35;

        /// <summary>Bir dönüş sinyalini dışarıdan tetikler (sürüş girdisi / AI yön değişimi).</summary>
        public void TriggerSignal(int direction)
        {
            if (direction == 0) return;
            SignalDirection = Math.Sign(direction);
            signalHoldTimer = 55; // ~0.9 sn boyunca yanıp söner
        }

        /// <summary>Tüm görsel animasyon durumlarını bir kare ilerletir. Update() sonunda,
        /// fizik tamamlandıktan sonra çağrılır - böylece hız/konum değerleri o karenin nihai
        /// haliyle okunur.</summary>
        private void UpdateVisualAnimationState(double dt, bool airborne)
        {
            // ── Doğuş animasyonu
            if (SpawnProgress < 1.0)
                SpawnProgress = Math.Min(1.0, SpawnProgress + dt / SpawnDurationFrames);

            double speed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);

            // ── Rölanti titreşimi
            // Yalnızca motor çalışırken (yakıt var), araç zeminde ve neredeyse dururken hissedilir.
            // Hareket halindeyken zaten süspansiyon/tekerlek animasyonu devrede olduğundan kapanır.
            bool engineIdling = HasEngine && Fuel > 0 && !IsDragged && !airborne
                                && EdgeSide == 0 && speed < 0.7;

            idleIntensity = Easing.Approach(idleIntensity, engineIdling ? 1.0 : 0.0, 0.12, dt);

            if (idleIntensity > 0.001)
            {
                // Ağır dizel: yavaş/güçlü titreşim. Hafif benzinli: hızlı/ince titreşim.
                double freq = IsHeavyVehicle ? 0.85 : 1.35;
                double amplitude = Height * (IsHeavyVehicle ? 0.030 : 0.018);

                idlePhase += freq * dt;
                if (idlePhase > 1e6) idlePhase = 0;

                // İki farklı frekansın toplamı tek bir sinüsten daha "mekanik"/düzensiz hissettirir.
                double wave = Math.Sin(idlePhase) * 0.75 + Math.Sin(idlePhase * 2.7) * 0.25;
                IdleShakeOffset = wave * amplitude * idleIntensity;
            }
            else
            {
                IdleShakeOffset = 0;
            }

            // ── Süspansiyon yayı (hedef her zaman 0 = dinlenme konumu; darbeler hızı iter)
            Easing.Spring(ref suspensionOffsetBacking, ref suspensionVelocity, 0.0,
                          stiffness: 0.22, damping: 0.14, dt);
            suspensionOffsetBacking = Math.Clamp(suspensionOffsetBacking, -Height * 0.35, Height * 0.35);
            SuspensionOffset = suspensionOffsetBacking;

            // ── Dönüş sinyali yanıp sönmesi
            if (signalHoldTimer > 0)
            {
                signalHoldTimer -= dt;
                signalBlinkPhase += dt;
                // ~0.45 sn yanık / 0.45 sn sönük (gerçek bir sinyal flaşörü ritmi)
                SignalLampOn = (signalBlinkPhase % 54.0) < 27.0;
            }
            else
            {
                SignalDirection = 0;
                SignalLampOn = false;
                signalBlinkPhase = 0;
            }
        }

        private double suspensionOffsetBacking;

        /// <summary>Zemine/duvara inişte süspansiyonu sıkıştırır (darbe şiddetiyle orantılı).
        /// Squash/stretch ile aynı anda tetiklenir ama farklı bir şey anlatır: squash gövdenin
        /// esnemesi, bu ise gövdenin tekerlekler üzerine çökmesidir.</summary>
        private void ApplySuspensionImpact(double impactSpeed)
        {
            // Ağır araç aynı hızda daha çok çöker ama daha yavaş geri açılır (yay sabiti aynı,
            // itiş kütleyle ölçekli).
            double push = Math.Clamp(impactSpeed * 0.30 * Math.Sqrt(Mass), 0, 9);
            suspensionVelocity += push;
        }

        // Sağlık ve yakıt
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public double Fuel { get; set; }
        public double MaxFuel { get; set; }
        public double FuelConsumption { get; set; } = 0.1;
        public double Mileage { get; set; }

        /// <summary>Lastik aşınması: 0 (yeni) - 100 (tamamen aşınmış). Kat edilen mesafeyle birlikte
        /// yavaşça artar, aracı hafifçe yavaşlatır (kayganlık hissi). RepairKit kullanılınca sıfırlanır
        /// (bkz. Models/Item.cs RepairKit.Use).</summary>
        public double TireWear { get; set; }

        // Durum
        public double Happiness { get; set; } = 100;
        public string State { get; set; } = "wandering"; // wandering, chasing, fleeing
        public bool IsClicked { get; set; }
        /// <summary>Tıklama animasyonunun ilerlemesi - 60 FPS karesi cinsinden (kesirli olabilir).</summary>
        public double ClickTime { get; set; }
        public double Rotation { get; set; }
        public int AnimationFrame { get; set; }

        // Sürükleme ve VPet Fizik Özellikleri
        public bool IsDragged { get; set; }
        public double DragRotation { get; set; }
        public double DragLastX { get; set; }
        public double DragLastY { get; set; }

        /// <summary>Sürüklerken Ctrl+fare tekerleği ile kullanıcının manuel ayarladığı ek döndürme
        /// (derece) - aracı düşürmeden önce yönünü elle düzeltebilmesi için (bkz. MainWindow
        /// Canvas_MouseWheel). Her yeni sürükleme başladığında sıfırlanır.</summary>
        public double DragManualRotation { get; set; }

        /// <summary>Detaylı Yıkama: zamanla yavaşça artar (0-1), araç üzerinde çamur lekesi olarak
        /// görünür; "W" tuşu basılıyken fareyle üzerine gelinip sürülünce azalır (sünger hareketi).</summary>
        public double Dirtiness { get; set; }

        // Uçuş Modu ve Kilit (Kullanılabilirlik Özellikleri)
        public bool IsFlying { get; set; }
        public bool IsLocked { get; set; }

        /// <summary>Sanal Otopark: kilitlenen araçlar rastgele durdukları yerde donmak yerine bu
        /// konuma (varsa) yumuşakça kayarak nizami şekilde park eder. -1 = atanmış slot yok.</summary>
        public double DockX { get; set; } = -1;
        public double DockY { get; set; } = -1;

        // Sürüş Modu (1:1 Kontrol) ve Vites Sistemi: oyuncu bu aracı tekil olarak kullanmaya
        // başladığında (IsDriven=true) AI durur, WASD/ok tuşları doğrudan aracı kontrol eder.
        // Vites (Gear): -1=R (geri), 0=N (boşta, güçsüz), 1-4=D (ileri viteslar, düşük vites güçlü
        // ivme/düşük tepe hız, yüksek vites zayıf ivme/yüksek tepe hız - gerçek şanzıman hissi).
        public bool IsDriven { get; set; }
        public int Gear { get; set; } = 1;

        /// <summary>Bu araç tipinin gerçek şanzımanındaki ileri vites sayısı (otomobil 6, motosiklet
        /// 6, bisiklet 3, kamyon/otobüs 6 ana + Range ile 12 efektif, traktör 8). Vites arayüzü ve
        /// hız/ivme eğrileri bu değere göre orantılı ölçeklenir.</summary>
        public int TopGear { get; set; } = 6;

        /// <summary>Bu aracın gerçek dünyadaki azami hızı (km/s cinsinden, en yüksek viteste/Range
        /// Yüksek'te ulaşılır) - HUD'daki "km/s" değeri artık gerçekten bu sayıya ulaşıyor.</summary>
        public double RealTopSpeedKmh { get; set; } = 180;

        /// <summary>Ağır ticari araçlarda (kamyon/otobüs) gerçek 12 viteslik şanzımanların çoğu, ana
        /// 6 pozisyonluk bir H-yuvası + ayrı bir Düşük/Yüksek Range (aralık) anahtarından oluşur -
        /// bu iki durumu çarparak 12 vites elde edilir. Bu araç için bu anahtar var mı?</summary>
        public bool HasRangeSplitter { get; set; }

        /// <summary>Range anahtarının konumu: true=Yüksek (yol hızı), false=Düşük (çekiş/tırmanma,
        /// yarı hız ama çok daha güçlü ivme) - gerçek bir kamyon şanzımanındaki gibi.</summary>
        public bool RangeHigh { get; set; } = true;

        /// <summary>Oyuncu sert fren yaparken (MainWindow sürüş döngüsünde set edilir) true olur -
        /// render katmanında araç arkasında kısa süreli kırmızı fren lambası parıltısı için kullanılır.</summary>
        public bool IsBraking { get; set; }

        // Polis Kovalamacası: hız upgrade'i alan bir araç için sahneye çıkan polis aracı bu
        // referansı kalıcı olarak takip eder (rastgele "chasing" state'inden bağımsız).
        public bool IsPolice { get; set; }
        public Vehicle ChaseTarget { get; set; }

        /// <summary>Araç tipine göre "sürüş kişiliği" profili: ağır ticari araçlar (kamyon/otobüs/
        /// traktör/tren) daha güçlü motor freniyle "hantal" hissettirir. Vites sayısı artık her araç
        /// tipinde farklı olduğundan (bkz. TopGear), çarpanlar sabit switch yerine gerçek bir
        /// şanzımandaki gibi ORANTISAL (gear/TopGear) formüllerle hesaplanır - böylece 6 viteslik bir
        /// otomobil ile 12 efektif viteslik (6 ana x Range) bir kamyon aynı mantıkla ama kendi vites
        /// sayısına göre ölçeklenmiş şekilde doğru çalışır.</summary>
        private bool IsHeavyVehicle => Type is "truck" or "bus" or "tractor" or "train";

        /// <summary>Geçerli vitesin, o aracın azami hızının yüzde kaçına izin verdiği (0-1). Vites 1
        /// hıza güçlü bir taban (yaklaşık %20) ile başlar, TopGear'da tam hıza (1.0) ulaşır - gerçek
        /// bir şanzımanın kademeli hız bandı artışına benzer.</summary>
        private double GearMaxSpeedMult(int gear)
        {
            if (gear == 0) return 0.0;
            if (gear == -1) return 0.28; // Geri vites her zaman yavaştır (gerçek araçlarda da öyle)

            double r = Math.Clamp((double)gear / Math.Max(1, TopGear), 0, 1);
            double mult = 0.2 + 0.8 * r;

            // Range Düşük: yarı hız ama (aşağıda) çok daha güçlü ivme - gerçek bir kamyonun
            // tırmanma/çekiş modu gibi.
            if (HasRangeSplitter && !RangeHigh) mult *= 0.5;
            return mult;
        }

        /// <summary>Geçerli vitesin ivme çarpanı - düşük vites güçlü tork/hızlı ivme, yüksek vites
        /// zayıf tork/yüksek hız verir (gerçek dişli oranı mantığı, vites sayısından bağımsız olarak
        /// orantısal çalışır).</summary>
        private double GearAccelMult(int gear)
        {
            if (gear == 0) return 0.0;
            if (gear == -1) return 0.6;

            double r = Math.Clamp((double)gear / Math.Max(1, TopGear), 0, 1);
            double mult = 2.2 - 1.4 * r;

            // Range Düşük: gerçek bir kamyon şanzımanında düşük range çok daha yüksek tork verir
            // (yarı hız pahasına) - yüklü/dik yokuşta kalkış için.
            if (HasRangeSplitter && !RangeHigh) mult *= 1.6;
            return mult;
        }

        /// <summary>Gaz bırakıldığında viteste kalan motor freni (kompresyon) etkisi - düşük vites
        /// güçlü/hızlı yavaşlatır (gerçek dişli oranı gibi), yüksek vites uzun süzülmeye izin verir.
        /// Ağır araçlarda (daha büyük motor/şanzıman kütlesi) motor freni belirgin daha güçlüdür.</summary>
        private double GearCoastFrictionMult(int gear)
        {
            double baseMult;
            if (gear == -1)
            {
                baseMult = 0.965;
            }
            else
            {
                double r = Math.Clamp((double)gear / Math.Max(1, TopGear), 0, 1);
                baseMult = 0.955 + 0.03 * r;
            }
            if (IsHeavyVehicle) return baseMult - 0.03; // daha hızlı yavaşlar (motor freni güçlü)
            return baseMult;
        }

        /// <summary>Geçerli vitesin izin verdiği tepe hızı döndürür (piksel/kare) - HUD'da "redline"
        /// göstergesi ve hız göstergesi için kullanılır. Artık eski soyut `MaxSpeed` (AI gezinme hız
        /// tavanı, dokunulmadı) yerine dokrudan gerçekçi `RealTopSpeedKmh` değerinden hesaplanır -
        /// TopGear'da/Range Yüksek'te HUD gerçekten o aracın gerçek km/s tavanını gösterir.</summary>
        public double CurrentGearMaxSpeed => (RealTopSpeedKmh / SpeedDisplayScale) * GearMaxSpeedMult(Gear);

        /// <summary>HUD'daki "km/s" değerine çevirmek için piksel-hızı bu sayıyla çarpılır (bkz.
        /// MainWindow SpeedTxt) - iki tarafta da aynı sabit kullanılmalı ki kalibrasyon tutarlı olsun.</summary>
        public const double SpeedDisplayScale = 22.0;

        /// <summary>
        /// Sürüş modundayken her tikte çağrılır: throttle (-1..1, ileri/geri) ve strafe (-1..1,
        /// sol/sağ - helikopter/uçuş modunda dikey kontrol) girdisini geçerli vitese göre
        /// ölçeklenmiş ivme/tepe hız ile doğrudan hıza uygular (1:1 kontrol hissi).
        /// </summary>
        public void ApplyDriveInput(double throttle, double strafe, double dt = 1.0)
        {
            if (Gear == 0) return; // Boşta (N) - motor gücü tekerleklere/pervaneye iletilmiyor
            if (dt <= 0) dt = 1.0;

            double gearAccel = Acceleration * GearAccelMult(Gear) * 2.5;
            double gearMaxSpeed = CurrentGearMaxSpeed;
            double dirSign = Gear == -1 ? -1.0 : 1.0; // R vitesi girdiyi ters çevirir

            // Dorse/ekipman takılıyken (kamyon/traktör) ek yük ivmeyi belirgin düşürür - gerçek bir
            // çekicinin yüklü dorseyle çok daha ağır kalkması gibi. Kargo cezasıyla (EffectiveAcceleration)
            // aynı anda uygulanabilir (ikisi de aktifse üst üste biner - gerçekçi, "hem yüklü hem çekiyor").
            double trailerPenalty = string.IsNullOrEmpty(TrailerType) ? 1.0 : 0.65;
            gearAccel *= trailerPenalty;

            // Vites değişimi sonrası kısa "debriyaj" kesintisi: motor gücü birkaç kare boyunca
            // neredeyse sıfıra iner, gerçek bir manuel şanzımanda vites geçişinde hissedilen kısa
            // güç kesintisini taklit eder.
            if (GearShiftCooldown > 0)
            {
                gearAccel *= 0.12;
                GearShiftCooldown -= dt;
            }

            // Gerçek motor tork eğrisi: düşük hızda (vites tepe hızına uzakken) güçlü ivme
            // uygulanır, tepe hıza yaklaştıkça ivme kareye orantılı olarak yumuşar - sabit güçle
            // gidip aniden tavana çarpmak yerine gerçekçi, kademeli bir hızlanma hissi verir.
            double currentSpeed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            double speedRatio = gearMaxSpeed > 0 ? Math.Clamp(currentSpeed / gearMaxSpeed, 0, 1) : 0;
            double torqueCurve = Math.Max(0.08, 1.0 - speedRatio * speedRatio);
            double appliedAccel = gearAccel * torqueCurve;

            steerLean = 0;
            if (Type == "helicopter" || IsFlying)
            {
                VelocityY += -throttle * appliedAccel * dirSign * dt;
                VelocityX += strafe * appliedAccel * dirSign * dt;
            }
            else
            {
                VelocityX += throttle * appliedAccel * dirSign * dt;

                // Gaz bırakılınca (throttle ~0) motor freni/yuvarlanma direnci ile akışkan bir
                // yavaşlama uygulanır - önceden hız sabit kalıp aniden duruyordu, artık gerçek bir
                // araç gibi yumuşakça yavaşlayarak süzülüyor. Motor freni vitese göre değişir: düşük
                // vitesde (küçük dişli oranı) motor kompresyonu daha güçlü hissedilir ve araç hızla
                // yavaşlar, yüksek vitesde (overdrive hissi) çok daha uzun süzülür - gerçek bir
                // şanzımanda vites düşürünce/yükseltince hissedilen fark buna benzer.
                if (Math.Abs(throttle) < 0.05)
                {
                    double engineBraking = GearCoastFrictionMult(Gear);
                    VelocityX *= Damp(engineBraking, dt);
                }

                // Zeminde A/D (strafe) artık işlevsiz değil: gövdede hafif bir viraj/manevra yatışı
                // (lean) oluşturur - gerçek bir aracın viraja girerken yaptığı küçük yatık duruş
                // hissi. Sadece görsel (Rotation'a eklenir), hıza dokunmaz.
                if (EdgeSide == 0)
                {
                    steerLean = strafe * 5.0;

                    // Direksiyon belirgin şekilde çevrildiyse dönüş sinyali yanar.
                    if (Math.Abs(strafe) > 0.35)
                        TriggerSignal(strafe > 0 ? 1 : -1);
                }
            }

            double speed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            if (gearMaxSpeed > 0 && speed > gearMaxSpeed)
            {
                // Tepe hızı aşınca sert bir "duvara çarpma" gibi anında kırpmak yerine, birkaç kare
                // içinde yumuşakça tepe hıza yaklaştırılır (yumuşak limitleyici) - ani bir üst hız
                // kaynağından (çarpışma/itme eşyası) gelen fazlalık daha akıcı sönümlenir.
                double targetX = (VelocityX / speed) * gearMaxSpeed;
                double targetY = (VelocityY / speed) * gearMaxSpeed;
                double limitK = LerpK(0.35, dt);
                VelocityX += (targetX - VelocityX) * limitK;
                VelocityY += (targetY - VelocityY) * limitK;
            }

            // Ağırlık aktarımı (dive/squat): hızlanınca gövde hafifçe arkaya yaslanır (squat),
            // frenlerken/yavaşlarken öne yaslanır (dive) - gerçek bir aracın süspansiyon tepkisine
            // benzer, sadece görsel (DriveTilt render'da Rotation'a eklenir).
            double newSpeed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            double speedDelta = newSpeed - prevSpeedForTilt;
            prevSpeedForTilt = newSpeed;
            double diveLean = Math.Clamp(-speedDelta * 22.0, -4.5, 4.5);
            DriveTilt = diveLean + steerLean;
        }

        private double steerLean;

        // Dorse / Ekipman Sistemi (Kamyon: flatbed/tanker, Traktör: equipment)
        public string TrailerType { get; set; } = "";

        public static readonly Dictionary<string, (string label, string emoji)> TrailerCatalog = new()
        {
            { "flatbed", ("Açık Kasa Dorse", "🚛") },
            { "tanker", ("Tanker Dorse", "🛢️") },
            { "equipment", ("Tarım Römorku 1", "🌾") },
            { "wagon", ("Tarım Römorku 2", "🌾") }
        };

        // Yan Saklanma ve Kargo/Hedef Özellikleri (VPet ve Görevler için)
        public bool IsSideHidden { get; set; }
        public string HiddenDirection { get; set; } = "";
        public double TargetX { get; set; } = -1;
        public double TargetY { get; set; } = -1;
        public bool HasCargo { get; set; }
        public bool IsWet { get; set; }

        /// <summary>Açıkken kargo/hedef navigasyonu düz vektör-yönelme yerine bir Q-learning ajanı
        /// (bkz. QLearningAgent, UpdateLearningNavigation) tarafından yönetilir - araç zamanla
        /// (tekrarlanan teslimatlarla) duvara çarpmadan hedefe ulaşmayı öğrenir. Kalıcı değildir:
        /// öğrenilen Q-tablosu yalnızca bellekte tutulur, kayıt/yükleme veya uygulama yeniden
        /// başlatıldığında sıfırlanır (bilinçli kapsam sınırlaması).</summary>
        public bool UseLearningAI { get; set; }
        private QLearningAgent learningAgent;
        private int lastLearningState = -1;
        private int lastLearningAction = -1;
        private double lastLearningDistance = -1;
        private double learningDecisionTimer;
        public string NeonColor { get; set; } = "#00E5FF";

        // Gelişmiş Seçim ve Rota Özellikleri
        public string Name { get; set; } = "";
        public List<Point> CustomPath { get; set; } = new();

        private static readonly Dictionary<string, string> TypeLabelsTr = new()
        {
            { "car", "Otomobil" }, { "truck", "Kamyon" }, { "bus", "Otobüs" },
            { "bike", "Bisiklet" }, { "motorcycle", "Motosiklet" }, { "train", "Tren" },
            { "helicopter", "Helikopter" }, { "tractor", "Traktör" }
        };

        /// <summary>"Aktif Araçlar" listesinde ismin altında/yanında gösterilecek model etiketi -
        /// çizilmiş bir model varyantı varsa ("Lüks Sedan" gibi) onu, yoksa aracın genel Türkçe
        /// tipini döndürür. Kullanıcı aracı hiç yeniden adlandırmadıysa (Name boş/"İsimsiz" kalırsa)
        /// listede tek başına emoji görünüp hangi aracın hangisi olduğu anlaşılmıyordu.</summary>
        public string DisplayModel
        {
            get
            {
                var visual = VehicleVisuals.Get(Type, Brand);
                if (visual != null) return visual.Label;
                return TypeLabelsTr.TryGetValue(Type, out var label) ? label : Type;
            }
        }

        // Upgrade ve Dekorasyon Sistemi
        public List<string> Upgrades { get; set; } = new();
        public List<string> Decorations { get; set; } = new();

        public bool IsDestroyed { get; private set; }

        /// <summary>
        /// Bozulan Araç Çekicisi: HP sıfırlanan araç anında silinmez, kısa bir "çekiliyor" süresi
        /// (grace period) boyunca solarak ekranda kalır - -1 = henüz çekilmiyor.
        /// </summary>
        public double TowCountdown { get; set; } = -1;
        public const double TowDurationFrames = 90; // ~1.5 sn (60 FPS karesi cinsinden)

        /// <summary>Ekran duvarına çarpıp sekince tetiklenir (ses efekti bağlamak için).</summary>
        public event Action OnWallBounce;

        /// <summary>Sürükle-bırak sonrası (veya fare yakalamasının beklenmedik kaybı durumunda)
        /// çağrılır: EdgeSide'ı zemin moduna sıfırlar VE önceki kenarın (ör. tavan = 180°, duvar =
        /// ±90°) gövde açısını da zemin açısına (0°) doğru akıcı bir geçişle sıfırlar. Önceden
        /// yalnızca "vehicle.EdgeSide = 0" atanıyordu; bu, normal kenar-geçiş algılama bloğunun
        /// (Update() içindeki edgeSideBeforeTransition karşılaştırması) "gerçekten değişti" farkını
        /// hiç göremediği bir dıştan-atama olduğundan WallAngleSmoothed asla güncellenmiyordu - araç
        /// tavandan/duvardan tutulup zemine bırakılınca kalıcı olarak ters/yan görünüyordu.</summary>
        public void ResetEdgeStateAfterDrag() => ResetEdgeToGround();

        /// <summary>
        /// EdgeSide'ı zemin moduna sıfırlarken gövde açısını (WallAngleSmoothed) da 0°'ye doğru
        /// akıcı bir geçişe sokar.
        ///
        /// Update() dışından YA DA Update() içindeki normal kenar-geçiş bloğunun DIŞINDAN yapılan
        /// her "EdgeSide = 0" ataması bu metottan geçmelidir. Aksi halde geçiş algılama bloğu
        /// (edgeSideBeforeTransition karşılaştırması) değişimi hiç göremez, wallAngleFrom/
        /// wallAngleDeltaTotal güncellenmez ve araç zeminde kalıcı olarak TERS (tavandan geldiyse
        /// 180°) veya YAN (duvardan geldiyse ±90°) görünür.
        ///
        /// Bu hata uygulama çalıştırılarak gözlendi: otomatik hareket kapalıyken duvarda hareketsiz
        /// kalan araçlar "köşe sıkışma güvenlik ağı" tarafından zemine indiriliyor, ama gövde açıları
        /// duvardaki değerinde donuyordu - otomobil baş aşağı, motosiklet yan yatmış duruyordu.
        /// </summary>
        private void ResetEdgeToGround()
        {
            if (EdgeSide != 0)
            {
                double rawDelta = 0 - WallAngleSmoothed;
                rawDelta = ((rawDelta + 180) % 360 + 360) % 360 - 180;
                wallAngleFrom = WallAngleSmoothed;
                wallAngleDeltaTotal = rawDelta;
                edgeTransitionCooldown = 0.3;
            }
            EdgeSide = 0;
        }

        public Vehicle(double x, double y, string type, string brand = "")
        {
            X = x;
            Y = y;
            Type = type;
            Brand = brand;
            VelocityX = (Random.Shared.NextDouble() - 0.5) * 4;
            VelocityY = (Random.Shared.NextDouble() - 0.5) * 4;

            InitializeVehicleStats();
            Scale = DefaultScale; // baseWidth/Height InitializeVehicleStats'ta atandıktan SONRA - setter buna göre Width/Height'ı hesaplıyor
            HP = MaxHP;
            Fuel = MaxFuel * 0.7;
            DragLastX = x;
            DragLastY = y;

            // wanderTimer varsayılan (0) olduğunda ilk AI tikinde hemen "yön değiştir" tetiklenip
            // wanderDir anında -1'e (geri) dönüyordu - araçların doğuşta hep geriye gitmesine sebep
            // olan hataydı. Başlangıçta pozitif (ileri) yönde bir süre kalmasını sağlıyoruz.
            wanderTimer = 150 + Random.Shared.Next(210);
        }

        /// <summary>
        /// Ekrandaki bir metrenin kaç piksel ettiği. Tip tablosundaki ölçünün ta kendisi
        /// (otomobil 4.5 m = 40 px), artık örtük olmaktan çıkarılıp adlandırıldı - tüm araç
        /// boyutları tek bir gerçek-dünya ölçeğine dayanıyor.
        /// </summary>
        public const double PixelsPerMetre = 40.0 / 4.5;

        private static string BrandSizeKey(string type, string brand) => $"{type}_{brand}";

        /// <summary>
        /// Marka görseli olan araçların gerçek uzunlukları (metre). Tip tablosu yalnızca tipin
        /// TEMSİLİ uzunluğunu biliyor; burası aynı tipin modelleri arasındaki gerçek farkı taşıyor
        /// (turizm otobüsü 12.5 m ↔ midi 8.5 m, spor otomobil 4.5 m ↔ lüks sedan 5.2 m).
        ///
        /// ⚠️ Bu değerler ilgili sınıfın TİPİK uzunlukları - görsellerin hangi somut modeli
        /// temsil ettiği bilinmediğinden ölçülmüş değil, yaklaşık alınmıştır. Bir görsel belirli
        /// bir modeli temsil ediyorsa buradaki değeri gerçeğiyle değiştirmek yeterli; boyut
        /// kendiliğinden düzelir.
        ///
        /// Dorseler bu tabloda YOK: onlar ayrı bir çizim yolundan (bkz. MainWindow.DrawTrailerImage)
        /// çekicinin gövde yüksekliğine oranlanarak boyutlanıyor, Vehicle.Width'e bağlı değiller.
        /// </summary>
        private static readonly Dictionary<string, double> RealLengthMetres = new()
        {
            { "car_sedan",         4.7 },   // orta sınıf sedan
            { "car_sport",         4.5 },   // spor kupe
            { "car_luxury",        5.2 },   // üst sınıf sedan
            { "truck_rig",         6.5 },   // çekici (dorsesiz)
            { "truck_hauler",      8.5 },   // tek parça kamyon
            { "truck_freight",    10.0 },   // uzun yük kamyonu
            { "bus_coach",        12.5 },   // turizm otobüsü
            { "bus_intercity",    12.0 },   // şehirlerarası otobüs
            { "bus_midi",          8.5 },   // midibüs
            { "motorcycle_sport",  2.1 },
            { "motorcycle_race",   2.05 },
            { "tractor_farm",      4.5 },
        };

        /// <summary>
        /// Dorse/römork gerçek uzunlukları (metre). Araç tablosuyla AYNI yerde tutuluyor ki bir
        /// görsel değişince tek noktadan düzeltilebilsin. Dorseler ayrı bir çizim yolundan
        /// (bkz. MainWindow.DrawTrailerImage) boyutlanır; orada bu tablo bulunursa uzunluk
        /// gerçeğe göre, bulunmazsa eskisi gibi çekicinin yüksekliğine oranlanarak hesaplanır.
        ///
        /// ⚠️ Araç tablosundaki gibi, bunlar da sınıfın TİPİK uzunlukları - ölçülmüş değil.
        /// </summary>
        public static readonly Dictionary<string, double> TrailerRealLengthMetres = new()
        {
            { "flatbed",   13.6 },  // standart açık yarı-römork
            { "tanker",    12.0 },  // akaryakıt/kimyasal tanker
            { "wagon",      7.0 },  // kasalı römork
            { "equipment",  6.0 },  // ekipman/lowbed römork
        };

        private void InitializeVehicleStats()
        {
            // Boyutlar gerçek dünya uzunluk oranlarına göre hesaplanır (referans: otomobil ≈ 4.5m = 40px,
            // yani ~8.89 px/m). Her tipin eski en/boy oranı korunarak sadece gerçekçi göreli büyüklük
            // uygulanmıştır - artık bir kamyon otomobilden, bir otobüs/tren kamyondan küçük görünmüyor.
            // Kütle (mass) gerçek ağırlık sıralamasına göre: bisiklet/motor çok hafif, otomobil
            // referans (1.0), kamyon/otobüs/traktör belirgin ağır, tren en ağır.
            var stats = new Dictionary<string, (string emoji, int w, int h, string color, double speed, double accel, int hp, int fuel, double mass, int topGear, double realTopKmh, bool splitter)>
            {
                // realTopKmh: gerçek dünya azami hız yaklaşık değerleri. topGear: gerçek şanzıman
                // vites sayısı (kamyon/otobüs 6 ana + Range Düşük/Yüksek = 12 efektif vites).
                { "car",        ("🚗", 40, 25, "#FF6B6B", 2.0, 0.16, 100, 100, 1.0, 6, 200, false) },   // ~4.5m, 6 viteslik manuel
                { "truck",      ("🚚", 71, 36, "#4ECDC4", 1.5, 0.13, 150, 150, 3.0, 6, 120, true) },    // ~8.0m, 6x2 Range = 12 vites
                { "bus",        ("🚌", 107, 54, "#FFE66D", 1.2, 0.10, 120, 120, 3.5, 6, 100, true) },   // ~12.0m, 6x2 Range = 12 vites
                { "bike",       ("🚲", 16, 11, "#95E1D3", 2.5, 0.22, 70, 80, 0.3, 3, 32, false) },      // ~1.8m, 3 viteslik basit şanzıman
                { "motorcycle", ("🏍️", 20, 13, "#F38181", 3.0, 0.30, 80, 90, 0.5, 6, 260, false) },     // ~2.2m, 6 viteslik sekans şanzıman
                { "train",      ("🚂", 178, 89, "#696969", 1.0, 0.08, 200, 200, 6.0, 8, 140, false) },  // ~20.0m, 8 kademeli gaz (throttle notch)
                { "helicopter", ("🚁", 107, 107, "#A8E6CF", 2.2, 0.18, 110, 100, 2.0, 4, 250, false) },  // ~12.0m (pervane açıklığı)
                { "tractor",    ("🚜", 37, 25, "#2ECC71", 1.3, 0.14, 130, 110, 2.5, 8, 42, false) }     // ~4.2m (gövde), 8 viteslik ağır şanzıman
            };

            if (stats.TryGetValue(Type, out var stat))
            {
                Emoji = stat.emoji;
                baseWidth = stat.w;
                baseHeight = stat.h;

                // Tip tablosu doğru ORANI veriyor ama tipin İÇİNDEKİ farkları göz ardı ediyordu:
                // midi otobüs (8.5 m) ile turizm otobüsü (12.5 m) aynı boyda çiziliyordu. Marka
                // görseli olan araçlarda gerçek uzunluk kullanılıp boyut ona göre düzeltilir.
                if (RealLengthMetres.TryGetValue(BrandSizeKey(Type, Brand), out double metres) && baseWidth > 0)
                {
                    int scaledWidth = Math.Max(6, (int)Math.Round(metres * PixelsPerMetre));
                    double factor = scaledWidth / (double)baseWidth;
                    baseHeight = Math.Max(4, (int)Math.Round(baseHeight * factor));
                    baseWidth = scaledWidth;
                }

                Width = baseWidth;
                Height = baseHeight;
                Color = stat.color;
                MaxSpeed = stat.speed;
                Acceleration = stat.accel;
                MaxHP = stat.hp;
                MaxFuel = stat.fuel;
                Mass = stat.mass;
                TopGear = stat.topGear;
                RealTopSpeedKmh = stat.realTopKmh;
                HasRangeSplitter = stat.splitter;
            }
        }

        /// <summary>Ekranın bir kenarına tırmanmış araç 90° döndürülmüş çizilir (bkz. WallAngleSmoothed):
        /// duvardayken gövdenin YATAY kapladığı yarı-genişlik artık Width/2 değil Height/2, DİKEY
        /// kapladığı yarı-yükseklik ise Height/2 değil Width/2 olur. Fizik eskiden bunu hesaba
        /// katmadığı için araç duvara yapışmak yerine (Width-Height)/2 kadar boşlukta duruyor, tavana
        /// geçerken de gövdenin bir kısmı ekranın dışına taşıyordu.</summary>
        private bool OnVerticalWall => EdgeSide == -1 || EdgeSide == 1;
        private double HalfX => OnVerticalWall ? Height / 2.0 : Width / 2.0;
        private double HalfY => OnVerticalWall ? Width / 2.0 : Height / 2.0;

        /// <summary>Kare hızından bağımsız üstel sönümleme: "60 FPS'te kare başına k ile çarp"
        /// davranışını, geçen gerçek süreye (dt kare) göre birebir aynı sonucu verecek şekilde uygular.</summary>
        private static double Damp(double perFrameFactor, double dt) => Math.Pow(perFrameFactor, dt);

        /// <summary>Kare hızından bağımsız doğrusal yaklaşma (lerp) katsayısı.</summary>
        private static double LerpK(double perFrameK, double dt) => 1.0 - Math.Pow(1.0 - perFrameK, dt);

        /// <summary>Araç şu an içinde bulunduğu kenarın (zemin/duvar/tavan) yüzeyine değmiyorsa true.
        /// Hem AI'nin havada devreye girmemesi hem de tekerleklerin havadayken yuvarlanmaması için
        /// tek bir kaynaktan hesaplanır.</summary>
        private bool IsAirborne(double canvasWidth, double canvasHeight, double activeWinTop, double activeWinLeft, double activeWinRight)
        {
            if (EdgeSide == 0)
            {
                double groundY = canvasHeight - HalfY;
                if (activeWinTop > 0 && X >= activeWinLeft && X <= activeWinRight && Y <= activeWinTop - HalfY + 8)
                    groundY = activeWinTop - HalfY;
                return Y < groundY - 5;
            }
            if (EdgeSide == 2) return Y > HalfY + 5;

            double wallX = EdgeSide == -1 ? HalfX : canvasWidth - HalfX;
            return Math.Abs(X - wallX) > 5;
        }

        public void Update(int gameTime, List<Vehicle> otherVehicles, List<Obstacle> obstacles, double canvasWidth = 1200, double canvasHeight = 800, double activeWinTop = -1, double activeWinLeft = -1, double activeWinRight = -1, bool autoMoveEnabled = true, double dt = 1.0, double elapsedSeconds = 0, double dirtMultiplier = 1.0, double fuelMultiplier = 1.0, Func<double, double> floorAt = null)
        {
            if (canvasWidth <= 0) canvasWidth = 1200;
            if (canvasHeight <= 0) canvasHeight = 800;
            if (dt <= 0) dt = 1.0;

            // NaN/Infinity Kurtarma: bir bölme hatası (ör. iki aracın tam aynı noktaya denk gelip
            // mesafe sıfır çıkması) X/Y/Velocity'ye NaN sızdırırsa, araç köşede sonsuza kadar donup
            // titriyormuş gibi kalırdı - üstelik "Köşe Sıkışma Güvenlik Ağı" bile bunu kurtaramazdı,
            // çünkü NaN < 0.3 gibi karşılaştırmalar HER ZAMAN false döner ve sayaç hiç işlemezdi. Bu
            // yüzden konum/hız değerleri her karede en başta kontrol edilip, bozulmuşsa ekranın
            // ortasına, sıfır hızla geri getirilir.
            if (double.IsNaN(X) || double.IsNaN(Y) || double.IsInfinity(X) || double.IsInfinity(Y)
                || double.IsNaN(VelocityX) || double.IsNaN(VelocityY) || double.IsInfinity(VelocityX) || double.IsInfinity(VelocityY))
            {
                X = canvasWidth / 2.0;
                Y = canvasHeight / 2.0;
                VelocityX = 0;
                VelocityY = 0;
                // Gövde açısı da sıfırlanmalı (bkz. ResetEdgeToGround) - aksi halde kurtarılan araç
                // ekranın ortasında ters/yan duruyordu.
                ResetEdgeToGround();
            }

            // Çoklu-monitör modunda her ekranın çözünürlüğü/konumu farklı olabileceğinden tek bir
            // global "zemin" (canvasHeight) yanlış olurdu - ikincil ekranlarda araçlar sahte bir
            // zeminde havada asılı kalır ya da gerçek zeminin altına batardı. floorAt verildiğinde
            // (bkz. MainWindow.FloorYForX) aracın KENDİ X konumuna karşılık gelen ekranın taban
            // çizgisi kullanılır; tek monitör modunda floorAt null'dur ve eski davranış korunur.
            double effectiveFloorY = floorAt != null ? floorAt(X) : canvasHeight;

            if (IsDestroyed)
            {
                // Çekiliyor (tow) süresince araç hareketsiz kalır, sadece görsel olarak solar
                VelocityX = 0;
                VelocityY = 0;
                return;
            }

            if (IsSideHidden)
            {
                // Yan saklanma durumunda dur
                VelocityX = 0;
                VelocityY = 0;
                Rotation = 0;
                return;
            }

            if (IsLocked && !IsDragged)
            {
                // Sanal Otopark: atanmış bir garaj slotu varsa oraya yumuşakça kayar, yoksa
                // (eski davranış) olduğu yerde donar.
                if (DockX >= 0 && DockY >= 0)
                {
                    double dx = DockX - X;
                    double dy = DockY - Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 2)
                    {
                        VelocityX = Math.Clamp(dx * 0.12, -3, 3);
                        VelocityY = Math.Clamp(dy * 0.12, -3, 3);
                        X += VelocityX * dt;
                        Y += VelocityY * dt;
                        FacingLeft = VelocityX < -0.1;
                    }
                    else
                    {
                        X = DockX;
                        Y = DockY;
                        VelocityX = 0;
                        VelocityY = 0;
                    }
                }
                else
                {
                    VelocityX = 0;
                    VelocityY = 0;
                }
                return;
            }

            if (IsDragged)
            {
                // Sürüklenirken VelocityX/Y ARTIK sıfırlanmıyor: bu değerler MouseMove sırasında
                // fare hareketinden fırlatma (fling) hızı olarak hesaplanıyor (bkz. Canvas_MouseMove).
                // Eskiden burada her fizik karesinde 0'a sıfırlanıyordu, bu yüzden fare bırakıldığı
                // anda gerçek hız neredeyse her zaman kayboluyor, araç bırakılınca zemine "yapışmış"
                // gibi çok yavaş/geç düşüyordu. Sadece dönme (tilt/sway) efekti sıfırlanır, hareket
                // düz kalır; hız bilgisi bırakma anında olduğu gibi korunup yerçekimine devrediliyor.
                DragLastX = X;
                DragLastY = Y;
                DragRotation = 0;
                Rotation = DragManualRotation;
            }
            else
            {
                // Yakıt tüketimi
                if (Fuel > 0)
                {
                    double speed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
                    Fuel -= FuelConsumption * (speed / 2) * dt * fuelMultiplier;
                    Mileage += speed * 0.01 * dt;
                    TireWear = Math.Min(100, TireWear + speed * 0.004 * dt);

                    // Aşınmış lastikler tutuşu azaltır - hafif ekstra sürtünme olarak hissettirilir.
                    // %100 aşınmada hızın ~%15'i her karede ekstra kaybedilir; MaxSpeed/Acceleration'a
                    // dokunulmadığı için upgrade sistemiyle çakışmaz.
                    if (TireWear > 0)
                    {
                        double wearDamp = Damp(1.0 - (TireWear / 100.0) * 0.15, dt);
                        VelocityX *= wearDamp;
                        VelocityY *= wearDamp;
                    }
                }
                else
                {
                    // Yakıt yok - hızla yavaşla
                    double noFuelDamp = Damp(0.95, dt);
                    VelocityX *= noFuelDamp;
                    VelocityY *= noFuelDamp;
                }

                // Mutluluk azal
                Happiness = Math.Max(0, Happiness - 0.05 * dt);

                // Detaylı Yıkama: zamanla yavaşça kirlenir (hız ne kadar yüksekse o kadar hızlı -
                // toz/çamur toplama hissi)
                double dirtSpeed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
                Dirtiness = Math.Min(1.0, Dirtiness + (0.00025 + dirtSpeed * 0.00015) * dt * dirtMultiplier);

                // Sürüş Modu: oyuncu doğrudan kontrol ederken (ApplyDriveInput ile) AI/rota
                // mantığı devre dışı kalır, sadece fizik (yerçekimi/sürtünme/çarpışma) işler.
                if (!IsDriven && DriveTilt != 0)
                {
                    // Sürüşten çıkınca gövde eğimi (dive/squat/lean) yumuşakça düzleşir.
                    DriveTilt *= Damp(0.85, dt);
                    if (Math.Abs(DriveTilt) < 0.05) DriveTilt = 0;
                }

                // Yön çevirmesi ve köşe dönüşleri artık ANINDA sıçramıyor, her karede hedefe doğru
                // akıcı bir şekilde kayıyor - genel hareket hissini belirgin şekilde yumuşatır.
                double mirrorTarget = FacingLeft ? 1.0 : -1.0;
                MirrorScaleX += (mirrorTarget - MirrorScaleX) * LerpK(0.35, dt);

                // Gövde dönüşü (WallAngleSmoothed) artık CornerBlendSmoothed'e bağlı olarak, geçişin
                // GERÇEK kenar-değişim anından itibaren hesaplanıyor - bkz. aşağıdaki kenar geçiş
                // bloğu ve wallAngleFrom/wallAngleDeltaTotal alanları.
                double cornerBlendTarget = (EdgeSide == -1 || EdgeSide == 1) ? 1.0 : 0.0;
                if (CornerBlendSmoothed < 0) CornerBlendSmoothed = cornerBlendTarget;
                CornerBlendSmoothed += (cornerBlendTarget - CornerBlendSmoothed) * LerpK(0.25, dt);

                if (IsDriven)
                {
                    // hiçbir şey - VelocityX/Y zaten ApplyDriveInput tarafından ayarlandı
                }
                // Rota Takip Fiziği (Custom Pathing)
                else if (CustomPath.Count > 0)
                {
                    var pt = CustomPath[0];
                    double dx = pt.X - X;
                    double dy = pt.Y - Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist > 15)
                    {
                        VelocityX += (dx / dist) * EffectiveAcceleration * 2.2 * dt;
                        VelocityY += (dy / dist) * EffectiveAcceleration * 2.2 * dt;
                    }
                    else
                    {
                        CustomPath.RemoveAt(0);
                    }
                }
                else
                {
                    // Kenar (duvar) moduna göre "zemine olan mesafe" hesabı - AI/gezinme sadece
                    // yerdeyken/duvara yapışıkken çalışır, havadayken (düşerken) çalışmaz
                    // Aktif pencerenin üstünde duran araç da artık "yerde" sayılıyor (bkz. IsAirborne) -
                    // eskiden sadece ekranın en altı zemin kabul edildiğinden, bir pencerenin üstüne
                    // çıkan araç sürekli "düşüyor" sanılıp AI'si hiç çalışmıyor ve orada kıpırdamadan
                    // kalıyordu.
                    bool isFalling = Type != "helicopter" && !IsFlying
                        && IsAirborne(canvasWidth, effectiveFloorY, activeWinTop, activeWinLeft, activeWinRight);

                    if (autoMoveEnabled && !isFalling && Math.Abs(VelocityX) < 1.0 && Math.Abs(VelocityY) < 1.0)
                    {
                        UpdateAI(otherVehicles, dt);
                    }
                }

                // Kırmızı/Sarı Işık yavaşlaması ve durması
                bool intentionallyBraking = false;
                foreach (var obs in obstacles)
                {
                    if ((obs.Type == "traffic_light" && (obs.State == "red" || obs.State == "yellow")) || obs.Type == "police")
                    {
                        double dx = obs.X - X;
                        double dy = obs.Y - Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 100)
                        {
                            VelocityX *= 0.65;
                            VelocityY *= 0.65;
                            intentionallyBraking = true;
                            if (dist < 40)
                            {
                                VelocityX = 0;
                                VelocityY = 0;
                            }
                        }
                    }
                    // Masaüstü Yol Çalışması: trafik konisi tamamen durdurmaz, sadece belirgin
                    // şekilde yavaşlatır (gerçek bir şerit daralması/yol çalışması hissi)
                    else if (obs.Type == "cone")
                    {
                        double dx = obs.X - X;
                        double dy = obs.Y - Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 60)
                        {
                            VelocityX *= 0.9;
                            VelocityY *= 0.9;
                        }
                    }
                }

                // Yerçekimi ve Sekme Fiziği (VPet gibi) - Uçuş modundaki araçlar yerçekiminden muaf
                if (Type != "helicopter" && !IsFlying)
                {
                    const double gravity = 0.4;
                    // Ağır araçlar sekmede enerjiyi daha çok yutar (daha az zıplar), hafif araçlar
                    // (bisiklet/motor) daha canlı/zıplak sekiyor - gerçek kütle hissi için.
                    double bounce = -Math.Clamp(0.45 / Math.Sqrt(Mass), 0.18, 0.75);
                    double friction = IsWet ? 0.994 : 0.98;

                    // Kenar tırmanma modunu belirle: ekranın DÖRT kenarının hepsi "zemin" gibi
                    // tırmanılabilir (0=zemin, -1=sol duvar, 1=sağ duvar, 2=tavan/üst kenar) - araçlar
                    // artık tam bir çevre turu atarak (zemin→duvar→tavan→diğer duvar→zemin) ekranın
                    // her kenarında gezinebiliyor. Geçiş anında konum, o kenara ANINDA sabitlenir -
                    // aksi halde (özellikle uzun araçlarda, ör. tır) gövde bir sonraki karede 90°
                    // dönerken henüz kenara ulaşmamış oluyor, bu da aracın bir kısmının dönüş
                    // sırasında ekranın dışında/boşlukta kalıp görünmemesine sebep oluyordu.
                    // NOT: Geçişlerde konum artık ANINDA (Height/2.0 gibi) sabitlenmiyor - CornerBlendSmoothed
                    // henüz eski yönelime yakın olduğundan halfXSmoothed/halfYSmoothed geçiş anında ESKİ
                    // yarı-genişliğe çok yakın kalır (sıçrama yok), sonraki karelerde WallAngleSmoothed ile
                    // AYNI hızda yeni kenarın yarı-genişliğine doğru akıcı şekilde kayar. Eskiden burada
                    // doğrudan Height/2.0 yazıldığından (Width genelde Height'ten büyük olduğu için) araç
                    // geçiş anında aniden ekranın İÇİNE doğru bir adım atmış gibi görünüyordu.
                    double halfXSmoothed = Width / 2.0 + (Height / 2.0 - Width / 2.0) * CornerBlendSmoothed;
                    double halfYSmoothed = Height / 2.0 + (Width / 2.0 - Height / 2.0) * CornerBlendSmoothed;

                    // Kenar geçişleri kısa bir bekleme süresince (bkz. edgeTransitionCooldown alanı)
                    // devre dışı - bir geçiş olur olmaz hemen tekrar tetiklenmesini (köşede "flip-flop"/
                    // görünürde dönme) engeller.
                    edgeTransitionCooldown -= dt / 60.0;
                    int edgeSideBeforeTransition = EdgeSide;

                    if (edgeTransitionCooldown <= 0)
                    {
                        if (EdgeSide == 0)
                        {
                            // Zemindeyken yatay yarı-genişlik Width/2; duvara geçtikten SONRA Height/2 olur.
                            // Zeminden gelen yatay hız (VelocityX) artık duvarın "içine/dışına" olan normal
                            // bileşen olacağından sıfırlanır - aksi halde eski yatay hız bir sonraki karede
                            // duvar sekme mantığında sahte/ani bir geri sekmeye (araç aniden zemine geri
                            // fırlıyormuş gibi bir "ters gitme" hissine) sebep oluyordu. VelocityY (araç
                            // zaten sahip olduğu dikey hız) duvar boyunca tırmanma hızı olarak korunur.
                            if (X <= Width / 2 + 6) { EdgeSide = -1; X = halfXSmoothed; VelocityX = 0; }
                            else if (X >= canvasWidth - Width / 2 - 6) { EdgeSide = 1; X = canvasWidth - halfXSmoothed; VelocityX = 0; }
                        }
                        else if (EdgeSide == -1)
                        {
                            // Duvarda dikey yarı-yükseklik Width/2 (gövde dik duruyor); tavana geçince
                            // tekrar Height/2 olur.
                            //
                            // ÖNEMLİ DÜZELTME: Sol duvarda tavana doğru tırmanırken (Y azalıyor, yani
                            // VelocityY NEGATİF) araç sağa (tavan boyunca köşeden UZAĞA) devam etmeli.
                            // Eskiden burada VelocityX = VelocityY yazıyordu - negatif kalan bu değer
                            // aracı SOLA, yani tam köşenin içine doğru itiyordu; araç görünürde "geri
                            // gidiyor" ve köşede sıkışıp kalıyormuş gibi davranıyordu. Doğrusu işareti
                            // ÇEVİRMEK: VelocityX = -VelocityY.
                            if (Y <= Width / 2.0 + 6) { EdgeSide = 2; Y = halfYSmoothed; VelocityX = -VelocityY; VelocityY = 0; }
                            else if (X > halfXSmoothed + 40) { EdgeSide = 0; }
                        }
                        else if (EdgeSide == 1)
                        {
                            // Sağ duvarda tavana tırmanırken (VelocityY negatif) araç SOLA (tavan boyunca
                            // sağ köşeden uzağa) devam etmeli - eskiden işaret burada da ters (VelocityX =
                            // -VelocityY, pozitif çıkıp aracı köşeye geri itiyordu) idi.
                            if (Y <= Width / 2.0 + 6) { EdgeSide = 2; Y = halfYSmoothed; VelocityX = VelocityY; VelocityY = 0; }
                            else if (X < canvasWidth - halfXSmoothed - 40) { EdgeSide = 0; }
                        }
                        else if (EdgeSide == 2)
                        {
                            if (X <= Width / 2 + 6) { EdgeSide = -1; X = halfXSmoothed; VelocityY = -VelocityX; VelocityX = 0; }
                            else if (X >= canvasWidth - Width / 2 - 6) { EdgeSide = 1; X = canvasWidth - halfXSmoothed; VelocityY = VelocityX; VelocityX = 0; }
                            else if (Y > halfYSmoothed + 40) { EdgeSide = 0; }
                        }

                        if (EdgeSide != edgeSideBeforeTransition)
                        {
                            edgeTransitionCooldown = 0.3;

                            // Yeni kenara geçiş başladı: o anki (henüz döndürülmemiş) açıdan yeni
                            // hedefe olan en kısa yolu bir kerelik hesaplayıp sakla - aşağıdaki blok
                            // her karede bunun üzerinden CornerBlendSmoothed'in ilerleme yüzdesi
                            // kadarını uygular.
                            double newTarget = EdgeSide switch { -1 => 90, 1 => -90, 2 => 180, _ => 0 };
                            double rawDelta = newTarget - WallAngleSmoothed;
                            rawDelta = ((rawDelta + 180) % 360 + 360) % 360 - 180;
                            wallAngleFrom = WallAngleSmoothed;
                            wallAngleDeltaTotal = rawDelta;
                        }
                    }

                    // Gövde dönüşünü doğrudan CornerBlendSmoothed'in (yukarıda bu karede zaten
                    // güncellenmiş) ilerleme yüzdesine bağlar: araç %70 diğer kenara "geçtiyse" ön
                    // taraf da tam %70 oranında o kenarın açısına dönmüş olur. cornerBlendTarget 1 ise
                    // (duvara doğru geçiş) ilerleme CornerBlendSmoothed'in kendisi, 0 ise (duvardan
                    // uzaklaşma) tersidir (1 - CornerBlendSmoothed).
                    double angleProgress = cornerBlendTarget >= 0.5 ? CornerBlendSmoothed : (1.0 - CornerBlendSmoothed);
                    WallAngleSmoothed = wallAngleFrom + wallAngleDeltaTotal * angleProgress;

                    if (EdgeSide == 0)
                    {
                        double groundY = effectiveFloorY - halfYSmoothed;

                        // Aktif Win32 Penceresi Üzerinde Sürüş Fiziği (Window Clambering)
                        if (activeWinTop > 0 && X >= activeWinLeft && X <= activeWinRight)
                        {
                            double winTopY = activeWinTop - halfYSmoothed;
                            if (Y <= winTopY + 8) // Araç pencerenin üst seviyesindeyse orayı yer kabul et
                            {
                                groundY = winTopY;
                            }
                        }

                        if (Y < groundY)
                        {
                            VelocityY += gravity * dt;
                        }
                        else if (Y >= groundY)
                        {
                            Y = groundY;
                            if (Math.Abs(VelocityY) > 1.5)
                            {
                                // Ezilme, ÇARPMA anındaki hızdan hesaplanır - sekme sönümü
                                // uygulandıktan SONRAKİ hızdan değil. Eskiden sıra tersti: sert bir
                                // iniş hızı büyük ölçüde yuttuğu için en sert düşüşler en az eziliyordu.
                                ApplyGroundSquash(Math.Abs(VelocityY));
                                ApplySuspensionImpact(Math.Abs(VelocityY));
                                VelocityY *= bounce;
                                SquashAxisIsVertical = 1;
                            }
                            else
                            {
                                VelocityY = 0;
                            }
                            // Sürüş modunda VelocityX zaten ApplyDriveInput'taki vitese-özgü motor
                            // freni/tork mantığıyla yönetiliyor - buradaki genel zemin sürtünmesi
                            // (0.98) üzerine bindiğinde iki ayrı sürtünme sistemi sessizce çakışıp
                            // gaz tepkisini beklenenden zayıf, vites farkını (M-08/coast) fark
                            // edilmez hale getiriyordu. Sürüşte bu satır artık atlanıyor.
                            if (!IsDriven) VelocityX *= Damp(friction, dt); // Yerde sürtünme ile yavaşlama
                        }
                    }
                    else if (EdgeSide == 2)
                    {
                        // Tavan (üst kenar) tırmanma: yerçekimi ve "sekme" Y ekseninde (yukarı doğru
                        // çeker), hareket/sürtünme X ekseninde işler - duvar mantığının dikey aynası.
                        double ceilingY = halfYSmoothed;
                        if (Y > ceilingY)
                        {
                            VelocityY -= gravity * dt;
                        }
                        else
                        {
                            Y = ceilingY;
                            if (Math.Abs(VelocityY) > 1.5)
                            {
                                // Ezilme, ÇARPMA anındaki hızdan hesaplanır - sekme sönümü
                                // uygulandıktan SONRAKİ hızdan değil. Eskiden sıra tersti: sert bir
                                // iniş hızı büyük ölçüde yuttuğu için en sert düşüşler en az eziliyordu.
                                ApplyGroundSquash(Math.Abs(VelocityY));
                                ApplySuspensionImpact(Math.Abs(VelocityY));
                                VelocityY *= bounce;
                                SquashAxisIsVertical = 1;
                            }
                            else
                            {
                                VelocityY = 0;
                            }
                            if (!IsDriven) VelocityX *= Damp(friction, dt);
                        }
                    }
                    else
                    {
                        // Duvar tırmanma: yerçekimi ve sekme X ekseninde, hareket/sürtünme Y ekseninde işler
                        double wallX = EdgeSide == -1 ? halfXSmoothed : canvasWidth - halfXSmoothed;
                        bool towardWall = (EdgeSide == -1 && X > wallX) || (EdgeSide == 1 && X < wallX);

                        if (towardWall)
                        {
                            VelocityX += gravity * EdgeSide * dt;
                        }
                        else
                        {
                            X = wallX;
                            if (Math.Abs(VelocityX) > 1.5)
                            {
                                ApplyGroundSquash(Math.Abs(VelocityX));
                                ApplySuspensionImpact(Math.Abs(VelocityX));
                                VelocityX *= bounce;
                                SquashAxisIsVertical = 0;
                            }
                            else
                            {
                                VelocityX = 0;
                            }
                            VelocityY *= Damp(friction, dt); // Duvar boyunca sürtünme
                        }
                    }

                    // Köşe Sıkışma Güvenlik Ağı: bir duvar/tavan kenarındayken (EdgeSide != 0) uzunca
                    // bir süre neredeyse hiç hareket edemiyorsa (kenar geçiş eşikleri, özellikle uzun/
                    // geniş araçlarda, hiç tutmuyor olabilir) araç köşede sonsuza kadar donmuş kalır.
                    // Sürülmüyor/sürüklenmiyor/kilitli değilken bunu birkaç saniye sonra tespit edip
                    // zemine geri döndürür ve ondan uzağa doğru küçük bir itiş verir.
                    if (EdgeSide != 0 && !IsDriven && !IsDragged && !IsLocked && !intentionallyBraking)
                    {
                        double edgeSpeed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
                        if (edgeSpeed < 0.3) cornerStuckTimer += dt / 60.0;
                        else cornerStuckTimer = 0;

                        if (cornerStuckTimer > 8.0)
                        {
                            cornerStuckTimer = 0;
                            // Doğrudan "EdgeSide = 0" DEĞİL: gövde açısı da zemine döndürülmeli
                            // (bkz. ResetEdgeToGround) - aksi halde araç zeminde ters/yan kalıyordu.
                            ResetEdgeToGround();
                            Y = effectiveFloorY - Height / 2.0;
                            X = Math.Clamp(X, Width / 2.0 + 20, canvasWidth - Width / 2.0 - 20);
                            VelocityX = (Random.Shared.NextDouble() - 0.5) * 4;
                            VelocityY = 0;
                        }
                    }
                    else
                    {
                        cornerStuckTimer = 0;
                    }
                }

                // Hareket
                X += VelocityX * dt;
                Y += VelocityY * dt;
                Angle = Math.Atan2(VelocityY, VelocityX);

                // Tekerlek dönüş animasyonu: kat edilen mesafe biriktirilir (açı DEĞİL) - her
                // tekerlek kendi yarıçapına göre render sırasında gerçek yuvarlanma açısını hesaplar
                // (bkz. BuildWheelVisual). Kaynak görsel SOLA bakar; SAĞA giderken tüm görsel
                // (tekerlek dahil) yatay olarak aynalanıyor - bu yüzden mesafe burada aracın KENDİ
                // burun yönündeki (ileri) işaretli bileşeniyle birikir.
                //
                // Eskiden burada toplam hızın BÜYÜKLÜĞÜ (her zaman pozitif) kullanılıyordu; bu iki
                // gerçek hataya yol açıyordu: (1) araç havada dikey olarak DÜŞERKEN bile tekerlekler
                // hızla dönüyordu (yere temas yokken yuvarlanma olamaz), (2) geri giderken tekerlekler
                // yine ileri yönde dönüyordu. Artık yuvarlanma yalnızca temas yüzeyi boyunca alınan
                // işaretli yola bağlı: zeminde/tavanda yatay hıza, duvarda dikey hıza.
                double surfaceVel = OnVerticalWall ? VelocityY : VelocityX;

                // Havadayken (serbest düşüş / uçuş) tekerlek yuvarlanmaz - sadece ataletle çok yavaş döner.
                bool wheelsOnSurface = Type != "helicopter" && !IsFlying
                    && !IsAirborne(canvasWidth, effectiveFloorY, activeWinTop, activeWinLeft, activeWinRight);
                if (!wheelsOnSurface) surfaceVel *= 0.15;

                // Aracın burnu SOLA bakarken (aynalama yok) sola gitmek = ileri; görsel dönüş
                // saat yönünün TERSİ olmalı. Aynalama (sağa bakış) bu işareti görselde zaten ters
                // çevirdiğinden burada tek bir tutarlı kural yeterli.
                double forwardTravel = FacingLeft ? surfaceVel : -surfaceVel;
                WheelTravelDistance += forwardTravel * dt;
                // Çok uzun oturumlarda değer büyümeye devam etmesin diye periyodik olarak sarılır;
                // 2π*yarıçap birimlerinde sarma tam olmaz ama görsel olarak fark edilmez (~1 derecelik hata).
                if (WheelTravelDistance < -100_000 || WheelTravelDistance > 100_000) WheelTravelDistance %= 6.2832 * 500;

                // Görselin yönü - küçük titremeyi önlemek için belirgin bir eşik kullanılır.
                // Zeminde/tavanda yatay hıza, duvarda ise dikey hıza (tırmanma yönüne) göre belirlenir.
                if (EdgeSide == 0 || EdgeSide == 2)
                {
                    if (VelocityX > 0.3) FacingLeft = false;
                    else if (VelocityX < -0.3) FacingLeft = true;
                }
                else
                {
                    if (VelocityY > 0.3) FacingLeft = false;
                    else if (VelocityY < -0.3) FacingLeft = true;
                }

                // Tıklama animasyonu sönümlemesi
                if (IsClicked)
                {
                    ClickTime += dt;
                    if (ClickTime > 30)
                    {
                        IsClicked = false;
                        ClickTime = 0;
                    }
                    Rotation = Math.Sin(ClickTime * 0.3) * 20;
                }
                else
                {
                    Rotation *= Damp(0.9, dt);
                }
            }

            // Görsel durum güncellemeleri sürükleme dalının DIŞINDA: ikisi de yalnızca zamana bağlı
            // sönümler ve araç kullanıcının elindeyken de akmaları gerekir. `else` dalının içindeyken
            // sürüklenen araçta duygu rozeti donup ekranda asılı kalıyordu (ezilme de yarı ezik
            // hâlde çakılı kalıyordu).
            UpdateSquashSpring(dt);
            UpdateMood(dt);

            // NOT: Kenara yaklaşınca "gizlenme" davranışı kaldırıldı - artık ekran kenarları
            // gerçek bir zemin gibi tırmanılabiliyor (bkz. EdgeSide mantığı yukarıda).

            // Duvar çarpışması ve ekran sınırları - sadece zemin modundaki (tırmanmayan) ve
            // uçan araçlar için geçerli; duvara tırmanan araçlar zaten yukarıdaki EdgeSide
            // fiziğiyle o kenara hizalanıyor.
            if (EdgeSide == 0 && (X - Width / 2 < 0 || X + Width / 2 > canvasWidth))
            {
                double preImpactSpeed = Math.Abs(VelocityX);
                VelocityX *= -Math.Clamp(0.7 / Math.Sqrt(Mass), 0.25, 0.85); // ağır araç duvarda daha çok enerji yutar
                X = Math.Max(Width / 2, Math.Min(canvasWidth - Width / 2, X));
                if (preImpactSpeed > 3)
                {
                    OnWallBounce?.Invoke();
                    ApplyImpactDamage(preImpactSpeed);
                }
            }

            // Dikey sınırlar da gövdenin GERÇEK (kenara göre dönmüş) yarı-yüksekliğiyle kırpılır -
            // eskiden her durumda Height/2 kullanıldığı için duvara tırmanan (90° dönmüş, dolayısıyla
            // dikeyde Width/2 yer kaplayan) araçların üst kısmı ekranın dışına taşıyordu.
            if (Y - HalfY < 0)
            {
                VelocityY = 0;
                Y = HalfY;
            }

            double groundLimitY = effectiveFloorY - HalfY;
            if (Y > groundLimitY)
            {
                Y = groundLimitY;
                if (Type != "helicopter" && !IsFlying)
                {
                    VelocityY *= -Math.Clamp(0.45 / Math.Sqrt(Mass), 0.18, 0.75);
                }
                else
                {
                    VelocityY *= -1;
                }
            }

            // Araç-araç çarpışma fiziği: kütleye göre momentum aktarımı - hafif araç (bisiklet/motor)
            // ağır bir araca (kamyon/otobüs/tren) çarptığında büyük ölçüde geri sekip itilir, ağır
            // araç ise neredeyse etkilenmeden yoluna devam eder. Gerçek "ağırlık hissi" burada oluşur.
            if (!IsDragged && !IsSideHidden)
            {
                foreach (var other in otherVehicles)
                {
                    if (other == this || other.IsDragged || other.IsSideHidden) continue;

                    double dx = X - other.X;
                    double dy = Y - other.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double minDist = (Math.Max(Width, Height) + Math.Max(other.Width, other.Height)) / 2.0 * 0.7;

                    if (dist > 0.0001 && dist < minDist)
                    {
                        double nx = dx / dist;
                        double ny = dy / dist;
                        double totalMass = Mass + other.Mass;
                        double pushRatio = other.Mass / totalMass; // diğeri ne kadar ağırsa beni o kadar çok iter

                        double overlap = minDist - dist;
                        X += nx * overlap * pushRatio;
                        Y += ny * overlap * pushRatio * 0.3; // dikeyde daha az it - zemine gömülmeyi azalt

                        double relVX = VelocityX - other.VelocityX;
                        double relVY = VelocityY - other.VelocityY;
                        double closingSpeed = relVX * nx + relVY * ny;

                        if (closingSpeed < 0) // yaklaşıyorlarsa (çarpışma anı)
                        {
                            double impulse = -closingSpeed * pushRatio * 0.6;
                            VelocityX += nx * impulse;
                            VelocityY += ny * impulse;

                            if (Math.Abs(closingSpeed) > 3)
                            {
                                OnWallBounce?.Invoke();
                                // Hasar her iki araca da uygulanır - hafif araç (motor/bisiklet) ağır bir
                                // araca çarptığında kendisi çok daha fazla hasar alır (ApplyImpactDamage
                                // kütleye bölerek ölçekler), gerçek "küçük araç ezilir" hissi verir.
                                ApplyImpactDamage(Math.Abs(closingSpeed));
                                other.ApplyImpactDamage(Math.Abs(closingSpeed));
                            }
                        }
                    }
                }
            }

            // Helikopter uçuşu ve Uçuş Modu (VPet'teki serbest süzülme salınımı - birebir)
            // Salınım fazı kare sayısı yerine gerçek geçen süreye bağlı: 60 FPS'te "kare başına 0.05
            // radyan" = saniyede 3 radyan; kare hızı değişse de salınım hızı aynı kalsın diye.
            if ((Type == "helicopter" || IsFlying) && !IsDragged)
            {
                Y += Math.Sin(elapsedSeconds * 3.0) * 0.5 * dt;
                if (IsFlying && Type != "helicopter" && Y > effectiveFloorY * 0.65)
                {
                    // Uçuş moduna geçen kara araçları yerden yükselip süzülme irtifasına gelsin
                    VelocityY -= 0.15 * dt;
                }
            }

            // Sağlık kontrol
            if (HP <= 0)
            {
                IsDestroyed = true;
            }

            // Görsel canlılık animasyonları EN SONDA ilerletilir: rölanti titreşimi ve süspansiyon
            // o karenin NİHAİ hız/konum değerlerine bakmalı (çarpışma itmeleri ve sınır kırpmaları
            // dahil), aksi halde araç bir kare gecikmeli/yanlış durumda titrerdi.
            UpdateVisualAnimationState(dt,
                airborne: Type != "helicopter" && !IsFlying
                          && IsAirborne(canvasWidth, effectiveFloorY, activeWinTop, activeWinLeft, activeWinRight));
        }

        private void UpdateAI(List<Vehicle> otherVehicles, double dt)
        {
            // Polis Kovalamacası: kalıcı bir takip hedefi atanmışsa (rastgele "chasing" state'inden
            // bağımsız) doğrudan ona doğru gider - hız upgrade'i almış aracı gerçekten kovalar.
            if (ChaseTarget != null && !ChaseTarget.IsDestroyed)
            {
                double cdx = ChaseTarget.X - X;
                double cdy = ChaseTarget.Y - Y;
                double cdist = Math.Sqrt(cdx * cdx + cdy * cdy);
                if (cdist > 1)
                {
                    VelocityX += (cdx / cdist) * Acceleration * 1.8 * dt;
                    VelocityY += (cdy / cdist) * Acceleration * 1.8 * dt;
                }
                return;
            }

            if (TargetX > 0 && TargetY > 0)
            {
                if (UseLearningAI)
                {
                    UpdateLearningNavigation(dt);
                }
                else
                {
                    // Hedefe yönelme (Kargo toplama / teslimat için)
                    double dx = TargetX - X;
                    double dy = TargetY - Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 15)
                    {
                        VelocityX += (dx / dist) * EffectiveAcceleration * 1.5 * dt;
                        VelocityY += (dy / dist) * EffectiveAcceleration * 1.5 * dt;
                    }
                    else
                    {
                        VelocityX = 0;
                        VelocityY = 0;
                    }
                }
                return;
            }

            // Durum de değişimi eskisinden çok daha seyrek yapılır (her saniye zıplayan "dans"
            // görüntüsü yerine, uzun süreli ileri/geri devriye hareketi hakim olsun diye)
            if (Random.Shared.NextDouble() < 0.003 * dt)
            {
                double roll = Random.Shared.NextDouble();
                State = roll < 0.7 ? "wandering" : (roll < 0.85 ? "chasing" : "fleeing");
            }

            if (State == "chasing" && otherVehicles.Count > 0)
            {
                var target = otherVehicles[Random.Shared.Next(otherVehicles.Count)];
                if (target != this)
                {
                    double dx = target.X - X;
                    double dy = target.Y - Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    // dist>0 kontrolü: iki araç tam olarak aynı noktaya denk gelirse (dist==0) bölme
                    // NaN üretirdi - NaN bir kez X/Y'ye sızınca araç donup titriyor gibi görünüyor ve
                    // kenar geçiş koşulları (X<=... gibi karşılaştırmalar NaN ile hep false döner)
                    // hiç tetiklenmediğinden köşede sıkışmış/bozuk (gövde açısı NaN) kalabiliyordu.
                    if (dist > 0.01 && dist < 200)
                    {
                        VelocityX += (dx / dist) * 0.3 * dt;
                        VelocityY += (dy / dist) * 0.3 * dt;
                    }
                }
            }
            else if (State == "fleeing" && otherVehicles.Count > 0)
            {
                var target = otherVehicles[Random.Shared.Next(otherVehicles.Count)];
                if (target != this)
                {
                    double dx = target.X - X;
                    double dy = target.Y - Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 0.01 && dist < 150) // bkz. yukarıdaki "chasing" bloğundaki NaN notu
                    {
                        VelocityX -= (dx / dist) * 0.4 * dt;
                        VelocityY -= (dy / dist) * 0.4 * dt;
                    }
                }
            }
            else
            {
                // Gezinme (wandering): eskiden yön her periyotta KESİN olarak tersine dönüyordu (%50
                // ileri / %50 geri), bu da araçların sürekli aynı küçük bölgede ileri-geri gidip
                // gelmesine sebep oluyordu. Artık yeni periyotta %95 ihtimalle "ileri" (pozitif) yön
                // seçiliyor, sadece %5 ihtimalle geri dönüyor - araçlar büyük çoğunlukla tek yönde
                // ilerleyip nadiren geri dönerek çok daha canlı/gerçekçi bir gezinme sergiliyor.
                wanderTimer -= dt;
                if (wanderTimer <= 0)
                {
                    int previousDir = wanderDir;
                    wanderDir = Random.Shared.NextDouble() < 0.95 ? 1 : -1;
                    wanderTimer = 150 + Random.Shared.Next(210); // ~2.5-6 saniye (60 FPS karesi cinsinden)

                    // Yön gerçekten değiştiyse dönüş sinyalini yak - gerçek bir sürücünün manevra
                    // öncesi sinyal vermesi gibi, araçlara "niyetli" bir davranış hissi katar.
                    if (wanderDir != previousDir && EdgeSide == 0)
                        TriggerSignal(wanderDir);
                }

                if (EdgeSide == 0 || EdgeSide == 2)
                {
                    VelocityX += wanderDir * EffectiveAcceleration * dt;
                }
                else
                {
                    VelocityY += wanderDir * EffectiveAcceleration * dt;
                }
            }

            // Hız sınırı
            double maxSpeedBonus = Upgrades.Contains("speed") ? 1.2 : 1.0;
            double actualMaxSpeed = MaxSpeed * maxSpeedBonus;
            double currentSpeed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);

            if (currentSpeed > actualMaxSpeed)
            {
                VelocityX = (VelocityX / currentSpeed) * actualMaxSpeed;
                VelocityY = (VelocityY / currentSpeed) * actualMaxSpeed;
            }
        }

        /// <summary>8 pusula yönü - Q-learning ajanının eylem uzayı (bkz. UpdateLearningNavigation).</summary>
        private static readonly (double dx, double dy)[] CompassDirections =
        {
            (0, -1), (0.707, -0.707), (1, 0), (0.707, 0.707),
            (0, 1), (-0.707, 0.707), (-1, 0), (-0.707, -0.707)
        };

        /// <summary>
        /// Hedefe (kargo teslimat noktası vb.) düz vektör-yönelme yerine bir Q-learning ajanıyla
        /// gider: durum = hedefe olan açı sektörü (8) × mesafe kovası (yakın/orta/uzak, 3) = 24 durum,
        /// eylem = 8 pusula yönünde ivme. Ödül: bir önceki karara göre hedefe yaklaştıysa +1,
        /// uzaklaştıysa -1; duvara/kenara sıkışmışsa (EdgeSide != 0) ekstra ceza. Karar her karede
        /// değil, ~12 karede bir verilir - aksi halde durum sürekli değişip öğrenme sinyali gürültüye
        /// boğulurdu. Öğrenilen Q-tablosu yalnızca bu Vehicle örneğinin ömrü boyunca bellekte tutulur.
        /// </summary>
        private void UpdateLearningNavigation(double dt)
        {
            learningAgent ??= new QLearningAgent(actionCount: CompassDirections.Length);

            double dx = TargetX - X;
            double dy = TargetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < 15)
            {
                if (lastLearningState >= 0)
                    learningAgent.Learn(lastLearningState, lastLearningAction, reward: 20, nextState: EncodeLearningState(dx, dy, dist));
                VelocityX = 0;
                VelocityY = 0;
                lastLearningState = -1;
                return;
            }

            learningDecisionTimer -= dt;
            int state = EncodeLearningState(dx, dy, dist);

            if (learningDecisionTimer <= 0)
            {
                if (lastLearningState >= 0)
                {
                    double progress = lastLearningDistance - dist; // pozitifse hedefe yaklaştı
                    double reward = progress > 0 ? 1.0 : -1.0;
                    if (EdgeSide != 0) reward -= 3.0; // duvara/kenara sıkışmışsa ekstra ceza
                    learningAgent.Learn(lastLearningState, lastLearningAction, reward, state);
                }

                int action = learningAgent.ChooseAction(state);
                lastLearningState = state;
                lastLearningAction = action;
                lastLearningDistance = dist;
                learningDecisionTimer = 12;

                var (ax, ay) = CompassDirections[action];
                VelocityX += ax * EffectiveAcceleration * 1.4;
                VelocityY += ay * EffectiveAcceleration * 1.4;
            }
        }

        /// <summary>Hedefe göre açı sektörü (8) × mesafe kovası (3) = 24 ayrık durumdan biri.</summary>
        private static int EncodeLearningState(double dx, double dy, double dist)
        {
            double angle = Math.Atan2(dy, dx); // -π..π
            int sector = ((int)Math.Round(angle / (Math.PI / 4)) % 8 + 8) % 8; // 0..7
            int distBucket = dist < 80 ? 0 : dist < 250 ? 1 : 2;
            return sector * 3 + distBucket; // 0..23
        }

        /// <summary>
        /// Sert bir çarpma (duvar veya araç-araç) sonucu HP hasarı uygular. Hasar kütleye ters
        /// orantılı ölçeklenir: hafif araçlar (bisiklet/motor) aynı çarpma hızında çok daha fazla
        /// hasar alır, ağır araçlar (kamyon/tren) neredeyse çizilmeden atlatır - gerçek "küçük araç
        /// ezilir, büyük araç sağlam çıkar" hissi için.
        /// </summary>
        public void ApplyImpactDamage(double impactSpeed, double damageThreshold = 5.0, double scale = 2.2)
        {
            if (impactSpeed <= damageThreshold) return;
            int damage = (int)Math.Round(Math.Clamp((impactSpeed - damageThreshold) * scale / Math.Sqrt(Mass), 0, 45));
            if (damage <= 0) return;
            HP = Math.Max(0, HP - damage);
        }

        public void ReactToClick()
        {
            IsClicked = true;
            ClickTime = 0;
            Happiness = Math.Min(100, Happiness + 20);

            var reactions = new[] { "jump", "spin", "boost" };
            string reaction = reactions[Random.Shared.Next(reactions.Length)];

            if (reaction == "jump")
                VelocityY -= 5;
            else if (reaction == "spin")
                Rotation = 360;
            else if (reaction == "boost")
            {
                VelocityX *= 1.5;
                VelocityY *= 1.5;
            }
        }

        public bool IsPointInside(double px, double py)
        {
            double dx = px - X;
            double dy = py - Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            return distance < Math.Max(Width, Height);
        }

        public AutoMotionData Serialize()
        {
            return new AutoMotionData
            {
                X = X,
                Y = Y,
                Type = Type,
                HP = HP,
                Fuel = Fuel,
                Mileage = Mileage,
                TireWear = TireWear,
                Happiness = Happiness,
                Upgrades = new List<string>(Upgrades),
                Decorations = new List<string>(Decorations),
                VelocityX = VelocityX,
                VelocityY = VelocityY,
                Name = Name,
                IsFlying = IsFlying,
                IsLocked = IsLocked,
                TrailerType = TrailerType,
                Brand = Brand,
                Scale = Scale,
                NeonColor = NeonColor,
                Dirtiness = Dirtiness
            };
        }

        public void Deserialize(AutoMotionData data)
        {
            X = data.X;
            Y = data.Y;
            HP = data.HP;
            Fuel = data.Fuel;
            Mileage = data.Mileage;
            TireWear = data.TireWear;
            Happiness = data.Happiness;
            Upgrades = new List<string>(data.Upgrades);
            Decorations = new List<string>(data.Decorations);
            VelocityX = data.VelocityX;
            VelocityY = data.VelocityY;
            Name = data.Name ?? "";
            IsFlying = data.IsFlying;
            IsLocked = data.IsLocked;
            TrailerType = data.TrailerType ?? "";
            // Marka adlı eski varyant anahtarları ("bmw", "scania"...) yüklenirken marka-nötr güncel
            // karşılıklarına çevrilir (bkz. VehicleVisuals.MigrateBrandKey). Değer burada
            // normalleştirildiği için araç bir sonraki kaydetmede zaten yeni anahtarla yazılır.
            Brand = VehicleVisuals.MigrateBrandKey(Type, data.Brand ?? "");
            Scale = data.Scale > 0 ? data.Scale : 1.0;
            NeonColor = string.IsNullOrEmpty(data.NeonColor) ? NeonColor : data.NeonColor;
            Dirtiness = data.Dirtiness;
        }
    }

    public class AutoMotionData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Type { get; set; }
        public int HP { get; set; }
        public double Fuel { get; set; }
        public double Mileage { get; set; }
        public double TireWear { get; set; }
        public double Happiness { get; set; }
        public List<string> Upgrades { get; set; } = new();
        public List<string> Decorations { get; set; } = new();
        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public string Name { get; set; } = "";
        public bool IsFlying { get; set; }
        public bool IsLocked { get; set; }
        public string TrailerType { get; set; } = "";
        public string Brand { get; set; } = "";
        public double Scale { get; set; } = 1.0;
        public string NeonColor { get; set; } = "";
        public double Dirtiness { get; set; }
    }
}
