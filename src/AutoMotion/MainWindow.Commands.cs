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
    // Üst çubuk komutları: tema, ses, kayıtlar, araç ekleme, mağaza, araç işlemleri.
    // MainWindow.xaml.cs ~6.000 satıra ulaştığı için sorumluluk bazında partial dosyalara bölündü;
    // üyeler olduğu gibi (sırası ve yorumlarıyla) taşındı, davranış değişikliği yok.
    public partial class MainWindow
    {
        // Button handlers
        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            currentTheme = (currentTheme + 1) % themeEngine.ThemeCount;
            themeEngine.SetTheme(currentTheme);

            settings.Current.ThemeIndex = currentTheme;
            settings.SaveDeferred(); // temalar arasında hızlıca gezinirken her tıklamada yazma olmasın
            PlayUiClick();
        }

        private void SoundBtn_Click(object sender, RoutedEventArgs e)
        {
            soundEngine.ToggleMute();
            SyncSoundButton();

            settings.Current.Muted = soundEngine.IsMuted;
            settings.Save();
            PlayUiClick();
        }

        /// <summary>Ses butonunu motorun sustur durumuna göre tazeler.
        /// Buton 36x36 sabit boyutlu tek-ikon kutusu - çok kelimeli metin ("Ses Kapalı") içine
        /// sığmayıp taşıyor ve görünmüyordu. Durum bu yüzden ikon + ToolTip ile gösteriliyor.</summary>
        private void SyncSoundButton()
        {
            SoundBtn.Content = soundEngine.IsMuted ? "🔇" : "🔊";
            SoundBtn.ToolTip = Loc.T(soundEngine.IsMuted ? "ui.sound_state_muted" : "ui.sound_state_on");
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var saveName = $"AutoMotion_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

            // Kaydetme başarısız olabilir (disk dolu, klasör salt-okunur, dosya kilitli...). Eskiden
            // dönüş değeri hiç kontrol edilmiyor ve her durumda "✅ Oyun kaydedildi" deniyordu -
            // kullanıcı kaydettiğini sanıp ilerlemesini kaybediyordu.
            if (saveManager.Save(saveName, gameCore.Save()))
            {
                soundEngine.PlaySuccess();
                await ShowDialogAsync(Loc.T("msg.game_saved", saveName), Loc.T("dlg.success"), false);
            }
            else
            {
                await ShowDialogAsync(
                    Loc.T("msg.save_failed", saveManager.LastError),
                    Loc.T("dlg.save_error"), false);
            }
        }

        private void RefreshSaveList()
        {
            var saves = saveManager.GetAllSaves();
            saves.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            SaveListControl.ItemsSource = new ObservableCollection<SaveInfo>(saves);
        }

        private void LoadBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshSaveList();
            SaveListModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        private void CloseSaveListModal_Click(object sender, RoutedEventArgs e)
        {
            SaveListModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private async void LoadSaveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string saveName)
            {
                var data = saveManager.Load(saveName);
                if (data != null)
                {
                    gameCore.Load(data);
                    // Ayarlar panelindeki kaydırıcıları da yüklenen değerlerle senkronize et - aksi
                    // halde slider görsel olarak varsayılan 1.0x'te kalır ama motor farklı bir
                    // çarpanla çalışıyormuş gibi görünürdü (bkz. GameSaveData.DirtMultiplier).
                    if (DirtMultiplierSlider != null) DirtMultiplierSlider.Value = gameCore.DirtMultiplier;
                    if (FuelMultiplierSlider != null) FuelMultiplierSlider.Value = gameCore.FuelMultiplier;
                    AssignGarageSlots(); // kilitli araçları kaydedilmiş garaj ızgarasına yeniden yerleştir
                    soundEngine.PlaySuccess();
                    SaveListModal.Visibility = Visibility.Collapsed;
                    await ShowDialogAsync(Loc.T("msg.game_loaded", saveName), Loc.T("dlg.success"), false);

                    // "Özlem" bildirimi: kayıt uzun süre önce alınmışsa araçların yakıt/mutluluk
                    // kaybettiğini kullanıcıya hatırlat (VPet'in gerçek-zaman ihtiyaç döngüsüne benzer).
                    if (gameCore.LastAbsenceHours > 1.0)
                    {
                        string awayText = gameCore.LastAbsenceHours >= 24
                            ? Loc.T("msg.absence_over_a_day")
                            : Loc.T("msg.absence_hours", (int)gameCore.LastAbsenceHours);
                        ShowToast(Loc.T("msg.absence_notice", awayText));
                    }
                }
                else
                {
                    // Eskiden bozuk/eksik bir kayıt seçildiğinde HİÇBİR ŞEY olmuyordu - modal açık
                    // kalıyor, kullanıcı butona tekrar tekrar basıp uygulamayı donmuş sanıyordu.
                    await ShowDialogAsync(
                        Loc.T("msg.load_failed", saveManager.LastError),
                        Loc.T("dlg.load_error"), false);
                }
            }
        }

        private void DeleteSaveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string saveName)
            {
                saveManager.DeleteSave(saveName);
                PlayUiClick();
                RefreshSaveList();
            }
        }

        private void AddVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            SpawnModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        /// <summary>Seçili aracı (yoksa filodaki ilk aracı) bulunduğu yerden mevcut fare imleci
        /// konumuna çağırır - bir köşede sıkışmış/kaybolmuş bir aracı hızlıca geri getirmek için.
        /// Mevcut Shift+tık rota atama fiziğiyle aynı CustomPath mekanizmasını kullanır, böylece
        /// araç ışınlanmaz, yumuşakça sürerek gelir.</summary>
        private void CallVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            var target = selectedVehicle ?? gameCore.AutoMotions.FirstOrDefault();
            if (target == null)
            {
                ShowToast(Loc.T("msg.cagrilacak_arac_yok"));
                return;
            }
            if (target.IsDriven || target.IsLocked)
            {
                ShowToast(Loc.T("msg.bu_arac_suruluyor_kilitli_cagrilamaz"));
                return;
            }

            target.CustomPath.Clear();
            target.CustomPath.Add(lastMousePos);
            selectedVehicle = target;
            VehicleListBox.SelectedItem = target;

            ShowToast(Loc.T("msg.vehicle_summoned", VehicleDisplayName(target)));
            soundEngine.PlayDrift();
        }

        private void CloseSpawnModal_Click(object sender, RoutedEventArgs e)
        {
            SpawnModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Visible;
            PlayUiClick();
        }

        private void CloseHelpModal_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Collapsed;
            PlayUiClick();
        }

        private void SpawnVehicleSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                string type = tag;
                string brand = "";
                int sep = tag.IndexOf('|');
                if (sep >= 0)
                {
                    type = tag.Substring(0, sep);
                    brand = tag.Substring(sep + 1);
                }

                var vehicle = new Vehicle(RenderCanvas.ActualWidth / 2, RenderCanvas.ActualHeight / 2, type, brand);
                gameCore.AddAutoMotion(vehicle);
                selectedVehicle = vehicle;
                soundEngine.PlaySuccess();
                SpawnModal.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Kullanıcının kendi araç görselini (jpg/png) içe aktarır: beyaz arkaplanı temizler,
        /// sıkı kırpar, %AppData%'a kaydeder ve varsayılan tekerlek konumlarıyla yeni bir
        /// "custom_" markası olarak canlı ekler (Araç Listesi entegrasyonuyla aynı boru hattı,
        /// çalışma zamanında/manuel dosya seçimiyle).
        /// </summary>
        private void ImportCustomVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.T("dlg.pick_vehicle_image"),
                Filter = Loc.T("dlg.image_files_filter") + "|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                source.EndInit();

                var processed = ImageBackgroundRemover.Process(source);

                string customDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoMotion", "CustomVehicles");
                System.IO.Directory.CreateDirectory(customDir);

                string brandKey = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string filePath = System.IO.Path.Combine(customDir, brandKey + ".png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(processed));
                using (var stream = System.IO.File.Create(filePath))
                {
                    encoder.Save(stream);
                }

                var visual = new VehicleVisual
                {
                    Type = "car",
                    Brand = brandKey,
                    Label = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                    ImagePath = filePath,
                    AspectRatio = (double)processed.PixelWidth / processed.PixelHeight,
                    Wheels = new[]
                    {
                        new WheelSpec(0.20, 0.75, 0.18),
                        new WheelSpec(0.78, 0.75, 0.18)
                    }
                };
                customVisuals[brandKey] = visual;

                var vehicle = new Vehicle(RenderCanvas.ActualWidth / 2, RenderCanvas.ActualHeight / 2, "car", brandKey);
                gameCore.AddAutoMotion(vehicle);
                selectedVehicle = vehicle;
                soundEngine.PlaySuccess();
                ShowToast(Loc.T("msg.custom_vehicle_added", visual.Label));
            }
            catch (Exception ex)
            {
                ShowToast(Loc.T("msg.image_import_failed", ex.Message));
            }
        }

        private async void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowDialogAsync(Loc.T("msg.tum_araclari_silmek_istediginden_emin_misin"), "Onay", true))
            {
                // Aynı sebeple: sürülen araç da silinen filoya dahilse önce sürüş modundan çıkılır.
                if (drivenVehicle != null) StopDriving();
                gameCore.Reset();
                InitializeGame();
                selectedVehicle = null;
                PlayUiClick();
            }
        }

        // ============ Mağaza: Para Karşılığı Eşya/Yükseltme Satın Alma ============

        private bool TryPurchase(Item item)
        {
            if (gameCore.GameData.Money < item.Cost)
            {
                PlayUiClick();
                return false;
            }

            gameCore.GameData.Money -= item.Cost;
            gameCore.AddItem(item);
            RefreshItemsList();
            soundEngine.PlaySuccess();
            return true;
        }

        private void AddFuelBtn_Click(object sender, RoutedEventArgs e)
        {
            var fuel = new Fuel("regular");
            fuel.Cost = CurrentFuelPrice();
            TryPurchase(fuel);
        }

        /// <summary>
        /// Borsa Tipi Yakıt Fiyatları: yakıt fiyatı sabit değil, zamanla yavaşça dalgalanır
        /// (~%40 aralığında sinüs eğrisi) - ucuzken stoklama stratejisi mantıklı hale gelir.
        /// </summary>
        private int CurrentFuelPrice()
        {
            double multiplier = 1.0 + Math.Sin(gameCore.GameTime * 0.0025) * 0.4;
            return Math.Max(3, (int)Math.Round(10 * multiplier));
        }

        private void AddRepairBtn_Click(object sender, RoutedEventArgs e)
        {
            TryPurchase(new RepairKit("standard"));
        }

        private void BuyUpgradeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string upgradeType)
            {
                TryPurchase(new Upgrade(upgradeType));
            }
        }

        private void BuyDecorationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string decorationType)
            {
                TryPurchase(new Decoration(decorationType));
            }
        }

        private void NeonColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorHex && selectedVehicle != null)
            {
                selectedVehicle.NeonColor = colorHex;
                PlayUiClick();
            }
        }

        // ============ Kullanılabilirlik Özellikleri: Uçuş / Kilit / Tek Tek Silme ============

        private void DeleteVehicle(Vehicle vehicle)
        {
            if (vehicle == null) return;

            if (drivenVehicle == vehicle)
            {
                StopDriving();
            }

            gameCore.RemoveAutoMotion(vehicle);
            selectedVehicles.Remove(vehicle);

            if (selectedVehicle == vehicle)
            {
                selectedVehicle = selectedVehicles.Count > 0 ? selectedVehicles[0] : null;
            }

            PlayUiClick();
        }

        private void DeleteVehicleRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Vehicle vehicle)
            {
                DeleteVehicle(vehicle);
            }
        }

        private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicles.Count > 1)
            {
                foreach (var v in selectedVehicles.ToList())
                {
                    // Sürülen araç toplu silmeye dahilse önce sürüş modundan çıkılır - aksi halde
                    // drivenVehicle artık filoda olmayan bir araca işaret etmeye devam eder ve
                    // Sürüş Modu tuşu bir daha ASLA yeni bir sürüş başlatamaz, sadece bu "hayalet"
                    // durumu kapatır (vites panelini anında kapatıyormuş gibi görünür).
                    if (drivenVehicle == v) StopDriving();
                    gameCore.RemoveAutoMotion(v);
                }
                selectedVehicles.Clear();
                selectedVehicle = null;
                PlayUiClick();
            }
            else if (selectedVehicle != null)
            {
                DeleteVehicle(selectedVehicle);
            }
        }

        private void GrowVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale += 0.1;
                PlayUiClick();
            }
        }

        private void ShrinkVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale -= 0.1;
                PlayUiClick();
            }
        }

        private void ResetVehicleScaleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.Scale = Vehicle.DefaultScale;
                PlayUiClick();
            }
        }

        private void ToggleFlyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.IsFlying = !selectedVehicle.IsFlying;
                if (selectedVehicle.IsFlying)
                {
                    selectedVehicle.IsLocked = false;
                }
                PlayUiClick();
            }
        }

        private void AttachTrailer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string trailerType && selectedVehicle != null)
            {
                // Aynı dorse butonuna tekrar basılınca çıkar (toggle) - kullanıcı ayrı bir "✕ Çıkar"
                // butonu aramadan, aynı butona ikinci kez basarak dorseyi kaldırabilsin.
                selectedVehicle.TrailerType = (selectedVehicle.TrailerType == trailerType) ? "" : trailerType;
                UpdateTrailerButtonHighlights();
                PlayUiClick();
            }
        }

        /// <summary>Seçili aracın o an takılı dorsesine karşılık gelen butonu tam opak, diğerlerini
        /// soluk gösterir - "hangisi seçili" görsel geri bildirimi (bkz. AttachTrailer_Click).</summary>
        private void UpdateTrailerButtonHighlights()
        {
            string active = selectedVehicle?.TrailerType ?? "";
            TrailerFlatbedBtn.Opacity = active == "flatbed" ? 1.0 : 0.5;
            TrailerTankerBtn.Opacity = active == "tanker" ? 1.0 : 0.5;
            TrailerEquipmentBtn.Opacity = active == "equipment" ? 1.0 : 0.5;
            TrailerWagonBtn.Opacity = active == "wagon" ? 1.0 : 0.5;
        }

        private void ToggleLockBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle != null)
            {
                selectedVehicle.IsLocked = !selectedVehicle.IsLocked;
                // Kilitleme artık aracı "Sanal Otopark" ızgarasına ışınlamıyor - kullanıcı "olduğu
                // yerde kilitlesin" istedi, bu yüzden DockX/DockY hep -1 kalır ve Vehicle.Update'teki
                // "yoksa olduğu yerde donar" dalı devreye girer (bkz. AutoMotion.cs ~496).
                if (selectedVehicle.IsLocked)
                {
                    selectedVehicle.IsFlying = false;
                }
                selectedVehicle.DockX = -1;
                selectedVehicle.DockY = -1;
                PlayUiClick();
            }
        }

        private void ToggleLearningAIBtn_Click(object sender, RoutedEventArgs e)
        {
            if (selectedVehicle == null) return;

            selectedVehicle.UseLearningAI = !selectedVehicle.UseLearningAI;
            ToggleLearningAIBtn.Opacity = selectedVehicle.UseLearningAI ? 1.0 : 0.4;
            ShowToast(Loc.T(selectedVehicle.UseLearningAI ? "msg.learning_ai_on" : "msg.learning_ai_off",
                selectedVehicle.Emoji));
            PlayUiClick();
        }

        /// <summary>
        /// Sanal Otopark: tüm kilitli araçları ekranın sol alt köşesindeki görünmez bir garaj
        /// alanına, düzenli bir ızgara halinde (satır başına 8 araç) yeniden yerleştirir.
        /// Kilit her açılıp kapandığında yeniden çağrılır.
        /// </summary>
        private void AssignGarageSlots()
        {
            var lockedVehicles = gameCore.AutoMotions.Where(v => v.IsLocked).ToList();
            const int slotsPerRow = 8;
            const double slotSpacingX = 55;
            const double slotSpacingY = 45;
            double baseY = RenderCanvas.ActualHeight > 0 ? RenderCanvas.ActualHeight - 35 : 700;

            for (int i = 0; i < lockedVehicles.Count; i++)
            {
                lockedVehicles[i].DockX = 30 + (i % slotsPerRow) * slotSpacingX;
                lockedVehicles[i].DockY = baseY - (i / slotsPerRow) * slotSpacingY;
            }
        }
    }
}
