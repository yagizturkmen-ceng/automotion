<#
.SYNOPSIS
    AutoMotion'ı yayın için derler, paketi denetler ve SteamCMD ile Steam'e yükler.

.DESCRIPTION
    1. dotnet publish (Release, self-contained win-x64 - ayarlar csproj'da sabit)
    2. Paket denetimi: steam_appid.txt ve *.pdb YOK, AutoMotion.exe / lisans bildirimleri VAR,
       steam_api64.dll yoksa uyarı
    3. steam\app_build.template.vdf ve depot_build.template.vdf'den gerçek App/Depot ID ile
       build betikleri üretilir
    4. steamcmd +login <kullanıcı> +run_app_build ... (parola ve Steam Guard kodunu steamcmd
       KENDİSİ sorar - bu betik hiçbir kimlik bilgisini saklamaz veya iletmez)

    Yükleme varsayılan dala gider ama CANLIYA ALINMAZ; Steamworks panelinden "Set build live"
    elle yapılır.

.EXAMPLE
    .\steam\yayinla.ps1 -SteamUser benim_steam_kullanicim
.EXAMPLE
    .\steam\yayinla.ps1 -SteamUser benim -AppId 3456780 -SadeceDerle
#>
param(
    [string]$SteamUser,
    [uint32]$AppId = 0,          # 0 = AutoMotion\steam_appid.txt'den oku
    [uint32]$DepotId = 0,        # 0 = AppId + 1 (Steam'in varsayılan ilk depo numarası)
    [string]$SteamCmd = "steamcmd",
    [string]$Aciklama = "",
    [switch]$SadeceDerle,        # Steam'e yüklemeden yalnızca derle + denetle
    [switch]$TestAppIdIzinVer    # 480 (Spacewar) ile yüklemeye izin ver - yalnızca deneme için
)

$ErrorActionPreference = "Stop"
# Türkçe karakterler hem konsolda hem çıktı bir dosyaya yönlendirildiğinde düzgün görünsün.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$steamDir = $PSScriptRoot
# İki klasör düzeni desteklenir: GitHub deposu (src\AutoMotion) ve eski yerel düzen (AutoMotion).
$appRel = if (Test-Path (Join-Path $root "src\AutoMotion\AutoMotion.csproj")) { "src\AutoMotion" } else { "AutoMotion" }
$appDir = Join-Path $root $appRel
$publishRel = "$appRel\bin\Release\net9.0-windows\win-x64\publish"
$publishDir = Join-Path $root $publishRel

function Fail($msg) { Write-Host "HATA: $msg" -ForegroundColor Red; exit 1 }

# --- App ID ------------------------------------------------------------------
if ($AppId -eq 0) {
    $AppId = [uint32]((Get-Content (Join-Path $appDir "steam_appid.txt") -Raw).Trim())
}
$serviceSource = Get-Content (Join-Path $appDir "Services\SteamService.cs") -Raw
if ($serviceSource -notmatch "AppId\s*=\s*$AppId\b") {
    Fail "App ID $AppId, Services\SteamService.cs içindeki AppId ile aynı değil. İkisini birlikte güncelleyin."
}
if ($AppId -eq 480 -and -not $SadeceDerle -and -not $TestAppIdIzinVer) {
    Fail "App ID hâlâ 480 (Valve'ın test uygulaması Spacewar). Gerçek App ID'yi steam_appid.txt ve SteamService.AppId'ye yazın ya da -TestAppIdIzinVer kullanın."
}
if ($DepotId -eq 0) { $DepotId = $AppId + 1 }

# --- 1. Derleme --------------------------------------------------------------
Write-Host "==> dotnet publish (Release)" -ForegroundColor Cyan
& dotnet publish (Join-Path $appDir "AutoMotion.csproj") -c Release -nologo
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish başarısız." }

# --- 2. Paket denetimi -------------------------------------------------------
Write-Host "==> Paket denetimi: $publishDir" -ForegroundColor Cyan
if (Test-Path (Join-Path $publishDir "steam_appid.txt")) { Fail "steam_appid.txt pakette - yayına girmemeli." }
$pdbs = Get-ChildItem $publishDir -Filter *.pdb -Recurse
if ($pdbs) { Fail "Pakette .pdb dosyaları var: $($pdbs.Name -join ', ')" }
foreach ($required in "AutoMotion.exe", "THIRD_PARTY_NOTICES.txt", "Assets\Lang\tr.json", "Assets\Lang\en.json", "Assets\Lang\de.json") {
    if (-not (Test-Path (Join-Path $publishDir $required))) { Fail "Pakette eksik: $required" }
}
if (-not (Test-Path (Join-Path $publishDir "steam_api64.dll"))) {
    Write-Host "UYARI: steam_api64.dll pakette yok - Steamworks SDK'dan exe'nin yanına kopyalanmalı (başarımlar/bulut çalışmaz)." -ForegroundColor Yellow
}
$sizeMb = [math]::Round(((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Paket hazır: $sizeMb MB, $((Get-ChildItem $publishDir -Recurse -File).Count) dosya" -ForegroundColor Green

if ($SadeceDerle) { Write-Host "-SadeceDerle: Steam'e yükleme atlandı."; exit 0 }
if (-not $SteamUser) { Fail "-SteamUser gerekli (Steamworks yükleme yetkisi olan hesap)." }

# --- 3. Build betikleri ------------------------------------------------------
if (-not $Aciklama) { $Aciklama = "AutoMotion $(git -C $root describe --always --dirty 2>$null) $(Get-Date -Format 'yyyy-MM-dd HH:mm')" }
$appVdf = Join-Path $steamDir "app_build.vdf"
$depotVdf = Join-Path $steamDir "depot_build.vdf"
(Get-Content (Join-Path $steamDir "app_build.template.vdf") -Raw).
    Replace("{{APPID}}", "$AppId").Replace("{{DEPOTID}}", "$DepotId").Replace("{{DESC}}", $Aciklama).
    Replace("{{CONTENTROOT}}", "..\$publishRel\") |
    Set-Content $appVdf -Encoding ASCII
(Get-Content (Join-Path $steamDir "depot_build.template.vdf") -Raw).Replace("{{DEPOTID}}", "$DepotId") |
    Set-Content $depotVdf -Encoding ASCII

# --- 4. Yükleme --------------------------------------------------------------
Write-Host "==> SteamCMD ile yükleniyor (App $AppId, Depot $DepotId)" -ForegroundColor Cyan
& $SteamCmd +login $SteamUser +run_app_build $appVdf +quit
if ($LASTEXITCODE -ne 0) { Fail "steamcmd başarısız (çıkış kodu $LASTEXITCODE). Ayrıntılar: steam\output\" }
Write-Host "Yüklendi. Steamworks > SteamPipe > Builds sayfasından derlemeyi canlıya alın." -ForegroundColor Green
