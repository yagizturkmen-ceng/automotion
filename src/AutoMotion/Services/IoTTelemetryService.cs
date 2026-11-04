using System;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace AutoMotion.Services
{
    /// <summary>
    /// Bir MQTT broker'ına bağlanma ayarları.
    ///
    /// Parola bilerek burada (bellekte) kalır ve HİÇBİR ZAMAN diske yazılmaz - bkz.
    /// SettingsService/AppSettings: adres, port, konu, kullanıcı adı ve TLS tercihleri saklanır,
    /// parola her oturumda yeniden girilir. Şifreli bir kimlik deposu (DPAPI vb.) kurmadan parolayı
    /// düz metin bir ayar dosyasında tutmak, TLS eklemenin amacını baştan bozardı.
    /// </summary>
    public sealed class MqttConnectionOptions
    {
        /// <summary>Şifresiz MQTT'nin standart portu.</summary>
        public const int DefaultPlainPort = 1883;

        /// <summary>TLS üzerinden MQTT'nin (MQTTS) standart portu.</summary>
        public const int DefaultTlsPort = 8883;

        public string Host { get; init; } = "";
        public int Port { get; init; } = DefaultPlainPort;
        public string Topic { get; init; } = DefaultTopic;

        /// <summary>Bağlantı TLS ile şifrelensin mi. Kapalıyken hem telemetri hem de kullanıcı
        /// adı/parola ağ üzerinde AÇIK METİN gider (MQTT protokolünde kimlik bilgileri CONNECT
        /// paketinde şifresiz taşınır) - bu yüzden kimlik doğrulama kullanılıyorsa TLS şarttır.</summary>
        public bool UseTls { get; init; }

        /// <summary>Sunucu sertifikasının doğrulanmasını atlar. Ev kurulumlarındaki kendinden imzalı
        /// sertifikalar için bir kaçış kapısı; açıkken TLS ortadaki-adam saldırısına karşı KORUMASIZ
        /// kalır, o yüzden varsayılan kapalı ve arayüzde açıkça uyarılıyor.</summary>
        public bool AllowUntrustedCertificate { get; init; }

        public string Username { get; init; } = "";
        public string Password { get; init; } = "";

        public const string DefaultTopic = "automotion/telemetry";

        public bool HasCredentials => !string.IsNullOrEmpty(Username);

        /// <summary>Kimlik bilgisi TLS'siz gönderiliyor mu - arayüzün kullanıcıyı uyarabilmesi için.</summary>
        public bool SendsCredentialsInClear => HasCredentials && !UseTls;

        /// <summary>"broker.ev.local:8883" gibi tek bir metni adres+porta ayırır. Port yazılmamışsa
        /// TLS tercihine göre standart port (1883 / 8883) kullanılır.</summary>
        public static (string Host, int Port) ParseAddress(string address, bool useTls)
        {
            string text = address?.Trim() ?? "";
            int defaultPort = useTls ? DefaultTlsPort : DefaultPlainPort;

            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text[(colon + 1)..], out int port) && port > 0 && port <= 65535)
                return (text[..colon], port);

            return (text, defaultPort);
        }
    }

    /// <summary>
    /// İsteğe bağlı (varsayılan kapalı) IoT/MQTT entegrasyonu: kullanıcının kendi belirttiği bir
    /// MQTT broker'ına (ör. yerel bir Home Assistant/Mosquitto kurulumu) periyodik filo telemetrisi
    /// (araç sayısı, para, toplam mesafe) JSON olarak yayınlar. Gerçek bir IoT cihazı/broker
    /// gerektirir - bu ortamda test edilebilecek fiziksel donanım/broker bulunmadığından, tüm ağ
    /// çağrıları try/catch ile korunur ve bağlantı durumu çağırana (bkz. MainWindow) bildirilir.
    /// Haptik geri bildirim KAPSAM DIŞI bırakıldı: Windows'ta jenerik haptik donanım için standart bir
    /// API yok, üretici SDK'sı ve gerçek cihaz gerektirir.
    ///
    /// GÜVENLİK (P1-6): bağlantı eskiden her zaman şifresiz ve kimlik doğrulamasızdı. Artık TLS ve
    /// kullanıcı adı/parola destekleniyor; TLS'te en az TLS 1.2 dayatılır ve sunucu sertifikası
    /// varsayılan olarak doğrulanır. Özellik yine varsayılan kapalı ve yalnızca kullanıcının kendi
    /// yazdığı adrese bağlanır.
    /// </summary>
    public class IoTTelemetryService : IAsyncDisposable
    {
        private IMqttClient mqttClient;
        private string topic = MqttConnectionOptions.DefaultTopic;

        public bool IsConnected => mqttClient?.IsConnected ?? false;

        /// <summary>Bağlantı TLS ile mi kuruldu (arayüzün durum satırında göstermesi için).</summary>
        public bool IsEncrypted { get; private set; }

        /// <summary>Son bağlantı denemesinin hata açıklaması (yoksa null). Eskiden tüm hatalar
        /// yutuluyor, kullanıcı yalnızca "bağlanılamadı" görüyordu; sertifika/kimlik hatasıyla
        /// yanlış port arasındaki farkı ayırt edemiyordu.</summary>
        public string LastError { get; private set; }

        public async Task<bool> ConnectAsync(MqttConnectionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            await DisconnectAsync();

            topic = string.IsNullOrWhiteSpace(options.Topic) ? MqttConnectionOptions.DefaultTopic : options.Topic;
            LastError = null;

            try
            {
                var factory = new MqttFactory();
                mqttClient = factory.CreateMqttClient();

                var result = await mqttClient.ConnectAsync(BuildClientOptions(options), CancellationToken.None);

                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    LastError = DescribeResultCode(result.ResultCode);
                    return false;
                }

                IsEncrypted = options.UseTls;
                return true;
            }
            catch (Exception ex)
            {
                // Ağ/TLS/kimlik hataları burada toplanır; en içteki istisna genelde asıl sebebi söyler
                // (ör. "The remote certificate is invalid").
                LastError = (ex.InnerException ?? ex).Message;
                mqttClient = null;
                IsEncrypted = false;
                return false;
            }
        }

        /// <summary>
        /// Kullanıcı ayarlarını MQTT istemci seçeneklerine çevirir. Bağlantı kurmadan çağrılabilir
        /// olması bilinçli: güvenlik kurulumunun (TLS açık mı, hangi protokol sürümleri, sertifika
        /// doğrulaması, kimlik bilgileri) gerçekten uygulandığı gerçek bir broker olmadan
        /// doğrulanabiliyor - bkz. AutoMotion.Tests/IoTTelemetryTests.
        /// </summary>
        public static MqttClientOptions BuildClientOptions(MqttConnectionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(options.Host, options.Port)
                .WithClientId("AutoMotion_" + Guid.NewGuid().ToString("N")[..8])
                .WithTimeout(TimeSpan.FromSeconds(5));

            if (options.HasCredentials)
                builder = builder.WithCredentials(options.Username, options.Password);

            if (options.UseTls)
            {
                builder = builder.WithTlsOptions(tls =>
                {
                    tls.UseTls(true);

                    // Eski/kırılmış sürümler (SSL 3.0, TLS 1.0/1.1) dışarıda bırakılır.
                    tls.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);

                    // Sertifika doğrulaması varsayılan olarak AÇIK; yalnızca kullanıcı açıkça
                    // istediğinde (kendinden imzalı ev broker'ı) devre dışı bırakılır.
                    tls.WithAllowUntrustedCertificates(options.AllowUntrustedCertificate);
                    tls.WithIgnoreCertificateChainErrors(options.AllowUntrustedCertificate);
                    tls.WithIgnoreCertificateRevocationErrors(options.AllowUntrustedCertificate);
                });
            }

            return builder.Build();
        }

        /// <summary>Broker'ın reddetme sebebini kullanıcının anlayacağı bir cümleye çevirir.
        /// Bu metinler arayüzde görüldüğü için sözlükten geçer; eşlenmemiş kodlarda ham kod adı
        /// döner (teşhis için yine de anlamlıdır).</summary>
        private static string DescribeResultCode(MqttClientConnectResultCode code) => code switch
        {
            MqttClientConnectResultCode.BadUserNameOrPassword => Loc.T("err.mqtt_bad_credentials"),
            MqttClientConnectResultCode.NotAuthorized => Loc.T("err.mqtt_not_authorized"),
            MqttClientConnectResultCode.ServerUnavailable => Loc.T("err.mqtt_server_unavailable"),
            MqttClientConnectResultCode.UnsupportedProtocolVersion => Loc.T("err.mqtt_unsupported_protocol"),
            _ => code.ToString()
        };

        public async Task PublishFleetTelemetryAsync(int vehicleCount, int money, double totalMileageKm)
        {
            if (mqttClient == null || !mqttClient.IsConnected) return;

            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    vehicleCount,
                    money,
                    totalMileageKm = (int)totalMileageKm,
                    timestamp = DateTime.UtcNow
                });

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .Build();

                await mqttClient.PublishAsync(message, CancellationToken.None);
            }
            catch
            {
                // Ağ/broker hatası sessizce yutulur - IoT yayını isteğe bağlı bir ek özellik,
                // başarısızlığı oyunun geri kalanını etkilememeli.
            }
        }

        public async Task DisconnectAsync()
        {
            IsEncrypted = false;
            if (mqttClient == null) return;
            try
            {
                if (mqttClient.IsConnected)
                    await mqttClient.DisconnectAsync();
            }
            catch { }
            finally
            {
                mqttClient.Dispose();
                mqttClient = null;
            }
        }

        public async ValueTask DisposeAsync() => await DisconnectAsync();
    }
}
