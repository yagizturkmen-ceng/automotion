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
    // Ekran görüntüsü ve GIF kaydı.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        // === Ekran Görüntüsü / GIF Kaydet ===
        // Paylaşım özelliği (D-08, önceden ertelenmiş) - dış bağımlılık gerektirmez: PNG için
        // PngBitmapEncoder, GIF için .NET'in yerleşik GifBitmapEncoder'ı kullanılır. Pencerenin
        // KENDİSİ (WPF görsel ağacı) yakalanır - şeffaf temada arka plan da şeffaf kalır (gerçek
        // masaüstü değil, boş piksel olarak kaydedilir).
        private bool isRecordingGif;
        private List<BitmapSource> gifFrames;
        private DispatcherTimer gifCaptureTimer;

        private RenderTargetBitmap CaptureWindowBitmap()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = Math.Max(1, (int)(ActualWidth * dpi.DpiScaleX));
            int h = Math.Max(1, (int)(ActualHeight * dpi.DpiScaleY));
            var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(this);
            return rtb;
        }

        private string GetScreenshotDirectory()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion", "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        private void ScreenshotBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(GetScreenshotDirectory(), $"automotion_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(CaptureWindowBitmap()));
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                    encoder.Save(fs);
                ShowToast(Loc.T("msg.ekran_goruntusu_kaydedildi") + path);
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.ekran_goruntusu_alinamadi") + ex.Message);
            }
            PlayUiClick();
        }

        private void GifRecordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (isRecordingGif) return; // kayıt zaten sürüyor, tekrar tıklamayı yok say
            isRecordingGif = true;
            gifFrames = new List<BitmapSource>();
            GifRecordBtn.Content = "⏺️";
            ShowToast(Loc.T("msg.3_saniyelik_gif_kaydi_basladi"));
            PlayUiClick();

            const int totalFrames = 30; // ~100ms aralıkla 30 kare = 3 saniye
            int framesLeft = totalFrames;
            gifCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            gifCaptureTimer.Tick += (s, args) =>
            {
                gifFrames.Add(CaptureWindowBitmap());
                framesLeft--;
                if (framesLeft <= 0)
                {
                    gifCaptureTimer.Stop();
                    FinishGifRecording();
                }
            };
            gifCaptureTimer.Start();
        }

        private void FinishGifRecording()
        {
            isRecordingGif = false;
            GifRecordBtn.Content = "🎬";
            try
            {
                string path = System.IO.Path.Combine(GetScreenshotDirectory(), $"automotion_{DateTime.Now:yyyyMMdd_HHmmss}.gif");
                var encoder = new GifBitmapEncoder();
                foreach (var frame in gifFrames)
                {
                    // Kare gecikmesi (frame delay) - GifBitmapEncoder varsayılan olarak bunu
                    // ayarlamaz, bu yüzden çoğu görüntüleyicide GIF olması gerekenden çok hızlı
                    // oynar. "/grctlext/Delay" 1/100 saniye biriminde: 10 = 100ms/kare.
                    var metadata = new BitmapMetadata("gif");
                    metadata.SetQuery("/grctlext/Delay", (ushort)10);
                    encoder.Frames.Add(BitmapFrame.Create(frame, null, metadata, null));
                }
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                    encoder.Save(fs);
                ShowToast(Loc.T("msg.gif_kaydedildi") + path);
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.gif_kaydedilemedi") + ex.Message);
            }
            finally
            {
                gifFrames = null;
            }
        }
    }
}
