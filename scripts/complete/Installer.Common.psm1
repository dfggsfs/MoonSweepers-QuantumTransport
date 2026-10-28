Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-QPath {
    param([string]$Root, [string]$Relative)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative -match '(^|[\\/])\.\.?([\\/]|$)|[:\x00]') { throw "不安全的相对路径：$Relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $path.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "路径超出指定目录：$path" }
    $current = $base
    foreach ($part in $path.Substring($base.Length + 1).Split('\')) {
        $current = Join-Path $current $part
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "为避免操作到其他目录，不处理链接或联接点：$current"
            }
        }
    }
    return $path
}

function Remove-QItem {
    param([string]$Root, [string]$Relative)
    $path = Get-QPath $Root $Relative
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

function Copy-QItem {
    param([string]$Source, [string]$Destination)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Write-QJson {
    param($Value, [string]$Path)
    $text = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function ConvertTo-QExtendedPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\?\')) { throw '不接受设备路径作为输入。' }
    if ($full.StartsWith('\\')) { return '\\?\UNC\' + $full.Substring(2) }
    return '\\?\' + $full
}

function Get-QTree {
    param([string]$Root)
    # The PowerShell 5.1 filesystem provider can fail at MAX_PATH even when
    # Windows supports long paths. Use extended .NET paths for every save IO.
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $entries = [Collections.Generic.List[object]]::new()
    $native = ConvertTo-QExtendedPath $base
    try { $attributes = [IO.File]::GetAttributes($native) }
    catch [IO.FileNotFoundException] { return ,$entries }
    catch [IO.DirectoryNotFoundException] { return ,$entries }
    if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "不处理链接或联接点：$base" }
    if (-not ($attributes -band [IO.FileAttributes]::Directory)) { throw "需要文件夹：$base" }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($native)
    while ($pending.Count) {
        foreach ($path in [IO.Directory]::GetFileSystemEntries($pending.Pop())) {
            $attributes = [IO.File]::GetAttributes($path)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "不处理链接或联接点：$path" }
            $relative = $path.Substring($native.Length + 1)
            $directory = [bool]($attributes -band [IO.FileAttributes]::Directory)
            $entries.Add([pscustomobject]@{relative=$relative;path=$path;directory=$directory})
            if ($directory) { $pending.Push($path) }
        }
    }
    return ,$entries
}

function Read-QFileSet {
    param([string]$Root)
    $result = @{}
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($entry in (Get-QTree $Root)) {
            if ($entry.directory) { continue }
            $stream = [IO.File]::OpenRead($entry.path)
            try { $result[$entry.relative] = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','') }
            finally { $stream.Dispose() }
        }
    }
    finally { $hash.Dispose() }
    return ,$result
}

function Copy-QSaveBackup {
    param([string]$Source, [string]$Destination)
    # Enumerate and reject links before writing any backup content. Destination
    # is a new transaction-owned folder, never an existing save directory.
    $entries = Get-QTree $Source
    $target = ConvertTo-QExtendedPath $Destination
    if ([IO.Directory]::Exists($target) -or [IO.File]::Exists($target)) { throw "存档备份目标已存在：$Destination" }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    foreach ($entry in $entries) {
        $path = $target + '\' + $entry.relative
        if ($entry.directory) { [IO.Directory]::CreateDirectory($path) | Out-Null }
        else {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
            [IO.File]::Copy($entry.path, $path, $false)
        }
    }
}

function Test-QFileSetsEqual {
    param($First, $Second)
    if ($First.Count -ne $Second.Count) { return $false }
    foreach ($key in $First.Keys) {
        if (-not $Second.ContainsKey($key) -or $First[$key] -ne $Second[$key]) { return $false }
    }
    return $true
}

function Assert-QBundle {
    param([string]$PackageRoot)
    $file = Get-QPath $PackageRoot 'bundle-manifest.json'
    $manifest = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    if ($manifest.format -ne 1 -or $manifest.modVersion -ne '0.7.0' -or
        $manifest.loaderVersion -ne '0.1.0' -or $manifest.platform -ne 'win-x64') { throw '不支持此安装包格式或版本。' }
    $seen = @{}
    foreach ($entry in $manifest.files) {
        $path = Get-QPath $PackageRoot $entry.path
        if ($seen.ContainsKey($path)) { throw "安装包清单有重复路径：$($entry.path)" }
        $seen[$path] = $true
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "安装包不完整或文件已变化，请重新完整解压：$($entry.path)"
        }
    }
    $archive = Get-QPath $PackageRoot 'payload\lustral-win-x64-il2cpp-0.1.0.zip'
    $official = '9DDA93F4E2A10830FAB8FD6B196562398D89E4760F492E3F7B1E9ADE6D9E917B'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $official) { throw '加载器与固定的官方发行包 SHA256 不符。' }
    return $manifest
}

