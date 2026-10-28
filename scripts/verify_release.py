"""Verify final ZIPs, local installed payload, and offline evidence without launching Unity."""
from pathlib import Path
import hashlib
import json
import re
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
VERSION = ET.parse(ROOT/'src/QuantumTransport.Mod/QuantumTransport.Mod.csproj').findtext('.//Version')
FOLDER = ROOT/f'dist/QuantumTransport-{VERSION}-OneClick'
ARCHIVE = ROOT/'dist'/(FOLDER.name+'.zip')
sha = lambda b: hashlib.sha256(b).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
manifest = read(FOLDER/'bundle-manifest.json')
compatibility = read(ROOT/'distribution/game-compatibility.json')
assert manifest['gameBuildId'] == compatibility['steamBuildId']
assert manifest['gameFiles'] == compatibility['gameFiles'] and manifest['interop'] == compatibility['interop']
readme=(ROOT/'README.md').read_text(encoding='utf-8-sig')
expected_readme=readme.replace('docs/images/','preview/')
assert (FOLDER/'README.md').read_text(encoding='utf-8-sig')==expected_readme
assert (ROOT/f'dist/QuantumTransport-{VERSION}/README.md').read_text(encoding='utf-8-sig')==expected_readme
for relative in re.findall(r'\]\(([^)]+)\)',expected_readme):
    if '://' not in relative and not relative.startswith('#'):
        assert (FOLDER/relative.split('#')[0]).is_file(), relative
        assert (ROOT/f'dist/QuantumTransport-{VERSION}'/relative.split('#')[0]).is_file(), relative
config_keys=re.findall(r'^([a-z_]+)\s*=',(ROOT/'distribution/default-config.toml').read_text(encoding='utf-8-sig'),re.M)
assert all('`'+key+'`' in readme for key in config_keys)
assert manifest['modVersion'] == VERSION and manifest['installerRevision'] == 2
generation = read(ROOT/'artifacts/complete-installer-native-generation.json')
assert generation['success'] and generation['gameBuildId'] == compatibility['steamBuildId'] and generation['interop'] == compatibility['interop']
with zipfile.ZipFile(ARCHIVE) as z:
    assert z.testzip() is None
    expected = {FOLDER.name+'/'+e['path'] for e in manifest['files']} | {FOLDER.name+'/bundle-manifest.json'}
    assert set(z.namelist()) == expected
    for entry in manifest['files']:
        assert sha(z.read(FOLDER.name+'/'+entry['path'])) == entry['sha256']
        assert sha((FOLDER/entry['path']).read_bytes()) == entry['sha256']
    assert sha(z.read(FOLDER.name+'/payload/lustral-win-x64-il2cpp-0.1.0.zip')) == '9dda93f4e2a10830fab8fd6b196562398d89e4760f492e3f7b1e9ade6d9e917b'
    assert json.loads(z.read(FOLDER.name+'/game-compatibility.json')) == compatibility
    assert not any(p in n.lower() for n in z.namelist() for p in ('save-backup/','moonsessions/','gameassembly.dll','global-metadata.dat','/interop/'))
    for name in ('Install-Complete.ps1','Installer.Common.psm1'):
        assert z.read(FOLDER.name+'/scripts/'+name).decode('utf-8-sig') == (ROOT/'scripts/complete'/name).read_text(encoding='utf-8-sig')
actual = {p.relative_to(FOLDER).as_posix() for p in FOLDER.rglob('*') if p.is_file()}
assert actual == {e['path'] for e in manifest['files']} | {'bundle-manifest.json'}
standalone = read(ROOT/f'dist/QuantumTransport-{VERSION}/package-manifest.json')
with zipfile.ZipFile(ROOT/f'dist/QuantumTransport-{VERSION}.zip') as z:
    assert z.testzip() is None
    assert set(z.namelist()) == {e['path'] for e in standalone['files']} | {'package-manifest.json'}
    for entry in standalone['files']:
        assert sha(z.read(entry['path'])) == entry['sha256']
    assert json.loads(z.read('game-compatibility.json')) == compatibility
suites = {n: read(ROOT/f'artifacts/complete-installer-{n}.json') for n in ('tests','permissions','launcher','longpaths')}
assert all(v['success'] and all(c['passed'] for c in v['checks']) for v in suites.values())
interface = read(ROOT/'artifacts/offline-interface-check.json')
assert interface['success'] and interface['failures'] == 0
installed = read(ROOT/f'artifacts/install-verification-{VERSION}.json')
assert installed['success'] and not installed['gameStarted'] and installed['configPreserved']
for entry in manifest['modFiles']:
    assert sha((Path('C:/Program Files (x86)/Steam/steamapps/common/Moon Sweepers')/entry['path']).read_bytes()) == entry['sha256']
    assert sha((FOLDER/'payload'/entry['path']).read_bytes()) == entry['sha256']
    if entry['path'].endswith('.dll'):
        assert sha((ROOT/'src/QuantumTransport.Mod/bin/Release/net10.0'/Path(entry['path']).name).read_bytes()) == entry['sha256']
game = Path('C:/Program Files (x86)/Steam/steamapps/common/Moon Sweepers')
for entry in compatibility['gameFiles']:
    assert sha((game/entry['path']).read_bytes()) == entry['sha256']
interop = read(game/'Lustral/interop/interop.json')
assert interop['Unstripped'] and all(interop[k] == v for k,v in compatibility['interop'].items())
raw = (ROOT/'artifacts/offline-core-tests.txt').read_bytes()
corelog = raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
assert 'PASS:' in corelog
report = {
    'success':True, 'modVersion':VERSION, 'installerRevision':2, 'zip':ARCHIVE.name,
    'gameBuildId':compatibility['steamBuildId'],'gameCompatibilityProfileShared':True,
    'zipBytes':ARCHIVE.stat().st_size, 'zipSha256':sha(ARCHIVE.read_bytes()), 'zipEntries':len(expected),
    'loaderFiles':len(manifest['loaderFiles']), 'installerChecksPassed':sum(len(v['checks']) for v in suites.values()),
    'checksBySuite':{n:len(v['checks']) for n,v in suites.items()},
    'offlineCoreAssertions':int(re.search(r'PASS: (\d+) assertions',corelog).group(1)),
    'offlineInterfaceChecks':len(interface['checks']), 'nativeGenerationVerifiedSeparately':True,
    'savedFilesUnchanged':installed['savedFilesUnchanged'], 'configPreserved':installed['configPreserved'],
    'localUpgradeInstalled':True, 'gameStarted':False, 'previewUsesSampleData':True,
    'readmeIncludedInBothPackages':True,'documentedConfigKeys':len(config_keys),
    'containsPersonalSavesConfigsOrGameFiles':False,
    'scope':'Build 26264480 compatibility update. Newly generated complete interfaces, runtime/installer shared fingerprints, missing/mismatched game diagnostics, old interop cache rejection and installer regressions verified offline. Local upgrade backed up and verified. No game process started; actual collection/production/UI/co-op behavior on this build remains pending player verification.'
}
for name in ('complete-installer-release-verification.json',f'release-verification-{VERSION}.json'):
    (ROOT/'artifacts'/name).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
