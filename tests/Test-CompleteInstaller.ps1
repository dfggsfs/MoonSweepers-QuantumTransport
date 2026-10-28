param([string]$Bundle=(Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\QuantumTransport-0.7.0-OneClick'), [switch]$ReuseVerifiedInterop)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$workspace=Split-Path $PSScriptRoot -Parent
$gameSource='C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
$module=Import-Module (Join-Path $Bundle 'scripts\Installer.Common.psm1') -Force -PassThru -DisableNameChecking
$tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$private=Join-Path $tempBase ('QuantumInstallerTests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($private)|Out-Null
[IO.File]::WriteAllText((Join-Path $private '.test-owned'),'QuantumInstallerTests')
$checks=[Collections.Generic.List[object]]::new()
$completed=$false
$testError=$null
$playerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$actualBefore=Read-QFileSet (Join-Path $gameSource 'Mods\local.quantumtransport')
$savesReal=Join-Path $env:USERPROFILE 'AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions'
$saveBefore=Read-QFileSet $savesReal
$realLoaderConfig=Get-FileHash -LiteralPath (Join-Path $gameSource 'Lustral\loader.toml') -Algorithm SHA256
$realModConfig=Get-FileHash -LiteralPath (Join-Path $gameSource 'Lustral\config\local.quantumtransport.toml') -Algorithm SHA256

function Check([bool]$Condition,[string]$Label) {
    $checks.Add([pscustomobject]@{passed=$Condition;label=$Label})
    if(-not $Condition){throw ('FAIL: ' + $Label)}
    Write-Host ('PASS: ' + $Label)
}
function Reject([scriptblock]$Action,[string]$Label,[string]$Expected='') {
    $rejected=$false
    try{& $Action|Out-Null}catch{
        $rejected=$true
        if($Expected -and $_.Exception.Message -notlike ('*'+$Expected+'*')) {throw}
    }
    Check $rejected $Label
}
function Fixture([string]$Name) {
    $root=Join-Path $private $Name
    [IO.Directory]::CreateDirectory($root)|Out-Null
    foreach($relative in @('MoonSweepers.exe','GameAssembly.dll','MoonSweepers_Data\globalgamemanagers','MoonSweepers_Data\globalgamemanagers.assets','MoonSweepers_Data\app.info','MoonSweepers_Data\il2cpp_data\Metadata\global-metadata.dat')) {
        Copy-QItem (Join-Path $gameSource $relative) (Get-QPath $root $relative)
    }
    $package=Get-QPath $root 'QuantumTransport-0.7.0-OneClick'
    Copy-QItem $Bundle $package
    $log=Get-QPath $package 'logs\test'
    [IO.Directory]::CreateDirectory($log)|Out-Null
    $save=Get-QPath $private ($Name + '-profile\MoonSessions')
    [IO.Directory]::CreateDirectory($save)|Out-Null
    [IO.File]::WriteAllText((Get-QPath $save '模拟存档.bin'),'fixture save: no gameplay execution')
    $chunk='slot-1\e42ac78bf8beac22da5697fa882413bc78e2aa069d2fce61cb9f32d48a2451f7\chunk_100_8162256371e3594a320f0b268c406dde079230f6ae57532f46df3e044e3852bb.moon'
    $longSave=ConvertTo-QExtendedPath ($save+'\'+$chunk)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($longSave))|Out-Null
    [IO.File]::WriteAllText($longSave,'representative long-path chunk save')
    return [pscustomobject]@{root=$root;package=$package;log=$log;saves=$save}
}
function New-CustomOldState($Fixture) {
    foreach($pair in @(
        @('Lustral\core\old-core.bin','old loader'),
        @('Lustral\dotnet\old-runtime.bin','old runtime'),
        @('Lustral\generator\old-generator.bin','old generator'),
        @('Lustral\loader.toml',"[log]`nlevel = `"warn`"`nconsole = false`n[updates]`nmode = `"off`"`n"),
        @('Lustral\config\local.quantumtransport.toml',"enabled = false`nlog_level = `"Debug`"`nrun_native_checks = false`n"),
        @('Lustral\data\local.quantumtransport\keep.txt','user data'),
        @('Mods\local.quantumtransport\old-plugin.bin','old mod'),
        @('Mods\another.mod\keep.txt','unrelated mod')
    )) {
        $p=Get-QPath $Fixture.root $pair[0]
        [IO.Directory]::CreateDirectory((Split-Path -Parent $p))|Out-Null
        [IO.File]::WriteAllText($p,$pair[1])
    }
    Copy-QItem (Join-Path $workspace 'tools\lustral\winhttp.dll') (Get-QPath $Fixture.root 'winhttp.dll')
    Copy-QItem (Join-Path $workspace 'tools\lustral\Lustral\install.json') (Get-QPath $Fixture.root 'Lustral\install.json')
}

try {
    $manifest=Assert-QBundle $Bundle
    Check (@($manifest.loaderFiles).Count -eq 266) 'pinned complete loader/runtime archive verified'
    foreach($bad in @('..\escape','nested\..\escape','C:\outside','\\server\share','nested:stream')) {
        Reject {Get-QPath $private $bad} ('reject unsafe relative path: ' + $bad)
    }
    $empty=Get-QPath $private 'wrong-folder'
    [IO.Directory]::CreateDirectory($empty)|Out-Null
    Reject {Assert-QGame $empty $manifest} 'wrong folder rejected before installation'

    Write-Host 'Creating isolated game-file fixture; no game executable will be launched.'
    $fresh=Fixture "游戏 & 空格's"
    Assert-QGame $fresh.root $manifest
    Assert-QLoaderCompatibility $fresh.root $manifest
    Check $true 'game fingerprints and special-character directory accepted'
    $tamper=Get-QPath $fresh.package 'payload\Mods\local.quantumtransport\QuantumTransport.dll'
    $original=[IO.File]::ReadAllBytes($tamper)
    [IO.File]::WriteAllBytes($tamper,[byte[]](1,2,3))
    Reject {Assert-QBundle $fresh.package} 'tampered Mod payload rejected'
    [IO.File]::WriteAllBytes($tamper,$original)
    $proxy=Get-QPath $fresh.root 'winhttp.dll'
    [IO.File]::WriteAllText($proxy,'other loader proxy')
    Reject {Assert-QLoaderCompatibility $fresh.root $manifest} 'unknown proxy not overwritten'
    Check ([IO.File]::ReadAllText($proxy) -eq 'other loader proxy') 'conflicting proxy retained byte for byte'
    Remove-QItem $fresh.root 'winhttp.dll'
    $meta=Get-QPath $fresh.root 'MoonSweepers_Data\il2cpp_data\Metadata\global-metadata.dat'
    $metaBytes=[IO.File]::ReadAllBytes($meta)
    [IO.File]::WriteAllText($meta,'different game version')
    Reject {Assert-QGame $fresh.root $manifest} 'unsupported game version rejected'
    [IO.File]::WriteAllBytes($meta,$metaBytes)

    $shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fresh.package 'scripts\Install-Complete.ps1') -CheckOnly
    Check ($LASTEXITCODE -eq 0) 'actual PowerShell 5.1 entry point resolves parent game with spaces ampersand apostrophe and Chinese'
    Check (-not (Test-Path -LiteralPath (Get-QPath $fresh.root 'winhttp.dll'))) 'check-only entry does not install loader'
    if($ReuseVerifiedInterop){Copy-QItem (Join-Path $gameSource 'Lustral\interop') (Get-QPath $fresh.root 'Lustral\interop')}

    $new=Invoke-QInstall $fresh.package $fresh.root $manifest $fresh.log $fresh.saves $playerSid
    Check $new.success 'clean installation succeeds with real bundled generator and runtime'
    if($ReuseVerifiedInterop){Check (-not $new.generatedInterop) 'focused fault regression reuses previously verified interfaces'}
    else{Check $new.generatedInterop 'fresh interfaces generated locally without executing game'}
    Check $new.saveFilesUnchanged 'fixture save unchanged after fresh install'
    $writeRules=(Get-Acl -LiteralPath (Join-Path $fresh.root 'Lustral\config')).Access | Where-Object {
        $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $playerSid -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -eq [Security.AccessControl.FileSystemRights]::Modify }
    Check (@($writeRules).Count -gt 0) 'original player has Modify access to runtime config directory'
    Check (Test-QInterop (Get-QPath $fresh.root 'Lustral\interop') $manifest) 'generated unstripped interop identity and required CLR assemblies validated'
    Check (Test-Path -LiteralPath (Get-QPath $new.backup 'save-backup\MoonSessions\模拟存档.bin')) 'save backup created and verified'
    Check (Test-QFileSetsEqual (Read-QFileSet $fresh.saves) (Read-QFileSet (Join-Path $new.backup 'save-backup\MoonSessions'))) 'complete install backs up and SHA256-verifies long-path chunk saves'
    Check ($new.installerRevision -eq 2) 'installation report identifies long-path installer revision 2'
    Check (-not (Test-Path -LiteralPath (Get-QPath $fresh.root 'Mods\local.quantumtransport\old-plugin.bin'))) 'fresh Mod contains declared files only'
    $cache=Get-QPath $fresh.root 'Lustral\interop\interop.json'
    $cacheText=[IO.File]::ReadAllText($cache)
    [IO.File]::WriteAllText($cache,$cacheText.Replace('"Unstripped": true','"Unstripped": false'))
    Check (-not (Test-QInterop (Get-QPath $fresh.root 'Lustral\interop') $manifest)) 'missing unstripping is not treated as successful install'
    [IO.File]::WriteAllText($cache,$cacheText)
    $binding=Get-QPath $fresh.root 'Lustral\interop\Unity.InputSystem.dll'
    $bindingBytes=[IO.File]::ReadAllBytes($binding)
    [IO.File]::WriteAllText($binding,'broken assembly')
    Check (-not (Test-QInterop (Get-QPath $fresh.root 'Lustral\interop') $manifest)) 'truncated cached binding rejected even if manifest claims success'
    [IO.File]::WriteAllBytes($binding,$bindingBytes)
    $uiBinding=Get-QPath $fresh.root 'Lustral\interop\Unity.TextMeshPro.dll'
    $uiBytes=[IO.File]::ReadAllBytes($uiBinding)
    [IO.File]::WriteAllText($uiBinding,'missing UI interface')
    Check (-not (Test-QInterop (Get-QPath $fresh.root 'Lustral\interop') $manifest)) 'incomplete dashboard text interfaces cannot be reused as valid cache'
    [IO.File]::WriteAllBytes($uiBinding,$uiBytes)

    $config=Get-QPath $fresh.root 'Lustral\config\local.quantumtransport.toml'
    [IO.File]::WriteAllText($config,"enabled = false`nlog_level = `"Debug`"`nrun_native_checks = false`n")
    $configHash=(Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
    $other=Get-QPath $fresh.root 'Mods\another.mod\keep.txt'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $other))|Out-Null
    [IO.File]::WriteAllText($other,'unrelated')
    $stale=Get-QPath $fresh.root 'Mods\local.quantumtransport\obsolete.dll'
    [IO.File]::WriteAllText($stale,'stale plugin file')
    $second=Invoke-QInstall $fresh.package $fresh.root $manifest $fresh.log $fresh.saves $playerSid
    Check ($second.success -and -not $second.generatedInterop) 'reinstall reuses complete matching interfaces'
    Check ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -eq $configHash) 'disabled custom config preserved byte for byte'
    Check ([IO.File]::ReadAllText($other) -eq 'unrelated') 'unrelated Mod retained'
    Check (-not (Test-Path -LiteralPath $stale)) 'stale files removed only from this Mod folder'
    Check (Test-Path -LiteralPath (Get-QPath $second.backup 'previous\Mods\local.quantumtransport\obsolete.dll')) 'obsolete old plugin remains recoverable in backup'

    $failed=Fixture 'rollback-network'
    New-CustomOldState $failed
    $oldLoader=Read-QFileSet (Join-Path $failed.root 'Lustral')
    $oldMods=Read-QFileSet (Join-Path $failed.root 'Mods')
    $oldSaves=Read-QFileSet $failed.saves
    & $module {
        $script:QOriginalGenerator=(Get-Command Invoke-QGenerator).ScriptBlock
        Set-Item -Path Function:script:Invoke-QGenerator -Value {param($GameDir,$Output,$LogRoot) throw 'Simulated Unity library/network failure'}
    }
    try { Reject {Invoke-QInstall $failed.package $failed.root $manifest $failed.log $failed.saves} 'generator failure triggers transactional rollback' 'Simulated Unity library/network failure' }
    finally { & $module {Set-Item -Path Function:script:Invoke-QGenerator -Value $script:QOriginalGenerator} }
    Check (Test-QFileSetsEqual $oldLoader (Read-QFileSet (Join-Path $failed.root 'Lustral'))) 'all old loader/config/data files restored after generator failure'
    Check (Test-QFileSetsEqual $oldMods (Read-QFileSet (Join-Path $failed.root 'Mods'))) 'old target Mod and unrelated Mod restored after generator failure'
    Check (Test-QFileSetsEqual $oldSaves (Read-QFileSet $failed.saves)) 'save unchanged during failure rollback'
    $failedJournal=Get-ChildItem -LiteralPath (Join-Path $failed.root 'QuantumTransportBackups') -Filter install-journal.json -Recurse | Select-Object -First 1
    Check (([IO.File]::ReadAllText($failedJournal.FullName)|ConvertFrom-Json).status -eq 'rolled-back') 'rollback result persisted to journal'

    & $module {
        param($target)
        $script:QOriginalCopy=(Get-Command Copy-QItem).ScriptBlock
        $script:QCopyFaultTarget=$target; $script:QCopyFaultOnce=$true
        Set-Item -Path Function:script:Copy-QItem -Value {
            param($Source,$Destination)
            if($script:QCopyFaultOnce -and $Destination -eq $script:QCopyFaultTarget) {
                $script:QCopyFaultOnce=$false; throw 'Simulated write failure after target removal'
            }
            & $script:QOriginalCopy $Source $Destination
        }
    } (Join-Path $failed.root 'Mods\local.quantumtransport')
    try { Reject {Invoke-QInstall $failed.package $failed.root $manifest $failed.log $failed.saves} 'copy failure after directory removal rolls back' 'Simulated write failure after target removal' }
    finally { & $module {Set-Item -Path Function:script:Copy-QItem -Value $script:QOriginalCopy} }
    Check (Test-QFileSetsEqual $oldLoader (Read-QFileSet (Join-Path $failed.root 'Lustral'))) 'loader restored after partial copy failure'
    Check (Test-QFileSetsEqual $oldMods (Read-QFileSet (Join-Path $failed.root 'Mods'))) 'deleted old plugin restored from backup after copy failure'

    $identity=Get-QPath $failed.root 'Lustral\install.json'
    $oldIdentity=[IO.File]::ReadAllText($identity)
    [IO.File]::WriteAllText($identity,$oldIdentity.Replace('"version": "0.1.0"','"version": "0.2.0"'))
    Reject {Assert-QLoaderCompatibility $failed.root $manifest} 'different loader version not silently downgraded'
    [IO.File]::WriteAllText($identity,$oldIdentity)

    $outside=Get-QPath $private 'junction-target'
    [IO.Directory]::CreateDirectory($outside)|Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'),'outside sentinel')
    $junction=Get-QPath $failed.root 'Lustral\core\linked'
    New-Item -ItemType Junction -Path $junction -Target $outside|Out-Null
    try {
        Reject {Invoke-QInstall $failed.package $failed.root $manifest $failed.log $failed.saves} 'junction inside replacement tree rejected before mutation'
        Check ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -eq 'outside sentinel') 'junction target untouched'
    }
    finally { [IO.Directory]::Delete($junction) }

    Check (Test-QFileSetsEqual $actualBefore (Read-QFileSet (Join-Path $gameSource 'Mods\local.quantumtransport'))) 'actual user game Mod installation unchanged'
    Check (Test-QFileSetsEqual $saveBefore (Read-QFileSet $savesReal)) 'all actual user saves unchanged'
    Check ((Get-FileHash -LiteralPath $realLoaderConfig.Path -Algorithm SHA256).Hash -eq $realLoaderConfig.Hash -and
           (Get-FileHash -LiteralPath $realModConfig.Path -Algorithm SHA256).Hash -eq $realModConfig.Hash) 'actual user loader/Mod configs unchanged'
    Check (-not (Get-Process MoonSweepers -ErrorAction SilentlyContinue)) 'no MoonSweepers game process started'
    $completed=$true
}
catch { $testError=$_.Exception.Message; throw }
finally {
    $success=$completed -and @($checks|Where-Object {-not $_.passed}).Count -eq 0
    $report=[pscustomobject]@{success=$success;error=$testError;reusedPreviouslyVerifiedInterop=[bool]$ReuseVerifiedInterop;checks=$checks.ToArray();scope='Windows PowerShell 5.1 isolated game-file fixture; native generation evidence recorded separately when using focused replay; no game execution or gameplay validation.'}
    Write-QJson $report (Join-Path $workspace 'artifacts\complete-installer-tests.json')
    foreach($file in Get-ChildItem -LiteralPath $private -Filter 'generator.*.log' -File -Recurse) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $workspace ('artifacts\complete-installer-' + $file.Name)) -Force
    }
    # Only this newly created, marked fixture directory may be recursively deleted.
    $resolved=[IO.Path]::GetFullPath($private)
    $owned=Join-Path $resolved '.test-owned'
    if ($resolved.StartsWith($tempBase+'\',[StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolved).StartsWith('QuantumInstallerTests-') -and
        (Test-Path -LiteralPath $owned) -and [IO.File]::ReadAllText($owned) -eq 'QuantumInstallerTests') {
        [IO.Directory]::Delete((ConvertTo-QExtendedPath $resolved),$true)
    }
}
Write-Host ('COMPLETE: ' + $checks.Count + ' installer checks passed. No user game files changed.')
