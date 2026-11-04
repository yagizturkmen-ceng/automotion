using System;
using System.Collections.Generic;

namespace AutoMotion.Services
{
    /// <summary>
    /// Oyunun Steam'den ihtiyaç duyduğu her şey. Somut Steamworks çağrıları bu arayüzün ARKASINDA
    /// kalır; oyun kodu hiçbir zaman doğrudan Steamworks tiplerine dokunmaz.
    ///
    /// Neden arayüz: Steam çalışmıyorken (geliştirme sırasında, oyun Steam dışından başlatıldığında
    /// ya da istemci kapalıyken) oyunun BİREBİR aynı çalışması gerekiyor. Bu, boş bir uygulama
    /// (<see cref="NullSteamBridge"/>) ile garanti altına alınıyor ve test edilebiliyor.
    /// </summary>
    public interface ISteamBridge
    {
        bool IsAvailable { get; }
        string PlayerName { get; }

        void UnlockAchievement(string achievementId);
        void SetStat(string statName, int value);
        void SetRichPresence(string key, string value);

        /// <summary>Steam geri çağrılarını işler - düzenli aralıklarla çağrılmalı.</summary>
        void RunCallbacks();

        void Shutdown();
    }

    /// <summary>Steam yokken kullanılan boş uygulama: her çağrı sessizce yutulur, oyun etkilenmez.</summary>
    public sealed class NullSteamBridge : ISteamBridge
    {
        public bool IsAvailable => false;
        public string PlayerName => "";

        public void UnlockAchievement(string achievementId) { }
        public void SetStat(string statName, int value) { }
        public void SetRichPresence(string key, string value) { }
        public void RunCallbacks() { }
        public void Shutdown() { }
    }

    /// <summary>
    /// Steam entegrasyonunun tek giriş noktası.
    ///
    /// KURULUM NOTLARI (yayın öncesi yapılacaklar):
    ///  • <see cref="AppId"/> Steam iş ortağı panelinden alınan gerçek App ID ile değiştirilmeli.
    ///    Şu anki değer 480 = Valve'ın herkese açık test uygulaması "Spacewar"; gerçek bir App ID
    ///    alınana kadar entegrasyonu uçtan uca denemeye yarar.
    ///  • `steam_appid.txt` dosyası exe'nin yanında bulunmalı (geliştirme için). Steam üzerinden
    ///    başlatılan YAYIN sürümünde bu dosya OLMAMALI - aksi halde sahiplik kontrolü atlanabilir.
    ///  • Native `steam_api64.dll` çıktı klasörüne kopyalanmalı (Steamworks SDK'sından gelir).
    ///  • Başarım API adları iş ortağı panelinde <see cref="ToSteamAchievementName"/> ile
    ///    ÜRETİLEN adlarla birebir aynı tanımlanmalı (ör. "ACH_FIRST_VEHICLE").
    ///  • Kayıt senkronu için Steam **Auto-Cloud** kullanılması planlanıyor: iş ortağı panelinde
    ///    `%APPDATA%\AutoMotion\Saves` yolu tanımlanır, KOD DEĞİŞİKLİĞİ GEREKMEZ. (ISteamRemoteStorage
    ///    API'si SaveManager'ın tamamen yeniden yazılmasını gerektirirdi - bilinçli olarak seçilmedi.)
    /// </summary>
    public static class SteamService
    {
        /// <summary>Valve'ın açık test App ID'si (Spacewar). Yayın öncesi gerçek App ID ile değiştirilecek.</summary>
        public const uint AppId = 480;

        private static ISteamBridge _bridge = new NullSteamBridge();

        /// <summary>Etkin köprü. Steam başlatılamadıysa boş uygulamadır - çağıran taraf ayrım yapmaz.</summary>
        public static ISteamBridge Bridge => _bridge;

        public static bool IsAvailable => _bridge.IsAvailable;

        /// <summary>Son başlatma denemesinin sonucu (tanılama/loglama için).</summary>
        public static string LastStatus { get; private set; } = "başlatılmadı";

        /// <summary>
        /// Steam'i başlatmayı dener. BAŞARISIZLIK NORMAL BİR DURUMDUR: Steam istemcisi kapalıysa,
        /// oyun Steam dışından çalıştırıldıysa veya native DLL yoksa sessizce boş köprüye düşülür ve
        /// oyun tam olarak eskisi gibi çalışır.
        /// </summary>
        public static void Initialize(Func<ISteamBridge> bridgeFactory = null)
        {
            try
            {
                var bridge = (bridgeFactory ?? DefaultFactory)();
                if (bridge != null && bridge.IsAvailable)
                {
                    _bridge = bridge;
                    LastStatus = "bağlandı";
                    return;
                }

                _bridge = new NullSteamBridge();
                LastStatus = "Steam bulunamadı - çevrimdışı çalışılıyor";
            }
            catch (Exception ex)
            {
                // DllNotFoundException (steam_api64.dll yok) dahil HER hata burada yakalanır;
                // Steam'in yokluğu asla oyunun açılmasını engellememeli.
                _bridge = new NullSteamBridge();
                LastStatus = "Steam başlatılamadı: " + ex.Message;
                System.Diagnostics.Debug.WriteLine(LastStatus);
            }
        }

        private static ISteamBridge DefaultFactory() => SteamworksBridge.TryCreate();

        public static void Shutdown()
        {
            try { _bridge.Shutdown(); }
            catch { /* kapanışta Steam hatası uygulamayı engellememeli */ }
            finally
            {
                _bridge = new NullSteamBridge();
                LastStatus = "kapatıldı";
            }
        }

        /// <summary>Test/geliştirme için köprüyü doğrudan değiştirir.</summary>
        internal static void SetBridgeForTest(ISteamBridge bridge)
        {
            _bridge = bridge ?? new NullSteamBridge();
        }

        // ------------------------------------------------------------------ oyun tarafı yardımcıları

        /// <summary>Oyun içi başarım kimliğini ("first_vehicle") Steam API adına ("ACH_FIRST_VEHICLE")
        /// çevirir. İş ortağı panelindeki adlar bu kuralla tanımlanmalı.</summary>
        public static string ToSteamAchievementName(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId)) return "";
            return "ACH_" + achievementId.Trim().ToUpperInvariant();
        }

        /// <summary>Steam istatistik adları (iş ortağı panelinde aynı adlarla tanımlanmalı).</summary>
        public static class Stats
        {
            public const string TotalVehicles = "total_vehicles";
            public const string TotalEarnings = "total_earnings";
            public const string TotalDistanceKm = "total_distance_km";
        }

        /// <summary>Steam arkadaş listesinde görünen durum metni için anahtar/değer çiftleri.
        /// "steam_display" değeri, iş ortağı panelindeki yerelleştirme belirteçlerine karşılık gelir.</summary>
        public static void UpdateRichPresence(int vehicleCount, int money)
        {
            _bridge.SetRichPresence("vehicles", vehicleCount.ToString());
            _bridge.SetRichPresence("money", money.ToString());
            _bridge.SetRichPresence("steam_display", "#Status_Fleet");
        }
    }
}
