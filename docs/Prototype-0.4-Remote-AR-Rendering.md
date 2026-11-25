# Prototype 0.4 — Remote AR Rendering

## Objective

Receive authoritative shared-space ping state over UDP on Android and render it in the calibrated local ARCore frame.

## Data flow

    remote sender
    -> PING_CREATE shared coordinates
    -> relay validates/stores
    -> relay forwards accepted message
    -> Android UDP receiver
    -> JSON parse
    -> remote ping state
    -> beaconId check
    -> sharedToLocal
    -> ARCore anchor
    -> Lynx billboard marker

PING_REMOVE follows the same authoritative path and detaches the matching remote anchor.

## Android receive port

The Android client listens on UDP port 7778.

The current relay still has a single forward endpoint. For the first Prototype 0.4 slice, point the relay directly at the tablet:

    .\scripts\Run-Prototype04.ps1 -TabletIp <tablet-ip>

The tablet IP is DHCP-dependent and may change.

## Receiver state rules

Remote pings and local pings have separate ownership.

Local anchors:
- created by the local PING button.
- cleared by local CLEAR.
- may be transmitted to the relay.

Remote pings:
- stored by ping ID.
- rendered only when a compatible calibration exists.
- duplicate identical creates are ignored.
- mismatched beacon frames are not rendered.
- remove messages detach only the matching remote anchor.
- local CLEAR does not delete authoritative remote state.
- recalibration detaches stale remote anchors and reprojects cached remote pings into the new local frame.

## Self echo

The local development sender ID is currently 1.

If the relay echoes a locally-created ping back to the same Android client, the receiver treats it as confirmation instead of rendering a duplicate remote marker.

Unique runtime client identity is still future work.

## Pending-before-calibration behavior

A valid remote PING_CREATE received before calibration is retained in remote state.

After successful calibration, cached remote pings with the matching beacon ID are projected and rendered automatically.

This prevents network state from being lost just because spatial calibration happened later.

## Verification completed

Real relay-to-tablet network smoke test on SM-X800:

- tablet IP observed: 192.168.0.132.
- Android receiver listened on UDP :7778.
- PC sender created ping ID 4001, sender 99, beacon 0, shared (0.50, 0.00, 1.00).
- relay accepted and forwarded the create to the tablet.
- Android HUD reported:

      RX: ping 4001 pending calibration
      REMOTE 1 ping(s) 0 rendered

- PC sender then sent PING_REMOVE for ID 4001.
- relay accepted and forwarded the remove.
- Android HUD reported:

      RX: removed ping 4001
      REMOTE 0 ping(s) 0 rendered

This proves the real network receive, parse, store, and remove path.

## Physical validation

The calibrated network render/remove path was validated on the physical SM-X800 tablet.

Observed result:
- tablet calibrated successfully to beacon 0 with 4/4 plane hits.
- remote PING_CREATE id=5002, sender=99, beacon=0, shared (0.50, 0.00, 1.00) was accepted by the relay.
- Android reported `RX: rendered ping 5002` and `REMOTE 1 ping(s) 1 rendered`.
- the remote Lynx AR marker rendered in the calibrated scene.
- remote PING_REMOVE id=5002 was accepted and forwarded.
- Android reported `RX: removed ping 5002` and `REMOTE 0 ping(s) 0 rendered`.
- the Android process remained alive after remove.

An earlier remove attempt exposed a native SceneView/Filament lifecycle crash:

    Invalid texture still bound to MaterialInstance: Transparent Textured

The crash happened because removing the Compose-rendered billboard could destroy its texture while Filament was still using it in the current frame.

The Prototype 0.4 fix removes authoritative remote ping state and detaches the ARCore anchor, but retains the SceneView render object instead of destroying it immediately. This avoids the unsafe mid-frame texture teardown while correctly making the remote ping inactive.

## Multi-client relay

The relay now supports registered UDP clients instead of relying only on one hardcoded forward endpoint.

Protocol additions:
- `CLIENT_REGISTER { clientId }`
- `CLIENT_HEARTBEAT { clientId }`
- `SNAPSHOT { pings[] }`

Registration uses the source endpoint of the UDP datagram. Clients do not tell the relay what IP/port to send to; the relay learns the return endpoint from the socket that actually contacted it.

The relay:
- tracks client ID, UDP endpoint, and last-seen time.
- refreshes the registration on heartbeat.
- expires stale clients after the configured timeout.
- broadcasts accepted PING_CREATE and PING_REMOVE messages to all active clients.
- sends the authoritative ping snapshot immediately when a client registers.
- retains the old `--forward` option temporarily for backward-compatible regression testing.

ViewerSim now registers from the same UDP socket it listens on, sends periodic heartbeats, applies snapshots, and continues to enforce beacon-frame isolation.

Automated `scripts/Test-Prototype04.ps1` verifies:
- two independently registered clients receive the same create.
- a third late-joining client recovers the existing ping from snapshot.
- remove is broadcast to all three clients.
- all viewers end with zero stored pings.

## Known limitations

- Android has not yet been migrated to CLIENT_REGISTER / heartbeat / SNAPSHOT; current physical testing still uses relay `--forward`.
- Android local sender ID is hardcoded to 1.
- relay host configuration is still prototype configuration.
- snapshot currently contains the complete ping set in one UDP datagram; chunking is future work if state grows.
- no encrypted transport/authentication yet.