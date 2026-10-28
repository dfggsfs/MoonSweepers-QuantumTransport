"""Build a standalone folder-to-game installer from pinned, unmodified loader assets.

Never copy the installed game's interop, cache, logs, settings, or saves into a release.
"""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
VERSION = ET.parse(ROOT/'src/QuantumTransport.Mod/QuantumTransport.Mod.csproj').findtext('.//Version')
COMPATIBILITY = json.loads((ROOT/'distribution/game-compatibility.json').read_text(encoding='utf-8'))
NAME = f'QuantumTransport-{VERSION}-OneClick'
ZIP_NAME = NAME + '.zip'
OUT = ROOT / 'dist' / NAME
LOADER = ROOT / 'tools/downloads/lustral.zip'
OFFICIAL_SHA = '9dda93f4e2a10830fab8fd6b196562398d89e4760f492e3f7b1e9ade6d9e917b'
sha = lambda data: hashlib.sha256(data).hexdigest()
if sha(LOADER.read_bytes()) != OFFICIAL_SHA:
    raise SystemExit('Pinned official loader archive digest mismatch')
if OUT.exists():
    if '--refresh' not in sys.argv:
        raise SystemExit(f'Refusing to overwrite existing bundle: {OUT.name}')
    # Refresh only this exact generated bundle, and only its declared files.
    # Never delete a user's added logs, backup, or other files.
    prior = json.loads((OUT/'bundle-manifest.json').read_text(encoding='utf-8'))
    declared = {e['path'] for e in prior['files']} | {'bundle-manifest.json'}
    actual = {p.relative_to(OUT).as_posix() for p in OUT.rglob('*') if p.is_file()}
    if actual != declared or OUT.resolve().parent != (ROOT/'dist').resolve():
        raise SystemExit('Bundle contains additional files; refusing refresh')
    for relative in declared:
        p = (OUT/relative).resolve()
        if OUT.resolve() not in p.parents:
            raise SystemExit('Invalid prior manifest path')
    for relative in declared:
        (OUT/relative).unlink()
    for directory in sorted((p for p in OUT.rglob('*') if p.is_dir()), key=lambda p:len(p.parts), reverse=True):
        directory.rmdir()
    OUT.rmdir()

release = ROOT / 'dist' / f'QuantumTransport-{VERSION}'
old = json.loads((release / 'package-manifest.json').read_text(encoding='utf-8'))
for e in old['files']:
    if sha((release / e['path']).read_bytes()) != e['sha256']:
        raise SystemExit(f'Prior verified Mod release changed: {e["path"]}')

OUT.mkdir()
def copy(source, relative):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

def write(relative, text, encoding='utf-8-sig'):
    target = OUT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    # Explicit byte output preserves batch CRLF without Windows text mode
    # turning an existing CRLF into CRCRLF.
    target.write_bytes(text.encode(encoding))

for file in ['Install-Complete.ps1', 'Installer.Common.psm1']:
    # Windows PowerShell 5.1 needs a BOM for Chinese literals and instructions.
    write('scripts/' + file, (ROOT / 'scripts/complete' / file).read_text(encoding='utf-8-sig'))
copy(LOADER, 'payload/lustral-win-x64-il2cpp-0.1.0.zip')
for file in ['QuantumTransport.dll', 'QuantumTransport.Core.dll', 'mod.toml']:
    copy(release / 'Mods/local.quantumtransport' / file, 'payload/Mods/local.quantumtransport/' + file)
copy(ROOT / 'distribution/default-config.toml', 'defaults/local.quantumtransport.toml')
copy(ROOT / 'distribution/game-compatibility.json', 'game-compatibility.json')
copy(ROOT / 'scripts/Inspect-GameVersion.ps1', 'scripts/Inspect-GameVersion.ps1')
write('诊断游戏版本.cmd', '@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\Inspect-GameVersion.ps1"\r\npause\r\n', 'ascii')
write('defaults/loader.toml', '[log]\nlevel = "info"\nconsole = false\n\n[updates]\nmode = "off"\n')

for title, args in [('一键安装.cmd', ''), ('安装前检查.cmd', ' -CheckOnly')]:
    write(title, '@echo off\r\nsetlocal DisableDelayedExpansion\r\n'
          '"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" '
          '-NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\Install-Complete.ps1"' + args + '\r\n'
          'set "QT_INSTALL_EXIT=%ERRORLEVEL%"\r\necho.\r\npause\r\nexit /b %QT_INSTALL_EXIT%\r\n', 'ascii')

copy(ROOT / 'distribution/完整安装说明.md', '先看这里-安装说明.md')
write('README.md',(ROOT/'README.md').read_text(encoding='utf-8-sig').replace('docs/images/','preview/'))
copy(ROOT/f'docs/compatibility-{VERSION}.md', f'docs/compatibility-{VERSION}.md')
details = (ROOT / 'distribution/开始测试.md').read_text(encoding='utf-8-sig')
details = details.replace('其他电脑也需要对应游戏与 Lustral；ZIP 不含游戏、加载器、存档或备份。',
                          '本完整包已内置 Lustral 和运行时，其他电脑无需另装；首次接口补全可能需要联网。ZIP 不含游戏文件、个人存档或备份。')
