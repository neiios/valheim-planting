#!/usr/bin/env bash
# Builds the mod and copies it into BepInEx/plugins. Run inside `nix develop`.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VALHEIM_DIR="${VALHEIM_DIR:-$HOME/.local/share/Steam/steamapps/common/Valheim}"

if [[ ! -f "$VALHEIM_DIR/BepInEx/core/BepInEx.dll" ]]; then
  echo "BepInEx is not installed yet; running scripts/install-bepinex.sh first"
  VALHEIM_DIR="$VALHEIM_DIR" "$root/scripts/install-bepinex.sh"
fi

dotnet build "$root/BulkPlanting/BulkPlanting.csproj" -c Release -p:ValheimDir="$VALHEIM_DIR"

dest="$VALHEIM_DIR/BepInEx/plugins/BulkPlanting"
mkdir -p "$dest"
cp "$root/BulkPlanting/bin/Release/net462/BulkPlanting.dll" "$dest/"
echo "Installed $dest/BulkPlanting.dll"
