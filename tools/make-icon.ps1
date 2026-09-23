$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeIcon {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@

$assetFolder = Join-Path $PSScriptRoot '..\assets'
$pngPath = Join-Path $assetFolder 'Pace-icon.png'
$icoPath = Join-Path $assetFolder 'Pace.ico'
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,49,104,87))
$graphics.FillEllipse($background,2,2,60,60)

$font = New-Object System.Drawing.Font -ArgumentList @('Segoe UI Semibold',31.0,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
$letter = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,248,245,235))
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$graphics.DrawString('P',$font,$letter,(New-Object System.Drawing.RectangleF 3,2,54,58),$format)

$leaf = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,178,210,173))
$graphics.TranslateTransform(48,13)
$graphics.RotateTransform(-35)
$graphics.FillEllipse($leaf,-2,-5,12,19)
$graphics.ResetTransform()

$bitmap.Save($pngPath,[System.Drawing.Imaging.ImageFormat]::Png)
$handle = $bitmap.GetHicon()
try {
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($icoPath)
    try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose() }
} finally {
    [void][NativeIcon]::DestroyIcon($handle)
    $leaf.Dispose(); $format.Dispose(); $letter.Dispose(); $font.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}

Write-Host "Created assets\Pace.ico and assets\Pace-icon.png"
