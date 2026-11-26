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

## Desktop operator (Prototype 0.5)

The native WinUI 3 operator projects are:

```text
desktop/Lynx.Operator.Core
desktop/Lynx.Operator
```

Requirements:
- .NET 9 SDK.
- Windows 10/11 SDK.
- Windows App SDK / NuGet restore access.

Build the operator:

```powershell
dotnet build .\desktop\Lynx.Operator\Lynx.Operator.csproj -p:Platform=x64
```

Run the complete Prototype 0.5 software verification:

```powershell
.\scripts\Test-Prototype05-All.ps1
```

Launch the relay and WinUI operator together:

```powershell
.\scripts\Run-Prototype05.ps1
```

The operator defaults to `127.0.0.1:7777` for a local relay. Enter `auto` in the Relay field or press **Auto** to use Lynx LAN discovery.

Operator floorplan workflow:
1. Load a PNG or JPEG.
2. Start calibration.
3. Click shared origin / AprilTag center.
4. Click a +X reference and enter its known physical distance.
5. Click the side representing positive shared +Z.
6. Switch to Place Marker.
7. Click the floorplan to send an authoritative shared-space marker.

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

Prototype 0.5 software scope: complete.

Implemented:
- native WinUI 3 desktop operator.
- PNG/JPEG floorplan loading.
- tested pan/zoom viewport transform.
- reversible floorplan pixel <-> shared X/Z calibration.
- three-click origin/+X/+Z calibration UI.
- stable desktop operator identity.
- relay discovery/register/heartbeat/snapshot recovery.
- authoritative marker create/remove.
- shared-state marker rendering back onto the floorplan.
- desktop process reconnect and relay-restart recovery.
- operator-to-independent-client propagation regression.

Full verification:

```powershell
.\scripts\Test-Prototype05-All.ps1
```

Remaining Prototype 0.5 validation is physical only: use the WinUI floorplan and calibrated Android device against the same AprilTag frame and visually confirm the desktop-selected marker appears/disappears at the expected real-world location.

Prototype 0.6 software scope: complete.

Implemented:
- generalized MARKER / PATH / ZONE overlay model.
- client command ingress (`OVERLAY_CREATE`, `OVERLAY_UPDATE`, `OVERLAY_REMOVE`).
- relay-only authoritative `OVERLAY_DIFF` egress.
- per-overlay revisions plus global relay epoch/version ordering.
- generalized reconnect snapshots and 1200-byte adaptive chunking.
- optimistic revision checks, ownership enforcement, endpoint registration checks, and geometry validation.
- 16-point maximum geometry bound for atomic UDP snapshot recovery.
- desktop generalized overlay state/client API.
- Android generalized overlay state, single-socket registered client, and shared-space geometry projection.
- legacy ping compatibility adapter backed by the generalized relay authority.

Full verification:

```powershell
.\scripts\Test-Prototype06-All.ps1
```

With an Android device connected:

```powershell
.\scripts\Test-Prototype06-All.ps1 -ConnectedAndroid
```

Prototype 0.7 can now build operator PATH/ZONE drawing and Android PATH/ZONE rendering on top of the generalized state layer.
