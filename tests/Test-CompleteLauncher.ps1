$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$bundle=Join-Path $workspace 'dist\QuantumTransport-0.7.0-OneClick'
Import-Module (Join-Path $bundle 'scripts\Installer.Common.psm1') -Force -DisableNameChecking
$gameSource='C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$private=Join-Path $temp ('QuantumLauncherTests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($private)|Out-Null
[IO.File]::WriteAllText((Join-Path $private '.test-owned'),'QuantumLauncherTests')
$results=[Collections.Generic.List[object]]::new()
$completed=$false
function Check([bool]$ok,[string]$label) {$results.Add([pscustomobject]@{passed=$ok;label=$label});if(-not $ok){throw $label};Write-Host "PASS: $label"}
function Run-Batch([string]$Path,[string]$Profile,[string]$LogPrefix) {
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=Join-Path $env:WINDIR 'System32\cmd.exe'
    $start.Arguments='/d /s /c ""'+$Path+'" < nul"'
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.EnvironmentVariables['USERPROFILE']=$Profile
    $process=[Diagnostics.Process]::Start($start)
    $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(180000)){ $process.Kill();throw 'Launcher test timeout' }
    [IO.File]::WriteAllText((Join-Path $workspace ('artifacts\'+$LogPrefix+'.stdout.log')),$stdout.Result)
    [IO.File]::WriteAllText((Join-Path $workspace ('artifacts\'+$LogPrefix+'.stderr.log')),$stderr.Result)
    $code=$process.ExitCode; $process.Dispose();return $code
}
try {
    foreach($name in @('一键安装.cmd','安装前检查.cmd')) {
        $bytes=[IO.File]::ReadAllBytes((Join-Path $bundle $name))
        Check (-not [Text.Encoding]::ASCII.GetString($bytes).Contains("`r`r`n")) ('batch line endings valid: '+$name)
    }
    $game=Get-QPath $private "游戏 & 带空格's"
    [IO.Directory]::CreateDirectory($game)|Out-Null
    foreach($relative in @('MoonSweepers.exe','GameAssembly.dll','MoonSweepers_Data\il2cpp_data\Metadata\global-metadata.dat')) {
        Copy-QItem (Join-Path $gameSource $relative) (Get-QPath $game $relative)
    }
    Copy-QItem (Join-Path $gameSource 'Lustral\interop') (Get-QPath $game 'Lustral\interop')
    $package=Get-QPath $game 'QuantumTransport-0.7.0-OneClick';Copy-QItem $bundle $package
    $profile=Get-QPath $private 'private-player-profile'
    $saves=Get-QPath $profile 'AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions'
    [IO.Directory]::CreateDirectory($saves)|Out-Null
    [IO.File]::WriteAllText((Get-QPath $saves 'test-save.bin'),'launcher fixture')
    $longSave=ConvertTo-QExtendedPath ($saves+'\slot-1\'+('a'*64)+'\chunk_100_'+('b'*64)+'.moon')
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($longSave))|Out-Null
    [IO.File]::WriteAllText($longSave,'long-path launcher fixture')
    $before=Read-QFileSet $saves
    Check ((Run-Batch (Join-Path $package '安装前检查.cmd') $profile 'complete-launcher-check') -eq 0) 'real CMD check launcher succeeds with special-character paths'
    Check ((Run-Batch (Join-Path $package '一键安装.cmd') $profile 'complete-launcher-install') -eq 0) 'real one-click CMD executes complete PowerShell entry and returns success'
    $reportFile=Get-ChildItem -LiteralPath (Join-Path $package 'logs') -Filter install-result.json -Recurse -File|Select-Object -First 1
    $report=[IO.File]::ReadAllText($reportFile.FullName)|ConvertFrom-Json
    Check ($report.success -and -not $report.gameStarted -and -not $report.generatedInterop) 'launcher writes successful final report and reuses valid interfaces'
    Check ($report.installerRevision -eq 2 -and (Test-QFileSetsEqual $before (Read-QFileSet (Join-Path $report.backup 'save-backup\MoonSessions')))) 'actual CMD launcher backs up long-path player saves with installer revision 2'
    Check (Test-QFileSetsEqual $before (Read-QFileSet $saves)) 'launcher preserves original player save contents'
    $transcript=Get-ChildItem -LiteralPath (Join-Path $package 'logs') -Filter installer.log -Recurse -File|Select-Object -First 1
    Check ($transcript.Length -gt 0) 'PowerShell 5.1 transcript created by actual entry point'
    Check (-not (Get-Process MoonSweepers -ErrorAction SilentlyContinue)) 'one-click launcher never starts game'
    $completed=$true
}
finally {
    Write-QJson ([pscustomobject]@{success=$completed;checks=$results.ToArray();scope='Actual CMD to Windows PowerShell 5.1 entry point with a private player profile and isolated copied game files; valid interfaces reused; no game launched.'}) (Join-Path $workspace 'artifacts\complete-installer-launcher.json')
    if([IO.Path]::GetFullPath($private).StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase) -and
       (Split-Path -Leaf $private).StartsWith('QuantumLauncherTests-') -and
       [IO.File]::ReadAllText((Join-Path $private '.test-owned')) -eq 'QuantumLauncherTests') {
        [IO.Directory]::Delete((ConvertTo-QExtendedPath $private),$true)
    }
}
