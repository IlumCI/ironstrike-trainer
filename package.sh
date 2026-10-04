#!/usr/bin/env bash
# Package IronstrikeTrainer for distribution.
#
# Produces dist/IronstrikeTrainer-<version>.zip laid out so that BOTH work:
#   * Thunderstore / r2modman / Gale  -> reads manifest.json + icon.png at the zip root
#   * manual install                  -> drag BepInEx/ over the game folder
#
# BepInEx already defines the mods folder (BepInEx/plugins). We do not invent our own
# loader or mods directory -- there is nothing to gain from it.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
VER="$(grep -oP '<Version>\K[^<]+' "$HERE/IronstrikeTrainer/IronstrikeTrainer.csproj")"
DLL="$HERE/IronstrikeTrainer/bin/Release/net6.0/IronstrikeTrainer.dll"
OUT="$HERE/dist"
STAGE="$OUT/stage"

[[ -f "$DLL" ]] || { echo "ERROR: build first (./build.sh)" >&2; exit 1; }

rm -rf "$STAGE"; mkdir -p "$STAGE/BepInEx/plugins"
cp "$DLL" "$STAGE/BepInEx/plugins/"
cp "$HERE/README.md" "$STAGE/README.md"
cp "$HERE/icon.png"  "$STAGE/icon.png"

cat > "$STAGE/manifest.json" <<JSON
{
  "name": "IronstrikeTrainer",
  "version_number": "$VER",
  "website_url": "",
  "description": "Unlocks the developer menu and adds a VR trainer submenu. Single-player only.",
  "dependencies": ["BepInEx-BepInExPack_IL2CPP-6.0.755"]
}
JSON

mkdir -p "$OUT"
ZIP="$OUT/IronstrikeTrainer-$VER.zip"
rm -f "$ZIP"
( cd "$STAGE" && python3 -c "
import zipfile,os,sys
z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED)
for root,_,files in os.walk('.'):
    for f in files:
        p=os.path.join(root,f)
        z.write(p, os.path.relpath(p,'.'))
z.close()
" "$ZIP" )

rm -rf "$STAGE"
echo "==> $ZIP"
python3 -c "
import zipfile,sys
for n in zipfile.ZipFile(sys.argv[1]).namelist(): print('   ', n)
" "$ZIP"
