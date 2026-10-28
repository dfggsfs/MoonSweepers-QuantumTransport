param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers',
    [string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'diagnostic-bundles')
)
$ErrorActionPreference = 'Stop'
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
$root = Join-Path $OutputDir "quantum-diagnostics-$stamp"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$diagnostics = Join-Path $GameDir 'Lustral\data\local.quantumtransport\diagnostics'
if (Test-Path -LiteralPath $diagnostics) {
    foreach ($name in @('status.json','quantum.jsonl','quantum.1.jsonl','quantum.2.jsonl')) {
        $path = Join-Path $diagnostics $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { Copy-Item -LiteralPath $path -Destination $root }
    }
}
foreach ($relative in @('Lustral\Logs\latest.log','Lustral\loader.toml','Lustral\config\local.quantumtransport.toml')) {
    $path = Join-Path $GameDir $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) { Copy-Item -LiteralPath $path -Destination (Join-Path $root ([IO.Path]::GetFileName($relative))) }
}
$hashes = foreach ($relative in @('GameAssembly.dll','Mods\local.quantumtransport\QuantumTransport.dll','Mods\local.quantumtransport\QuantumTransport.Core.dll')) {
    $path = Join-Path $GameDir $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) { [pscustomobject]@{ File=$relative; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'file-hashes.json') -Encoding utf8
Compress-Archive -LiteralPath $root -DestinationPath ($root + '.zip')
Write-Host "Diagnostic bundle: $root.zip"
Write-Host 'No saves, no upload, and no gameplay commands were included.'
