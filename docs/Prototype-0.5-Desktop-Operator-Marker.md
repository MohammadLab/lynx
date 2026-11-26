# Prototype 0.5 — Desktop Operator Marker Baseline

## Status

Software-complete.

All non-hardware Prototype 0.5 scope is implemented and passes the consolidated software verification suite.

Remaining physical validation:
- calibrate Android and the WinUI floorplan to the same AprilTag shared frame.
- place a marker from the real WinUI floorplan and visually confirm the marker appears at the expected physical location in Android AR.
- remove it from the WinUI operator and visually confirm it disappears from Android without a render crash.

That physical test requires the tablet to be handled in the test area and is intentionally not claimed as complete while the operator is away from the device.

## Core goal

Prove the first real operator-to-field Lynx workflow:

    operator clicks a point on a floorplan
    -> desktop converts that point into Lynx shared coordinates
    -> desktop sends the command to the authoritative relay
    -> relay validates and owns the state
    -> Android receives the authoritative point
    -> Android projects shared coordinates into its calibrated ARCore frame
    -> responder sees the marker at the corresponding real-world location

Prototype 0.5 is the first milestone where Lynx behaves like the intended product rather than a networking/AR test harness.

## Why this is the next milestone

Prototype 0.4 proved that authoritative shared-space points can move reliably through the relay and appear in calibrated AR.

The largest missing architectural component is now the desktop operator side.

Jumping directly to paths, zones, multiple overlay types, editing tools, and a generic diff protocol would combine too many new systems at once:
- first WinUI 3 application.
- floorplan rendering.
- floorplan-to-shared spatial calibration.
- operator interaction/state.
- new overlay abstractions.
- new protocol/store semantics.

Prototype 0.5 therefore keeps the network object intentionally simple: one point marker using the already-proven ping transport.

The result gives us a working vertical slice before generalizing the data model.

## Scope

### 1. Native WinUI 3 operator application

Create:

    desktop/Lynx.Operator/

The application must remain native Windows code.

Initial UI:
- application shell.
- connection/relay status.
- floorplan viewport.
- floorplan calibration controls.
- marker placement mode.
- current marker list.
- clear/remove controls.
- simple diagnostic panel showing shared coordinates and current calibration.

Visual polish is secondary to predictable interaction and correct spatial math.

## 2. Floorplan loading

For 0.5 the operator app supports raster floorplans:
- PNG.
- JPEG.

Required behavior:
- load an image from disk.
- preserve aspect ratio.
- pan.
- zoom.
- convert pointer positions from viewport coordinates back into original image pixel coordinates.

The coordinate conversion must not depend on current zoom/pan.

That means the application needs an explicit distinction between:

    screen/view coordinates
    image pixel coordinates
    Lynx shared coordinates

These three spaces must never be mixed implicitly.

## 3. Manual floorplan calibration

The desktop needs a transform from 2D floorplan pixels to Lynx's shared horizontal plane.

The current shared AR convention is:

    shared X = horizontal axis
    shared Y = vertical/elevation
    shared Z = horizontal axis

Ground markers created from the floorplan use:

    Y = 0

Therefore the required desktop transform is:

    floorplan pixel (u, v)
    -> shared ground point (X, Z)
    -> shared Vec3(X, 0, Z)

### Calibration workflow

Use a three-click calibration workflow on the floorplan:

1. Shared origin
   - operator clicks the floorplan position corresponding to the AprilTag center.
   - this point maps to shared (0, 0, 0).

2. Shared +X reference
   - operator clicks another point lying along the physical +X direction of the AprilTag/shared frame.
   - operator enters the known physical distance from the origin in metres.

3. Shared +Z side
   - operator clicks a point on the side of the origin that should be treated as positive shared Z.
   - this resolves the floorplan image orientation/reflection unambiguously.

From these inputs the desktop derives:
- pixel origin.
- normalized floorplan +X axis.
- normalized floorplan +Z axis.
- metres-per-pixel scale.

The transform must be represented as a pure math model, independent of WinUI controls.

## 4. Floorplan transform model

Create a reusable model similar in spirit to Android's AR calibration:

    FloorplanCalibration

Conceptual fields:

    originPixel
    xAxisPixel
    zAxisPixel
    metersPerPixel

Required operations:

    pixelToShared(pixel) -> Vec3
    sharedToPixel(shared) -> pixel

Both directions are required.

Why both matter:
- click -> shared coordinate uses pixelToShared.
- authoritative state received later from the relay can be drawn back onto the floorplan with sharedToPixel.

