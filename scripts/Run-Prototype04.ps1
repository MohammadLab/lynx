$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"

Write-Host "Lynx Prototype 0.4"
Write-Host "------------------"
Write-Host "Relay listen : UDP :7777"
Write-Host "Clients self-register from their listening socket."
Write-Host ""
Write-Host "Checking Windows Firewall..."
$blockedRelayRules = @(
    Get-NetFirewallRule -Enabled True -Direction Inbound -Action Block -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match "lynxrelay" }
)
if ($blockedRelayRules.Count -gt 0) {
    Write-Warning "Windows Firewall currently has an enabled inbound BLOCK rule for LynxRelay."
    Write-Warning "LAN clients and auto-discovery will not reach UDP 7777 until that rule is changed in Windows Firewall."
}

Write-Host ""
Write-Host "Building .NET solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Starting authoritative relay..."
dotnet run --no-build --project $relayProject -- --listen 7777 --client-timeout-seconds 15