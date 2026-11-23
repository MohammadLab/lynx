$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $root "android\LynxHud"

Write-Host "[0.3] Android calibration/unit tests..."
Push-Location $androidRoot
try {
    & .\gradlew.bat testDebugUnitTest assembleDebug
    if ($LASTEXITCODE -ne 0) { throw "Android Prototype 0.3 tests/build failed." }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "[0.3] .NET protocol tests..."
dotnet test (Join-Path $root "Lynx.sln") --nologo
if ($LASTEXITCODE -ne 0) { throw ".NET tests failed." }

Write-Host ""
Write-Host "[0.3] Transport and frame-isolation regression..."
& (Join-Path $root "scripts\Test-Prototype02.ps1")
if ($LASTEXITCODE -ne 0) { throw "Transport regression failed." }

Write-Host ""
Write-Host "[PASS] Prototype 0.3 software verification passed."
Write-Host "[PASS] Calibration math and two-frame transform tests passed."
Write-Host "[PASS] Android/OpenCV build passed."
Write-Host "[PASS] Relay remained calibration-agnostic."
Write-Host "[PASS] Receiver rejected mismatched beacon frames."
Write-Host "[NOTE] Physical AprilTag calibration still requires the beacon in view."