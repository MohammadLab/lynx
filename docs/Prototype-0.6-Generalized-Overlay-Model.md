# Prototype 0.6 — Generalized Overlay Model

## Status

Software-complete.

The consolidated `Test-Prototype06-All.ps1` closeout gate passed with 70/70 .NET tests, Android JVM/build verification, every 0.6 integration regression, and the complete Prototype 0.5 -> 0.4 compatibility chain.

Connected Android instrumentation was not run during final 0.6 closeout because the tablet was not ADB-connected. The generalized Android instrumentation sources compile as part of the Android test build path and are ready for the next connected-device run.

Prototype 0.6 replaces ping-specific authoritative state with a reusable overlay architecture for markers, paths, and zones.

The central rule is now:

    client sends command
    -> relay validates command
    -> relay mutates authoritative overlay state
    -> relay assigns overlay revision + global state version
    -> relay broadcasts OVERLAY_DIFF
    -> clients apply authoritative diff

Client commands are never themselves authoritative state.

## Overlay types

### MARKER

Exactly one shared-space point.

### PATH

Ordered shared-space point list with at least two points.

### ZONE

Ordered shared-space polygon vertices with at least three points.

Prototype 0.6 limits every overlay to at most 16 geometry points. This is intentional: one overlay must remain atomic and recoverable inside the 1200-byte snapshot transport budget.

## Shared overlay metadata

Every authoritative overlay contains:

- `overlayId`.
- `ownerId`.
- `beaconId`.
- `kind`.
- `revision`.
- `geometry[]`.
- `createdAt`.
- `updatedAt`.

Coordinates remain Lynx shared-space metres.

## Command ingress

Generalized client commands:

- `OVERLAY_CREATE`.
- `OVERLAY_UPDATE`.
- `OVERLAY_REMOVE`.
- `OVERLAY_SNAPSHOT_REQUEST`.

### Create

Client supplies:
- overlay ID.
- sender ID.
- beacon ID.
- overlay kind.
- geometry.
- creation time.

Relay assigns revision 1.

Exact duplicate creates are idempotent. Reusing an active overlay ID with different contents is rejected.

### Update

Client supplies:
- overlay ID.
- sender ID.
- expected current revision.
- existing overlay kind.
- replacement geometry.

Relay verifies ownership and expected revision before applying the update, then advances revision by exactly one.

The overlay kind cannot change through an update.

### Remove

Client supplies:
- overlay ID.
- sender ID.
- expected current revision.

Relay verifies ownership/revision, removes the active overlay, and emits a removal diff using the next revision number as a tombstone revision.

## Endpoint identity rule

Generalized commands must come from the same UDP endpoint registered for the supplied sender/client ID.

This is not cryptographic authentication. It is a correctness boundary that prevents a random unregistered socket from merely writing another client ID into JSON.

Cryptographic identity belongs to the later cyber phase.

## Authoritative diff

Relay-only message:

    OVERLAY_DIFF

Operations:

- `UPSERT`.
- `REMOVE`.

Each diff contains:
- overlay ID.
- operation.
- per-overlay revision.
- authoritative `stateEpoch`.
- authoritative `stateVersion`.
- complete overlay data for UPSERT.

Clients are not allowed to submit `OVERLAY_DIFF`.

## Two ordering layers

Prototype 0.6 intentionally has two ordering mechanisms.

### Global epoch/version

`stateEpoch` identifies one relay process timeline.

`stateVersion` increments once for every accepted authoritative overlay mutation.

It detects missing/reordered global state:

    local version 10
    receives version 12
    -> gap
    -> do not guess
    -> request authoritative overlay snapshot

### Per-overlay revision

Each overlay independently advances:

    create -> revision 1
    update -> revision 2
    update -> revision 3
    remove -> tombstone revision 4

Per-overlay revision prevents delayed geometry from rolling one object backward.

Clients remember the latest revision seen for an overlay, including a removal tombstone during the current synchronized timeline.

A stale per-overlay revision does not modify geometry, but a valid contiguous global diff still advances the client global state version.

## Generalized snapshots

Message:

    OVERLAY_SNAPSHOT

Snapshot chunks contain:
- `snapshotId`.
- `stateEpoch`.
- `stateVersion`.
- `chunkIndex`.
- `chunkCount`.
- `overlays[]`.

Snapshot rules remain the same reliability model established in Prototype 0.4:
- out-of-order chunks allowed.
- identical duplicates harmless.
- conflicts cause resync.
- state replaced only after complete assembly.
- newer epoch may reset version to zero.
- missing chunks trigger timeout and a fresh snapshot request.

`OverlaySnapshotChunker` measures serialized JSON and keeps each packet within the 1200-byte snapshot budget.

