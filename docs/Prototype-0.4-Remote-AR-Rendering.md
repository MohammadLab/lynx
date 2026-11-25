# Prototype 0.4 — Remote AR Rendering

## Status

Prototype 0.4 is software-complete for the current one-tablet development setup.

Completed:
- authoritative multi-client relay.
- Android self-registration and heartbeats.
- stable per-install client identity.
- remote PING_CREATE / PING_REMOVE rendering path.
- snapshot-on-join and reconnect recovery.
- state epoch/version ordering.
- adaptive chunked snapshots.
- missing-chunk timeout and explicit snapshot resync.
- relay restart recovery.
- LAN relay discovery with explicit-host fallback.
- deferred SceneView marker retirement.
- automated regression coverage for the full synchronization path.

Physical validation already completed:
- AprilTag calibration on the SM-X800.
- calibrated remote network ping rendered in AR.
- authoritative remote remove removed the ping without closing the app.
- passive self-registration/heartbeat over real Wi-Fi.
- automatic relay discovery with `lynx.relayHost=auto`, followed by registration and heartbeat with no desktop IP compiled into the APK.

Physical validation still worth repeating when the tablet can be handled again:
- calibrated create/remove after the new deferred marker-retirement cleanup.
- two real Android devices calibrated to the same beacon, once a second device is available.

## Objective

Receive authoritative shared-space overlay state over UDP and render it in the local calibrated ARCore frame while remaining deterministic under duplicate packets, reordering, temporary disconnects, client restart, relay restart, and incomplete snapshot delivery.

## Final data flow

    local/remote command sender
    -> PING_CREATE / PING_REMOVE
    -> authoritative relay validates and mutates state
    -> relay assigns stateEpoch + stateVersion
    -> relay broadcasts accepted mutation
    -> Android client validates ordering
    -> shared coordinates
    -> beaconId compatibility check
    -> sharedToLocal()
    -> ARCore anchor
    -> Lynx billboard marker

Recovery path:

    client register / reconnect / version gap / snapshot timeout
    -> authoritative snapshot request
    -> byte-budgeted SNAPSHOT chunks
    -> complete assembly only
    -> replace client remote state

## Authoritative relay model

The relay is the source of truth for active pings.

Clients submit commands. The relay:
- validates messages.
- suppresses exact duplicate creates.
- rejects conflicting ID reuse while an ID is active.
- enforces sender ownership on remove.
- stores accepted ping state.
- assigns authoritative ordering metadata.
- broadcasts only accepted mutations.
- sends authoritative snapshots to joining/recovering clients.

The relay remains calibration-agnostic. `beaconId` is transported as data but spatial compatibility is enforced by the receiving client.

## Client registration

Protocol messages:
- `CLIENT_REGISTER { clientId }`
- `CLIENT_HEARTBEAT { clientId }`
- `SNAPSHOT_REQUEST { clientId }`

Registration uses the source endpoint of the UDP datagram. A client never tells the relay which IP/port to send back to.

The relay tracks:
- stable client ID.
- observed UDP endpoint.
- last-seen timestamp.

Heartbeats refresh the registration. Stale clients expire after the configured timeout.

If a heartbeat arrives after a relay restart and the client ID is unknown to the new registry, the relay treats it as registration and sends a fresh snapshot.

## Stable client identity

Android uses `ClientIdentityStore`.

On first launch it:
- generates a random positive 63-bit value with `SecureRandom`.
- stores it in app-private SharedPreferences.
- reuses it across process restarts.

For Prototype 0.4 that value is used as both:
- `clientId` for relay registration.
- `senderId` for ping ownership.

Physical validation on the SM-X800 confirmed the same identity before and after force-stop/relaunch.

This identity is not authenticated. Cryptographic identity belongs to the later cyber/security phase.

## Authoritative ordering

Every accepted relay mutation carries:
- `stateEpoch`.
- `stateVersion`.

`stateEpoch` identifies the current relay process/timeline.

`stateVersion` starts at 0 for a new epoch and increments exactly once for each accepted `PING_CREATE` or `PING_REMOVE`.

Clients do not choose these values.

Client ordering rules:
- same epoch + next version: apply.
- same epoch + old/equal version: stale/duplicate, ignore.
- same epoch + skipped version: do not guess; request snapshot.
- older epoch: ignore.
- newer epoch mutation: request snapshot before applying state.
- newer epoch snapshot: replace old-epoch state even if its version resets to 0.

This prevents late UDP packets from rolling client state backward.

## Snapshot format

Snapshot chunks carry:
- `snapshotId`.
- `stateEpoch`.
- `stateVersion`.
- `chunkIndex`.
- `chunkCount`.
- `pings[]`.

Clients assemble by snapshot ID and authoritative epoch/version.

Rules:
- chunks may arrive out of order.
- identical duplicate chunks are harmless.
- conflicting duplicate chunks trigger resync.
- incomplete older snapshots may be superseded by newer snapshots.
- client state is replaced only after every chunk is present.

## Adaptive UDP chunking

Snapshots are not split by a fixed ping count.

`SnapshotChunker` greedily packs pings while measuring the actual serialized JSON size.

Current snapshot payload budget:

    1200 bytes

This is intentionally below the old 4096-byte application ceiling to reduce IP fragmentation on typical networks.

The relay verifies every final serialized snapshot packet remains within the budget before sending it.

## Missing-chunk recovery

Android and ViewerSim track snapshot chunk receipt separately from semantic snapshot assembly.

If a snapshot remains incomplete for 3 seconds:
- the partial receipt is abandoned.
- the client sends `SNAPSHOT_REQUEST` from its registered UDP socket.
- the relay creates a fresh snapshot ID.
- the fresh snapshot supersedes the incomplete one.

