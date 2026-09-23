$ErrorActionPreference = 'Stop'

$version = (Get-Content -LiteralPath "$PSScriptRoot\version.txt" -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "version.txt must contain a version such as 0.1.0" }

& "$PSScriptRoot\build.ps1"

$compilerCandidates = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$PSScriptRoot\.tools\InnoSetup\ISCC.exe"
)
$compiler = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $compiler) {
    $toolFolder = "$PSScriptRoot\.tools\InnoSetup"
    New-Item -ItemType Directory -Force -Path $toolFolder | Out-Null
    $download = Join-Path $env:TEMP 'pace-innosetup-6.7.3.exe'
    $uri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
    Write-Host 'Downloading the official Inno Setup compiler...'
    Invoke-WebRequest -Uri $uri -OutFile $download
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') {
        throw 'The Inno Setup download did not have the expected valid Pyrsys B.V. signature.'
    }
    $arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR=`"$toolFolder`""
    $process = Start-Process -FilePath $download -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed with exit code $($process.ExitCode)." }
    $compiler = Get-ChildItem -LiteralPath $toolFolder -Filter ISCC.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
    if (-not $compiler) { throw 'The Inno Setup compiler could not be found after installation.' }
}

New-Item -ItemType Directory -Force -Path "$PSScriptRoot\dist" | Out-Null
& $compiler "/DMyAppVersion=$version" "$PSScriptRoot\installer\Pace.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$installer = "$PSScriptRoot\dist\Pace-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "Installer output was not created: $installer" }
Write-Host "Built $installer"
