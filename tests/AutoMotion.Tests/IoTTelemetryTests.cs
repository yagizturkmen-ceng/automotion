using System;
using System.Linq;
using System.Security.Authentication;
using AutoMotion.Services;
using MQTTnet.Client;

namespace AutoMotion.Tests;

/// <summary>
/// IoT/MQTT bağlantısının GÜVENLİK kurulumu (P1-6).
///
/// Bağlantı eskiden her zaman şifresiz ve kimlik doğrulamasızdı. Gerçek bir broker olmadan
/// "bağlanabiliyor muyuz" test edilemez - ama asıl risk orada değil: TLS'in gerçekten açık olup
/// olmadığı, hangi protokol sürümlerine izin verildiği ve sertifika doğrulamasının açık kalıp
/// kalmadığı. Bunların hepsi istemci seçeneklerinde görünür, o yüzden seçenek kurulumu bağlantıdan
/// ayrıldı (IoTTelemetryService.BuildClientOptions) ve burada doğrudan doğrulanıyor.
/// </summary>
public class IoTTelemetryTests
{
    private static MqttClientTcpOptions TcpOptionsFor(MqttConnectionOptions options)
        => Assert.IsType<MqttClientTcpOptions>(IoTTelemetryService.BuildClientOptions(options).ChannelOptions);

    // ---------------------------------------------------------------- adres çözümleme

    [Theory]
    [InlineData("localhost:1883", false, "localhost", 1883)]
    [InlineData("broker.ev.local:8883", true, "broker.ev.local", 8883)]
    [InlineData("  10.0.0.5:1884  ", false, "10.0.0.5", 1884)]
    public void ParseAddress_ReadsExplicitPort(string text, bool useTls, string host, int port)
    {
        var parsed = MqttConnectionOptions.ParseAddress(text, useTls);

        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
    }

    /// <summary>Port yazılmadığında standart port TLS tercihine göre seçilmeli - kullanıcı TLS'i
    /// açıp aynı adresi bırakınca sessizce şifresiz porta bağlanmaya çalışmasın.</summary>
    [Theory]
    [InlineData("localhost", false, 1883)]
    [InlineData("localhost", true, 8883)]
    public void ParseAddress_FallsBackToStandardPort_ForTheChosenTransport(string text, bool useTls, int expectedPort)
    {
        Assert.Equal(expectedPort, MqttConnectionOptions.ParseAddress(text, useTls).Port);
    }

    [Theory]
    [InlineData("localhost:0")]
    [InlineData("localhost:99999")]
    [InlineData("localhost:abc")]
    public void ParseAddress_IgnoresInvalidPort_AndKeepsWholeTextAsHost(string text)
    {
        var parsed = MqttConnectionOptions.ParseAddress(text, useTls: false);

        Assert.Equal(text, parsed.Host);
        Assert.Equal(MqttConnectionOptions.DefaultPlainPort, parsed.Port);
    }

    // ---------------------------------------------------------------- TLS

    [Fact]
    public void PlainConnection_DoesNotEnableTls()
    {
        var tcp = TcpOptionsFor(new MqttConnectionOptions { Host = "localhost", Port = 1883 });

        Assert.False(tcp.TlsOptions.UseTls);
    }