This prevents a single lost UDP chunk from leaving the client permanently stuck.

## Disconnect and reconnect recovery

Client disappearance does not delete relay state.

Verified sequence:

    client online
    -> ping 7001 created
    -> client disconnects
    -> registry entry expires
    -> ping 7002 created while client is offline
    -> same client ID reconnects
    -> snapshot contains 7001 + 7002
    -> client reconstructs authoritative state

## Relay restart recovery

A relay restart creates a new `stateEpoch` and an empty in-memory authoritative state.

The client keeps heartbeating. The new relay does not know the client ID, so the heartbeat causes a new snapshot to be sent.

A newer-epoch snapshot is allowed to reset the client from an older nonzero version to the new epoch at version 0.

Transient UDP socket errors while the relay is down are treated as recoverable rather than fatal.

Relay state persistence across process restarts is intentionally not part of Prototype 0.4.

## Relay discovery

Android defaults to:

    lynx.relayHost=auto
    lynx.relayPort=7777

Discovery uses the same UDP service port as normal Lynx traffic.

Flow:
1. Android sends `LYNX_DISCOVER_V1` to LAN broadcast targets on the configured relay port.
2. If broadcast is filtered, Android performs a bounded /24 unicast sweep on the local subnet.
3. A relay replies `LYNX_RELAY_V1|<port>`.
4. Android uses the response source IP plus advertised port for registration and all subsequent sends.

An explicit `lynx.relayHost=<IPv4>` in `local.properties` bypasses discovery.

Discovery is only a bootstrap mechanism. It does not establish trust; authenticated relay identity belongs to the cyber/security phase.

### Windows Firewall note

LAN discovery and normal client traffic require Windows Firewall to allow inbound `lynxrelay.exe` traffic on the active network profile.

During closeout testing the host firewall state changed while diagnosing discovery. Once the current rules reported `lynxrelay.exe` as Inbound Allow on the Public profile, physical tablet auto-discovery succeeded.

`scripts/Run-Prototype04.ps1` performs a preflight check and warns if an enabled inbound block rule for `lynxrelay.exe` is present.

## Android remote state rules

Remote pings:
- are stored by ping ID.
- may exist before calibration.
- render only when a compatible beacon calibration exists.
- are reprojected after calibration/recalibration.
- are unaffected by local CLEAR.
- are replaced from complete authoritative snapshots.

Self-echo handling:
- a locally active ping echoed by the relay is treated as authoritative confirmation, not rendered twice.
- after app restart, old authoritative pings from the same sender ID may return from snapshot because they are no longer live local UI objects.

## SceneView / Filament marker lifecycle

The first physical remote remove exposed a native Filament abort:

    Invalid texture still bound to MaterialInstance: Transparent Textured

Immediate Compose removal could dispose the billboard texture while Filament was still using it in the current render frame.

Final 0.4 lifecycle:
- active local/remote ownership is updated immediately.
- removed/replaced anchors are detached immediately.
- rendered anchors remain in a shared render queue for three additional frames plus a short guard delay.
- only then are their SceneView nodes removed and disposed.
- the same retirement path is used for remote remove, snapshot replacement, recalibration, and local CLEAR.

This avoids both immediate native teardown and the earlier memory leak where detached remote render objects were retained forever.

## Physical validation completed

On the SM-X800:
- AprilTag 36h11 ID 0 calibration reached 4/4 ARCore plane hits.
- a network `PING_CREATE` from sender 99 at shared `(0.50, 0.00, 1.00)` reached Android through the relay.
- Android reported `RX: rendered ping 5002`.
- the Lynx marker appeared in calibrated AR.
- authoritative `PING_REMOVE 5002` reached Android.
- Android reported zero remote pings and stayed alive after the original Filament crash was fixed.
- client identity persisted across force-stop/relaunch.
- client self-registration and heartbeat were validated over real Wi-Fi.
- automatic relay discovery was physically validated after the Windows Firewall rule returned to Inbound Allow: the tablet at `192.168.0.208` discovered the desktop relay, received the discovery response, registered from UDP `:7778`, and began heartbeating without a configured desktop IP.

## Automated verification

Primary command:

    .\scripts\Test-Prototype04-All.ps1

With a connected Android device:

    .\scripts\Test-Prototype04-All.ps1 -ConnectedAndroid

Coverage includes:
- .NET protocol/relay tests.
- Android unit/build tests.
- optional Android instrumentation tests.
- multi-client fan-out.
- late-join snapshot recovery.
- disconnect/reconnect recovery.
- adaptive snapshot chunking and byte budget.
- version-gap/stale-packet handling.
- relay restart / epoch recovery.
- missing snapshot chunk timeout and resync.
- relay discovery protocol.

Individual regressions:
- `Test-Prototype04.ps1`.
- `Test-Prototype04-Reconnect.ps1`.
- `Test-Prototype04-Chunking.ps1`.
- `Test-Prototype04-Versioning.ps1`.
- `Test-Prototype04-RelayRestart.ps1`.
- `Test-Prototype04-SnapshotTimeout.ps1`.
- `Test-Prototype04-Discovery.ps1`.

## Remaining items outside 0.4 scope

These are intentionally deferred rather than unfinished 0.4 work:
- encryption and authenticated transport.
- authenticated device/role identity.
- replay protection.
- audit-log integrity.
- persistent relay state across relay process restarts.
- operational packet-loss/latency/reordering stress campaigns.
- authenticated discovery/trust.

Hardware-only validation still pending:
- repeat calibrated create/remove with the deferred marker-retirement implementation.
- real two-Android-device shared-location test when a second ARCore device is available.
