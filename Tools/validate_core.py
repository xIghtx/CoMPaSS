"""Run the pure C# regression suite with installed .NET references; no NuGet/network restore."""
from pathlib import Path
import json
import subprocess

root = Path(__file__).resolve().parents[1]
dotnet = Path('C:/Program Files/dotnet')
sdk = max((dotnet/'sdk').glob('*'), key=lambda p: tuple(int(x) for x in p.name.split('.') if x.isdigit()))
refs = max((dotnet/'packs/Microsoft.NETCore.App.Ref').glob('*'), key=lambda p: tuple(int(x) for x in p.name.split('.') if x.isdigit()))
framework = next((refs/'ref').glob('net*'))
out = root/'.validation/core'
out.mkdir(parents=True, exist_ok=True)
globals_file = out/'GlobalUsings.cs'
globals_file.write_text('global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\nglobal using System.IO;\nglobal using System.Threading;\n',encoding='utf-8')
lines = ['-target:exe','-langversion:latest','-nologo','-utf8output','-out:"'+str(out/'Regression.dll')+'"']
lines += ['-r:"'+str(p)+'"' for p in framework.glob('*.dll')]
for directory in ['Core','Algorithms','Experiment','Persistence']:
    lines += ['"'+str(p)+'"' for p in (root/'Assets/Scripts'/directory).glob('*.cs')]
lines += ['"'+str(root/'Tools/Regression'/name)+'"' for name in ['Program.cs','JsonUtilityShim.cs']]
lines.append('"'+str(globals_file)+'"')
rsp = out/'compile.rsp'
rsp.write_text('\n'.join(lines),encoding='utf-8')
subprocess.run([str(dotnet/'dotnet.exe'),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],cwd=root,check=True)
(out/'Regression.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':framework.name,'framework':{'name':'Microsoft.NETCore.App','version':refs.name}}}),encoding='utf-8')
with (out/'results.txt').open('w',encoding='utf-8') as report:
    result = subprocess.run([str(dotnet/'dotnet.exe'),str(out/'Regression.dll')],cwd=root,stdout=report,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace',timeout=180)
print((out/'results.txt').read_text(encoding='utf-8'))
raise SystemExit(result.returncode)
