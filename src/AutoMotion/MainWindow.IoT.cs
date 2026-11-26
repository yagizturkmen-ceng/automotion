using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoMotion.Expansions;
using AutoMotion.Models;
using AutoMotion.Services;
using AutoMotion.Views;
using AutoMotion.Utilities;
using Microsoft.Win32;

namespace AutoMotion
{
    // MQTT telemetri bağlantısı ve tercihleri.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        // IoT/MQTT Telemetri: isteğe bağlı, varsayılan kapalı - bkz. Services/IoTTelemetryService.cs
        // için kapsam/kısıtlama notları (gerçek donanım/broker gerektirir, bu ortamda doğrulanamaz).
        private readonly IoTTelemetryService iotService = new();
        private DateTime lastIotPublishAt = DateTime.MinValue;

        private async void IoTConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            PlayUiClick();

            if (iotService.IsConnected)
            {
                await iotService.DisconnectAsync();
                IoTStatusTxt.Text = Loc.T("ui.bagli_degil");
                IoTConnectBtn.Content = Loc.T("ui.baglan");
                ShowToast(Loc.T("msg.iot_baglantisi_kesildi"));
                return;
            }

            bool useTls = IoTUseTlsCheck.IsChecked == true;
            var (host, port) = MqttConnectionOptions.ParseAddress(IoTBrokerBox.Text, useTls);

            if (string.IsNullOrWhiteSpace(host))
            {
                ShowToast(Loc.T("msg.broker_adresi_bos_olamaz"));
                return;
            }

            var options = new MqttConnectionOptions
            {
                Host = host,
                Port = port,
                Topic = IoTTopicBox.Text,
                UseTls = useTls,
                AllowUntrustedCertificate = useTls && IoTAllowUntrustedCheck.IsChecked == true,
                Username = IoTUsernameBox.Text?.Trim() ?? "",
                Password = IoTPasswordBox.Password ?? ""
            };

            // MQTT'de kullanıcı adı/parola CONNECT paketinde AÇIK METİN taşınır - TLS'siz bir
            // bağlantıda broker parolası ağı dinleyen herkese görünür. Bağlantı engellenmez
            // (kullanıcının kendi yerel ağı olabilir) ama sessiz de kalınmaz.
            if (options.SendsCredentialsInClear)
                ShowToast(Loc.T("msg.iot_kimlik_tlssiz_uyari"));

            IoTStatusTxt.Text = Loc.T("ui.iot_connecting");
            bool connected = await iotService.ConnectAsync(options);

            if (connected)
            {
                IoTStatusTxt.Text = Loc.T(options.UseTls ? "ui.iot_connected_tls" : "ui.iot_connected", host, port);
                IoTConnectBtn.Content = "📡 Kes";
                ShowToast(Loc.T("msg.iot_broker_a_baglanildi_filo_telemetrisi"));
                SaveIoTPreferences();
            }
            else
            {
                // Eskiden her başarısızlık aynı jenerik mesajdı; artık broker'ın/TLS katmanının
                // söylediği asıl sebep de gösteriliyor (yanlış parola mı, sertifika mı, yanlış port mu).
                IoTStatusTxt.Text = string.IsNullOrEmpty(iotService.LastError)
                    ? Loc.T("ui.iot_connect_failed")
                    : Loc.T("ui.iot_connect_failed") + ": " + iotService.LastError;
                ShowToast(Loc.T("msg.broker_a_baglanilamadi_adresi_portu_kontrol"));
            }
        }

        /// <summary>TLS kapatılınca sertifika doğrulamasını atlama seçeneği anlamsızlaşır - devre
        /// dışı bırakılıp temizlenir ki kullanıcı "atlıyorum" sanmasın.</summary>
        private void IoTUseTlsCheck_Click(object sender, RoutedEventArgs e)
        {
            bool useTls = IoTUseTlsCheck.IsChecked == true;
            IoTAllowUntrustedCheck.IsEnabled = useTls;
            if (!useTls) IoTAllowUntrustedCheck.IsChecked = false;
            PlayUiClick();
        }

        /// <summary>Broker adresi/konusu/kullanıcı adı ve TLS tercihleri saklanır; PAROLA SAKLANMAZ
        /// (bkz. MqttConnectionOptions). Böylece kullanıcı her açılışta adresi yeniden yazmaz ama
        /// düz metin bir ayar dosyasında parola tutulmuş olmaz.</summary>
        private void SaveIoTPreferences()
        {
            settings.Current.IoTBrokerAddress = IoTBrokerBox.Text?.Trim() ?? "";
            settings.Current.IoTTopic = IoTTopicBox.Text?.Trim() ?? "";
            settings.Current.IoTUsername = IoTUsernameBox.Text?.Trim() ?? "";
            settings.Current.IoTUseTls = IoTUseTlsCheck.IsChecked == true;
            settings.Current.IoTAllowUntrustedCertificate = IoTAllowUntrustedCheck.IsChecked == true;
            settings.Save();
        }

        /// <summary>Kaydedilmiş IoT alanlarını açılışta geri yükler (parola hariç - o hiç saklanmaz).</summary>
        private void ApplyPersistedIoTPreferences()
        {
            var prefs = settings.Current;

            if (!string.IsNullOrWhiteSpace(prefs.IoTBrokerAddress)) IoTBrokerBox.Text = prefs.IoTBrokerAddress;
            if (!string.IsNullOrWhiteSpace(prefs.IoTTopic)) IoTTopicBox.Text = prefs.IoTTopic;
            IoTUsernameBox.Text = prefs.IoTUsername ?? "";

            IoTUseTlsCheck.IsChecked = prefs.IoTUseTls;
            IoTAllowUntrustedCheck.IsEnabled = prefs.IoTUseTls;
            IoTAllowUntrustedCheck.IsChecked = prefs.IoTUseTls && prefs.IoTAllowUntrustedCertificate;
        }

        private async void PublishIotTelemetryIfDue()
        {
            if (!iotService.IsConnected) return;
            if ((DateTime.Now - lastIotPublishAt).TotalSeconds < 15) return;
            lastIotPublishAt = DateTime.Now;

            await iotService.PublishFleetTelemetryAsync(
                gameCore.AutoMotions.Count, gameCore.GameData.Money, gameCore.GameData.TotalMileage);
        }
    }
}
