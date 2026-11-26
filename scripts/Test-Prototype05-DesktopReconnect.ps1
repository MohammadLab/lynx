$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-05-reconnect-relay.log"
$seedLog = Join-Path $env:TEMP "lynx-05-reconnect-seed.log"
$rejoinLog = Join-Path $env:TEMP "lynx-05-reconnect-rejoin.log"
Remove-Item $relayLog, $seedLog, $rejoinLog -ErrorAction SilentlyContinue

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

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject = Join-Path $root "tools\LynxOperatorSim\LynxOperatorSim.csproj"
$senderProject = Join-Path $root "tools\LynxPingSender\LynxPingSender.csproj"
$relay = $null
$seed = $null
$rejoin = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList @("run","--no-build","--project",$relayProject,"--","--listen","7831")
    Wait-ForPattern $relayLog "Listening on UDP :7831"

    $seed = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $seedLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7831","--client","601","--scenario","seed-only")
    Wait-ForPattern $seedLog "SEED CONFIRMED id=(\d+)" 12

    $seedText = Get-Content $seedLog -Raw
    $idMatch = [regex]::Match($seedText, "SEED CONFIRMED id=(\d+)")
    if (-not $idMatch.Success) { throw "Could not read seeded ping ID." }
    $pingId = $idMatch.Groups[1].Value

    $rejoin = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $rejoinLog -ArgumentList @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7831","--client","601","--scenario","snapshot-only")
    Wait-ForPattern $rejoinLog "INITIAL SNAPSHOT READY total=1" 12
    Wait-ForPattern $rejoinLog "\[PASS\] Operator snapshot recovery total=1\." 12

    & dotnet run --no-build --project $senderProject -- --host 127.0.0.1 --port 7831 --remove --id $pingId --sender 601 | Out-Null

    Write-Host ""
    Write-Host "[PASS] Prototype 0.5 desktop reconnect regression passed."
    Write-Host "[PASS] A restarted desktop client recovered authoritative state from snapshot."
}
finally {
    foreach ($process in @($rejoin, $seed, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}