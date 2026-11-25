$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-timeout-relay.log"
$viewerLog = Join-Path $env:TEMP "lynx-04-timeout-viewer.log"
Remove-Item $relayLog, $viewerLog -ErrorAction SilentlyContinue

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

Write-Host "[SnapshotTimeout] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7802")
$viewerArgs = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7824", "--client", "304", "--relay", "127.0.0.1:7802", "--heartbeat-seconds", "1")
$relay = $null
$viewer = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Wait-ForPattern $relayLog "Listening on UDP :7802"

    $viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList $viewerArgs
    Wait-ForPattern $viewerLog "SNAPSHOT applied total=0 .*version=0"

    $relayText = Get-Content $relayLog -Raw
    $epochMatch = [regex]::Match($relayText, "State epoch: (\d+)")
    if (-not $epochMatch.Success) { throw "Could not read relay epoch." }
    $epoch = $epochMatch.Groups[1].Value

    Write-Host "[SnapshotTimeout] Injecting only chunk 1 of a 2-chunk snapshot..."
    $partial = "{`"type`":`"SNAPSHOT`",`"stateEpoch`":$epoch,`"stateVersion`":0,`"snapshotId`":999,`"chunkIndex`":0,`"chunkCount`":2,`"pings`":[]}"
    Send-UdpJson 7824 $partial
    Wait-ForPattern $viewerLog "SNAPSHOT chunk 1/2"

    Write-Host "[SnapshotTimeout] Waiting for timeout-driven resync..."
    Wait-ForPattern $viewerLog "SNAPSHOT timeout; requesting fresh snapshot" 8
    Wait-ForPattern $relayLog "SNAPSHOT-REQUEST client=304" 8

    Start-Sleep -Milliseconds 500
    $viewerText = Get-Content $viewerLog -Raw
    $matches = [regex]::Matches($viewerText, "SNAPSHOT applied total=0 .*version=0")
    if ($matches.Count -lt 2) {
        throw "Viewer did not apply a fresh snapshot after timeout recovery."
    }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 snapshot-timeout regression passed."
    Write-Host "[PASS] Incomplete snapshot was detected."
    Write-Host "[PASS] Missing chunk timed out instead of hanging indefinitely."
    Write-Host "[PASS] Client sent SNAPSHOT_REQUEST from its registered socket."
    Write-Host "[PASS] Fresh authoritative snapshot completed recovery."
}
finally {
    foreach ($process in @($viewer, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}