details = details.replace('关闭游戏后运行“安装.cmd”。', '将本完整文件夹放在游戏目录后，运行“一键安装.cmd”。')
write('联机与功能测试.md', '> 本包安装步骤以《先看这里-安装说明.md》为准；已内置加载器。\n\n' + details)
copy(ROOT/f'artifacts/garage-dashboard-{VERSION}.md', '车库与面板说明.md')
for page in ('overview','machines','rules','interactions'):
    copy(ROOT/f'artifacts/dashboard-{page}.png',f'preview/dashboard-{page}.png')
copy(ROOT / 'artifacts/offline-core-tests.txt', 'verification/offline-core-tests.txt')
copy(ROOT / 'artifacts/offline-interface-check.json', 'verification/offline-interface-check.json')
copy(ROOT / f'artifacts/scale-benchmark-{VERSION}.json', f'verification/scale-benchmark-{VERSION}.json')
for name in ['complete-installer-tests.json', 'complete-installer-permissions.json', 'complete-installer-native-generation.json', 'complete-installer-launcher.json', 'complete-installer-longpaths.json']:
    evidence = json.loads((ROOT/'artifacts'/name).read_text(encoding='utf-8-sig'))
    if not evidence['success']:
        raise SystemExit(f'Installer verification has not passed: {name}')
    copy(ROOT/'artifacts'/name, 'verification/'+name)

# Preserve all upstream binary notices inside the unmodified loader ZIP; also make
# the main notices and corresponding LGPL sources directly available to recipients.
with zipfile.ZipFile(LOADER) as z:
    loader_files = []
    for name in z.namelist():
        if name.endswith('/'):
            continue
        data = z.read(name)
        loader_files.append({'path': name, 'sha256': sha(data), 'bytes': len(data)})
    for name in ['COPYING', 'COPYING.LESSER', 'THIRD-PARTY-NOTICES.md']:
        target = OUT / 'licenses' / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(z.read('Lustral/' + name))
copy(ROOT / 'tools/downloads/lustral-source.zip', 'licenses/sources/Lustral-v0.1.0-source.zip')
copy(ROOT / 'tools/downloads/il2cppinterop-1f168bf-source.zip', 'licenses/sources/Il2CppInterop-1f168bf-source.zip')
write('licenses/来源与许可.md', '''# 来源与许可

本包使用 Lustral 0.1.0（IL2CPP / Windows x64），发布者 LustralTeam。
加载器官方压缩包保持原样，SHA256：
9dda93f4e2a10830fab8fd6b196562398d89e4760f492e3f7b1e9ade6d9e917b

官方发行：https://github.com/LustralTeam/Lustral/releases/tag/v0.1.0
源代码：https://github.com/LustralTeam/Lustral/tree/v0.1.0
Lustral 提交：d8bacd68609ae2fac843517b273d2422759c8dfb
Il2CppInterop 源码版本：ds5678 分支提交 1f168bf，见 sources 中归档。
其生成器适配补丁的构建代码在 Lustral 源码 src/Lustral.Generator 内。

Lustral 与 Il2CppInterop 采用 LGPL-3.0，完整文本见 COPYING、COPYING.LESSER。
保留并随包提供 sources 内对应源码。库是独立 DLL，不禁止替换同版本库，
也不限制为调试库修改所需的逆向分析。本安装器仅组合发行，未修改上游加载器二进制。
其他组件（.NET、HarmonyX、MonoMod 等）的完整许可见 THIRD-PARTY-NOTICES.md，
及官方 ZIP 内运行时自己的 LICENSE.TXT / THIRD-PARTY-NOTICES.TXT。

Unity 补全库未随包再分发；首次需要时由上游 Generator 从其官方指定源下载并核验哈希：
https://github.com/LustralTeam/Lustral.UnityLibraries
游戏特定接口文件在接收者本机生成，不包含游戏 DLL 或开发者生成的游戏接口文件。
''')

mod_files = []
for name in ['QuantumTransport.dll', 'QuantumTransport.Core.dll', 'mod.toml']:
    data = (OUT / 'payload/Mods/local.quantumtransport' / name).read_bytes()
    mod_files.append({'path': 'Mods/local.quantumtransport/' + name, 'sha256': sha(data), 'bytes': len(data)})
files = [{'path': p.relative_to(OUT).as_posix(), 'sha256': sha(p.read_bytes()), 'bytes': p.stat().st_size}
         for p in sorted(OUT.rglob('*')) if p.is_file()]
manifest = {
    'format': 1, 'installerRevision': 2, 'modVersion': VERSION, 'loaderVersion': '0.1.0', 'platform': 'win-x64',
    'gameBuildId': COMPATIBILITY['steamBuildId'],
    'gameFiles': COMPATIBILITY['gameFiles'],
    # XxHash128 values used by Lustral's own cache manifest for these exact SHA-pinned inputs.
    'interop': COMPATIBILITY['interop'],
    'files': files, 'loaderFiles': loader_files, 'modFiles': mod_files,
    'scope': 'Loader/runtime and Mod installation only; static local interface generation; no game execution or gameplay validation.'}
write('bundle-manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2), 'utf-8')
with zipfile.ZipFile(ROOT / 'dist' / ZIP_NAME, 'w', zipfile.ZIP_DEFLATED) as z:
    for e in files + [{'path': 'bundle-manifest.json'}]:
        z.write(OUT / e['path'], NAME + '/' + e['path'])
print(json.dumps({'folder': NAME, 'files': len(files)+1, 'loaderFiles': len(loader_files),
                  'zip': ZIP_NAME, 'zipBytes': (ROOT/'dist'/ZIP_NAME).stat().st_size}))
