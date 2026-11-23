param(
    [Parameter(Mandatory = $true)]
    [string]$TabletIp
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"

Write-Host "Lynx Prototype 0.4"
Write-Host "------------------"
Write-Host "Relay listen      : UDP :7777"
Write-Host "Tablet receiver   : ${TabletIp}:7778"
Write-Host ""
Write-Host "Building .NET solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Starting relay -> tablet..."
dotnet run --no-build --project $relayProject -- --listen 7777 --forward "${TabletIp}:7778"