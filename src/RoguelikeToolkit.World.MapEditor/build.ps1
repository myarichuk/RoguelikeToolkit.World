# Builds the map editor: publishes the engine (Core -> browser-wasm), copies its _framework into
# public/, then builds the web app into dist/.
#   ./build.ps1            interpreted .NET
#   ./build.ps1 -Aot       AOT-compiled .NET (needs `dotnet workload install wasm-tools`)
#   ./build.ps1 -EngineOnly  engine only (then `npm run dev`)
[CmdletBinding()]
param([switch]$Aot, [switch]$EngineOnly)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$engine = Join-Path $here '..\RoguelikeToolkit.World.MapEditor.Engine'
$publish = Join-Path $engine 'bin\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }  # stale fingerprinted files would ship too
dotnet publish (Join-Path $engine 'RoguelikeToolkit.World.MapEditor.Engine.csproj') -c Release -o $publish "-p:RunAOTCompilation=$($Aot.IsPresent.ToString().ToLower())"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
$target = Join-Path $here 'public\_framework'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item (Join-Path $publish 'wwwroot\_framework') $target -Recurse
if ($EngineOnly) { Write-Host 'Engine published to public/_framework. Run: npm run dev'; return }
Push-Location $here
try {
  if (Test-Path 'package-lock.json') { npm ci } else { npm install }
  if ($LASTEXITCODE -ne 0) { throw "npm install failed ($LASTEXITCODE)" }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "npm run build failed ($LASTEXITCODE)" }
} finally { Pop-Location }
Write-Host "Built into $here\dist. Preview with: npm run preview"
