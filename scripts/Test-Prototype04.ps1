$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-relay.log"
$viewer1Log = Join-Path $env:TEMP "lynx-04-viewer1.log"
$viewer2Log = Join-Path $env:TEMP "lynx-04-viewer2.log"
$viewer3Log = Join-Path $env:TEMP "lynx-04-viewer3.log"

Remove-Item $relayLog, $viewer1Log, $viewer2Log, $viewer3Log -ErrorAction SilentlyContinue

Write-Host "[0.4] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$senderProject = Join-Path $root "tools\LynxPingSender\LynxPingSender.csproj"

$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7797", "--client-timeout-seconds", "15")
$viewer1Args = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7801", "--client", "101", "--relay", "127.0.0.1:7797", "--heartbeat-seconds", "5")
$viewer2Args = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7802", "--client", "102", "--relay", "127.0.0.1:7797", "--heartbeat-seconds", "5")
$viewer3Args = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7803", "--client", "103", "--relay", "127.0.0.1:7797", "--heartbeat-seconds", "5")

$relay = $null
$viewer1 = $null
$viewer2 = $null
$viewer3 = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Start-Sleep -Milliseconds 500

    $viewer1 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewer1Log -ArgumentList $viewer1Args
    $viewer2 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewer2Log -ArgumentList $viewer2Args

    Start-Sleep -Seconds 1

    Write-Host "[0.4] Sending create to two registered clients..."
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7797 --id 6001 --sender 99 --beacon 0 --x 1.25 --y -0.5 --z 3.75 --createdAt 1800000001000

    Start-Sleep -Seconds 1

    Write-Host "[0.4] Starting late-joining third client..."
    $viewer3 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewer3Log -ArgumentList $viewer3Args

    Start-Sleep -Seconds 1

    Write-Host "[0.4] Removing ping from all clients..."
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7797 --remove --id 6001 --sender 99

    Start-Sleep -Seconds 1

    $relayText = Get-Content $relayLog -Raw
    $viewer1Text = Get-Content $viewer1Log -Raw
    $viewer2Text = Get-Content $viewer2Log -Raw
    $viewer3Text = Get-Content $viewer3Log -Raw

    if ($relayText -notmatch "REGISTER client=101") { throw "Client 101 did not register." }
    if ($relayText -notmatch "REGISTER client=102") { throw "Client 102 did not register." }
    if ($relayText -notmatch "broadcast PING_CREATE id=6001 to 2 endpoint\(s\)") { throw "Relay did not fan out create to two clients." }
    if ($viewer1Text -notmatch "REMOTE PING RECEIVED") { throw "Viewer 101 did not receive create." }
    if ($viewer2Text -notmatch "REMOTE PING RECEIVED") { throw "Viewer 102 did not receive create." }
    if ($relayText -notmatch "REGISTER client=103.*snapshot=1") { throw "Late client did not register against one-ping state." }
    if ($viewer3Text -notmatch "SNAPSHOT applied total=1") { throw "Late client did not recover ping from snapshot." }
    if ($relayText -notmatch "broadcast PING_REMOVE id=6001 to 3 endpoint\(s\)") { throw "Relay did not fan out remove to three clients." }
    if ($viewer1Text -notmatch "REMOTE PING REMOVED id=6001") { throw "Viewer 101 did not apply remove." }
    if ($viewer2Text -notmatch "REMOTE PING REMOVED id=6001") { throw "Viewer 102 did not apply remove." }
    if ($viewer3Text -notmatch "REMOTE PING REMOVED id=6001") { throw "Viewer 103 did not apply remove." }
    if ($viewer1Text -notmatch "total     : 0") { throw "Viewer 101 did not end empty." }
    if ($viewer2Text -notmatch "total     : 0") { throw "Viewer 102 did not end empty." }
    if ($viewer3Text -notmatch "total     : 0") { throw "Viewer 103 did not end empty." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 multi-client regression passed."
    Write-Host "[PASS] Two registered clients received the same create."
    Write-Host "[PASS] Late client recovered current state from snapshot."
    Write-Host "[PASS] Remove broadcast reached all three clients."
    Write-Host "[PASS] All viewers ended with zero pings."
}
finally {
    foreach ($process in @($viewer3, $viewer2, $viewer1, $relay)) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
}