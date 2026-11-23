# Lynx Calibration Beacon

Prototype 0.3 uses one passive visual reference:

- Family: AprilTag 36h11
- Beacon ID: 0
- File: apriltag36h11_id0.png

The marker file is the official AprilRobotics tag36h11 ID 0 image:
https://github.com/AprilRobotics/apriltag-imgs

## Physical setup

1. Print or display the marker as a sharp square with a clear white border around it.
2. Keep the marker flat on one physical plane.
3. For the current prototype, that plane must also be tracked by ARCore.
4. Aim Lynx at the marker until AR tracking and plane detection are stable.
5. Press CALIBRATE.
6. Success is shown as: CAL: beacon 0 ready.

The exact printed size is not used in Prototype 0.3 because calibration comes from ARCore hit positions at the four detected marker corners rather than camera-only pose estimation.

## Shared axes

- Marker center = shared origin (0, 0, 0)
- Marker right = +X
- Marker up = +Y
- Marker front = +Z

All devices using beacon ID 0 therefore describe positions in the same beacon-relative coordinate frame.

## Prototype limitation

This first implementation requires all four detected marker corners to map onto a tracked ARCore plane. Later versions can estimate the beacon pose directly from camera intrinsics and marker geometry, which will remove that requirement.