    [Fact]
    public void TlsConnection_EnablesTls_AndRefusesObsoleteProtocols()
    {
        var tcp = TcpOptionsFor(new MqttConnectionOptions
        {
            Host = "broker.ev.local",
            Port = MqttConnectionOptions.DefaultTlsPort,
            UseTls = true
        });

        Assert.True(tcp.TlsOptions.UseTls);

        // Yalnızca TLS 1.2/1.3 - eşitlik kontrolü, kırılmış eski sürümlerin (SSL3, TLS 1.0/1.1)
        // listede OLMADIĞINI da kanıtlar.
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, tcp.TlsOptions.SslProtocol);
    }

    /// <summary>
    /// En kritik varsayılan: TLS açıkken sunucu sertifikası DOĞRULANIR. Bu sessizce kapalı olsaydı
    /// TLS yalnızca şifreleme sağlar, ortadaki-adam saldırısına karşı hiçbir koruma vermezdi.
    /// </summary>
    [Fact]
    public void TlsConnection_ValidatesServerCertificate_ByDefault()
    {
        var tcp = TcpOptionsFor(new MqttConnectionOptions { Host = "broker.ev.local", UseTls = true });

        Assert.False(tcp.TlsOptions.AllowUntrustedCertificates);
        Assert.False(tcp.TlsOptions.IgnoreCertificateChainErrors);
        Assert.False(tcp.TlsOptions.IgnoreCertificateRevocationErrors);
    }

    /// <summary>Kaçış kapısı yalnızca kullanıcı açıkça istediğinde açılır (kendinden imzalı ev
    /// broker'ı senaryosu).</summary>
    [Fact]
    public void UntrustedCertificates_AreAcceptedOnlyWhenExplicitlyAllowed()
    {
        var tcp = TcpOptionsFor(new MqttConnectionOptions
        {
            Host = "broker.ev.local",
            UseTls = true,
            AllowUntrustedCertificate = true
        });

        Assert.True(tcp.TlsOptions.AllowUntrustedCertificates);
        Assert.True(tcp.TlsOptions.IgnoreCertificateChainErrors);
    }

    // ---------------------------------------------------------------- kimlik doğrulama

    [Fact]
    public void Credentials_AreSent_WhenUsernameIsGiven()
    {
        var built = IoTTelemetryService.BuildClientOptions(new MqttConnectionOptions
        {
            Host = "broker.ev.local",
            UseTls = true,
            Username = "filo",
            Password = "s3cret"
        });

        Assert.NotNull(built.Credentials);
        Assert.Equal("filo", built.Credentials!.GetUserName(built));
        Assert.Equal("s3cret", System.Text.Encoding.UTF8.GetString(built.Credentials.GetPassword(built)));
    }

    [Fact]
    public void NoCredentials_AreSent_WhenUsernameIsEmpty()
    {
        var built = IoTTelemetryService.BuildClientOptions(new MqttConnectionOptions { Host = "localhost" });

        Assert.Null(built.Credentials);
    }

    /// <summary>MQTT kimlik bilgilerini CONNECT paketinde AÇIK METİN taşır; TLS kapalıyken bu
    /// durumun arayüzde uyarılabilmesi için ayrı bir bayrak var.</summary>
    [Theory]
    [InlineData("filo", false, true)]   // kimlik var, TLS yok -> uyarılmalı
    [InlineData("filo", true, false)]   // kimlik var, TLS var -> sorun yok
    [InlineData("", false, false)]      // anonim -> uyarılacak bir şey yok
    public void SendsCredentialsInClear_FlagsTheRiskyCombination(string username, bool useTls, bool expected)
    {
        var options = new MqttConnectionOptions { Host = "localhost", Username = username, UseTls = useTls };

        Assert.Equal(expected, options.SendsCredentialsInClear);
    }

    // ---------------------------------------------------------------- servis davranışı

    [Fact]
    public void NewService_IsNeitherConnectedNorEncrypted()
    {
        var service = new IoTTelemetryService();

        Assert.False(service.IsConnected);
        Assert.False(service.IsEncrypted);
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task ConnectAsync_RejectsMissingOptions()
    {
        var service = new IoTTelemetryService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConnectAsync(null!));
    }

    /// <summary>Yayın çağrısı bağlantı yokken sessizce geçilmeli - telemetri isteğe bağlı bir ek
    /// özellik, oyunun döngüsünü hiçbir koşulda kesmemeli.</summary>
    [Fact]
    public async Task PublishAsync_IsSafeWithoutAConnection()
    {
        var service = new IoTTelemetryService();

        var ex = await Record.ExceptionAsync(() => service.PublishFleetTelemetryAsync(3, 500, 12.5));

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------- gizlilik

    /// <summary>
    /// REGRESYON KORUMASI: broker parolası ayar dosyasına ASLA yazılmamalı. settings.json düz
    /// metindir; parolayı oraya koymak TLS eklemenin amacını baştan bozardı. Biri ileride
    /// "kolaylık olsun" diye bir Password alanı eklerse bu test uyarır.
    /// </summary>
    [Fact]
    public void AppSettings_NeverStoresAPassword()
    {
        var passwordish = typeof(AppSettings).GetProperties()
            .Where(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.True(passwordish.Count == 0,
            "Ayar dosyası düz metindir, kimlik bilgisi taşımamalı: " + string.Join(", ", passwordish));
    }

    [Fact]
    public void IoTPreferences_RoundTripThroughSettings_WithoutAPassword()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoMotionIoTTests_" + Guid.NewGuid());
        try
        {
            using (var settings = new SettingsService(dir))
            {
                settings.Current.IoTBrokerAddress = "broker.ev.local:8883";
                settings.Current.IoTTopic = "ev/filo";
                settings.Current.IoTUsername = "filo";
                settings.Current.IoTUseTls = true;
                settings.Save();
            }

            using var reloaded = new SettingsService(dir);
            Assert.Equal("broker.ev.local:8883", reloaded.Current.IoTBrokerAddress);
            Assert.Equal("ev/filo", reloaded.Current.IoTTopic);
            Assert.Equal("filo", reloaded.Current.IoTUsername);
            Assert.True(reloaded.Current.IoTUseTls);
            Assert.False(reloaded.Current.IoTAllowUntrustedCertificate);

            // Dosyanın kendisinde parolaya benzer hiçbir alan olmamalı.
            string json = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "settings.json"));
            Assert.DoesNotContain("assword", json);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
