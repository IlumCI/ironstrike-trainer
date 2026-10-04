#!/usr/bin/env bash
# Package IronstrikeTrainer for distribution.
#
# Produces dist/IronstrikeTrainer-<version>.zip laid out so BOTH install paths work:
#   * Thunderstore / r2modman / Gale  -> manifest.json + icon.png + README.md at zip root
#   * manual install                  -> drag BepInEx/ over the game folder
#
# BepInEx already defines the mods folder (BepInEx/plugins). We do not invent our own
# loader or mods directory; there is nothing to gain from it.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$HERE/IronstrikeTrainer/IronstrikeTrainer.csproj"
VER="$(grep -oP '<Version>\K[^<]+' "$CSPROJ")"
DLL="$HERE/IronstrikeTrainer/bin/Release/net6.0/IronstrikeTrainer.dll"
OUT="$HERE/dist"; STAGE="$OUT/stage"

[[ -f "$DLL" ]] || { echo "ERROR: build first (./build.sh)" >&2; exit 1; }

# Version lives in the csproj; Plugin.cs must agree or the in-game log lies.
SRCVER="$(grep -oP 'Version\s*=\s*"\K[^"]+' "$HERE/IronstrikeTrainer/Plugin.cs" | head -1)"
if [[ "$SRCVER" != "$VER" ]]; then
  echo "ERROR: version drift -- csproj=$VER but Plugin.cs Id.Version=$SRCVER" >&2
  exit 1
fi

DESC="Unlocks the developer menu and adds a VR-navigable trainer submenu: god mode, insta-kill, invisibility, movement and projectile multipliers, friendly fire, and skill/weapon pickers. Single-player only."

rm -rf "$STAGE"; mkdir -p "$STAGE/BepInEx/plugins"
cp "$DLL" "$STAGE/BepInEx/plugins/"
cp "$HERE/README.md" "$STAGE/README.md"
cp "$HERE/icon.png"  "$STAGE/icon.png"

python3 - "$STAGE" "$VER" "$DESC" <<'PY'
import json, sys, re, os
stage, ver, desc = sys.argv[1], sys.argv[2], sys.argv[3]
m = {
    "name": "IronstrikeTrainer",
    "version_number": ver,
    "website_url": "",
    "description": desc,
    "dependencies": ["BepInEx-BepInExPack_IL2CPP-6.0.755"],
}
# Validate against Thunderstore's documented constraints before writing.
errs = []
if not re.fullmatch(r"[A-Za-z0-9_]+", m["name"]):
    errs.append("name must be alphanumeric/underscore only")
if not re.fullmatch(r"\d+\.\d+\.\d+", m["version_number"]):
    errs.append("version_number must be major.minor.patch")
if len(m["description"]) > 250:
    errs.append(f"description is {len(m['description'])} chars, max 250")
for d in m["dependencies"]:
    if not re.fullmatch(r"[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+", d):
        errs.append(f"bad dependency string: {d}")
if errs:
    print("manifest validation FAILED:"); [print("  -", e) for e in errs]; sys.exit(1)
open(os.path.join(stage, "manifest.json"), "w").write(json.dumps(m, indent=2) + "\n")
print(f"manifest.json ok (description {len(m['description'])}/250 chars)")
PY

# Icon must be exactly 256x256 PNG.
python3 - "$STAGE/icon.png" <<'PY'
import sys
from PIL import Image
im = Image.open(sys.argv[1])
assert im.format == "PNG", f"icon must be PNG, got {im.format}"
assert im.size == (256, 256), f"icon must be 256x256, got {im.size}"
print(f"icon.png ok ({im.format} {im.size[0]}x{im.size[1]})")
PY

mkdir -p "$OUT"
ZIP="$OUT/IronstrikeTrainer-$VER.zip"
rm -f "$ZIP"
( cd "$STAGE" && python3 -c "
import zipfile,os,sys
z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED)
for root,_,files in os.walk('.'):
    for f in sorted(files):
        p=os.path.join(root,f)
        z.write(p, os.path.relpath(p,'.'))
z.close()
" "$ZIP" )
rm -rf "$STAGE"

echo
echo "==> $ZIP"
python3 -c "
import zipfile,sys
z=zipfile.ZipFile(sys.argv[1])
for i in z.infolist(): print(f'    {i.file_size:>8}  {i.filename}')
print('    bad zip!' if z.testzip() else '    zip integrity OK')
" "$ZIP"
echo
sha256sum "$ZIP" | sed 's/^/    /'
