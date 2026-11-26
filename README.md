<div align="center">

<img src="src/AutoMotion/Assets/logo.png" alt="AutoMotion" width="140" />

# AutoMotion

**Masaüstünde yaşayan minyatür araçlar — Windows için şeffaf, fizik tabanlı bir "desktop pet" simülatörü.**

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-0C54C2)
![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)
![Tests](https://img.shields.io/badge/tests-359-brightgreen)
![License](https://img.shields.io/badge/license-MIT-blue)

</div>

---

## Hakkında

AutoMotion, ekranınızda serbestçe dolaşan, yerçekimine tabi, sürüklenip fırlatılabilen araçlar (araba, kamyon, otobüs, motosiklet, traktör, helikopter…) ekleyen bir masaüstü uygulamasıdır. *VPet-Simulator*, *Desktop Goose* ve *Shimeji* gibi masaüstü arkadaşı uygulamalarından esinlenmiştir; evcil hayvan yerine araç simüle eder.

Araçlar kendi başına gezinir, yakıt tüketir, hasar alır, kargo teslim edip para kazanır; ekran kenarlarına ve odaktaki pencerenin üstüne "tırmanabilir".

## Özellikler

| Alan | Neler var |
|---|---|
| **Fizik** | Yerçekimi ve sönümlü sekme, sürükle-fırlat, kenar tırmanma, aktif pencere üzerinde sürüş, uçuş modu |
| **Yapay zekâ** | Gezinme / kovalama / kaçma durumları, Q-Learning ajanı, sağ tıkla rota çizme, çoklu seçim ve grup komutu |
| **Oyun döngüsü** | Yakıt, hasar ve onarım; kargo teslimat görevleri; trafik ışıkları; yağmur ve gece-gündüz döngüsü |
| **Ekonomi** | Para sistemi, kalıcı yükseltmeler (hız, hızlanma, verimlilik, dayanıklılık), dekorasyonlar, dorse/ekipman |
| **Eklentiler** | `Expansion` tabanlı mod mimarisi — Trafik, Ekonomi ve Görev eklentileri hazır gelir |
| **Kişiselleştirme** | 11 tema (cam, gün batımı, synthwave, otomatik gece/gündüz…) ve aralarında yumuşak geçiş, kendi araç görselini içe aktarma (otomatik arka plan temizleme), ölçekleme |
| **Kalıcılık** | JSON kayıt sistemi (diske zorlanmış atomik yazım), ayarlanabilir otomatik kayıt, kayıt yönetim ekranı |
| **Entegrasyon** | Sistem tepsisi, mini HUD widget'ı, MQTT ile IoT filo telemetrisi (Home Assistant / Mosquitto), Steamworks |
| **Yerelleştirme** | Türkçe, İngilizce ve Almanca arayüz, anlık dil değişimi |

## Teknolojiler

- **C# / .NET 9** (`net9.0-windows`) ve **WPF**
- Win32 P/Invoke (aktif pencere takibi), `System.Text.Json`
- [MQTTnet](https://github.com/dotnet/MQTTnet), [NAudio](https://github.com/naudio/NAudio), [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)
- **xUnit** ile 350+ birim testi

## Kurulum

Gereksinimler: Windows 10/11, [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
git clone https://github.com/yagizturkmen-ceng/automotion.git
cd automotion
dotnet run --project src/AutoMotion
```

Testleri çalıştırmak için:

```bash
dotnet test
```

Tek klasörlük dağıtım paketi (self-contained, win-x64 ayarları `AutoMotion.csproj` içinde sabit; ek bayrak gerekmez):

```bash
dotnet publish src/AutoMotion -c Release
# çıktı: src/AutoMotion/bin/Release/net9.0-windows/win-x64/publish/
```

### Steam'e yükleme

[SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) kuruluysa:

```powershell
.\steam\yayinla.ps1 -SadeceDerle                  # derle + paketi denetle, yükleme yok
.\steam\yayinla.ps1 -SteamUser <steam_kullanici>  # derle, denetle, Steam'e yükle
```

Betik yüklemeden önce paketi denetler (`steam_appid.txt` ve `.pdb` yok; lisans bildirimleri ve dil
dosyaları var; `steam_api64.dll` yoksa uyarır) ve App ID'nin `steam_appid.txt` ile
`SteamService.AppId` içinde aynı olduğunu kontrol eder. Parolayı steamcmd kendisi sorar; yükleme
canlıya **alınmaz**, Steamworks panelinden elle yayına alınır.

## Kısayollar ve kullanım

- **Sürükle-bırak**: aracı tutup fırlatın
- **Sağ tık**: rota çizin
- **Shift + tık / alan seçimi**: birden fazla aracı seçip birlikte yönlendirin
- **Delete**: seçili aracı silin
- Sistem tepsisindeki simgeden paneli açıp kapatabilir, hızlıca araç ekleyebilirsiniz.
- Uygulama yalnızca üst çubuktaki **✕** veya tepsi menüsündeki **Çıkış** ile kapanır; Alt+F4 / görev
  çubuğu "Kapat" pencereyi tepsiye gizler (tam ekran oyun açılırken yanlışlıkla kapanmasın diye).

## Kullanıcı verileri

Tümü `%AppData%\AutoMotion\` altında, yalnızca yerelde:

| Dosya | İçerik |
|---|---|
| `settings.json` | Tüm tercihler (sürümlü şema, atomik yazım; MQTT parolası yazılmaz) |
| `Saves\` | Kayıtlar ve otomatik kayıt |
| `error.log` | Hata günlüğü (5 MB'ta döndürülür) |
| `session.log` | Çıkış sebepleri ve 30 dakikada bir sağlık satırı (bellek, handle, thread) — uzun süreli çalışmada sızıntı takibi için |

## Proje yapısı

```
src/AutoMotion/
├── MainWindow.*.cs  # Ana pencere, sorumluluğa göre partial dosyalar: GameLoop, Rendering,
│                    # Input, Panels, World, Commands, Driving, Preferences, Tray, Interop, IoT, Capture, Health
├── Models/        # Araç, eşya, oyun verisi modelleri
├── Services/      # Kayıt, ayarlar, tema, ses, yerelleştirme, IoT, Steam
├── Expansions/    # Eklenti (mod) sistemi
├── Views/         # Pencereler ve diyaloglar
├── Utilities/     # Yardımcı sınıflar
└── Assets/        # Araç görselleri, fontlar, dil dosyaları
tests/AutoMotion.Tests/   # xUnit testleri
steam/                    # SteamPipe yükleme betiği ve şablonları
docs/                     # Gereksinimler ve teknik raporlar
```

### Yeni dil eklemek

1. `src/AutoMotion/Assets/Lang/en.json` dosyasını `<kod>.json` olarak kopyalayıp çevirin.
2. `Services/Localization.cs` içindeki `SupportedLanguages` listesine bir satır ekleyin.
3. `AutoMotion.csproj` içindeki `SatelliteResourceLanguages` değerine dil kodunu ekleyin.

`LocalizationTests`, listedeki her dilin İngilizce ile aynı anahtarlara ve yer tutuculara sahip
olduğunu, kodda kullanıcıya görünen sabit Türkçe metin kalmadığını kendiliğinden denetler.

Ayrıntılı mimari açıklaması için [docs/AutoMotion_Proje_Raporu.md](docs/AutoMotion_Proje_Raporu.md) dosyasına bakın.

## Lisans

[MIT](LICENSE) © Yağız Türkmen

Üçüncü taraf bileşenlerin (NAudio, MQTTnet, Steamworks.NET, Press Start 2P) lisansları:
[src/AutoMotion/THIRD_PARTY_NOTICES.txt](src/AutoMotion/THIRD_PARTY_NOTICES.txt)
