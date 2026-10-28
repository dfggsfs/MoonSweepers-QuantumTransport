$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $workspace 'dist\QuantumTransport-0.7.0-OneClick\scripts\Installer.Common.psm1') -Force -DisableNameChecking
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$root=Join-Path $temp ('QuantumPermissionTests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $root 'Lustral\config'))|Out-Null
[IO.File]::WriteAllText((Join-Path $root '.test-owned'),'QuantumPermissionTests')
[IO.File]::WriteAllText((Join-Path $root 'Lustral\loader.toml'),'old loader config')
[IO.File]::WriteAllText((Join-Path $root 'Lustral\config\local.quantumtransport.toml'),'old mod config')
$results=[Collections.Generic.List[object]]::new()
$completed=$false
function Check([bool]$ok,[string]$label) {$results.Add([pscustomobject]@{passed=$ok;label=$label}); if(-not $ok){throw $label};Write-Host "PASS: $label"}
function AccessSignature([string]$sddl) {
    $descriptor=[Security.AccessControl.RawSecurityDescriptor]::new($sddl)
    $acl=$descriptor.DiscretionaryAcl
    $bytes=[byte[]]::new($acl.BinaryLength); $acl.GetBinaryForm($bytes,0)
    # Windows may set the bookkeeping AutoInherited flag while restoring an ACL;
    # compare actual ACEs and inheritance protection, not that bookkeeping bit.
    return ([Convert]::ToBase64String($bytes)+':'+($descriptor.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected))
}
try {
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $top=(Get-Acl -LiteralPath $root).Sddl
    $beforeFiles=Read-QFileSet $root
    $plan=Get-QAccessPlan $root
    Set-QPlayerAccess $root $sid $plan
    Check ((Get-Acl -LiteralPath $root).Sddl -eq $top) 'whole game directory permissions unchanged'
    Check (Test-Path -LiteralPath (Join-Path $root 'Lustral\Logs') -PathType Container) 'runtime log directory prepared'
    Check (Test-QFileSetsEqual $beforeFiles (Read-QFileSet $root)) 'permission setup does not alter configuration contents'
    Restore-QAccess $root $plan
    foreach($entry in $plan) {
        $path=Get-QPath $root $entry.relative
        if($entry.existed) {
            $restored=(Get-Acl -LiteralPath $path).GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
            Check ((AccessSignature $restored) -eq (AccessSignature $entry.sddl)) ('original access rules and inheritance protection restored: '+$entry.relative)
        }
    }
    Check (-not (Test-Path -LiteralPath (Join-Path $root 'Lustral\Logs'))) 'new empty runtime directory removed on permission rollback'
    Check (Test-QFileSetsEqual $beforeFiles (Read-QFileSet $root)) 'permission rollback preserves original files'
    $completed=$true
}
finally {
    Write-QJson ([pscustomobject]@{success=$completed;checks=$results.ToArray();scope='Local temporary fixture, grant only current user and restore original ACLs; no user game ACL or files changed.'}) (Join-Path $workspace 'artifacts\complete-installer-permissions.json')
    if([IO.Path]::GetFullPath($root).StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase) -and
       (Split-Path -Leaf $root).StartsWith('QuantumPermissionTests-') -and
       [IO.File]::ReadAllText((Join-Path $root '.test-owned')) -eq 'QuantumPermissionTests') {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
