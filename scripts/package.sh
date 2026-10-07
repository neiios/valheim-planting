#!/usr/bin/env bash
# Builds the release files into dist/. Run inside `nix develop`.
#   dist/BulkPlanting.dll                     for the PowerShell one-liner and manual installs
#   dist/BulkPlanting-<version>-windows.zip   double-click Windows installer + the DLL
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VALHEIM_DIR="${VALHEIM_DIR:-$HOME/.local/share/Steam/steamapps/common/Valheim}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/BulkPlanting/BulkPlanting.csproj")"

dotnet build "$root/BulkPlanting/BulkPlanting.csproj" -c Release -p:ValheimDir="$VALHEIM_DIR"

dist="$root/dist"
rm -rf "$dist"
mkdir -p "$dist/windows"
cp "$root/BulkPlanting/bin/Release/net462/BulkPlanting.dll" "$dist/"

# Windows tools expect CRLF line endings.
crlf() { sed 's/\r*$/\r/' "$1" > "$2"; }
crlf "$root/windows/install.ps1" "$dist/windows/install.ps1"
crlf "$root/windows/Install.bat" "$dist/windows/Install.bat"
crlf "$root/windows/Uninstall.bat" "$dist/windows/Uninstall.bat"
crlf "$root/windows/README.txt" "$dist/windows/README.txt"
cp "$dist/BulkPlanting.dll" "$dist/windows/"

(cd "$dist/windows" && zip -q -X "../BulkPlanting-$version-windows.zip" ./*)
rm -rf "$dist/windows"
ls -l "$dist"
