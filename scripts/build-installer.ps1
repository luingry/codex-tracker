[CmdletBinding()]
param(
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'installer\publish'
$installerScript = Join-Path $repoRoot 'installer\CodexTracker.iss'
if (-not $SkipTests) {
    & dotnet run --project (Join-Path $repoRoot 'tests\CodexTracker.Tests\CodexTracker.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed; installer was not created.' }
}

& dotnet publish (Join-Path $repoRoot 'src\CodexTracker\CodexTracker.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publishDir -p:PublishSingleFile=false -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

function Find-InnoCompiler {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }

    $uninstallKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $install = Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like 'Inno Setup*' -and $_.InstallLocation } |
        Select-Object -First 1 -ExpandProperty InstallLocation
    if ($install) {
        $fromRegistry = Join-Path $install 'ISCC.exe'
        if (Test-Path -LiteralPath $fromRegistry) { return $fromRegistry }
    }

    foreach ($candidate in @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-InnoCompiler

if ($null -eq $iscc) {
    Write-Host 'Installing Inno Setup with winget...'
    & winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup installation failed.' }
    $iscc = Find-InnoCompiler
    if ($null -eq $iscc) { throw 'Inno Setup was installed but ISCC.exe was not found.' }
}

& $iscc "/O$(Join-Path $repoRoot 'artifacts')" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts') -Filter 'CodexTracker-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 FullName, Length, LastWriteTime
