param(
    [ValidateSet('true','false')][string]$Enabled = 'false',
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
)
$ErrorActionPreference = 'Stop'
$path = Join-Path $GameDir 'Lustral\config\local.quantumtransport.toml'
if (-not (Test-Path -LiteralPath $path)) { throw 'Mod configuration not found.' }
$text = [IO.File]::ReadAllText($path)
if ($text -notmatch '(?m)^enabled\s*=\s*(true|false)\s*$') { throw 'Enabled setting is missing or malformed. No file changed.' }
$text = [regex]::Replace($text, '(?m)^enabled\s*=\s*(true|false)\s*$', "enabled = $Enabled")
[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
Write-Host "enabled = $Enabled. Restart the game for this file change to take effect. In-game control: Ctrl+F8."