Round-trip correctness must be tested:

    pixel
    -> shared
    -> pixel

within a small numeric tolerance.

## 5. Operator marker placement

After calibration:

1. operator selects marker placement mode.
2. operator clicks the floorplan.
3. app converts the original image pixel coordinate to shared coordinates.
4. app displays a local preview.
5. app sends the existing point/ping create command to the relay.
6. relay acceptance becomes authoritative.
7. desktop renders the authoritative marker on the floorplan.
8. Android renders the same point in AR if its beacon calibration matches.

Marker deletion follows the same rule:
- operator requests remove.
- relay validates ownership/state.
- authoritative remove updates both desktop and Android.

The desktop must not behave as though a command succeeded merely because the mouse click occurred.

## 6. Desktop relay client

The WinUI application becomes a real registered Lynx client.

It must:
- generate/persist a stable client ID.
- discover or explicitly configure the relay.
- register from its listening UDP socket.
- heartbeat.
- receive snapshots.
- receive authoritative create/remove mutations.
- obey stateEpoch/stateVersion ordering.
- request a snapshot on version gaps.
- recover after disconnect.
- recover after relay restart.
- use the existing snapshot timeout/recovery semantics from Prototype 0.4.

Do not fork a weaker desktop-specific networking implementation.

Shared protocol/state-ordering logic should be reused where practical.

## 7. Desktop authoritative state view

The desktop keeps a local projection of authoritative relay state.

For each point:
- authoritative shared coordinate is the source of truth.
- floorplan position is derived using sharedToPixel().
- viewport position is derived from current pan/zoom.

A zoom or resize must never modify the stored spatial coordinate.

This distinction is important:

    relay/shared state != UI pixel position

## Success criteria

Prototype 0.5 is complete when all of the following are true:

1. WinUI 3 operator app launches and loads a raster floorplan.
2. pan and zoom do not corrupt pointer-to-image coordinate conversion.
3. operator completes the three-step floorplan calibration.
4. calibration transform passes unit and round-trip tests.
5. clicking a calibrated floorplan produces the expected shared X/Z coordinate.
6. desktop registers with the existing authoritative relay.
7. desktop recovers existing state from snapshot.
8. operator-created marker is accepted by the relay.
9. authoritative marker renders on the operator floorplan.
10. the same authoritative marker reaches Android.
11. with Android calibrated to the same beacon, the marker renders in the expected real-world location.
12. operator remove is authoritative and removes the marker on both desktop and Android.
13. desktop handles duplicate/stale/out-of-order state using the 0.4 ordering rules.
14. desktop reconnect and relay-restart recovery pass automated tests.
15. Prototype 0.4 regression suite still passes unchanged.

## Implemented software architecture

### Desktop projects

    desktop/Lynx.Operator.Core/
    desktop/Lynx.Operator/

`Lynx.Operator.Core` contains UI-independent engineering logic:
- `FloorplanCalibration`: reversible pixel <-> shared X/Z transform.
- `FloorplanViewportTransform`: aspect-fit, pan, zoom, and viewport/image conversion.
- `AuthoritativePingState`: epoch/version/snapshot ordering rules.
- `DesktopClientIdentityStore`: persistent random positive 63-bit operator identity.
- `RelayEndpointResolver`: explicit host or Lynx auto-discovery.
- `OperatorRelayClient`: register, heartbeat, snapshot assembly, snapshot timeout recovery, create/remove commands, reconnect, and relay-restart recovery.

`Lynx.Operator` is the native unpackaged WinUI 3 workstation:
- PNG/JPEG floorplan picker.
- pan mode and cursor-centered mouse-wheel zoom.
- three-click origin/+X/+Z calibration workflow.
- known +X physical-distance input.
- calibrated pixel/shared diagnostics.
- authoritative marker list.
- pending local command preview.
- marker rendering derived from `sharedToPixel()`.
- operator-owned authoritative remove.
- relay host/port controls and auto-discovery option.
- client ID, relay endpoint, epoch/version, and diagnostic status UI.

### Test harness

`tools/LynxOperatorSim` exercises the exact reusable desktop relay client without depending on WinUI interaction.

It supports:
- create/remove lifecycle.
- seed-only state.
- snapshot-only recovery.
- relay-restart watch mode.

## Software verification

Primary command:

    .\scripts\Test-Prototype05-All.ps1

With the Android tablet connected through ADB:

    .\scripts\Test-Prototype05-All.ps1 -ConnectedAndroid

