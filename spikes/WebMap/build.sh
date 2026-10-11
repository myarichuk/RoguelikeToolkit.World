#!/usr/bin/env bash
# Publishes the engine (Core -> browser-wasm) and drops its _framework next to the page.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet publish "$here/Engine/WebMap.Engine.csproj" -c Release -o "$here/Engine/bin/publish" "$@"
rm -rf "$here/web/_framework"
cp -r "$here/Engine/bin/publish/wwwroot/_framework" "$here/web/_framework"
echo "Built. Serve with: python3 -m http.server -d \"$here/web\" 8080"
