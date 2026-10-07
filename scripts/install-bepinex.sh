#!/usr/bin/env bash
# Installs BepInExPack_Valheim (the mod loader) into the Valheim game folder.
# Safe to re-run; it only adds/overwrites BepInEx files and never touches game files.
set -euo pipefail

VERSION="5.4.2351"
SHA256="bce631497976a93977ceb08e166712e6c31d15244956f89f17df092a9b62e29f"
VALHEIM_DIR="${VALHEIM_DIR:-$HOME/.local/share/Steam/steamapps/common/Valheim}"

if [[ ! -f "$VALHEIM_DIR/valheim.exe" && ! -f "$VALHEIM_DIR/valheim.x86_64" ]]; then
  echo "Valheim not found in $VALHEIM_DIR (set VALHEIM_DIR=...)" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "Downloading BepInExPack_Valheim $VERSION..."
curl -fsSL -o "$work/pack.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$VERSION/"
echo "$SHA256  $work/pack.zip" | sha256sum -c --quiet

mkdir "$work/x"
unzip -q "$work/pack.zip" -d "$work/x"
cp -r "$work/x/BepInExPack_Valheim/." "$VALHEIM_DIR/"
chmod u+x "$VALHEIM_DIR/start_game_bepinex.sh" "$VALHEIM_DIR/start_server_bepinex.sh"
mkdir -p "$VALHEIM_DIR/BepInEx/plugins"

echo "BepInEx installed into $VALHEIM_DIR"
if [[ -f "$VALHEIM_DIR/valheim.exe" ]]; then
  echo
  echo "You run the Windows build through Proton. Set this once in Steam:"
  echo "  Valheim > Properties > General > Launch Options:"
  echo '    WINEDLLOVERRIDES="winhttp=n,b" %command%'
fi
