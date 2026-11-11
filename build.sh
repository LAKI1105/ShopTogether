#!/bin/bash
# Builds ShopTogether.dll. Usage: ./build.sh [probe]
set -euo pipefail
cd "$(dirname "$0")"
: "${PROBABLY_STOLEN_PATH:?Set PROBABLY_STOLEN_PATH to your installed game directory}"
mkdir -p gen
python3 - <<'PY'
import hashlib,glob,os
files=sorted(glob.glob('src/*.cs'),key=lambda p:os.path.abspath(p).lower())
def read(p):
    t=open(p,encoding='utf-8-sig').read()
    return t
text="\n".join(read(f) for f in files)+read('src/manifest.xml')
code=hashlib.sha256(text.encode('utf-8')).hexdigest().upper()[:24]
open('gen/BuildIdentity.cs','w').write('namespace ShopTogether { internal static class BuildIdentity { internal const string Code="'+code+'"; } }\n')
print("build identity",code)
PY
if [ "${1:-}" = "probe" ]; then
  dotnet build ShopTogether.csproj -c Release -o out-probe -p:DefineConstants=LAN_PROBE -p:ProbeBuild=true 2>&1 | grep -E "error|Build succeeded" | sort -u
else
  dotnet build ShopTogether.csproj -c Release -o out 2>&1 | grep -E "error|Build succeeded" | sort -u
fi
