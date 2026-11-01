#!/bin/sh
# Сборка и упаковка: dist/SltGrafika.bundle (+ zip). Нужен .NET SDK 8.
set -e
cd "$(dirname "$0")"
DOTNET=${DOTNET:-dotnet}
$DOTNET run --project tests/Slt.Tests
$DOTNET build src/Slt.Acad -c Release
rm -rf dist && mkdir -p dist/SltGrafika.bundle/Contents/net46 dist/SltGrafika.bundle/Contents/net8
cp bundle/SltGrafika.bundle/PackageContents.xml dist/SltGrafika.bundle/
cp src/Slt.Acad/bin/Release/net46/Slt.*.dll dist/SltGrafika.bundle/Contents/net46/
cp src/Slt.Acad/bin/Release/net8.0-windows/Slt.*.dll src/Slt.Acad/bin/Release/net8.0-windows/Slt.Acad.deps.json dist/SltGrafika.bundle/Contents/net8/
(cd dist && zip -qr SltGrafika.bundle.zip SltGrafika.bundle)
echo "Готово: dist/SltGrafika.bundle.zip"
