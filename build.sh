#!/bin/sh
# Builds BattleResupply.dll with the C# compiler that ships with Windows (.NET Framework 4), against the game's own
# libraries and BepInEx 5. Run from Git Bash:
#   GAME="/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)" sh build.sh
# BepInEx must already be installed in the game folder (its core libraries are referenced from there).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="${GAME:-/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)}"
M="$GAME/The Civil War (1861-1865)_Data/Managed"
B="${BEPINEX_CORE:-$GAME/BepInEx/core}"
w() { cygpath -w "$1"; }
mkdir -p "$HERE/bin"
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -noconfig -nologo -target:library -optimize+ -nostdlib+ \
  -out:"$(w "$HERE/bin/BattleResupply.dll")" \
  -reference:"$(w "$M/mscorlib.dll")" -reference:"$(w "$M/System.dll")" -reference:"$(w "$M/System.Core.dll")" \
  -reference:"$(w "$M/netstandard.dll")" -reference:"$(w "$M/Assembly-CSharp.dll")" \
  -reference:"$(w "$M/UnityEngine.dll")" -reference:"$(w "$M/UnityEngine.CoreModule.dll")" \
  -reference:"$(w "$B/BepInEx.dll")" -reference:"$(w "$B/0Harmony.dll")" \
  "$(w "$HERE/src")\*.cs"
echo "built $HERE/bin/BattleResupply.dll"
