$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /out:"$PSScriptRoot\ScreenTime.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll "$PSScriptRoot\ScreenTime.cs" "$PSScriptRoot\CornerBar.cs" "$PSScriptRoot\AlertSound.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Built ScreenTime.exe'
