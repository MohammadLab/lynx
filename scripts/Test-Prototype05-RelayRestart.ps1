$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relay1Log = Join-Path $env:TEMP "lynx-05-relayrestart-1.log"
$relay2Log = Join-Path $env:TEMP "lynx-05-relayrestart-2.log"
$simLog = Join-Path $env:TEMP "lynx-05-relayrestart-sim.log"
Remove-Item $relay1Log, $relay2Log, $simLog -ErrorAction SilentlyContinue

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
$simProject = Join-Path $root "tools\LynxOperatorSim\LynxOperatorSim.csproj"
$relayArgs = @("run","--no-build","--project",$relayProject,"--","--listen","7832")
$simArgs = @("run","--no-build","--project",$simProject,"--","--host","127.0.0.1","--port","7832","--client","602","--scenario","restart-watch")
$relay1 = $null
$relay2 = $null
$sim = $null

try {
    $relay1 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relay1Log -ArgumentList $relayArgs
    Wait-ForPattern $relay1Log "Listening on UDP :7832"

    $sim = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $simLog -ArgumentList $simArgs
    Wait-ForPattern $simLog "WAITING FOR RELAY RESTART" 12

    Stop-Process -Id $relay1.Id -Force
    $relay1 = $null
    Start-Sleep -Milliseconds 1200

    $relay2 = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relay2Log -ArgumentList $relayArgs
    Wait-ForPattern $relay2Log "Listening on UDP :7832"
    Wait-ForPattern $simLog "RELAY RESTART RECOVERY CONFIRMED" 15

    Write-Host ""
    Write-Host "[PASS] Prototype 0.5 desktop relay-restart regression passed."
    Write-Host "[PASS] Desktop client accepted a newer relay epoch and rebuilt empty state."
}
finally {
    foreach ($process in @($sim, $relay2, $relay1)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}