function Assert-QGame {
    param([string]$GameDir, $Manifest)
    if (-not [Environment]::Is64BitOperatingSystem) { throw '需要 64 位 Windows。' }
    if (-not (Test-Path -LiteralPath (Get-QPath $GameDir 'MoonSweepers.exe') -PathType Leaf)) {
        throw '位置不对：请把整个安装包文件夹放到 MoonSweepers.exe 旁边，再双击一键安装。不要直接在 ZIP 内运行。'
    }
    if (Get-Process -Name MoonSweepers -ErrorAction SilentlyContinue) { throw '请先正常保存并关闭 Moon Sweepers；安装程序不会强制关闭游戏。' }
    foreach ($entry in $Manifest.gameFiles) {
        $path = Get-QPath $GameDir $entry.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "游戏版本不匹配或文件缺失：$($entry.path)。本包仅适配 Moon Sweepers 1.0.4 / Steam Build 26203101。"
        }
    }
}

function Assert-QLoaderCompatibility {
    param([string]$GameDir, $Manifest)
    $proxy = Get-QPath $GameDir 'winhttp.dll'
    $expected = @($Manifest.loaderFiles | Where-Object { $_.path -eq 'winhttp.dll' })
    if ($expected.Count -ne 1) { throw '加载器清单缺少唯一的 winhttp.dll。' }
    if ((Test-Path -LiteralPath $proxy) -and (Get-FileHash -LiteralPath $proxy -Algorithm SHA256).Hash -ne $expected[0].sha256) {
        throw '现有 winhttp.dll 属于其他版本或其他加载器，已停止且未覆盖。请先确认原加载器的处理方式。'
    }
    $installed = Get-QPath $GameDir 'Lustral\install.json'
    if (Test-Path -LiteralPath $installed) {
        $identity = [IO.File]::ReadAllText($installed) | ConvertFrom-Json
        if ($identity.product -ne 'Lustral' -or $identity.version -ne '0.1.0' -or
            $identity.backend -ne 'il2cpp' -or $identity.platform -ne 'win-x64') {
            throw '检测到其他版本/后端的 Lustral，已停止；不自动降级或混装。'
        }
    }
    elseif ((Test-Path -LiteralPath (Get-QPath $GameDir 'Lustral\core')) -and -not (Test-Path -LiteralPath $proxy)) {
        throw '发现无法确认版本的加载器 core 目录，已停止且未覆盖。'
    }
    foreach ($relative in @('BepInEx\core\BepInEx.Preloader.dll','BepInEx\core\BepInEx.Unity.IL2CPP.dll','MelonLoader\net6\MelonLoader.dll')) {
        if (Test-Path -LiteralPath (Get-QPath $GameDir $relative)) { throw "检测到另一套加载器：$relative。不自动混装。" }
    }
}

