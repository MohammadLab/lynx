$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

Write-Host "Lynx Prototype 0.2"
Write-Host "------------------"
Write-Host "Relay UDP port : 7777"
Write-Host "ViewerSim port : 7778"
Write-Host ""
Write-Host "PC IPv4 addresses:"
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown" } |
    Select-Object -ExpandProperty IPAddress |
    ForEach-Object { Write-Host "  $_" }

Write-Host ""
Write-Host "Building .NET solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"

Start-Process powershell -ArgumentList @("-NoExit", "-Command", "dotnet run --no-build --project `"$viewerProject`" -- --listen 7778")
Start-Sleep -Milliseconds 500
Start-Process powershell -ArgumentList @("-NoExit", "-Command", "dotnet run --no-build --project `"$relayProject`" -- --listen 7777 --forward 127.0.0.1:7778")

Write-Host ""
Write-Host "Started ViewerSim and LynxRelay in separate windows."
Write-Host "Point the Android app at this PC Wi-Fi IPv4 address on port 7777."
Write-Host "Close those two windows when finished."