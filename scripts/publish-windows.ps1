# Publishes the self-contained Windows application, the CLI and the demo projects for the installer.
param([string]$Configuration = "Release", [string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
Set-Location $root
$out = Join-Path $root "artifacts/publish"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/DND.Electrical.App -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true -o "$out/app"
dotnet publish src/DND.Electrical.Cli -c $Configuration -r $Runtime --self-contained true -o "$out/app/cli"
dotnet run --project src/DND.Electrical.Cli -c $Configuration -- demo all --out "$out/demo"
Write-Host "Published to $out"
