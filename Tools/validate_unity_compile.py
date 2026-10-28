"""Compile workspace scripts using an installed project's actual Unity references. No Library writes."""
import argparse
from pathlib import Path
import subprocess

p = argparse.ArgumentParser()
p.add_argument('project', type=Path)
p.add_argument('--editor', type=Path, default=Path('C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data'))
args = p.parse_args()
root = Path(__file__).resolve().parents[1]
responses = list((args.project/'Library/Bee/artifacts').glob('*.dag/Assembly-CSharp.rsp'))
if not responses:
    raise SystemExit('Open this project in Unity once to produce compilation references.')
source = max(responses, key=lambda x:x.stat().st_mtime)
output = root/'.validation/unity'
output.mkdir(parents=True, exist_ok=True)
lines = ['-target:library', '-langversion:9.0', '-nologo', '-utf8output', '-debug:portable', '-out:"'+str(output/'CoMPaSS.Validation.dll')+'"']
for line in source.read_text(encoding='utf-8-sig').splitlines():
    if line.startswith(('-define:', '/define:', '-reference:', '/reference:', '-r:')):
        if 'reference:' in line or line.startswith('-r:'):
            prefix, value = line.split(':',1)
            path = Path(value.strip('"'))
            if not path.is_absolute(): path = args.project/path
            line = prefix+':"'+str(path)+'"'
        lines.append(line)
lines += ['"'+str(path)+'"' for path in (root/'Assets/Scripts').rglob('*.cs') if 'Editor' not in path.parts]
response = output/'compile.rsp'
response.write_text('\n'.join(lines),encoding='utf-8')
result = subprocess.run([str(args.editor/'NetCoreRuntime/dotnet.exe'), str(args.editor/'DotNetSdkRoslyn/csc.dll'), '@'+str(response)],cwd=root)
raise SystemExit(result.returncode)
