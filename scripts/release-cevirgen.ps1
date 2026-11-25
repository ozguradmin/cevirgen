# SPDX-License-Identifier: AGPL-3.0-only
<#
.SYNOPSIS
  Çevirgen Windows sürümünü paketler (Velopack): dist/releases altında kurulum dosyası, tam paket ve sürüm listesi.

.DESCRIPTION
  1. dotnet publish (Release, win-x64, kendi içinde .NET) -p:Version=<sürüm>.
  2. Paket klasörü: yayımlanan dosyalar + C# derlemesinin üretmediği yerel ve üçüncü taraf çalışma dosyaları
     (couchsubs_native.dll, ct2/, xbox-bridge/ çalışma dosyaları, tts/, VC++ çalışma kitaplıkları) -NativeFrom
     klasöründen; LICENSE.txt, THIRD-PARTY-NOTICES.md, NOTICE, README.md depodan; dist/tv-app'teki en yeni .apk tv-app/ altına.
     *.pdb atılır.
  3. vpk pack (packId CevirgenApp, ana exe Cevirgen.exe) → dist/releases.
  Hiçbir yere yüklemez, GitHub sürümü açmaz.

  -NativeFrom bugün CouchSubs'ın aynı AGPL kaynaktan derlenmiş kurulumudur (%LOCALAPPDATA%\CouchSubsApp\current);
  yerel kitaplık (windows/native) ve ct2/tts derlemesi bu makinede yapılamadığı için oradan alınır. CouchSubs'ın kendi
  TV paketleri (tv-app) alınmaz; Çevirgen'in TV paketi dist/tv-app'ten gelir.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\release-cevirgen.ps1 -Version 1.1.0
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\release-cevirgen.ps1 -Version 1.2.0 -NativeFrom D:\native-runtime
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$NativeFrom = (Join-Path $env:LOCALAPPDATA 'CouchSubsApp\current'),
    [string]$Dotnet = 'C:\dev\gta6\_dotnet\dotnet.exe',
    [string]$ToolPath = 'C:\dev\gta6\_dotnet\tools',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'dist\releases' }
$appProject = Join-Path $repo 'windows\src\Cevirgen.App'
$icon = Join-Path $appProject 'Assets\Cevirgen.ico'
$work = Join-Path $repo "build\cevirgen-release\$Version"
$publishDir = Join-Path $work 'publish'
$packDir = Join-Path $work 'package'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Geçersiz sürüm: $Version (ör. 1.1.0 ya da 1.2.0-beta.1)" }
if (-not (Test-Path $Dotnet)) { throw "dotnet bulunamadı: $Dotnet" }
if (-not (Test-Path (Join-Path $NativeFrom 'couchsubs_native.dll'))) { throw "-NativeFrom klasöründe couchsubs_native.dll yok: $NativeFrom" }
# Önceki sürümlerin ara klasörleri (her biri ~760 MB; yeniden üretilebilir) silinir, disk dolmasın.
Get-ChildItem (Split-Path $work) -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne $Version } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
# dist/releases önceki sürümleri tutar (vpk fark paketlerini onlardan üretir); aynı sürüm ikinci kez paketlenmez.
if (Test-Path (Join-Path $OutputDir "CevirgenApp-$Version-full.nupkg")) {
    throw "$Version zaten paketlenmiş ($OutputDir). Yeniden paketlemek için o sürümün dosyalarını ve releases.win.json'daki kaydını sil."
}

$env:DOTNET_ROOT = Split-Path -Parent $Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# --- 1. Yayımla ---
Write-Step "dotnet publish $Version"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $publishDir | Out-Null
& $Dotnet publish $appProject -c Release -r win-x64 --self-contained "-p:Version=$Version" -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $publishDir 'Cevirgen.exe'))) { throw 'Yayımlanan klasörde Cevirgen.exe yok' }

# --- 2. Paket klasörü ---
Write-Step 'Paket klasörü'
Copy-Item -Recurse $publishDir $packDir

# Paket klasöründe yoksa kopyalar (C# derlemesinin ürettiği dosya ezilmez).
function Copy-Missing([string]$source, [string]$target) {
    if (Test-Path $source -PathType Container) {
        Get-ChildItem -Recurse -File $source | ForEach-Object {
            $relative = $_.FullName.Substring($source.Length).TrimStart('\')
            $destination = Join-Path $target $relative
            if (-not (Test-Path $destination)) {
                New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
                Copy-Item $_.FullName $destination
            }
        }
    } elseif (Test-Path $source) {
        if (-not (Test-Path $target)) { Copy-Item $source $target }
    }
}

# Yerel kitaplık ve VC++ çalışma kitaplıkları (couchsubs_native.dll ve ct2 bunlara bağlı).
$nativeFiles = @('couchsubs_native.dll', 'concrt140.dll', 'msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll', 'msvcp140_atomic_wait.dll',
    'msvcp140_codecvt_ids.dll', 'vccorlib140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcruntime140_threads.dll')
foreach ($name in $nativeFiles) {
    $source = Join-Path $NativeFrom $name
    if (Test-Path $source) { Copy-Missing $source (Join-Path $packDir $name) }
    elseif ($name -eq 'couchsubs_native.dll') { throw "$name eksik" }
    else { Write-Warning "$name -NativeFrom klasöründe yok; atlandı" }
}
# Çevirgen: C# kodunun çağırdığı her cs_* işlevi paketlenen couchsubs_native.dll'de var mı (dışa aktarılan adlar DLL'de
# sıfırla biten metin olarak durur). 1.4.13–1.4.19'da cs_discover_id yoktu, PS5 araması çöküyordu (2026-11-25).
$nativeText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $packDir 'couchsubs_native.dll')))
$calls = Get-ChildItem -Recurse -Filter *.cs (Join-Path $repo 'windows\src') |
    Select-String -Pattern 'partial\s+\w+\*?\s+(cs_\w+)\s*\(|extern\s+\w+\s+(cs_\w+)\s*\(' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } } |
    Sort-Object -Unique
