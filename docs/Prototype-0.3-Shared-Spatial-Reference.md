# Prototype 0.3 — Shared Spatial Reference

## Objective

Give every Lynx device a common coordinate frame using the same passive physical beacon.

Prototype 0.3 uses AprilTag 36h11 marker ID 0 as the calibration beacon.

## Core rule

ARCore world coordinates are device-local and are never transmitted as shared truth.

Each calibrated client owns a transform between:

    local ARCore coordinates <-> shared beacon coordinates

The relay remains unaware of ARCore and calibration math.

## Calibration pipeline

1. OpenCV reads the ARCore CPU camera image.
2. ArucoDetector searches the AprilTag 36h11 dictionary for marker ID 0.
3. The four detected image-space corners are transformed from IMAGE_PIXELS to VIEW coordinates using ARCore Frame.transformCoordinates2d.
4. ARCore hit-tests each corner against tracked planes.
5. The four resulting local 3D points define:
   - shared origin: marker center
   - +X: marker right
   - +Y: marker up
   - +Z: marker front
6. CalibrationBuilder creates orthonormal local axes for the shared frame.

## Coordinate conversion

Calibration.localToShared(localPoint) projects a device-local AR point onto the beacon axes.

Calibration.sharedToLocal(sharedPoint) reconstructs the point in the current device local AR world.

The math is pure Kotlin and unit tested independently from ARCore/OpenCV.

## Ping behavior

Before calibration:

    PING -> local anchor only
    network send -> disabled

After calibration:

    AR hit -> local point
    localToShared -> shared point
    Ping(x,y,z) -> shared coordinates
    Ping.beaconId -> calibration beacon ID
    relay -> transports state unchanged

CLEAR still removes both local anchors and all transmitted pings.

## Protocol change

PING_CREATE now includes beaconId inside ping:

    {"type":"PING_CREATE","ping":{"id":123,"senderId":1,"beaconId":0,"x":0.2,"y":-0.1,"z":1.4,"createdAt":1800000000000}}

The relay validates beaconId but does not interpret it.

A receiver configured for beacon 0 ignores pings from another beacon frame. The PC regression explicitly tests this.

## Verification currently implemented

- Calibration origin and axis construction unit tests.
- local -> shared -> local round-trip test.
- translated beacon frame test.
- rotated beacon frame test.
- two simulated devices with different local AR frames resolving the same shared point.
- protocol validation of beaconId.
- relay remains calibration-agnostic.
- ViewerSim rejects mismatched beacon frames.
- Android debug APK builds with OpenCV 5 and ARCore.
- Real SM-X800 smoke test: AR tracking remained active and CALIBRATE executed the OpenCV detection path, safely reporting beacon 0 not detected when no marker was visible.

## UI

The top-left diagnostics HUD shows:
- AR tracking state.
- calibration state.
- detected tag ID.
- successful tracked-plane corner hits out of 4.
- UDP/network state.
- latest shared ping coordinates.

CALIBRATE
- Detect AprilTag 36h11 ID 0 in current AR frame.
- Require all four marker corners to resolve to a tracked ARCore plane.
- Log each corner's view coordinate and local 3D hit.
- Log the resulting origin and X/Y/Z axes.
- Replace existing calibration on success.
- Calibration exceptions are caught and surfaced in the HUD instead of crashing the app.

SIM REMOTE
- Builds a synthetic remote ping at shared (0.50, 0.00, 1.00).
- Projects it through sharedToLocal using the current calibration.
- Creates a real ARCore anchor at the projected receiver-local position.
- Exercises the Prototype 0.4 receiver-rendering path on one physical device.

PING
- Always allows local AR placement.
- Only transmits when calibrated.
- Transmitted x/y/z are shared beacon coordinates.

CLEAR
- Detaches local anchors.
- Sends PING_REMOVE for transmitted pings.

## Physical validation

Prototype 0.3 was validated successfully on the physical SM-X800 tablet.

Observed result:
- ARCore reached TRACKING and generated a usable tracked plane.
- AprilTag 36h11 ID 0 was detected successfully.
- All four tag corners resolved to tracked-plane hits: 4/4.
- Calibration reached READY for beacon 0.
- The shared coordinate frame was built successfully on real hardware.
- SIM REMOTE projected the synthetic shared point (0.50, 0.00, 1.00) through sharedToLocal.
- The projected point rendered successfully as a Lynx AR marker.
- The app remained stable after the earlier OpenCV corner-matrix crash was fixed.

This validates the core Prototype 0.3 spatial pipeline end-to-end on one physical device:

    camera image
    -> AprilTag detection
    -> 4/4 ARCore plane hits
    -> shared calibration frame
    -> shared coordinate
    -> sharedToLocal
    -> rendered AR anchor

A real two-device spatial alignment test remains future work for Prototype 0.4.

## Boundary

Prototype 0.3 establishes the shared coordinate system and conversion infrastructure.

Prototype 0.4 will use that infrastructure for full multi-device remote AR rendering and synchronization.