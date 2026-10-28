param([switch]$Elevated, [switch]$CheckOnly,[string]$PlayerSaveRoot='', [string]$PlayerSid='')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$package = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$game = Split-Path -Parent $package
$transcript=$false
$mutex=$null
$lockHeld=$false
if(-not $PlayerSaveRoot) { $PlayerSaveRoot=Join-Path $env:USERPROFILE 'AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions' }
if(-not $PlayerSid) { $PlayerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
try {
    Import-Module (Join-Path $PSScriptRoot 'Installer.Common.psm1') -Force -DisableNameChecking
    Write-Host '量子传送 0.7.0 完整安装（含长路径备份修复）：加载器 + .NET 运行时 + Mod' -ForegroundColor Cyan
    Write-Host ('游戏目录：' + $game)
    Write-Host '正在检查安装包和游戏版本。'
    $manifest = Assert-QBundle $package
    Assert-QGame $game $manifest
    Assert-QLoaderCompatibility $game $manifest
    if ($CheckOnly) { Write-Host '安装前检查通过，未修改游戏文件。'; exit 0 }
    $probe = Get-QPath $game ('.quantum-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try { [IO.File]::WriteAllText($probe, 'probe'); [IO.File]::Delete($probe) }
    catch [UnauthorizedAccessException] {
        if ($Elevated) { throw '取得管理员权限后仍无法写入，请检查游戏文件夹权限。' }
        Write-Host '游戏目录需要写入权限，正在请求 Windows 管理员授权。'
        $shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Elevated -PlayerSaveRoot "' + $PlayerSaveRoot + '" -PlayerSid "' + $PlayerSid + '"'
        try {
            $child=Start-Process -FilePath $shell -ArgumentList $arguments -Verb RunAs -WindowStyle Normal -PassThru
            $null=$child.Handle
            $child.WaitForExit()
            $childExit=$child.ExitCode
            if($null -eq $childExit){throw '无法读取提升权限后安装程序的退出状态。'}
        }
        catch { throw '未获得管理员写入权限；安装尚未开始。' }
        exit $childExit
    }
    $hash=[Security.Cryptography.SHA256]::Create()
    try { $key=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($game.ToLowerInvariant())))).Replace('-','').Substring(0,24) }
    finally { $hash.Dispose() }
    $mutex=[Threading.Mutex]::new($false,('Local\QuantumTransportInstaller-' + $key))
    try { $lockHeld=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $lockHeld=$true }
    if (-not $lockHeld) { throw '另一个安装程序正在处理此游戏目录，请等待其完成。' }
    $disk=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($game))
    if ($disk.AvailableFreeSpace -lt 2GB) { throw '安装及备份需要至少2GB可用磁盘空间。' }
    $stamp=[DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
    $log = Get-QPath $package ('logs\' + $stamp)
    [IO.Directory]::CreateDirectory($log) | Out-Null
    Start-Transcript -LiteralPath (Get-QPath $log 'installer.log') | Out-Null
    $transcript=$true
    $report=Invoke-QInstall $package $game $manifest $log $PlayerSaveRoot $PlayerSid
    Write-Host ('日志：' + $log)
    Write-Host '联机完整功能需要主机和客户端都安装本版本。'
    exit 0
}
catch {
    Write-Host ('安装未完成：' + $_.Exception.Message) -ForegroundColor Red
    if ($transcript) { Write-Host '请保留 logs 中的安装日志，以及游戏目录 QuantumTransportBackups 中的备份。' }
    exit 1
}
finally {
    if ($transcript) { Stop-Transcript | Out-Null }
    if ($lockHeld) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
}