Verified in the closeout run:
- 50/50 .NET tests passed.
- WinUI operator built with 0 warnings and 0 errors.
- actual WinUI executable stayed alive through startup smoke testing.
- desktop auto-discovery/register/initial-snapshot passed.
- operator authoritative create and remove echo passed.
- desktop process restart recovered relay state from snapshot.
- relay restart/new epoch reset desktop state correctly.
- operator create/remove propagated to an independent registered viewer.
- a shared cross-platform spatial-contract fixture is consumed by both C# and Kotlin tests.
- the same shared -> Android-local projection contract passed on the physical SM-X800 through Android instrumentation.
- all 9 Android instrumentation tests passed on the SM-X800.
- the complete Prototype 0.4 software verification suite still passed.

Individual 0.5 regressions:
- `Test-Prototype05-DesktopClient.ps1`.
- `Test-Prototype05-DesktopReconnect.ps1`.
- `Test-Prototype05-RelayRestart.ps1`.
- `Test-Prototype05-OperatorToViewer.ps1`.

Operational launcher:

    .\scripts\Run-Prototype05.ps1

The launcher builds the relay and WinUI operator, starts a local relay on UDP 7777 when one is not already bound, and launches the operator workstation.

## Automated test targets

### Pure calibration tests

Cover:
- origin maps to shared zero.
- +X reference maps to expected X distance.
- selected +Z side produces positive Z.
- arbitrary point conversion.
- pixel -> shared -> pixel round trip.
- invalid zero-length X reference rejected.
- invalid scale rejected.
- collinear/ambiguous Z-side input rejected.

### View-coordinate tests

Where practical, isolate viewport math from WinUI:
- image pixel -> viewport.
- viewport -> image pixel.
- zoom round trip.
- pan round trip.
- resize/aspect-fit behavior.

### Network/state tests

Reuse or extend 0.4 regression infrastructure:
- desktop registration.
- initial snapshot.
- create accepted and echoed authoritatively.
- remove accepted.
- stale mutation ignored.
- version gap triggers snapshot.
- disconnect/reconnect.
- relay restart/new epoch.

## Physical validation

The final physical test should use the existing AprilTag shared frame.

Workflow:

1. place the AprilTag in the physical test area.
2. calibrate Android to beacon 0.
3. load a floorplan/test diagram representing that area in the WinUI app.
4. calibrate the floorplan to the same shared origin/axes.
5. click a known floorplan position.
6. confirm the relay accepts the marker.
7. confirm Android renders the marker at the corresponding physical location.
8. remove it from desktop.
9. confirm it disappears from Android without crashing.

This is the key Prototype 0.5 demonstration.

## Explicit non-goals

Do not add these during 0.5:
- paths/polylines.
- zones/polygons.
- arbitrary overlay styling.
- multi-select/edit handles.
- undo/redo history.
- generic OVERLAY_DIFF abstraction.
- encrypted transport.
- authenticated users/roles.
- replay protection.
- audit logs.
- persistent relay database.
- automatic computer-vision floorplan registration.
- AR navigation/path following.

Those belong to later milestones.

## Planned follow-on milestones

### Prototype 0.6 — Generalized Overlay Model

Once the operator-marker vertical slice works:
- replace the ping-specific state model with overlay primitives.
- introduce marker/path/zone IDs and common metadata.
- define command ingress versus authoritative OVERLAY_DIFF egress.
- extend snapshots to generalized overlay state.
- migrate the 0.5 marker workflow onto the generalized model.

### Prototype 0.7 — Operator Paths and Zones

Build actual operator drawing tools:
- polyline/path drawing.
- polygon/zone drawing.
- editing and removal.
- floorplan rendering.
- Android non-AR/AR rendering of those primitives.

### Later phases

After the core C2 workflow is correct:
- cyber layer: authentication, ChaCha20-Poly1305, replay protection, roles, audit integrity.
- degraded-network/stress layer.
- HUD simplification and operational behavior.

## Implementation sequence completed

Prototype 0.5 was implemented in dependency order:

1. pure `FloorplanCalibration` and geometry model.
2. floorplan viewport/pan/zoom transform.
3. unit tests for reversible spatial math and invalid geometry.
4. authoritative desktop state engine.
5. stable desktop identity and relay discovery.
6. reusable UDP operator client.
7. headless operator network harness and regressions.
8. native WinUI 3 operator shell and floorplan interaction.
9. full 0.5 software closeout suite.

This preserved the intended separation between spatial correctness, distributed-state correctness, and UI behavior.
