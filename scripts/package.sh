#!/usr/bin/env bash
# Builds the plugin in Release and assembles dist/Nuclearstasi/ ready to copy into BepInEx/plugins/.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj -c Release
out=src/Nuclearstasi.Plugin/bin/Release/net472
rm -rf dist && mkdir -p dist/Nuclearstasi
cp "$out/Nuclearstasi.dll" "$out/Nuclearstasi.Core.dll" dist/Nuclearstasi/
(cd dist && zip -qr Nuclearstasi.zip Nuclearstasi)
echo "Packaged dist/Nuclearstasi.zip"
