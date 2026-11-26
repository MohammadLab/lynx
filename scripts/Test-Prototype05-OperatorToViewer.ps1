$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-05-e2e-relay.log"
$viewerLog = Join-Path $env:TEMP "lynx-05-e2e-viewer.log"
$operatorLog = Join-Path $env:TEMP "lynx-05-e2e-operator.log"
Remove-Item $relayLog, $viewerLog, $operatorLog -ErrorAction SilentlyContinue

function Wait-ForPattern([string]$Path, [string]$Pattern, [int]$TimeoutSeconds = 12) {
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

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$viewerProject = Join-Path $root "tools\LynxViewerSim\LynxViewerSim.csproj"
$operatorProject = Join-Path $root "tools\LynxOperatorSim\LynxOperatorSim.csproj"
$relay = $null
$viewer = $null
$operator = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7833")
    Wait-ForPattern $relayLog "Listening on UDP :7833"

    $viewer = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $viewerLog -ArgumentList @("run","--no-build","--project",$viewerProject,"--","--listen","7843","--client","701","--relay","127.0.0.1:7833","--beacon","0","--heartbeat-seconds","1")
    Wait-ForPattern $relayLog "REGISTER client=701"

    $operator = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $operatorLog -ArgumentList @("run","--no-build","--project",$operatorProject,"--","--host","127.0.0.1","--port","7833","--client","702","--scenario","create-remove")

    Wait-ForPattern $operatorLog "\[PASS\] Operator relay client lifecycle passed\." 15
    Wait-ForPattern $viewerLog "REMOTE PING RECEIVED" 15
    Wait-ForPattern $viewerLog "REMOTE PING REMOVED" 15

    $relayText = Get-Content $relayLog -Raw
    if ($relayText -notmatch "broadcast PING_CREATE .* to 2 endpoint\(s\)") {
        throw "Relay did not fan out operator create to both clients."
    }
    if ($relayText -notmatch "broadcast PING_REMOVE .* to 2 endpoint\(s\)") {
        throw "Relay did not fan out operator remove to both clients."
    }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.5 operator-to-viewer regression passed."
    Write-Host "[PASS] Operator marker create reached an independent registered client."
    Write-Host "[PASS] Operator marker remove reached the independent client."
}
finally {
    foreach ($process in @($operator, $viewer, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}