$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-version-relay.log"
$viewerLog = Join-Path $env:TEMP "lynx-04-version-viewer.log"
Remove-Item $relayLog, $viewerLog -ErrorAction SilentlyContinue

function Wait-ForPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 8) {
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

Write-Host "[Versioning] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7800")
$viewerArgs = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7822", "--client", "302", "--relay", "127.0.0.1:7800")
$relay = $null
$viewer = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Start-Sleep -Milliseconds 300
    $viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList $viewerArgs
    Wait-ForPattern $viewerLog "SNAPSHOT applied total=0 .*version=0"
    $relayText = Get-Content $relayLog -Raw
    $epochMatch = [regex]::Match($relayText, "State epoch: (\d+)")
    if (-not $epochMatch.Success) { throw "Could not read relay state epoch." }
    $epoch = $epochMatch.Groups[1].Value

    Write-Host "[Versioning] Applying authoritative version 1..."
    $create1 = "{`"type`":`"PING_CREATE`",`"ping`":{`"id`":9001,`"senderId`":99,`"beaconId`":0,`"x`":1,`"y`":0,`"z`":1,`"createdAt`":1800000020000}}"
    Send-UdpJson 7800 $create1
    Wait-ForPattern $relayLog "CREATE id=9001.*version=1"
    Wait-ForPattern $viewerLog "id        : 9001"

    Write-Host "[Versioning] Injecting version gap 3 directly at viewer..."
    $gap = "{`"type`":`"PING_CREATE`",`"stateEpoch`":$epoch,`"stateVersion`":3,`"ping`":{`"id`":9003,`"senderId`":99,`"beaconId`":0,`"x`":3,`"y`":0,`"z`":3,`"createdAt`":1800000020002}}"
    Send-UdpJson 7822 $gap
    Wait-ForPattern $viewerLog "VERSION GAP local=.*:1 received=.*:3; requesting snapshot"
    Wait-ForPattern $relayLog "REGISTER client=302.*snapshot=1.*version=1"

    Write-Host "[Versioning] Injecting stale version 1..."
    $stale = "{`"type`":`"PING_CREATE`",`"stateEpoch`":$epoch,`"stateVersion`":1,`"ping`":{`"id`":9004,`"senderId`":99,`"beaconId`":0,`"x`":4,`"y`":0,`"z`":4,`"createdAt`":1800000020003}}"
    Send-UdpJson 7822 $stale
    Wait-ForPattern $viewerLog "STALE create ignored epoch=.* version=1"

    $viewerText = Get-Content $viewerLog -Raw
    if ($viewerText -match "id        : 9003") { throw "Gap packet was incorrectly applied." }
    if ($viewerText -match "id        : 9004") { throw "Stale packet was incorrectly applied." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 state-version regression passed."
    Write-Host "[PASS] Contiguous version 1 applied."
    Write-Host "[PASS] Version gap 1 -> 3 triggered snapshot resync instead of apply."
    Write-Host "[PASS] Stale version 1 was ignored."
}
finally {
    foreach ($process in @($viewer, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}