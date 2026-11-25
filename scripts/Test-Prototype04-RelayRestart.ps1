$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relay1Log = Join-Path $env:TEMP "lynx-04-restart-relay1.log"
$relay2Log = Join-Path $env:TEMP "lynx-04-restart-relay2.log"
$viewerLog = Join-Path $env:TEMP "lynx-04-restart-viewer.log"
Remove-Item $relay1Log, $relay2Log, $viewerLog -ErrorAction SilentlyContinue

function Wait-ForPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            $text = Get-Content $Path -Raw -ErrorAction SilentlyContinue
            if ($text -match $Pattern) { return }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for pattern: $Pattern"
}

function Send-UdpJson([int]$Port, [string]$Json) {
    $udp = [System.Net.Sockets.UdpClient]::new()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Json)
        [void]$udp.Send($bytes, $bytes.Length, "127.0.0.1", $Port)
    } finally { $udp.Dispose() }
}

Write-Host "[RelayRestart] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7801")
$viewerArgs = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7823", "--client", "303", "--relay", "127.0.0.1:7801", "--heartbeat-seconds", "1")
$relay1 = $null
$relay2 = $null
$viewer = $null

try {
    Write-Host "[RelayRestart] Starting first relay epoch..."
    $relay1 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relay1Log -ArgumentList $relayArgs
    Wait-ForPattern $relay1Log "Listening on UDP :7801"

    $viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList $viewerArgs
    Wait-ForPattern $viewerLog "SNAPSHOT applied total=0 .*version=0"

    $relay1Text = Get-Content $relay1Log -Raw
    $epoch1Match = [regex]::Match($relay1Text, "State epoch: (\d+)")
    if (-not $epoch1Match.Success) { throw "Could not read first relay epoch." }
    [long]$epoch1 = $epoch1Match.Groups[1].Value

    Write-Host "[RelayRestart] Creating state under first epoch..."
    $create = "{`"type`":`"PING_CREATE`",`"ping`":{`"id`":9101,`"senderId`":99,`"beaconId`":0,`"x`":1,`"y`":0,`"z`":1,`"createdAt`":1800000030000}}"
    Send-UdpJson 7801 $create
    Wait-ForPattern $viewerLog "id        : 9101"

    Write-Host "[RelayRestart] Killing relay and starting a new authoritative epoch..."
    Stop-Process -Id $relay1.Id -Force
    $relay1 = $null
    Start-Sleep -Milliseconds 1200

    $relay2 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relay2Log -ArgumentList $relayArgs
    Wait-ForPattern $relay2Log "Listening on UDP :7801"
    Wait-ForPattern $relay2Log "HEARTBEAT-REGISTER client=303.*snapshot=0.*version=0"
    Wait-ForPattern $viewerLog "SNAPSHOT applied total=0 .*version=0"

    $relay2Text = Get-Content $relay2Log -Raw
    $epoch2Match = [regex]::Match($relay2Text, "State epoch: (\d+)")
    if (-not $epoch2Match.Success) { throw "Could not read second relay epoch." }
    [long]$epoch2 = $epoch2Match.Groups[1].Value

    if ($epoch2 -le $epoch1) { throw "Second relay epoch did not advance." }

    $viewerText = Get-Content $viewerLog -Raw
    $snapshotMatches = [regex]::Matches($viewerText, "SNAPSHOT applied total=0 .*version=0")
    if ($snapshotMatches.Count -lt 2) { throw "Viewer did not apply fresh empty snapshot after relay restart." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 relay-restart regression passed."
    Write-Host "[PASS] Client held state under relay epoch A."
    Write-Host "[PASS] Relay restart produced newer epoch B."
    Write-Host "[PASS] Unknown heartbeat triggered automatic snapshot registration."
    Write-Host "[PASS] New epoch snapshot reset old client state despite version returning to 0."
}
finally {
    foreach ($process in @($viewer, $relay2, $relay1)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}