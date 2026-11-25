$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-04-chunk-relay.log"
$viewerLog = Join-Path $env:TEMP "lynx-04-chunk-viewer.log"
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

Write-Host "[Chunking] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7799")
$viewerArgs = @("run", "--no-build", "--project", $viewerProject, "--", "--listen", "7821", "--client", "301", "--relay", "127.0.0.1:7799")
$relay = $null
$viewer = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Wait-ForPattern $relayLog "Listening on UDP :7799"

    Write-Host "[Chunking] Seeding 35 authoritative pings..."
    for ($i = 1; $i -le 35; $i++) {
        $id = 8000 + $i
        $json = "{`"type`":`"PING_CREATE`",`"ping`":{`"id`":$id,`"senderId`":99,`"beaconId`":0,`"x`":$i,`"y`":0,`"z`":1,`"createdAt`":1800000010000}}"
        Send-UdpJson 7799 $json
    }
    Wait-ForPattern $relayLog "CREATE id=8035.*version=35"

    Write-Host "[Chunking] Registering late viewer..."
    $viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList $viewerArgs
    Wait-ForPattern $relayLog "REGISTER client=301.*snapshot=35.*chunks=3.*version=35"
    Wait-ForPattern $viewerLog "SNAPSHOT applied total=35 .*version=35"

    Write-Host "[Chunking] Verifying next contiguous diff after snapshot..."
    $remove = "{`"type`":`"PING_REMOVE`",`"pingId`":8001,`"senderId`":99}"
    Send-UdpJson 7799 $remove
    Wait-ForPattern $viewerLog "REMOTE PING REMOVED id=8001"
    Wait-ForPattern $relayLog "REMOVE id=8001.*version=36"

    $viewerText = Get-Content $viewerLog -Raw
    if ($viewerText -notmatch "SNAPSHOT chunk 1/3") { throw "Chunk 1/3 was not observed." }
    if ($viewerText -notmatch "SNAPSHOT chunk 2/3") { throw "Chunk 2/3 was not observed." }
    if ($viewerText -notmatch "SNAPSHOT applied total=35 .*version=35") { throw "Chunked snapshot was not assembled." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.4 snapshot chunking regression passed."
    Write-Host "[PASS] 35 pings were split into three bounded snapshot chunks."
    Write-Host "[PASS] Viewer assembled all 35 before replacing local state."
    Write-Host "[PASS] Version 36 diff applied after version 35 snapshot."
}
finally {
    foreach ($process in @($viewer, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}