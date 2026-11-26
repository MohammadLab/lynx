$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Run-Step([string]$Name, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "=== $Name ==="
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Run-Step ".NET + desktop core tests" {
    dotnet test (Join-Path $root "Lynx.sln") --nologo
}

Run-Step "WinUI operator build" {
    dotnet build (Join-Path $root "desktop\Lynx.Operator\Lynx.Operator.csproj") -p:Platform=x64 --nologo
}

Write-Host ""
Write-Host "=== WinUI runtime smoke ==="
$operatorExe = Join-Path $root "desktop\Lynx.Operator\bin\x64\Debug\net9.0-windows10.0.19041.0\Lynx.Operator.exe"
if (-not (Test-Path $operatorExe)) { throw "Operator executable not found: $operatorExe" }
$operatorProcess = Start-Process $operatorExe -PassThru
try {
    Start-Sleep -Seconds 2
    $operatorProcess.Refresh()
    if ($operatorProcess.HasExited) {
        throw "WinUI operator exited during startup smoke test."
    }
    Write-Host "[PASS] WinUI operator remained alive after startup."
}
finally {
    if (-not $operatorProcess.HasExited) {
        Stop-Process -Id $operatorProcess.Id -Force
    }
}

$desktopRegressions = @(
    "Test-Prototype05-DesktopClient.ps1",
    "Test-Prototype05-DesktopReconnect.ps1",
    "Test-Prototype05-RelayRestart.ps1",
    "Test-Prototype05-OperatorToViewer.ps1"
)

foreach ($scriptName in $desktopRegressions) {
    Run-Step $scriptName {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $scriptName)
    }
}

Run-Step "Prototype 0.4 regression suite" {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-Prototype04-All.ps1")
}

Write-Host ""
Write-Host "=========================================="
Write-Host "[PASS] Prototype 0.5 software verification"
Write-Host "=========================================="
Write-Host "[PASS] Floorplan calibration + viewport math tests passed."
Write-Host "[PASS] Stable desktop client identity/state ordering tests passed."
Write-Host "[PASS] Native WinUI operator build/startup passed."
Write-Host "[PASS] Desktop relay discovery/register/snapshot/create/remove passed."
Write-Host "[PASS] Desktop process reconnect/snapshot recovery passed."
Write-Host "[PASS] Desktop relay restart/new-epoch recovery passed."
Write-Host "[PASS] Operator-to-independent-viewer propagation passed."
Write-Host "[PASS] Prototype 0.4 regression suite still passes."
Write-Host ""
Write-Host "Physical Android calibrated floorplan-to-world validation remains hardware-only."