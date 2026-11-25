$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"

Write-Host "Lynx Prototype 0.4"
Write-Host "------------------"
Write-Host "Relay listen : UDP :7777"
Write-Host "Clients self-register from their listening socket."
Write-Host ""
Write-Host "Building .NET solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Starting authoritative relay..."
dotnet run --no-build --project $relayProject -- --listen 7777 --client-timeout-seconds 15