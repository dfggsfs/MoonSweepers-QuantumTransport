param(
    [string]$GameDir = '',
    [string]$OutputDirectory = [Environment]::GetFolderPath('DesktopDirectory')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if (-not $GameDir) {
    $package = Split-Path -Parent $PSScriptRoot
    $besidePackage = Split-Path -Parent $package
    $candidates = @($besidePackage, $package, 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers')
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'MoonSweepers.exe') -PathType Leaf)) {
            $GameDir = $candidate
            break
        }
    }
    if (-not $GameDir) { throw 'Game folder not found. Put the whole diagnostic folder beside MoonSweepers.exe, then run again.' }
}
$GameDir = [IO.Path]::GetFullPath($GameDir)
$profilePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'game-compatibility.json'
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    $profilePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'distribution/game-compatibility.json'
}
$profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
$supported = [ordered]@{}
foreach ($entry in $profile.gameFiles) { $supported[$entry.path] = $entry.sha256 }
$files = foreach ($relative in $supported.Keys) {
    $path = Join-Path $GameDir $relative
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $actual = $null
    $size = $null
    $errorText = $null
    $state = 'missing'
    if ($exists) {
        try {
            $size = (Get-Item -LiteralPath $path).Length
            $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $state = if ($actual -eq $supported[$relative]) { 'match' } else { 'mismatch' }
        }
        catch { $state = 'unreadable'; $errorText = $_.Exception.GetType().Name }
    }
    [pscustomobject]@{file=$relative; state=$state; bytes=$size; sha256=$actual; expectedSha256=$supported[$relative]; error=$errorText}
}
$exeFound = Test-Path -LiteralPath (Join-Path $GameDir 'MoonSweepers.exe') -PathType Leaf
$steamApps = Split-Path -Parent (Split-Path -Parent $GameDir)
$acfPath = Join-Path $steamApps 'appmanifest_5335510.acf'
$buildId = $null
$branch = $null
$manifestState = 'not-found'
if (Test-Path -LiteralPath $acfPath -PathType Leaf) {
    try {
        $acf = [IO.File]::ReadAllText($acfPath)
        if ($acf -match '(?im)^\s*"buildid"\s+"(\d+)"') { $buildId = $Matches[1] }
        if ($acf -match '(?im)^\s*"BetaKey"\s+"([^"]*)"') { $branch = $Matches[1] }
        $manifestState = 'read'
    }
    catch { $manifestState = 'unreadable' }
}
$matchesSupportedFiles = $exeFound -and @($files | Where-Object {$_.state -ne 'match'}).Count -eq 0
$report = [ordered]@{
    tool='QuantumTransport-GameVersionCheck'; expectedBuildId=$profile.steamBuildId
    steamManifest=$manifestState; installedBuildId=$buildId; recordedBetaKey=$branch
    executableFound=$exeFound; matchesSupportedFiles=[bool]$matchesSupportedFiles
    files=@($files); gameStarted=$false; gameFilesModified=$false
    note='Build ID is supplementary evidence. A hash mismatch alone cannot distinguish another build from a modified or damaged file. No saves, account IDs or full Steam manifest are collected.'
}
if (-not $OutputDirectory) { throw 'No output folder available. Pass -OutputDirectory.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$output = Join-Path $OutputDirectory ('QuantumTransport-GameVersion-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '.json')
[IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host ('Installed Steam Build: ' + $buildId)
foreach ($file in $files) { Write-Host ($file.state.ToUpperInvariant() + ': ' + $file.file) }
Write-Host ('Matches supported game files: ' + $matchesSupportedFiles)
Write-Host ('Report saved: ' + $output)
Write-Host 'Read-only diagnosis complete. No game files changed; game not started.'
