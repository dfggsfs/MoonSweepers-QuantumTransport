param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers',
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$ResetSettings
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Installer.Common.psm1') -Force -DisableNameChecking
if (Get-Process MoonSweepers -ErrorAction SilentlyContinue) { throw 'Close Moon Sweepers before installing. This script never closes it automatically.' }
& (Join-Path $PSScriptRoot 'Preflight.ps1') -GameDir $GameDir -PackageRoot $PackageRoot -PackageOnly
$backup = Join-Path $PackageRoot ('backups\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$installed = Join-Path $GameDir 'Mods\local.quantumtransport'
$config = Join-Path $GameDir 'Lustral\config\local.quantumtransport.toml'
$save = Join-Path $env:USERPROFILE 'AppData\LocalLow\DragDropGames\MoonSweepers'
if (Test-Path -LiteralPath $installed) { Copy-Item -LiteralPath $installed -Destination (Join-Path $backup 'previous-mod') -Recurse }
if (Test-Path -LiteralPath $config) { Copy-Item -LiteralPath $config -Destination (Join-Path $backup 'previous-config.toml') }
if ([IO.Directory]::Exists((ConvertTo-QExtendedPath $save))) {
    $beforeSaves=Read-QFileSet $save
    Copy-QSaveBackup $save (Join-Path $backup 'save-backup')
    if(-not (Test-QFileSetsEqual $beforeSaves (Read-QFileSet (Join-Path $backup 'save-backup')))) {throw 'Save backup verification failed; Mod files have not been changed.'}
}
New-Item -ItemType Directory -Path $installed -Force | Out-Null
foreach ($name in @('QuantumTransport.dll','QuantumTransport.Core.dll','mod.toml')) {
    Copy-Item -LiteralPath (Join-Path $PackageRoot "Mods\local.quantumtransport\$name") -Destination $installed -Force
}
if ($ResetSettings -or -not (Test-Path -LiteralPath $config)) {
    New-Item -ItemType Directory -Path (Split-Path $config -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PackageRoot 'default-config.toml') -Destination $config -Force
}
& (Join-Path $PSScriptRoot 'Preflight.ps1') -GameDir $GameDir -PackageRoot $PackageRoot
Write-Host "Installed. Backup: $backup"
Write-Host 'The game was not started. Start it normally when ready.'