function Test-QInterop {
    param([string]$Directory, $Manifest)
    $file = Get-QPath $Directory 'interop.json'
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $false }
    try {
        $info = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        $expected = $Manifest.interop
        foreach ($field in @('Format','UnityVersion','Il2CppInteropVersion','GameAssemblyHash','MetadataHash')) {
            if ($info.$field -ne $expected.$field) { return $false }
        }
        if (-not $info.Unstripped) { return $false }
        foreach ($required in @('Assembly-CSharp.dll','Il2Cppmscorlib.dll','UnityEngine.CoreModule.dll','Unity.InputSystem.dll','Il2Cppcom.rlabrecque.steamworks.net.dll',
            'UnityEngine.UI.dll','UnityEngine.UIModule.dll','Unity.TextMeshPro.dll')) {
            $assembly = Get-QPath $Directory $required
            if (-not (Test-Path -LiteralPath $assembly -PathType Leaf) -or (Get-Item -LiteralPath $assembly).Length -lt 512) { return $false }
            if ([Reflection.AssemblyName]::GetAssemblyName($assembly).Name -ne [IO.Path]::GetFileNameWithoutExtension($required)) { return $false }
        }
        return $true
    }
    catch { return $false }
}

function Expand-QLoader {
    param([string]$PackageRoot, [string]$Stage, $Manifest)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Get-QPath $PackageRoot 'payload\lustral-win-x64-il2cpp-0.1.0.zip'))
    try {
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $destination = Get-QPath $Stage $entry.FullName
            if ($seen.ContainsKey($destination)) { throw '加载器压缩包路径重复。' }
            $seen[$destination] = $true
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        }
        if ($seen.Count -ne @($Manifest.loaderFiles).Count) { throw '加载器文件数量与清单不符。' }
        foreach ($entry in $Manifest.loaderFiles) {
            $path = Get-QPath $Stage $entry.path
            if (-not $seen.ContainsKey($path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
                throw "加载器解压校验失败：$($entry.path)"
            }
        }
    }
    finally { $archive.Dispose() }
}

function Invoke-QGenerator {
    param([string]$GameDir, [string]$Output, [string]$LogRoot)
    $exe = Get-QPath $GameDir 'Lustral\generator\Lustral.Generator.exe'
    $stdout = Get-QPath $LogRoot 'generator.stdout.log'
    $stderr = Get-QPath $LogRoot 'generator.stderr.log'
    $arguments = '"' + $GameDir + '" --output "' + $Output + '" --jobs 2'
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    # Windows PowerShell 5.1 can lose ExitCode unless it retains the process handle
    # before waiting; null must never be confused with a successful exit.
    $null = $process.Handle
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $nextMessage = 5
    try {
        while (-not $process.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalSeconds -ge $nextMessage) {
                Write-Host ("正在生成本机接口，已用 {0:N0} 秒；游戏未启动。详细进度：{1}" -f $watch.Elapsed.TotalSeconds, $stdout)
                $nextMessage += 10
            }
            if ($watch.Elapsed.TotalMinutes -ge 20) { throw '接口生成超过20分钟，已停止；请查看 generator 日志。' }
        }
        $process.WaitForExit()
        $exitCode=$process.ExitCode
        if ($null -eq $exitCode -or $exitCode -ne 0) { throw "接口生成失败或无法读取退出码（$exitCode）。请查看 generator 日志。" }
    }
    finally {
        if (-not $process.HasExited) {
            # This PID is the generator instance created above, never a game process.
            & (Join-Path $env:WINDIR 'System32\taskkill.exe') /PID $process.Id /T /F | Out-Null
        }
        $process.Dispose()
    }
}

