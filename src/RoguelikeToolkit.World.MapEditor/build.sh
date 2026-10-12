#!/usr/bin/env bash
# Builds the map editor: publishes the engine (Core -> browser-wasm), copies its _framework into
# public/, then builds the web app into dist/.
#   ./build.sh            interpreted .NET (fast build, ~1.5 MB brotli)
#   ./build.sh --aot      AOT-compiled .NET (needs `dotnet workload install wasm-tools`; slower build, faster generation)
#   ./build.sh --engine   engine only (then `npm run dev`)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../RoguelikeToolkit.World.MapEditor.Engine"
aot=false; engine_only=false
for a in "$@"; do
  case "$a" in
    --aot) aot=true ;;
    --engine) engine_only=true ;;
    *) echo "unknown option: $a" >&2; exit 2 ;;
  esac
done
rm -rf "$engine/bin/publish"   # stale fingerprinted files from a previous (AOT/non-AOT) build would ship too
dotnet publish "$engine/RoguelikeToolkit.World.MapEditor.Engine.csproj" -c Release -o "$engine/bin/publish" -p:RunAOTCompilation=$aot
rm -rf "$here/public/_framework"
cp -r "$engine/bin/publish/wwwroot/_framework" "$here/public/_framework"
$engine_only && { echo "Engine published to public/_framework. Run: npm run dev"; exit 0; }
cd "$here"
if [ -f package-lock.json ]; then npm ci; else npm install; fi
npm run build
echo "Built into $here/dist. Preview with: npm run preview"
