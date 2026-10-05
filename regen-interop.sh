#!/usr/bin/env bash
# Regenerate local interop assemblies for COMPILE-TIME reference, on Linux, with no
# Windows machine and no running game.
#
# Pipeline:  GameAssembly.dll + global-metadata.dat
#              -> Cpp2IL (dummy assemblies)
#              -> Il2CppInterop.CLI generate (interop assemblies)
#
# These only need to match the game's BUILD, not BepInEx's own generated hash. At
# runtime BepInEx generates its own set; keep both in sync by re-running this after
# any game update.
set -euo pipefail

export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_ROLL_FORWARD=LatestMajor

HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="${1:-$HOME/.local/share/Steam/steamapps/common/IRONSTRIKE}"
WORK="$HERE/.interop-build"

[[ -f "$GAME/GameAssembly.dll" ]] || { echo "ERROR: no GameAssembly.dll in $GAME" >&2; exit 1; }

CPP2IL="$WORK/cpp2il"
CPP2IL_VER="2022.1.0-pre-release.21"
mkdir -p "$WORK"
if [[ ! -x "$CPP2IL" ]]; then
  echo "==> downloading Cpp2IL $CPP2IL_VER"
  curl -sL -o "$CPP2IL" \
    "https://github.com/SamboyCoding/Cpp2IL/releases/download/$CPP2IL_VER/Cpp2IL-$CPP2IL_VER-Linux"
  chmod +x "$CPP2IL"
fi

command -v il2cppinterop >/dev/null || {
  echo "==> installing Il2CppInterop.CLI"
  dotnet tool install -g Il2CppInterop.CLI --version 1.5.3
}

echo "==> Cpp2IL: dummy assemblies"
rm -rf "$WORK/dummy"
"$CPP2IL" --game-path "$GAME" --exe-name Ironstrike \
          --output-to "$WORK/dummy" --output-as dummydll >/dev/null

# Unity base libraries are required for unstripping. Without them the generated assemblies
# DIVERGE from what BepInEx produces at runtime -- e.g. List<T> loses its indexer and only
# exposes get_Item, so code that compiles here fails against the real game.
UNITY_VER="2021.3.28"
if [[ ! -d "$WORK/unity-libs" ]]; then
  echo "==> fetching Unity $UNITY_VER base libraries"
  curl -sL -o "$WORK/unity-libs.zip" "https://unity.bepinex.dev/libraries/$UNITY_VER.zip"
  mkdir -p "$WORK/unity-libs"
  python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" \
          "$WORK/unity-libs.zip" "$WORK/unity-libs"
fi

echo "==> Il2CppInterop: interop assemblies"
rm -rf "$HERE/interop"
il2cppinterop generate --input "$WORK/dummy" --output "$HERE/interop" \
                       --game-assembly "$GAME/GameAssembly.dll" \
                       --unity "$WORK/unity-libs" >/dev/null

echo "==> done: $HERE/interop ($(ls "$HERE/interop" | wc -l) assemblies)"
