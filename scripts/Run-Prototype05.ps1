$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$relayProject = Join-Path $root 'relay\LynxRelay\LynxRelay.csproj'
$operatorProject = Join-Path $root 'desktop\Lynx.Operator\Lynx.Operator.csproj'
$operatorExe = Join-Path $root 'desktop\Lynx.Operator\bin\x64\Debug\net9.0-windows10.0.19041.0\Lynx.Operator.exe'

Write-Host 'Lynx Prototype 0.5 - Desktop Operator Marker Baseline'
Write-Host '----------------------------------------------------'
Write-Host ''
Write-Host 'Building relay + operator...'
& dotnet build $relayProject --nologo
if ($LASTEXITCODE -ne 0) { throw 'Relay build failed.' }
& dotnet build $operatorProject '-p:Platform=x64' --nologo
if ($LASTEXITCODE -ne 0) { throw 'Operator build failed.' }

$existing = Get-NetUDPEndpoint -LocalPort 7777 -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'UDP :7777 is already bound; using the existing relay process.'
} else {
    Write-Host 'Starting authoritative relay on UDP :7777...'
    $relayArgs = @('run', '--no-build', '--project', $relayProject, '--', '--listen', '7777')
    $relay = Start-Process -FilePath 'dotnet' -PassThru -ArgumentList $relayArgs
    Write-Host ('Relay PID: ' + $relay.Id)
    Start-Sleep -Milliseconds 500
}

Write-Host 'Starting Lynx Operator...'
$operator = Start-Process -FilePath $operatorExe -PassThru
Write-Host ('Operator PID: ' + $operator.Id)
Write-Host ''
Write-Host 'In the operator app:'
Write-Host '  1. Click Connect (default relay 127.0.0.1:7777), or choose Auto.'
Write-Host '  2. Load a PNG/JPEG floorplan.'
Write-Host '  3. Start calibration: origin -> +X reference -> +Z side.'
Write-Host '  4. Switch to Place Marker and click the floorplan.'
Write-Host ''
Write-Host 'Prototype 0.5 is running.'