$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relayLog = Join-Path $env:TEMP "lynx-05-desktop-relay.log"
$simLog = Join-Path $env:TEMP "lynx-05-desktop-sim.log"
Remove-Item $relayLog, $simLog -ErrorAction SilentlyContinue

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

Write-Host "[0.5 Desktop] Building..."
dotnet build (Join-Path $root "tools\LynxOperatorSim\LynxOperatorSim.csproj") --nologo
if ($LASTEXITCODE -ne 0) { throw "Operator simulator build failed." }

$relayProject = Join-Path $root "relay\LynxRelay\LynxRelay.csproj"
$simProject = Join-Path $root "tools\LynxOperatorSim\LynxOperatorSim.csproj"
$relayArgs = @("run", "--no-build", "--project", $relayProject, "--", "--listen", "7830")
$simArgs = @("run", "--no-build", "--project", $simProject, "--", "--host", "auto", "--port", "7830", "--client", "501", "--scenario", "create-remove")
$relay = $null
$sim = $null

try {
    $relay = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $relayLog -ArgumentList $relayArgs
    Wait-ForPattern $relayLog "Listening on UDP :7830"

    $sim = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput $simLog -ArgumentList $simArgs
    Wait-ForPattern $simLog "\[PASS\] Operator relay client lifecycle passed\." 15

    $relayText = Get-Content $relayLog -Raw
    if ($relayText -notmatch "DISCOVERY reply") { throw "Desktop auto-discovery was not exercised." }
    if ($relayText -notmatch "REGISTER client=501") { throw "Desktop client did not register." }
    if ($relayText -notmatch "OVERLAY CREATE .*owner=501") { throw "Desktop create was not authoritative." }
    if ($relayText -notmatch "OVERLAY REMOVE") { throw "Desktop remove was not authoritative." }

    Write-Host ""
    Write-Host "[PASS] Prototype 0.5 desktop client lifecycle passed."
    Write-Host "[PASS] Auto-discovery, registration, snapshot, create echo, and remove echo passed."
}
finally {
    foreach ($process in @($sim, $relay)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}