param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers',
    [string]$InteropDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $InteropDir) { $InteropDir = Join-Path $GameDir 'Lustral/interop' }
$InteropDir = [IO.Path]::GetFullPath($InteropDir)
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/QuantumTransport.Mod/QuantumTransport.Mod.csproj') -Raw
$version = $project.Project.PropertyGroup.Version
$sdk = Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { throw 'Workspace .NET SDK not found in tools/dotnet.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
    & $sdk build 'src\QuantumTransport.Mod\QuantumTransport.Mod.csproj' --configuration Release --nologo -warnaserror "-p:GameDir=$GameDir" "-p:InteropDir=$InteropDir"
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
    New-Item -ItemType Directory -Path 'artifacts' -Force | Out-Null
    & $sdk run --project 'tests\QuantumTransport.Checks\QuantumTransport.Checks.csproj' --configuration Release -- --benchmark "artifacts\scale-benchmark-$version.json" | Tee-Object -FilePath 'artifacts\offline-core-tests.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    & $sdk run --project 'tools-src\AssemblyInspector\AssemblyInspector.csproj' --configuration Release -- --game $GameDir --interop $InteropDir --verify 'src\QuantumTransport.Mod\bin\Release\net10.0\QuantumTransport.dll' 'src\QuantumTransport.Mod\mod.toml' | Set-Content -LiteralPath 'artifacts\offline-interface-check.json' -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Offline interface checks failed.' }
    Write-Host 'Build and offline checks complete. No game process was started.'
}
finally { Pop-Location }
