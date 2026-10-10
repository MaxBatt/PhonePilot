# Deterministically package the approved PNG into a multi-resolution Windows ICO.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase

$iconRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets'))
$iconSource = [Windows.Media.Imaging.BitmapFrame]::Create([Uri]::new((Join-Path $iconRoot 'PhonePilot.png')))
# Fit the visible artwork to a square Windows tile, independently of PNG padding.
$iconPixels = [Windows.Media.Imaging.FormatConvertedBitmap]::new($iconSource, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
$iconStride = $iconPixels.PixelWidth * 4
$iconData = [byte[]]::new($iconStride * $iconPixels.PixelHeight)
$iconPixels.CopyPixels($iconData, $iconStride, 0)
$iconLeft = $iconPixels.PixelWidth; $iconTop = $iconPixels.PixelHeight
$iconRight = -1; $iconBottom = -1
for ($iconY = 0; $iconY -lt $iconPixels.PixelHeight; $iconY++) {
    for ($iconX = 0; $iconX -lt $iconPixels.PixelWidth; $iconX++) {
        if ($iconData[$iconY * $iconStride + $iconX * 4 + 3] -lt 128) { continue }
        $iconLeft = [Math]::Min($iconLeft, $iconX); $iconRight = [Math]::Max($iconRight, $iconX)
        $iconTop = [Math]::Min($iconTop, $iconY); $iconBottom = [Math]::Max($iconBottom, $iconY)
    }
}
if ($iconRight -lt 0) { throw 'The source icon contains no visible artwork.' }
$iconSource = [Windows.Media.Imaging.CroppedBitmap]::new($iconSource, [Windows.Int32Rect]::new($iconLeft, $iconTop, ($iconRight - $iconLeft + 1), ($iconBottom - $iconTop + 1)))
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$iconFrames = foreach ($iconSize in $iconSizes) {
    $iconVisual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($iconVisual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $iconDrawing = $iconVisual.RenderOpen()
    try {
        $iconBounds = [Windows.Rect]::new(0, 0, $iconSize, $iconSize)
        $iconClip = [Windows.Media.RectangleGeometry]::new($iconBounds, ($iconSize / 6), ($iconSize / 6))
        $iconDrawing.PushClip($iconClip)
        # The opaque underlay ensures equal width/height, even if the image's
        # antialiased edges or rounded corners differ slightly on each side.
        $iconBlue = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#0067EF'))
        $iconDrawing.DrawRectangle($iconBlue, $null, $iconBounds)
        $iconDrawing.DrawImage($iconSource, $iconBounds)
        $iconDrawing.Pop()
    }
    finally { $iconDrawing.Close() }
    $iconBitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($iconSize, $iconSize, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $iconBitmap.Render($iconVisual)
    $iconEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $iconEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($iconBitmap))
    $iconBuffer = [IO.MemoryStream]::new()
    try {
        $iconEncoder.Save($iconBuffer)
        [pscustomobject]@{ Size = $iconSize; Bytes = $iconBuffer.ToArray() }
    } finally { $iconBuffer.Dispose() }
}

$iconOutput = [IO.File]::Create((Join-Path $iconRoot 'PhonePilot.ico'))
$iconWriter = [IO.BinaryWriter]::new($iconOutput)
try {
    $iconWriter.Write([uint16]0)
    $iconWriter.Write([uint16]1)
    $iconWriter.Write([uint16]$iconFrames.Count)
    $iconOffset = 6 + 16 * $iconFrames.Count
    foreach ($iconFrame in $iconFrames) {
        $iconDimension = if ($iconFrame.Size -eq 256) { 0 } else { $iconFrame.Size }
        $iconWriter.Write([byte]$iconDimension)
        $iconWriter.Write([byte]$iconDimension)
        $iconWriter.Write([byte]0)
        $iconWriter.Write([byte]0)
        $iconWriter.Write([uint16]1)
        $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconFrame.Bytes.Length)
        $iconWriter.Write([uint32]$iconOffset)
        $iconOffset += $iconFrame.Bytes.Length
    }
    foreach ($iconFrame in $iconFrames) { $iconWriter.Write([byte[]]$iconFrame.Bytes) }
} finally { $iconWriter.Dispose() }
Write-Output 'PhonePilot.ico: 16, 20, 24, 32, 40, 48, 64, 128 und 256 Pixel, mit Transparenz.'
