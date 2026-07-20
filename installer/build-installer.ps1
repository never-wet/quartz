param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SkipToolBootstrap
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Quartz\Quartz.csproj'
$publishPath = Join-Path $repoRoot "artifacts\publish\$Runtime"
$vcRuntimePath = Join-Path $PSScriptRoot 'dependencies\VC_redist.x64.exe'
$releasePath = Join-Path $repoRoot 'release'
$localDotnet = 'C:\Users\junha\.cache\quartz-dotnet\dotnet.exe'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source

if (-not $dotnet -and (Test-Path $localDotnet)) {
    $dotnet = $localDotnet
}

if (-not $dotnet) {
    throw 'The .NET 8 SDK was not found. Install it or add dotnet to PATH.'
}

New-Item -ItemType Directory -Force -Path $publishPath, (Split-Path $vcRuntimePath), $releasePath | Out-Null

& $dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishPath `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw 'Quartz publish failed.'
}

if (-not (Test-Path $vcRuntimePath)) {
    Write-Host 'Downloading the official Microsoft Visual C++ 2015-2022 x64 Redistributable...'
    Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $vcRuntimePath
}

$vcSignature = Get-AuthenticodeSignature $vcRuntimePath
if ($vcSignature.Status -ne 'Valid' -or $vcSignature.SignerCertificate.Subject -notlike '*Microsoft*') {
    throw "The Visual C++ Redistributable signature is not valid: $($vcSignature.Status)"
}

$isccCandidates = @(
    (Join-Path $repoRoot '.tools\InnoSetup\ISCC.exe'),
    'C:\Program Files\Inno Setup 7\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc -and -not $SkipToolBootstrap) {
    $toolRoot = Join-Path $repoRoot '.tools'
    $innoInstaller = Join-Path $toolRoot 'innosetup-7.0.2-x64.exe'
    $innoInstallPath = Join-Path $toolRoot 'InnoSetup'
    New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null

    if (-not (Test-Path $innoInstaller)) {
        Write-Host 'Downloading Inno Setup 7.0.2 from the official release...'
        Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_0_2/innosetup-7.0.2-x64.exe' -OutFile $innoInstaller
    }

    $signature = Get-AuthenticodeSignature $innoInstaller
    if ($signature.Status -ne 'Valid') {
        throw "The Inno Setup installer signature is not valid: $($signature.Status)"
    }

    Write-Host 'Preparing the local Inno Setup compiler...'
    $installProcess = Start-Process -FilePath $innoInstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$innoInstallPath") -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Inno Setup installation failed with exit code $($installProcess.ExitCode)."
    }

    $iscc = Join-Path $innoInstallPath 'ISCC.exe'
}

if (-not $iscc -or -not (Test-Path $iscc)) {
    throw 'ISCC.exe was not found. Install Inno Setup 7 or rerun without -SkipToolBootstrap.'
}

& $iscc (Join-Path $PSScriptRoot 'Quartz.iss')
if ($LASTEXITCODE -ne 0) {
    throw 'Quartz installer compilation failed.'
}

$installerPath = Join-Path $releasePath 'Quartz-2.0.0-Setup.exe'
$installer = Get-Item $installerPath
$sizeMb = [Math]::Round($installer.Length / 1MB, 1)
Write-Host "Created $installerPath ($sizeMb MB)"
