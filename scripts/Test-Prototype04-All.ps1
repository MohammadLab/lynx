param(
    [switch]$ConnectedAndroid
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $root "android\LynxHud"

function Run-Step([string]$Name, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "=== $Name ==="
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Run-Step ".NET unit tests" {
    dotnet test (Join-Path $root "Lynx.sln") --nologo
}

Push-Location $androidRoot
try {
    if ($ConnectedAndroid) {
        Run-Step "Android unit + connected instrumentation + APK" {
            .\gradlew.bat testDebugUnitTest connectedDebugAndroidTest assembleDebug
        }
    } else {
        Run-Step "Android unit tests + APK" {
            .\gradlew.bat testDebugUnitTest assembleDebug
        }
    }
}
finally {
    Pop-Location
}

$regressions = @(
    "Test-Prototype04.ps1",
    "Test-Prototype04-Reconnect.ps1",
    "Test-Prototype04-Chunking.ps1",
    "Test-Prototype04-Versioning.ps1",
    "Test-Prototype04-RelayRestart.ps1",
    "Test-Prototype04-SnapshotTimeout.ps1",
    "Test-Prototype04-Discovery.ps1"
)

foreach ($scriptName in $regressions) {
    Run-Step $scriptName {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $scriptName)
    }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "[PASS] Prototype 0.4 software verification"
Write-Host "=========================================="
Write-Host "[PASS] Unit/build verification passed."
Write-Host "[PASS] Multi-client fan-out passed."
Write-Host "[PASS] Disconnect/reconnect recovery passed."
Write-Host "[PASS] Adaptive snapshot chunking passed."
Write-Host "[PASS] Epoch/version ordering passed."
Write-Host "[PASS] Relay restart recovery passed."
Write-Host "[PASS] Missing-chunk timeout/resync passed."
Write-Host "[PASS] Relay discovery protocol passed."
if ($ConnectedAndroid) {
    Write-Host "[PASS] Connected Android instrumentation passed."
}