Unlike the old ping snapshot, variable path/zone geometry is included in this byte calculation.

## Relay authority migration

`OverlayStore` is now the relay source of truth.

The old ping store is no longer the relay authority.

For backward compatibility during the migration:
- legacy `PING_CREATE` maps to a MARKER overlay.
- legacy `PING_REMOVE` removes the corresponding MARKER overlay.
- legacy snapshots are generated from active MARKER overlays.
- accepted marker mutations may also emit legacy ping messages so Prototype 0.4/0.5 viewers continue to work.

This compatibility adapter is intentionally one-way around the generalized authority; it is not a second state store.

The old 0.4/0.5 regression suites remain part of the 0.6 closeout gate.

## Desktop client migration

`OperatorRelayClient` now uses the generalized protocol internally.

It supports:
- `CreateOverlayAsync`.
- `UpdateOverlayAsync`.
- `RemoveOverlayAsync`.
- generalized snapshot recovery.
- generalized global ordering.
- per-overlay revision tracking.
- snapshot timeout/resync.
- relay restart/new-epoch recovery.

`CreateMarkerAsync` and `RemoveMarkerAsync` remain convenience wrappers over the generalized overlay API.

The WinUI Prototype 0.5 marker workflow therefore runs on the 0.6 model without needing duplicate marker networking code.

The current WinUI viewport still draws only MARKER overlays. PATH/ZONE drawing/editing UX belongs to Prototype 0.7.

## Android migration

Android now has:
- generalized `Overlay` / `SharedPoint` models.
- MARKER/PATH/ZONE geometry contract parity with C#.
- generalized diff/snapshot parser.
- `AuthoritativeOverlayState` with global ordering and per-overlay tombstones.
- `RemoteOverlayProjector` that transforms every point in an overlay through `sharedToLocal()`.
- one registered `UdpOverlayClient` socket for registration, heartbeat, snapshot recovery, and commands.

Using one UDP socket matters because the relay verifies generalized commands against the registered source endpoint.

The existing PING button now sends an `OVERLAY_CREATE` MARKER command.

CLEAR sends `OVERLAY_REMOVE` with the current relay-assigned revision.

Android currently renders MARKER overlays in AR. PATH and ZONE geometry is parsed, stored, ordered, snapshot-recovered, and spatially projectable, but visual path/zone rendering is a Prototype 0.7 concern.

## Cross-language contract

`testdata/overlay-contract-v1.json` is a shared authoritative PATH diff fixture.

C# parses the fixture with `OverlayWireCodec`.

Android instrumentation includes a parser test against the same file.

During this 0.6 work the tablet became unavailable over ADB, so the new generalized instrumentation could be compiled but not executed on-device. The Android JVM generalized state/projector tests and application build are part of the software closeout gate.

The earlier Prototype 0.5 shared spatial contract was already physically executed successfully on the SM-X800 before the device disconnected.

## Test tools

### LynxOverlaySim

Uses the same reusable desktop client as the WinUI operator.

Scenarios cover:
- MARKER/PATH/ZONE create/update/remove lifecycle.
- independent client fan-out.
- reconnect snapshot recovery.
- bulk variable-geometry state.
- relay restart recovery.

### LynxOverlayProbe

Low-level registered UDP probe used to deliberately send commands normal APIs prevent.

It verifies:
- stale expected revision is rejected.
- spoofed sender ID is rejected.
- invalid geometry is rejected.
- rejected commands do not advance global state version.
- correct remove advances the overlay tombstone revision.

## Automated verification

Primary command:

    .\scripts\Test-Prototype06-All.ps1

When an Android device is available:

    .\scripts\Test-Prototype06-All.ps1 -ConnectedAndroid

0.6-specific integration regressions:
- `Test-Prototype06-Lifecycle.ps1`.
- `Test-Prototype06-Revisions.ps1`.
- `Test-Prototype06-Reconnect.ps1`.
- `Test-Prototype06-Chunking.ps1`.
- `Test-Prototype06-RelayRestart.ps1`.

The consolidated gate also runs the complete Prototype 0.5 and 0.4 compatibility chain.

## Out of scope / Prototype 0.7

Prototype 0.6 deliberately does not add:
- finished PATH drawing tools.
- finished ZONE polygon drawing tools.
- vertex edit handles.
- path/zone AR rendering.
- styling/color systems.
- selection/multi-select UX.
- undo/redo.

Those features now have a correct generalized state layer to build on in Prototype 0.7.

## Security boundary

Still deferred to the cyber phase:
- authenticated users/devices.
- authenticated relay discovery.
- roles.
- ChaCha20-Poly1305 transport.
- replay protection.
- audit-log integrity.

Prototype 0.6 establishes deterministic state semantics first so the later security layer protects a protocol whose correctness rules are already understood.
