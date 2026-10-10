# Build a clean portable package without touching the user's publish folder.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $projectRoot ('artifacts\releases\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
$buildDir = Join-Path $releaseRoot 'build'
$packageDir = Join-Path $releaseRoot 'package'
$zipPath = Join-Path $releaseRoot 'PhonePilot-win-x64.zip'

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
& dotnet publish (Join-Path $projectRoot 'PhonePilot.csproj') -c Release -r win-x64 --self-contained true -o $buildDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }

# Only known distribution files enter the archive; never copy personal settings.
foreach ($name in @('PhonePilot.exe', 'THIRD-PARTY-NOTICES.txt', 'adb', 'scrcpy')) {
    Copy-Item -LiteralPath (Join-Path $buildDir $name) -Destination $packageDir -Recurse
}
@'
PhonePilot - Windows x64

1. Entpacke das gesamte ZIP in einen beschreibbaren Ordner.
2. Starte PhonePilot.exe. ADB, scrcpy und die .NET-Laufzeit sind enthalten.
3. Aktiviere am Android-Telefon WLAN-Debugging und kopple es in PhonePilot.

Windows 11 x64, Android 11 oder neuer und ein gemeinsames Netzwerk werden benoetigt.

Update: PhonePilot vollstaendig beenden (auch im Infobereich), die Programmdateien
ersetzen und die eigene config.json samt Sicherungen behalten.
Das Download-Paket enthaelt keine persoenlichen Einstellungen oder ADB-Schluessel.

Anleitung: https://github.com/MaxBatt/PhonePilot#readme
Downloads: https://github.com/MaxBatt/PhonePilot/releases
Lizenzen und Hinweise: THIRD-PARTY-NOTICES.txt, adb/NOTICE.txt, scrcpy/LICENSE.txt
'@ | Set-Content -LiteralPath (Join-Path $packageDir 'START-HIER.txt') -Encoding utf8

$files = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse -Force)
$privateFiles = @($files | Where-Object {
    $_.Name -like 'config.json*' -or $_.Name -like 'adbkey*' -or
    $_.Extension -in @('.log', '.pdb') -or $_.FullName -match '[\\/]\.android[\\/]'
})
if ($privateFiles.Count) { throw 'Private or development files found in package.' }
foreach ($required in @('PhonePilot.exe', 'adb\adb.exe', 'adb\NOTICE.txt', 'scrcpy\scrcpy.exe', 'scrcpy\scrcpy-server', 'scrcpy\LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDir $required) -PathType Leaf)) { throw "Missing: $required" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  PhonePilot-win-x64.zip" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ascii
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $hash"