foreach ($call in $calls) {
    if (-not $nativeText.Contains("`0$call`0")) { Write-Warning "couchsubs_native.dll'de $call yok: C# tarafında yedek yol olmalı" }
}
# CTranslate2 (ücretsiz çeviri motoru), seslendirme motoru, Xbox köprüsünün çalışma dosyaları (node.exe, lisanslar).
foreach ($folder in @('ct2', 'tts', 'xbox-bridge')) {
    $source = Join-Path $NativeFrom $folder
    if (Test-Path $source) { Copy-Missing $source (Join-Path $packDir $folder) }
    elseif ($folder -eq 'ct2') { throw "ct2 klasörü eksik: $source" }
    else { Write-Warning "$folder -NativeFrom klasöründe yok; atlandı" }
}
# Çevirgen: ct2 işçisi depodan (toplu çeviri isteği); -NativeFrom'daki eski kopyanın üstüne yazılır.
Copy-Item -Force (Join-Path $repo 'windows\ct2\worker.py') (Join-Path $packDir 'ct2\worker.py')

# Lisans ve bildirimler: Çevirgen'in kendi kopyaları (depodan), yoksa -NativeFrom'dakiler.
foreach ($name in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'NOTICE', 'README.md')) {
    $ours = Join-Path $repo $name
    $source = if (Test-Path $ours) { $ours } else { Join-Path $NativeFrom $name }
    if (-not (Test-Path $source)) { throw "$name bulunamadı" }
    Copy-Item -Force $source (Join-Path $packDir $name)
}

# TV uygulaması: dist/tv-app'teki en yeni Android TV paketi.
$tvTarget = Join-Path $packDir 'tv-app'
New-Item -ItemType Directory -Force $tvTarget | Out-Null
$apk = Get-ChildItem (Join-Path $repo 'dist\tv-app\*.apk') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($apk) {
    Get-ChildItem (Join-Path $tvTarget '*.apk') | Where-Object { $_.Name -ne $apk.Name } | Remove-Item -Force
    Copy-Item -Force $apk.FullName (Join-Path $tvTarget $apk.Name)
} else {
    Write-Warning 'dist\tv-app altında .apk yok; tv-app/ boş kalır'
}

# Hata ayıklama simgeleri pakete girmez.
Get-ChildItem -Recurse -File -Filter '*.pdb' $packDir | Remove-Item -Force

# --- 3. vpk ---
$csproj = Get-Content -Raw -Encoding UTF8 (Join-Path $appProject 'Cevirgen.App.csproj')
if ($csproj -notmatch '<PackageReference Include="Velopack" Version="([^"]+)"') { throw 'Cevirgen.App.csproj içinde Velopack sürümü bulunamadı' }
$velopackVersion = $Matches[1]
$vpk = Join-Path $ToolPath 'vpk.exe'
$installed = $null
if (Test-Path $vpk) {
    try { $installed = (& $vpk --version 2>$null | Select-Object -First 1) } catch { $installed = $null }
}
if (-not $installed -or $installed -notlike "$velopackVersion*") {
    Write-Step "vpk $velopackVersion kuruluyor ($ToolPath)"
    if (Test-Path $vpk) { & $Dotnet tool uninstall vpk --tool-path $ToolPath | Out-Null }
    & $Dotnet tool install vpk --version $velopackVersion --tool-path $ToolPath
    if ($LASTEXITCODE -ne 0) { throw "vpk kurulamadı ($LASTEXITCODE)" }
}
# vpk bu makinedeki .NET'le (taşınabilir SDK) çalışır; hedeflediği sürüm yoksa en yenisine geçer.
$env:DOTNET_ROLL_FORWARD = 'Major'

Write-Step "vpk pack CevirgenApp $Version"
New-Item -ItemType Directory -Force $OutputDir | Out-Null
& $vpk pack --packId CevirgenApp --packVersion $Version --packDir $packDir --mainExe Cevirgen.exe --packTitle 'Çevirgen' `
    --icon $icon --outputDir $OutputDir --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw "vpk pack başarısız ($LASTEXITCODE)" }

Write-Step 'Üretilen dosyalar'
Get-ChildItem -File $OutputDir | Where-Object { $_.Name -like "CevirgenApp-$Version-*" -or $_.Name -like 'CevirgenApp-win-*' -or
        $_.Name -like 'releases.*' -or $_.Name -eq 'RELEASES' -or $_.Name -like 'assets.*' } |
    Sort-Object Name | ForEach-Object { '{0,-48} {1,12:N0} bayt' -f $_.Name, $_.Length }
