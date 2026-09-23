$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath "$PSScriptRoot\assets\Pace.ico")) { & "$PSScriptRoot\tools\make-icon.ps1" }
& $compiler /nologo /target:winexe /win32icon:"$PSScriptRoot\assets\Pace.ico" /out:"$PSScriptRoot\ScreenTime.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll "$PSScriptRoot\ScreenTime.cs" "$PSScriptRoot\CornerBar.cs" "$PSScriptRoot\AlertSound.cs" "$PSScriptRoot\ActivityChart.cs" "$PSScriptRoot\AdvancedPlanEditor.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Built ScreenTime.exe'
