$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-relay-test.log"
$viewerLog = Join-Path $env:TEMP "lynx-viewer-test.log"

Remove-Item $relayLog, $viewerLog -ErrorAction SilentlyContinue

Write-Host "[Test] Building solution..."
dotnet build (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$viewerArgs = @("run", "--no-build", "--project", (Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"), "--", "--listen", "7778")
$relayArgs = @("run", "--no-build", "--project", (Join-Path $root "relay\LynxRelay\LynxRelay.csproj"), "--", "--listen", "7777", "--forward", "127.0.0.1:7778")

$viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList $viewerArgs
$relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs

try {
    Start-Sleep -Seconds 2

    $senderProject = Join-Path $root "tools\LynxPingSender\LynxPingSender.csproj"
    $createArgs = @("run", "--no-build", "--project", $senderProject, "--", "--id", "9001", "--sender", "1", "--x", "1.25", "--y", "-0.5", "--z", "3.75", "--createdAt", "1800000000000")

    Write-Host "[Test] Sending accepted create..."
    & dotnet @createArgs

    Write-Host "[Test] Sending exact duplicate create..."
    & dotnet @createArgs

    Write-Host "[Test] Sending conflicting create reuse..."
    $conflictArgs = $createArgs.Clone()
    $xIndex = [Array]::IndexOf($conflictArgs, "--x") + 1
    $conflictArgs[$xIndex] = "99"
    & dotnet @conflictArgs

    Write-Host "[Test] Sending different-beacon create..."
    & dotnet run --no-build --project $senderProject -- --id 9002 --sender 1 --beacon 99 --x 0.5 --y 0.5 --z 0.5 --createdAt 1800000000001

    Write-Host "[Test] Removing different-beacon ping from relay..."
    & dotnet run --no-build --project $senderProject -- --remove --id 9002 --sender 1

    Write-Host "[Test] Sending wrong-owner remove..."
    & dotnet run --no-build --project $senderProject -- --remove --id 9001 --sender 99

    Write-Host "[Test] Sending valid remove..."
    & dotnet run --no-build --project $senderProject -- --remove --id 9001 --sender 1

    Write-Host "[Test] Sending duplicate remove..."
    & dotnet run --no-build --project $senderProject -- --remove --id 9001 --sender 1

    Write-Host "[Test] Sending malformed payload..."
    $udp = [System.Net.Sockets.UdpClient]::new()
    $bad = [Text.Encoding]::UTF8.GetBytes("not-json")
    [void]$udp.Send($bad, $bad.Length, "127.0.0.1", 7777)

    Write-Host "[Test] Sending oversized payload..."
    $oversized = New-Object byte[] 5000
    [void]$udp.Send($oversized, $oversized.Length, "127.0.0.1", 7777)
    $udp.Dispose()

    Start-Sleep -Seconds 1

    $relayText = Get-Content $relayLog -Raw
    $viewerText = Get-Content $viewerLog -Raw

    if ($relayText -notmatch "CREATE id=9001") { throw "Relay did not accept the valid create." }
    if ($relayText -notmatch "DUP create ignored id=9001") { throw "Relay did not suppress duplicate create." }
    if ($relayText -notmatch "CONFLICT rejected id=9001") { throw "Relay did not reject conflicting create." }
    if ($relayText -notmatch "CREATE id=9002") { throw "Relay did not remain frame-agnostic for beacon 99." }
    if ($viewerText -notmatch "FRAME MISMATCH ignored id=9002") { throw "ViewerSim did not reject mismatched beacon frame." }
    if ($relayText -notmatch "REMOVE rejected id=9001") { throw "Relay did not reject wrong-owner remove." }
    if ($relayText -notmatch "REMOVE id=9001 sender=1") { throw "Relay did not accept valid remove." }
    if ($relayText -notmatch "DUP remove ignored id=9001") { throw "Relay did not suppress duplicate remove." }
    if ($relayText -notmatch "Invalid JSON") { throw "Relay did not reject malformed JSON." }
    if ($relayText -notmatch "payload too large") { throw "Relay did not reject oversized payload." }

    $createdCount = ([regex]::Matches($viewerText, "REMOTE PING RECEIVED")).Count
    $removedCount = ([regex]::Matches($viewerText, "REMOTE PING REMOVED id=9001")).Count

    if ($createdCount -ne 1) { throw "ViewerSim expected exactly 1 create, got $createdCount." }
    if ($removedCount -ne 1) { throw "ViewerSim expected exactly 1 remove, got $removedCount." }
    if ($viewerText -notmatch "total     : 0") { throw "ViewerSim did not end with zero stored pings." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.2 PC-side transport regression passed."
    Write-Host "[PASS] Create forwarded once."
    Write-Host "[PASS] Duplicate create suppressed."
    Write-Host "[PASS] Conflicting create rejected."
    Write-Host "[PASS] Relay stayed calibration-agnostic."
    Write-Host "[PASS] Receiver rejected mismatched beacon frame."
    Write-Host "[PASS] Wrong-owner remove rejected."
    Write-Host "[PASS] Remove forwarded once."
    Write-Host "[PASS] Duplicate remove suppressed."
    Write-Host "[PASS] ViewerSim ended with zero pings."
    Write-Host "[PASS] Malformed and oversized payloads rejected."
}
finally {
    if ($relay -and -not $relay.HasExited) { Stop-Process -Id $relay.Id -Force }
    if ($viewer -and -not $viewer.HasExited) { Stop-Process -Id $viewer.Id -Force }
}