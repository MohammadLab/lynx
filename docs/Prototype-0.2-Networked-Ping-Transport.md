# Prototype 0.2 — Networked Ping Transport

## Goal

Prove that Lynx ping state can travel from a client, through an authoritative relay, to another client.

Prototype 0.2 deliberately does **not** solve shared AR/world coordinates. Android x/y/z values are still local to the sending ARCore session.

## Protocol

Each UDP datagram is a typed Lynx message.

PING_CREATE example:

    {"type":"PING_CREATE","ping":{"id":12345,"senderId":1,"x":1.25,"y":-0.5,"z":3.75,"createdAt":1800000000000}}

PING_REMOVE example:

    {"type":"PING_REMOVE","pingId":12345,"senderId":1}

## Current flow

Android tablet -> typed JSON -> UDP :7777 -> LynxRelay -> UDP :7778 -> LynxViewerSim

LynxRelay is authoritative for the active ping set. It validates commands, deduplicates exact retransmissions, rejects conflicting ID reuse, rejects removal by the wrong sender, stores accepted creates, removes accepted deletes, and forwards accepted state changes.

For PC-only development, LynxPingSender replaces the Android tablet.

## Projects

- protocol/Lynx.Protocol: shared C# message and ping types plus JSON validation.
- relay/Lynx.Relay.Core: testable authoritative ping store.
- relay/LynxRelay: UDP listener on port 7777 and authoritative command processor.
- tools/LynxViewerSim: fake Device B listening on port 7778.
- tools/LynxPingSender: PC-side fake Device A for create/remove testing.
- tests/Lynx.Tests: unit tests for message validation and relay state semantics.

## Automated checks

Run unit tests:

    dotnet test .\Lynx.sln

Run the PC-side Prototype 0.2 transport regression:

    powershell -ExecutionPolicy Bypass -File .\scripts\Test-Prototype02.ps1

The regression proves:

1. A valid create reaches ViewerSim exactly once.
2. Exact duplicate create is suppressed.
3. Conflicting reuse of a ping ID is rejected.
4. Wrong-owner removal is rejected.
5. Valid removal reaches ViewerSim exactly once.
6. Duplicate removal is suppressed.
7. ViewerSim returns to zero stored pings.
8. Malformed and oversized payloads are rejected.

## Android behavior

PING creates a local Ping object, wraps it in PING_CREATE, and sends it to the relay.

CLEAR detaches local anchors, clears local ping state, and sends one PING_REMOVE command for each locally created ping.

## Future tablet test

Run:

    powershell -ExecutionPolicy Bypass -File .\scripts\Run-Prototype02.ps1

The script shows current PC IPv4 addresses and opens ViewerSim and LynxRelay in separate windows.

The Android app must send to the PC current Wi-Fi IPv4 address on UDP port 7777.

Expected create flow:

    [Relay] CREATE id=... sender=1 ... stored=1
    [Relay] -> forwarded PING_CREATE id=...
    [ViewerSim] REMOTE PING RECEIVED

Expected clear/remove flow:

    [Relay] REMOVE id=... sender=1 ... stored=0
    [Relay] -> forwarded PING_REMOVE id=...
    [ViewerSim] REMOTE PING REMOVED id=...

## Real tablet network verification

Verified from the physical SM-X800 tablet over Wi-Fi using the exact Lynx JSON envelopes: PING_CREATE reached LynxRelay and ViewerSim, then PING_REMOVE returned both relay and ViewerSim to zero stored pings.

This proves the tablet-to-PC UDP route and protocol compatibility. It does not yet prove the app's AR hit-test-triggered send path.

## Remaining before Prototype 0.2 is complete

- Physically aim the tablet center reticle at a detected plane and press PING so the real app generates the create message.
- Press CLEAR in the real app and verify the app-generated removal reaches ViewerSim.
- Do not interpret sender-local AR coordinates as shared physical coordinates.

Shared spatial alignment belongs to Prototype 0.3.