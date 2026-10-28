param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers',
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$PackageOnly
)
$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
    $label = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$label] $Name $Detail"
}
$manifestPath = Join-Path $PackageRoot 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Run this script from the complete release package, or pass -PackageRoot.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $path = Join-Path $PackageRoot $file.path
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $hashMatches = $exists -and ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $file.sha256)
    Add-Check "Package: $($file.path)" $hashMatches
}
$compatibility = Get-Content -LiteralPath (Join-Path $PackageRoot 'game-compatibility.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($entry in $compatibility.gameFiles) {
    $path = Join-Path $GameDir $entry.path
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    Add-Check "Supported game: $($entry.path) / Steam Build $($compatibility.steamBuildId)" ($exists -and ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $entry.sha256))
}
foreach ($relative in @('MoonSweepers.exe','winhttp.dll','Lustral\core\Lustral.Api.dll','Lustral\core\0Harmony.dll','Lustral\core\Il2CppInterop.Runtime.dll','Lustral\interop\Assembly-CSharp.dll','Lustral\interop\Il2Cppmscorlib.dll','Lustral\interop\Il2Cppcom.rlabrecque.steamworks.net.dll',
    'Lustral\interop\UnityEngine.UI.dll','Lustral\interop\UnityEngine.UIModule.dll','Lustral\interop\Unity.TextMeshPro.dll')) {
    Add-Check "Prerequisite: $relative" (Test-Path -LiteralPath (Join-Path $GameDir $relative) -PathType Leaf)
}
Import-Module (Join-Path $PSScriptRoot 'Installer.Common.psm1') -Force -DisableNameChecking
Add-Check 'Generated interfaces match this game build' (Test-QInterop (Join-Path $GameDir 'Lustral/interop') $compatibility)
if (-not $PackageOnly) {
    foreach ($relative in @('Mods\local.quantumtransport\QuantumTransport.dll','Mods\local.quantumtransport\QuantumTransport.Core.dll','Mods\local.quantumtransport\mod.toml')) {
        $path = Join-Path $GameDir $relative
        $packagePath = Join-Path $PackageRoot $relative
        $exists = Test-Path -LiteralPath $path -PathType Leaf
        Add-Check "Installed: $relative" ($exists -and ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash))
    }
    $configPath = Join-Path $GameDir 'Lustral\config\local.quantumtransport.toml'
    Add-Check 'Config exists' (Test-Path -LiteralPath $configPath -PathType Leaf)
    if (Test-Path -LiteralPath $configPath) {
        $config = Get-Content -LiteralPath $configPath -Raw
        Add-Check 'No automatic native tests' ($config -match '(?m)^run_native_checks\s*=\s*false\s*$')
    }
}
$failed = @($checks | Where-Object { -not $_.Passed }).Count
$report = [pscustomobject]@{ success=($failed -eq 0); scope='Files, prerequisites and checksums only; does not launch the game'; checks=$checks.ToArray() }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PackageRoot 'preflight-result.json') -Encoding utf8
if ($failed) { throw "Preflight failed: $failed checks. See preflight-result.json." }
Write-Host 'Preflight passed. This is not a gameplay test.'
