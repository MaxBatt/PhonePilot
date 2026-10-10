# Run from any directory: .\build.ps1
[CmdletBinding()]
param([switch]$NoLaunch)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $projectRoot 'PhonePilot.csproj'
$publishDir = Join-Path $projectRoot 'publish'

try {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw 'Place build.ps1 next to PhonePilot.csproj.'
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK is missing or is not available in PATH.'
    }

    # Validate every cleanup target before removing anything.
    $targets = foreach ($name in @('bin', 'obj', 'publish')) {
        $path = [IO.Path]::GetFullPath((Join-Path $projectRoot $name))
        if ([IO.Path]::GetDirectoryName($path) -ne $projectRoot) {
            throw "Cleanup target is outside the project: $path"
        }
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if (-not $item.PSIsContainer -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Cleanup target is not a regular directory: $path"
            }
            # Refuse linked descendants so cleanup cannot follow external directories.
            $linked = Get-ChildItem -LiteralPath $path -Recurse -Force |
                Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
                Select-Object -First 1
            if ($linked) { throw "Remove the link from the build output before cleaning: $($linked.FullName)" }
        }
        $path
    }

    Write-Host 'Cleaning build outputs...'
    foreach ($path in $targets) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        if ($path -eq $publishDir) {
            # Preserve settings and exact originals saved during migration/recovery.
            Get-ChildItem -LiteralPath $path -Force |
                Where-Object { $_.Name -ne 'config.json' -and $_.Name -notlike 'config.json.*.bak' } |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
        } else {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    Write-Host 'Publishing PhonePilot for Windows x64...'
    & dotnet publish $projectFile -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'PhonePilot.exe') -PathType Leaf)) {
        throw 'Publish finished but PhonePilot.exe was not found.'
    }
    Write-Host ""
    Write-Host "Build successful: $publishDir\PhonePilot.exe" -ForegroundColor Green
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $publishDir 'PhonePilot.exe') -WorkingDirectory $publishDir -WindowStyle Hidden
    }
} catch {
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'If a file is locked, close PhonePilot and scrcpy, then run this script again.'
    exit 1
}
