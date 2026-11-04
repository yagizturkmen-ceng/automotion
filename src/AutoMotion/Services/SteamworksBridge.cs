using System;
using System.Collections.Generic;
using Steamworks;

namespace AutoMotion.Services
{
    /// <summary>
    /// <see cref="ISteamBridge"/>'in gerçek Steamworks uygulaması. Steamworks.NET'e dokunan TEK
    /// dosya burasıdır - oyunun geri kalanı yalnızca arayüzü görür.
    ///
    /// Tüm çağrılar try/catch ile korunur: Steam istemcisi oyun çalışırken kapatılabilir ve o andan
    /// sonra API çağrıları hata verebilir. Böyle bir durumda oyun çökmez, yalnızca Steam
    /// özellikleri sessizce devre dışı kalır.
    /// </summary>
    public sealed class SteamworksBridge : ISteamBridge
    {
        private bool _initialized;

        private SteamworksBridge() { }

        /// <summary>
        /// Steam'i başlatmayı dener; başarısızsa null döner (çağıran taraf boş köprüye düşer).
        ///
        /// SteamAPI.Init() şu durumlarda false döner ve bu DURUMLARIN HEPSİ NORMALDİR:
        ///  • Steam istemcisi çalışmıyor
        ///  • Kullanıcı bu oyuna sahip değil
        ///  • steam_appid.txt yok ve oyun Steam üzerinden başlatılmadı
        /// </summary>
        public static ISteamBridge TryCreate()
        {
            try
            {
                if (!SteamAPI.Init()) return null;

                var bridge = new SteamworksBridge { _initialized = true };

                // Sunucudan bu oyuncunun mevcut başarım/istatistik durumunu iste - başarım
                // açmadan önce yerel önbelleğin dolu olması gerekir.
                try { SteamUserStats.RequestCurrentStats(); } catch { }

                return bridge;
            }
            catch (DllNotFoundException)
            {
                // steam_api64.dll çıktı klasöründe yok - geliştirme ortamında beklenen durum.
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Steam başlatılamadı: " + ex.Message);
                return null;
            }
        }

        public bool IsAvailable => _initialized;

        public string PlayerName
        {
            get
            {
                try { return _initialized ? SteamFriends.GetPersonaName() : ""; }
                catch { return ""; }
            }
        }

        /// <summary>Başarımı açar ve HEMEN sunucuya işler (StoreStats). Zaten açık bir başarımı
        /// tekrar açmak zararsızdır - Steam bunu yok sayar.</summary>
        public void UnlockAchievement(string achievementId)
        {
            if (!_initialized) return;

            try
            {
                string name = SteamService.ToSteamAchievementName(achievementId);
                if (string.IsNullOrEmpty(name)) return;

                SteamUserStats.SetAchievement(name);
                SteamUserStats.StoreStats();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Başarım gönderilemedi: " + ex.Message);
            }
        }

        /// <summary>İstatistik değerini günceller. StoreStats burada ÇAĞRILMAZ: istatistikler sık
        /// güncelleniyor ve her seferinde sunucuya yazmak gereksiz ağ trafiği olurdu; başarım
        /// açıldığında ya da oyun kapanırken (Shutdown) topluca işlenir.</summary>
        public void SetStat(string statName, int value)
        {
            if (!_initialized) return;
            try { SteamUserStats.SetStat(statName, value); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("İstatistik yazılamadı: " + ex.Message); }
        }

        public void SetRichPresence(string key, string value)
        {
            if (!_initialized) return;
            try { SteamFriends.SetRichPresence(key, value); }
            catch { /* Rich Presence kritik değil */ }
        }

        public void RunCallbacks()
        {
            if (!_initialized) return;
            try { SteamAPI.RunCallbacks(); }
            catch { /* istemci kapanmış olabilir */ }
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;

            try { SteamUserStats.StoreStats(); } catch { }
            try { SteamAPI.Shutdown(); } catch { }
        }
    }
}
