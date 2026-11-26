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

Run-Step "Build complete solution" {
    dotnet build (Join-Path $root "Lynx.sln") --nologo
}

Run-Step ".NET generalized overlay tests" {
    dotnet test (Join-Path $root "Lynx.sln") --no-build --nologo
}

Push-Location $androidRoot
try {
    if ($ConnectedAndroid) {
        Run-Step "Android unit + generalized instrumentation + APK" {
            .\gradlew.bat testDebugUnitTest connectedDebugAndroidTest assembleDebug installDebug
        }
    } else {
        Run-Step "Android generalized unit tests + APK" {
            .\gradlew.bat testDebugUnitTest assembleDebug
        }
    }
}
finally {
    Pop-Location
}

$regressions = @(
    "Test-Prototype06-Lifecycle.ps1",
    "Test-Prototype06-Revisions.ps1",
    "Test-Prototype06-Reconnect.ps1",
    "Test-Prototype06-Chunking.ps1",
    "Test-Prototype06-RelayRestart.ps1"
)

foreach ($scriptName in $regressions) {
    Run-Step $scriptName {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $scriptName)
    }
}

Run-Step "Prototype 0.5 + 0.4 compatibility chain" {
    if ($ConnectedAndroid) {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-Prototype05-All.ps1") -ConnectedAndroid
    } else {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-Prototype05-All.ps1")
    }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "[PASS] Prototype 0.6 software verification"
Write-Host "=========================================="
Write-Host "[PASS] MARKER / PATH / ZONE protocol contracts passed."
Write-Host "[PASS] Relay owns generalized authoritative overlay state."
Write-Host "[PASS] Command ingress -> OVERLAY_DIFF egress passed."
Write-Host "[PASS] Per-overlay revision / tombstone behavior passed."
Write-Host "[PASS] Stale revision, spoofed sender, and invalid geometry rejection passed."
Write-Host "[PASS] Generalized snapshot reconnect recovery passed."
Write-Host "[PASS] Variable-size geometry snapshot chunking stayed <=1200 bytes."
Write-Host "[PASS] Missing generalized snapshot chunk timeout/resync passed."
Write-Host "[PASS] Relay restart/new epoch recovery passed."
Write-Host "[PASS] Desktop and Android generalized state/math contracts build/test."
Write-Host "[PASS] Prototype 0.5 and 0.4 compatibility chain passed."
if ($ConnectedAndroid) {
    Write-Host "[PASS] Connected Android generalized instrumentation passed."
} else {
    Write-Host "[INFO] Connected Android instrumentation skipped: no device requested/available."
}