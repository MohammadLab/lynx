$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-reconnect-relay.log"
$viewer1Log = Join-Path $env:TEMP "lynx-04-reconnect-viewer1.log"
$viewer2Log = Join-Path $env:TEMP "lynx-04-reconnect-viewer2.log"

Remove-Item $relayLog, $viewer1Log, $viewer2Log -ErrorAction SilentlyContinue

function Wait-ForPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$TimeoutSeconds = 8
    )

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

Write-Host "[Reconnect] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$senderProject = Join-Path $root "tools\LynxPingSender\LynxPingSender.csproj"

$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7798", "--client-timeout-seconds", "3")
$viewerArgs = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7811", "--client", "201", "--relay", "127.0.0.1:7798", "--heartbeat-seconds", "1")

$relay = $null
$viewer1 = $null
$viewer2 = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Start-Sleep -Milliseconds 300

    Write-Host "[Reconnect] Starting client 201..."
    $viewer1 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewer1Log -ArgumentList $viewerArgs
    Wait-ForPattern $relayLog "REGISTER client=201"
    Wait-ForPattern $viewer1Log "SNAPSHOT applied total=0"

    Write-Host "[Reconnect] Creating ping 7001 while client is online..."
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7798 --id 7001 --sender 99 --beacon 0 --x 1 --y 0 --z 1 --createdAt 1800000002000
    Wait-ForPattern $viewer1Log "id        : 7001"

    Write-Host "[Reconnect] Simulating client disconnect..."
    Stop-Process -Id $viewer1.Id -Force
    $viewer1 = $null

    Start-Sleep -Seconds 4

    Write-Host "[Reconnect] Creating ping 7002 while client is offline..."
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7798 --id 7002 --sender 99 --beacon 0 --x 2 --y 0 --z 2 --createdAt 1800000002001
    Wait-ForPattern $relayLog "Expired 1 client\(s\); active=0"
    Wait-ForPattern $relayLog "CREATE id=7002"

    Write-Host "[Reconnect] Restarting same client ID..."
    $viewer2 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewer2Log -ArgumentList $viewerArgs
    Wait-ForPattern $relayLog "REGISTER client=201.*snapshot=2"
    Wait-ForPattern $viewer2Log "SNAPSHOT applied total=2"

    Write-Host "[Reconnect] Removing restored pings..."
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7798 --remove --id 7001 --sender 99
    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7798 --remove --id 7002 --sender 99
    Wait-ForPattern $viewer2Log "REMOTE PING REMOVED id=7002"

    $relayText = Get-Content $relayLog -Raw
    $viewer2Text = Get-Content $viewer2Log -Raw

    if ($relayText -notmatch "Expired 1 client\(s\); active=0") { throw "Disconnected client was not expired." }
    if ($relayText -notmatch "REGISTER client=201.*snapshot=2") { throw "Reconnect snapshot did not contain both pings." }
    if ($viewer2Text -notmatch "SNAPSHOT applied total=2") { throw "Restarted client did not recover both pings." }
    if ($viewer2Text -notmatch "REMOTE PING REMOVED id=7001") { throw "Restarted client did not apply remove for 7001." }
    if ($viewer2Text -notmatch "REMOTE PING REMOVED id=7002") { throw "Restarted client did not apply remove for 7002." }
    if ($viewer2Text -notmatch "total     : 0") { throw "Restarted client did not end empty." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 reconnect regression passed."
    Write-Host "[PASS] Disconnected client expired from registry."
    Write-Host "[PASS] Relay retained state while client was offline."
    Write-Host "[PASS] Same client ID re-registered after restart."
    Write-Host "[PASS] Snapshot recovered both authoritative pings."
    Write-Host "[PASS] Restored state accepted subsequent removals."
}
finally {
    foreach ($process in @($viewer2, $viewer1, $relay)) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
}