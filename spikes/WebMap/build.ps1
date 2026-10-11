#!/usr/bin/env pwsh
# Publishes the engine (Core -> browser-wasm) and drops its _framework next to the page.
# Extra arguments are passed through to dotnet publish.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

dotnet publish (Join-Path $here 'Engine/WebMap.Engine.csproj') -c Release -o (Join-Path $here 'Engine/bin/publish') @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$target = Join-Path $here 'web/_framework'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item (Join-Path $here 'Engine/bin/publish/wwwroot/_framework') $target -Recurse

Write-Host "Built. Serve with: python -m http.server -d `"$(Join-Path $here 'web')`" 8080"
