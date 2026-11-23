# Lynx Laptop Setup

## Clone

```powershell
git clone https://github.com/MohammadLab/lynx.git
cd lynx
```

If the repo already exists:

```powershell
git pull origin main
```

## Android

Open this folder in Android Studio:

```text
android/LynxHud
```

Let Gradle sync and install the Android SDK components Android Studio requests.

Run Android unit tests and build:

```powershell
cd android/LynxHud
.\gradlew.bat testDebugUnitTest assembleDebug
```

If an Android device is connected through ADB:

```powershell
.\gradlew.bat connectedDebugAndroidTest installDebug
```

## .NET relay and tools

The solution is:

```text
Lynx.sln
```

From the repo root:

```powershell
dotnet test
```

Prototype transport launcher:

```powershell
.\scripts\Run-Prototype02.ps1
```

Full Prototype 0.3 software verification:

```powershell
.\scripts\Test-Prototype03.ps1
```

## Important network note

The Android app currently uses a development-machine UDP host configured in `ArScreen.kt`.

Current value:

```text
192.168.0.241:7777
```

That address belongs to the current desktop network and may not be valid on the laptop or another Wi-Fi network. Before doing Android-to-relay network testing from the laptop, change `pcHost` to the laptop's IPv4 address on the same network as the Android device.

This hardcoded host is known prototype technical debt and should later become runtime configuration/discovery.

## Current checkpoint

Prototype 0.2:
- typed UDP ping create/remove transport
- authoritative relay
- duplicate/conflict/ownership handling
- ViewerSim
- transport regression tests

Prototype 0.3:
- AprilTag 36h11 ID 0 detection
- shared spatial calibration frame
- local-to-shared and shared-to-local transforms
- calibration diagnostics HUD
- OpenCV corner extraction crash fix
- receiver-side shared-to-local projection helper
- SIM REMOTE one-device receiver rendering path
- calibration/orientation/unit/instrumentation tests

Physical AprilTag calibration is still the remaining real-world validation step.
