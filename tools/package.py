"""Package the published compact build (or --standalone) and full sources."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from shutil import copy2
import hashlib
import argparse
import json
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--standalone', action='store_true')
parser.add_argument('--build-name', help='Name of a prepared folder inside artifacts')
args = parser.parse_args()

root = Path(__file__).resolve().parents[1]
artifacts = root / 'artifacts'
version = ET.parse(root/'src/Murbingo.App/Murbingo.App.csproj').findtext('./PropertyGroup/Version')
source_name = f'Murbingo2-v{version}-source.zip'
build_name = args.build_name or (f'Murbingo2-v{version}-win-x64' + ('-standalone' if args.standalone else ''))
if Path(build_name).name != build_name or build_name in ('.', '..'):
    parser.error('--build-name must be a folder name, not a path')
portable = artifacts / build_name
for name in ('README.md', 'TEST-REPORT.md'):
    copy2(root / name, portable / name)
with ZipFile(artifacts / f'{build_name}.zip', 'w', ZIP_DEFLATED, compresslevel=6) as archive:
    for file in sorted(portable.rglob('*')):
        if file.is_file():
            archive.write(file, file.relative_to(portable).as_posix())
with ZipFile(artifacts / source_name, 'w', ZIP_DEFLATED, compresslevel=6) as archive:
    for folder in ('src', 'tests', 'tools'):
        for file in sorted((root / folder).rglob('*')):
            if file.is_file() and not {'bin', 'obj', '__pycache__'}.intersection(file.relative_to(root).parts):
                archive.write(file, file.relative_to(root).as_posix())
    for name in ('README.md', 'TEST-REPORT.md', 'build.ps1', 'test.ps1', '.gitignore', '.gitattributes', 'Murbingo2.slnx'):
        archive.write(root / name, name)
for name in (f'{build_name}.zip', source_name):
    path = artifacts / name
    with ZipFile(path) as archive:
        assert archive.testzip() is None, name
        if 'win-x64' in name:
            for required in ('Murbingo2.exe', 'Murbingo2.dll', 'Murbingo.Core.dll', 'Murbingo.Physics.dll', 'Murbingo2.runtimeconfig.json', 'config.json', 'assets/raptor-1.png', 'assets/app.ico', 'assets/app-icon.png', 'assets/sound-on.png', 'assets/sound-off.png', 'assets/draw-1.wav', 'assets/draw-2.wav', 'assets/draw-3.wav', 'assets/empty-pool-pepe.png', 'assets/empty-pool-pepe.json', 'assets/empty-pool-music.wav', 'assets/banner-title.png', 'locales/ru.json', 'locales/en.json'):
                assert required in archive.namelist(), required
            runtime_files = {'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll'}
            if args.standalone:
                assert runtime_files.issubset(archive.namelist())
            else:
                assert not runtime_files.intersection(archive.namelist())
                runtime = json.loads(archive.read('Murbingo2.runtimeconfig.json'))['runtimeOptions']
                assert 'includedFrameworks' not in runtime
                assert any(f['name'] == 'Microsoft.WindowsDesktop.App' and f['version'].startswith('10.0.') for f in runtime['frameworks'])
                assert {n for n in archive.namelist() if n.lower().endswith('.dll')} == {'Murbingo2.dll', 'Murbingo.Core.dll', 'Murbingo.Physics.dll'}
            assert archive.read('assets/raptor-1.png') == (root/'src/Murbingo.App/assets/raptor-1.png').read_bytes()
    print(f'{name}: {path.stat().st_size:,} bytes; SHA256 {hashlib.sha256(path.read_bytes()).hexdigest()}')