function Restore-QChanged {
    param([string]$GameDir, [string]$Backup, $Changed)
    $errors = [Collections.Generic.List[string]]::new()
    for ($i = $Changed.Count - 1; $i -ge 0; $i--) {
        $entry = $Changed[$i]
        try {
            $destination = Get-QPath $GameDir $entry.relative
            $original = Get-QPath $Backup ('previous\' + $entry.relative)
            if ($entry.existed -and -not (Test-Path -LiteralPath $original)) { throw "备份缺失：$($entry.relative)" }
            Remove-QItem $GameDir $entry.relative
            if ($entry.existed) { Copy-QItem $original $destination }
        }
        catch { $errors.Add($_.Exception.Message) }
    }
    if ($errors.Count) { throw ('回滚未完全完成：' + ($errors -join '; ')) }
}

function Invoke-QInstall {
    param([string]$PackageRoot, [string]$GameDir, $Manifest, [string]$LogRoot, [string]$SaveRoot,[string]$PlayerSid='')
    $id = [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $workRelative = 'work\' + $id
    $work = Get-QPath $PackageRoot $workRelative
    $backup = Get-QPath $GameDir ('QuantumTransportBackups\' + $id)
    $loaderStage = Get-QPath $work 'loader'
    $generated = Get-QPath $work 'generated-interop'
    [IO.Directory]::CreateDirectory($loaderStage) | Out-Null
    [IO.File]::WriteAllText((Get-QPath $work '.quantum-owned'), $id)
    $changed = [Collections.Generic.List[object]]::new()
    $journal = [ordered]@{ format=1; installerRevision=2; status='preparing'; gameDir=$GameDir; modVersion=$Manifest.modVersion; backupComplete=$false; changes=@(); permissions=@(); error=$null; rollbackError=$null }
    $accessPlan=$null; $permissionsApplied=$false
    $report = [ordered]@{ success=$false; installerRevision=2; modVersion=$Manifest.modVersion; loaderVersion=$Manifest.loaderVersion; gameDir=$GameDir; backup=$backup; generatedInterop=$false; gameStarted=$false; saveFilesUnchanged=$false; configPreserved=$false; error=$null }
    try {
        Write-Host '[1/5] 解压并验证官方加载器（含 .NET 运行时）。'
        Expand-QLoader $PackageRoot $loaderStage $Manifest
        $needsInterop = -not (Test-QInterop (Get-QPath $GameDir 'Lustral\interop') $Manifest)
        $plan = [Collections.Generic.List[object]]::new()
        $relatives = @('winhttp.dll','Lustral\core','Lustral\dotnet','Lustral\generator','Lustral\install.json',
            'Lustral\COPYING','Lustral\COPYING.LESSER','Lustral\THIRD-PARTY-NOTICES.md','Mods\local.quantumtransport',
            'Lustral\loader.toml','Lustral\config\local.quantumtransport.toml')
        if ($needsInterop) { $relatives += 'Lustral\interop' }
        foreach ($relative in $relatives) {
            $path = Get-QPath $GameDir $relative
            $plan.Add([pscustomobject]@{relative=$relative;existed=(Test-Path -LiteralPath $path)})
        }
        # Validate descendant paths before any directory replacement; never traverse junctions.
        foreach ($entry in $plan) {
            $target = Get-QPath $GameDir $entry.relative
            if (Test-Path -LiteralPath $target -PathType Container) {
                foreach ($child in Get-ChildItem -LiteralPath $target -Force -Recurse) {
                    Get-QPath $GameDir $child.FullName.Substring($GameDir.TrimEnd('\').Length + 1) | Out-Null
                }
            }
        }
        $beforeSaves = Read-QFileSet $SaveRoot
        if($PlayerSid) { $accessPlan=Get-QAccessPlan $GameDir; $journal.permissions=@($accessPlan.ToArray()) }
        $beforeConfigs = @{}
        foreach ($relative in @('Lustral\loader.toml','Lustral\config\local.quantumtransport.toml')) {
            $path = Get-QPath $GameDir $relative
            if (Test-Path -LiteralPath $path -PathType Leaf) { $beforeConfigs[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
        Write-Host '[2/5] 备份旧加载器、目标 Mod、配置和当前用户存档。'
        [IO.Directory]::CreateDirectory($backup) | Out-Null
        foreach ($entry in $plan) {
            if ($entry.existed) { Copy-QItem (Get-QPath $GameDir $entry.relative) (Get-QPath $backup ('previous\' + $entry.relative)) }
        }
        if ([IO.Directory]::Exists((ConvertTo-QExtendedPath $SaveRoot))) {
            Copy-QSaveBackup $SaveRoot (Get-QPath $backup 'save-backup\MoonSessions')
            if (-not (Test-QFileSetsEqual $beforeSaves (Read-QFileSet (Get-QPath $backup 'save-backup\MoonSessions')))) { throw '存档备份校验失败，尚未修改安装文件。' }
        }
        $journal.backupComplete=$true; $journal.status='installing'
        Write-QJson $journal (Get-QPath $backup 'install-journal.json')
        Write-Host '[3/5] 安装加载器和量子传送；保留现有配置及其他 Mod。'
        foreach ($entry in $plan) {
            $relative = $entry.relative
            if ($relative -eq 'Lustral\interop') { continue }
            $source = Get-QPath $loaderStage $relative
            if ($relative -eq 'Mods\local.quantumtransport') { $source=Get-QPath $PackageRoot 'payload\Mods\local.quantumtransport' }
            if ($relative -eq 'Lustral\loader.toml') {
                if ($entry.existed) { continue }; $source=Get-QPath $PackageRoot 'defaults\loader.toml'
            }
            if ($relative -eq 'Lustral\config\local.quantumtransport.toml') {
                if ($entry.existed) { continue }; $source=Get-QPath $PackageRoot 'defaults\local.quantumtransport.toml'
            }
            $changed.Add($entry); $journal.changes=@($changed.ToArray())
            Write-QJson $journal (Get-QPath $backup 'install-journal.json')
            Remove-QItem $GameDir $relative
            Copy-QItem $source (Get-QPath $GameDir $relative)
        }
        Write-Host '[4/5] 检查或生成本机接口；首次可能需要联网下载 Unity 补全库。'
        if ($needsInterop) {
            Invoke-QGenerator $GameDir $generated $LogRoot
            if (-not (Test-QInterop $generated $Manifest)) {
                throw '接口不完整或缺少 Unity API 补全。首次安装请保持联网并能访问 GitHub；详情见 generator 日志，随后会恢复旧安装。'
            }
            $entry = @($plan | Where-Object { $_.relative -eq 'Lustral\interop' })[0]
            $changed.Add($entry); $journal.changes=@($changed.ToArray())
            Write-QJson $journal (Get-QPath $backup 'install-journal.json')
            Remove-QItem $GameDir $entry.relative
            Copy-QItem $generated (Get-QPath $GameDir $entry.relative)
            $report.generatedInterop=$true
        }
        Write-Host '[5/5] 核对安装文件、配置和存档。'
        if($PlayerSid) {
            $permissionsApplied=$true
            Set-QPlayerAccess $GameDir $PlayerSid $accessPlan
        }
        foreach ($entry in $Manifest.loaderFiles) {
            $path = Get-QPath $GameDir $entry.path
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "安装后加载器校验失败：$($entry.path)" }
        }
        foreach ($entry in $Manifest.modFiles) {
            $path = Get-QPath $GameDir $entry.path
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "安装后 Mod 校验失败：$($entry.path)" }
        }
        foreach ($relative in $beforeConfigs.Keys) {
            if ((Get-FileHash -LiteralPath (Get-QPath $GameDir $relative) -Algorithm SHA256).Hash -ne $beforeConfigs[$relative]) { throw '现有配置意外变化。' }
        }
        if (-not (Test-QFileSetsEqual $beforeSaves (Read-QFileSet $SaveRoot))) { throw '存档文件在安装期间发生变化，请保留备份并检查日志。' }
        $report.saveFilesUnchanged=$true; $report.configPreserved=$true
        $report.success=$true; $journal.status='complete'
        Write-Host '安装成功。请从 Steam 正常启动游戏；本程序未启动游戏。' -ForegroundColor Green
        Write-Host ('备份：' + $backup)
        return [pscustomobject]$report
    }
    catch {
        $report.error=$_.Exception.Message; $journal.error=$report.error
        if ($changed.Count -gt 0) {
            Write-Host '安装未完成，正在恢复安装前文件。' -ForegroundColor Yellow
            try {
                Restore-QChanged $GameDir $backup $changed
                if($permissionsApplied) {Restore-QAccess $GameDir $accessPlan}
                $journal.status='rolled-back'
            }
            catch { $journal.status='rollback-incomplete'; $journal.rollbackError=$_.Exception.Message }
        }
        else { $journal.status='failed-before-install' }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $backup -PathType Container) {
            Write-QJson $journal (Get-QPath $backup 'install-journal.json')
            Write-QJson $report (Get-QPath $backup 'install-report.json')
        }
        Write-QJson $report (Get-QPath $LogRoot 'install-result.json')
        $marker=Get-QPath $work '.quantum-owned'
        if ((Test-Path -LiteralPath $marker) -and [IO.File]::ReadAllText($marker) -eq $id) { Remove-QItem $PackageRoot $workRelative }
        if ($journal.rollbackError) { Write-Host ('自动恢复未完全完成，请保留备份：' + $journal.rollbackError) -ForegroundColor Red }
    }
}

function Get-QAccessPlan {
    param([string]$GameDir)
    $result=[Collections.Generic.List[object]]::new()
    foreach($relative in @('Lustral','Lustral\config','Lustral\Logs','Lustral\Reports','Lustral\data','Lustral\cache','Lustral\updates','Lustral\interop',
        'Lustral\loader.toml','Lustral\config\local.quantumtransport.toml')) {
        $path=Get-QPath $GameDir $relative
        $exists=Test-Path -LiteralPath $path
        $sddl=$null
        if($exists) { $sddl=(Get-Acl -LiteralPath $path).GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access) }
        $result.Add([pscustomobject]@{relative=$relative;existed=$exists;sddl=$sddl})
    }
    return ,$result
}

function Set-QPlayerAccess {
    param([string]$GameDir,[string]$PlayerSid,$Plan)
    $sid=[Security.Principal.SecurityIdentifier]::new($PlayerSid)
    foreach($entry in $Plan) {
        $path=Get-QPath $GameDir $entry.relative
        $isFile=$entry.relative.EndsWith('.toml')
        if(-not $isFile) { [IO.Directory]::CreateDirectory($path)|Out-Null }
        $acl=Get-Acl -LiteralPath $path
        $inherit=[Security.AccessControl.InheritanceFlags]::None
        $rights=[Security.AccessControl.FileSystemRights]::Modify
        if($entry.relative -eq 'Lustral') {
            $rights=[Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Write
        }
        elseif(-not $isFile) { $inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit }
        $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,$rights,$inherit,
            [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $path -AclObject $acl
    }
}

function Restore-QAccess {
    param([string]$GameDir,$Plan)
    for($i=$Plan.Count-1;$i -ge 0;$i--) {
        $entry=$Plan[$i]; $path=Get-QPath $GameDir $entry.relative
        if(-not (Test-Path -LiteralPath $path)) {continue}
        if($entry.existed) {
            $acl=Get-Acl -LiteralPath $path
            $acl.SetSecurityDescriptorSddlForm($entry.sddl,[Security.AccessControl.AccessControlSections]::Access)
            Set-Acl -LiteralPath $path -AclObject $acl
        }
        elseif((Test-Path -LiteralPath $path -PathType Container) -and @(Get-ChildItem -LiteralPath $path -Force).Count -eq 0) {
            [IO.Directory]::Delete($path)
        }
    }
}

Export-ModuleMember -Function *-Q*
