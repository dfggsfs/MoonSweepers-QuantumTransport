param([string]$Module=(Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts\complete\Installer.Common.psm1'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
Import-Module $Module -Force -DisableNameChecking
$workspace=Split-Path $PSScriptRoot -Parent
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$root=Join-Path $temp ('QuantumLongPathTests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
[IO.File]::WriteAllText((Join-Path $root '.test-owned'),'QuantumLongPathTests')
$checks=[Collections.Generic.List[object]]::new()
$completed=$false
$failure=$null
$legacyCopyFailed=$false
function Check([bool]$Condition,[string]$Label) {
    $checks.Add([pscustomobject]@{passed=$Condition;label=$Label})
    if(-not $Condition){throw ('FAIL: '+$Label)}
    Write-Host ('PASS: '+$Label)
}
function Reject([scriptblock]$Action,[string]$Label) {
    $rejected=$false
    try { & $Action|Out-Null } catch { $rejected=$true }
    Check $rejected $Label
}
try {
    $relative='slot-1\e42ac78bf8beac22da5697fa882413bc78e2aa069d2fce61cb9f32d48a2451f7\chunk_100_8162256371e3594a320f0b268c406dde079230f6ae57532f46df3e044e3852bb.moon'
    $source=Join-Path $root 'saves'
    $file=Join-Path $source $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))|Out-Null
    [IO.File]::WriteAllText($file,'reproduction fixture: chunk data')
    $target=Join-Path $root 'Steam\steamapps\common\Moon Sweepers\QuantumTransportBackups\20261027-180335-610-aaf87ecc\save-backup\MoonSessions'
    Check ($file.Length -lt 260 -and ($target+'\'+$relative).Length -gt 260) 'short source becomes MAX_PATH-exceeding backup as in reported failure'
    try { Copy-QItem $source $target } catch { $legacyCopyFailed=$true }
    Write-Host ('Legacy provider failed on this host: '+$legacyCopyFailed)
    [IO.Directory]::Delete((ConvertTo-QExtendedPath $target),$true)
    $before=Read-QFileSet $source
    Copy-QSaveBackup $source $target
    Check (Test-QFileSetsEqual $before (Read-QFileSet $target)) 'same save copies and SHA256-verifies with extended paths'
    Check ([IO.File]::ReadAllText((ConvertTo-QExtendedPath ($target+'\'+$relative))) -eq 'reproduction fixture: chunk data') 'long-path backup contains original bytes'
    Reject {Copy-QSaveBackup $source $target} 'existing backup is never overwritten'
    Check (Test-QFileSetsEqual $before (Read-QFileSet $source)) 'original source bytes unchanged'

    $deep=Join-Path $source (('a'*90)+'\'+('b'*90)+'\中文 & 空格''目录')
    $native=ConvertTo-QExtendedPath $deep
    [IO.Directory]::CreateDirectory($native+'\empty')|Out-Null
    [IO.File]::WriteAllText($native+'\hidden.bin','long source content')
    [IO.File]::SetAttributes($native+'\hidden.bin',[IO.FileAttributes]::Hidden)
    [IO.File]::WriteAllBytes($native+'\zero.bin',[byte[]]@())
    $deepBefore=Read-QFileSet $source
    Check ($deep.Length -gt 260 -and $deepBefore.Count -eq 3) 'source files beyond MAX_PATH including hidden and zero-byte files enumerated'
    $copy=Join-Path $root 'deep-copy'
    Copy-QSaveBackup $source $copy
    Check (Test-QFileSetsEqual $deepBefore (Read-QFileSet $copy)) 'long source and destination hashes match including Unicode paths'
    $deepRelative=$deep.Substring($source.Length+1)
    Check ([IO.Directory]::Exists((ConvertTo-QExtendedPath ($copy+'\'+$deepRelative+'\empty')))) 'empty save directories preserved'
    [IO.File]::WriteAllText((ConvertTo-QExtendedPath ($copy+'\'+$relative)),'corrupt backup')
    Check (-not (Test-QFileSetsEqual $deepBefore (Read-QFileSet $copy))) 'backup corruption remains detectable by SHA256'
    Check (Test-QFileSetsEqual $deepBefore (Read-QFileSet $source)) 'long-path original save remains unchanged'
    $missing=Read-QFileSet (Join-Path $root 'no-save-yet')
    Check ($missing.Count -eq 0) 'fresh player without existing saves supported'

    $outside=Join-Path $root 'outside'
    [IO.Directory]::CreateDirectory($outside)|Out-Null
    [IO.File]::WriteAllText(($outside+'\keep.txt'),'sentinel')
    $link=Join-Path $source 'linked'
    New-Item -ItemType Junction -Path $link -Target $outside|Out-Null
    try {
        Reject {Read-QFileSet $source} 'save junction rejected during inventory'
        $rejectTarget=Join-Path $root 'rejected-copy'
        Reject {Copy-QSaveBackup $source $rejectTarget} 'save junction rejected before backup creation'
        Check (-not [IO.Directory]::Exists($rejectTarget)) 'junction rejection leaves no partial backup'
        Check ([IO.File]::ReadAllText(($outside+'\keep.txt')) -eq 'sentinel') 'junction target untouched'
    }
    finally { [IO.Directory]::Delete($link) }
    $completed=$true
}
catch { $failure=$_.Exception.Message; throw }
finally {
    Write-QJson ([pscustomobject]@{success=$completed;error=$failure;legacyProviderFailureReproduced=$legacyCopyFailed;checks=$checks.ToArray();scope='Windows PowerShell 5.1 long-path save regression in marked temporary fixtures. Legacy provider behavior depends on host Windows/.NET configuration; remote failure not independently reproduced. No game execution, registry changes, or user files modified.'}) (Join-Path $workspace 'artifacts\complete-installer-longpaths.json')
    $resolved=[IO.Path]::GetFullPath($root)
    if($resolved.StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase) -and
       (Split-Path -Leaf $resolved).StartsWith('QuantumLongPathTests-') -and
       [IO.File]::ReadAllText((Join-Path $resolved '.test-owned')) -eq 'QuantumLongPathTests') {
        [IO.Directory]::Delete((ConvertTo-QExtendedPath $resolved),$true)
    }
}
