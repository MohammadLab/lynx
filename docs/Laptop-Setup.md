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

## Network configuration

Android now supports automatic relay discovery.

Default behavior:

```text
lynx.relayHost=auto
lynx.relayPort=7777
```

With `auto`, Android first tries LAN broadcast discovery on UDP 7777, then falls back to a bounded /24 unicast probe if broadcast is filtered.

To bypass discovery, add an explicit host in `android/LynxHud/local.properties`:

```text
lynx.relayHost=192.168.0.243
lynx.relayPort=7777
```

`local.properties` is ignored by Git, so machine/network-specific overrides do not create source changes.

Windows Firewall must allow inbound UDP traffic to `LynxRelay` on the local network. `scripts/Run-Prototype04.ps1` warns if an enabled inbound block rule for `lynxrelay.exe` is detected.

## Current checkpoint

Prototype 0.2: complete.

Prototype 0.3: complete and physically validated on the SM-X800.

Prototype 0.4 software scope:
- registered multi-client relay.
- stable Android identity.
- remote calibrated AR rendering.
- snapshots and reconnect recovery.
- authoritative state epochs/versions.
- adaptive 1200-byte snapshot chunking.
- missing-chunk timeout / `SNAPSHOT_REQUEST` recovery.
- relay restart recovery.
- auto relay discovery with explicit-host override.
- deferred SceneView anchor retirement.

Full software verification:

```powershell
.\scripts\Test-Prototype04-All.ps1
```

Include connected Android instrumentation:

```powershell
.\scripts\Test-Prototype04-All.ps1 -ConnectedAndroid
```

Hardware follow-up remains for a deferred-cleanup calibrated retest and a true two-Android-device shared-location test once a second ARCore device is available.
