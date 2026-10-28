"""Stage only declared release files; saves/backups must never enter the ZIP."""
from pathlib import Path
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile
import sys

root = Path(__file__).resolve().parent.parent
version = ET.parse(root / 'src/QuantumTransport.Mod/QuantumTransport.Mod.csproj').findtext('.//Version')
release = root / 'dist' / f'QuantumTransport-{version}'
if release.exists() and '--refresh' in sys.argv:
    prior = json.loads((release/'package-manifest.json').read_text(encoding='utf-8'))
    declared = {e['path'] for e in prior['files']} | {'package-manifest.json'}
    actual = {p.relative_to(release).as_posix() for p in release.rglob('*') if p.is_file()}
    if actual != declared or release.resolve().parent != (root/'dist').resolve():
        raise SystemExit('Refusing refresh: release contains added logs/backups/user files')
    for relative in declared:
        if release.resolve() not in (release/relative).resolve().parents:
            raise SystemExit('Unsafe prior release manifest path')
    for relative in declared:
        (release/relative).unlink()
    for directory in sorted((p for p in release.rglob('*') if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
        directory.rmdir()
    release.rmdir()
release.mkdir(exist_ok=False)
files = {
    'README.md': root/'README.md',
    '开始测试.md': root / 'distribution/开始测试.md',
    'default-config.toml': root / 'distribution/default-config.toml',
    'offline-core-tests.txt': root / 'artifacts/offline-core-tests.txt',
    'offline-interface-check.json': root / 'artifacts/offline-interface-check.json',
    'production-stall-0.2.1.md': root / 'artifacts/production-stall-0.2.1.md',
    'verification-0.2.1.json': root / 'artifacts/user-verification-0.2.1/verification.json',
    'collection-delivery-audit-0.4.0.md': root / 'artifacts/collection-delivery-audit-0.4.0.md',
    'coop-scale-audit-0.5.0.md': root / 'artifacts/coop-scale-audit-0.5.0.md',
    f'scale-benchmark-{version}.json': root / f'artifacts/scale-benchmark-{version}.json',
    f'garage-dashboard-{version}.md': root / f'artifacts/garage-dashboard-{version}.md',
    'preview/dashboard-overview.png': root / 'artifacts/dashboard-overview.png',
    'preview/dashboard-machines.png': root / 'artifacts/dashboard-machines.png',
    'preview/dashboard-rules.png': root / 'artifacts/dashboard-rules.png',
    'preview/dashboard-interactions.png': root / 'artifacts/dashboard-interactions.png',
    'Mods/local.quantumtransport/mod.toml': root / 'src/QuantumTransport.Mod/mod.toml',
}
for name in ['QuantumTransport.dll', 'QuantumTransport.Core.dll']:
    files[f'Mods/local.quantumtransport/{name}'] = root / 'src/QuantumTransport.Mod/bin/Release/net10.0' / name
for name in ['Preflight.ps1', 'Install.ps1', 'Set-Enabled.ps1', 'Collect-Diagnostics.ps1']:
    files[f'scripts/{name}'] = root / 'scripts' / name
files['scripts/Installer.Common.psm1'] = root/'scripts/complete/Installer.Common.psm1'
for relative, source in files.items():
    destination = release / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    if relative=='README.md':
        destination.write_text(source.read_text(encoding='utf-8-sig').replace('docs/images/','preview/'),encoding='utf-8')
commands = {
    '安装.cmd': 'Install.ps1',
    '运行前检查.cmd': 'Preflight.ps1',
    '启用量子传送.cmd': 'Set-Enabled.ps1 -Enabled true',
    '禁用量子传送.cmd': 'Set-Enabled.ps1 -Enabled false',
    '收集日志.cmd': 'Collect-Diagnostics.ps1',
}
for name, command in commands.items():
    script, _, arguments = command.partition(' ')
    (release / name).write_bytes((f'@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\{script}" {arguments}\r\npause\r\n').encode('ascii'))
entries = []
for path in sorted(release.rglob('*')):
    if path.is_file():
        entries.append({'path': path.relative_to(release).as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size})
manifest = {'version': version, 'scope': 'single-player and host-authoritative co-op quantum logistics, acknowledged native cargo handoff, bounded scheduler; co-op gameplay and actual frame-time verification pending', 'files': entries}
(release / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
with zipfile.ZipFile(Path(str(release) + '.zip'), 'w', zipfile.ZIP_DEFLATED) as archive:
    for entry in entries + [{'path': 'package-manifest.json'}]:
        archive.write(release / entry['path'], entry['path'])
print(release)
