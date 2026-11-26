package com.sentinelsystems.hud

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.key
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.ar.core.Anchor
import com.google.ar.core.Frame
import com.google.ar.core.HitResult
import com.google.ar.core.Plane
import com.google.ar.core.Pose
import com.google.ar.core.Session
import com.google.ar.core.TrackingState
import io.github.sceneview.ar.ARSceneView
import io.github.sceneview.ar.node.AnchorNode
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import io.github.sceneview.node.BillboardNode
import android.graphics.BitmapFactory
import android.util.Log
import androidx.compose.ui.platform.LocalContext
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Arrangement
import com.sentinelsystems.hud.model.Ping
import com.sentinelsystems.hud.model.Overlay
import com.sentinelsystems.hud.model.OverlayKinds
import com.sentinelsystems.hud.model.SharedPoint
import com.sentinelsystems.hud.identity.ClientIdentityStore
import com.sentinelsystems.hud.calibration.BeaconDetector
import com.sentinelsystems.hud.calibration.Calibration
import com.sentinelsystems.hud.calibration.CalibrationAttempt
import com.sentinelsystems.hud.calibration.Vec3
import com.sentinelsystems.hud.calibration.RemotePingProjector
import com.sentinelsystems.hud.calibration.RemoteOverlayProjector
import com.sentinelsystems.hud.protocol.overlayCreateToJson
import com.sentinelsystems.hud.protocol.overlayRemoveToJson
import androidx.compose.runtime.rememberCoroutineScope
import com.sentinelsystems.hud.network.UdpOverlayClient
import com.sentinelsystems.hud.network.RelayDiscovery
import com.sentinelsystems.hud.network.RelayEndpoint
import com.sentinelsystems.hud.protocol.AuthoritativeOverlayState
import com.sentinelsystems.hud.protocol.OverlayInboundMessage
import com.sentinelsystems.hud.protocol.OverlayStateApplyStatus
import kotlinx.coroutines.launch
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.delay


fun pickBestHit(hits: List<HitResult>): HitResult? {
    return hits.firstOrNull { hit ->
        val trackable = hit.trackable

        trackable is Plane &&
                trackable.trackingState == TrackingState.TRACKING &&
                trackable.isPoseInPolygon(hit.hitPose)
    }
}

@Composable
fun ArScreen() {
    var viewportSize by remember {
        mutableStateOf(IntSize.Zero)
    }

    var trackingState by remember {
        mutableStateOf("STARTING")
    }

    var latestFrame by remember {
        mutableStateOf<Frame?>(null)
    }

    var latestSession by remember {
        mutableStateOf<Session?>(null)
    }

    val localAnchors = remember {
        mutableStateListOf<Anchor>()
    }

    val renderAnchors = remember {
        mutableStateListOf<Anchor>()
    }

    val pings = remember {
        mutableStateListOf<Ping>()
    }

    val remoteOverlays = remember {
        mutableStateMapOf<Long, Overlay>()
    }

    val remoteAnchors = remember {
        mutableStateMapOf<Long, Anchor>()
    }

    val context = LocalContext.current

    val pingBitmap = remember {
        BitmapFactory.decodeResource(
            context.resources,
            R.drawable.lynx_ping
        )
    }

    var lastPing by remember {
        mutableStateOf<Ping?>(null)
    }

    var lastPingJson by remember {
        mutableStateOf<String?>(null)
    }

    val coroutineScope = rememberCoroutineScope()
    val retiringAnchors = remember { mutableSetOf<Anchor>() }

    fun registerRenderAnchor(anchor: Anchor): Anchor {
        renderAnchors.add(anchor)
        return anchor
    }

    fun retireAnchor(anchor: Anchor) {
        if (!retiringAnchors.add(anchor)) return

        anchor.detach()
        coroutineScope.launch {
            repeat(3) {
                withFrameNanos { }
            }
            delay(50)
            renderAnchors.remove(anchor)
            retiringAnchors.remove(anchor)
        }
    }

    fun retireRemoteAnchor(pingId: Long) {
        remoteAnchors.remove(pingId)?.let(::retireAnchor)
    }

    fun retireAllRemoteAnchors() {
        remoteAnchors.keys.toList().forEach(::retireRemoteAnchor)
    }

    var udpStatus by remember {
        mutableStateOf("UDP: idle")
    }

    var calibration by remember {
        mutableStateOf<Calibration?>(null)
    }

    var calibrationStatus by remember {
        mutableStateOf("NOT CALIBRATED")
    }

    var calibrationAttempt by remember {
        mutableStateOf<CalibrationAttempt?>(null)
    }

    val localSenderId = remember {
        ClientIdentityStore(context).getOrCreate()
    }
    val configuredRelayHost = BuildConfig.LYNX_RELAY_HOST
    val configuredRelayPort = BuildConfig.LYNX_RELAY_PORT
    val receivePort = 7778
    var relayEndpoint by remember {
        mutableStateOf<RelayEndpoint?>(null)
    }

    val currentCalibrationState = rememberUpdatedState(calibration)
    val currentSessionState = rememberUpdatedState(latestSession)
    val authoritativeOverlayState = remember { AuthoritativeOverlayState() }
    var activeOverlayClient by remember { mutableStateOf<UdpOverlayClient?>(null) }
    var receiverEpoch by remember { mutableStateOf(0) }

    fun reconcileAuthoritativeOverlays(
        overlays: List<Overlay>,
        cal: Calibration?,
        session: Session?
    ) {
        retireAllRemoteAnchors()
        remoteOverlays.clear()

        overlays.forEach { overlay ->
            val isLiveLocalMarker =
                overlay.ownerId == localSenderId &&
                    pings.any { it.id == overlay.overlayId }

            if (!isLiveLocalMarker) {
                remoteOverlays[overlay.overlayId] = overlay
            }

            if (
                isLiveLocalMarker ||
                overlay.kind != OverlayKinds.MARKER ||
                cal == null ||
                session == null
            ) return@forEach

            val localPoints = RemoteOverlayProjector.projectPoints(
                overlay,
                cal
            ) ?: return@forEach

            val local = localPoints.singleOrNull() ?: return@forEach
            remoteAnchors[overlay.overlayId] = registerRenderAnchor(
                session.createAnchor(
                    Pose.makeTranslation(local.x, local.y, local.z)
                )
            )
        }
    }

    LaunchedEffect(receiverEpoch) {
        val resolvedRelay = RelayDiscovery.resolve(
            configuredHost = configuredRelayHost,
            configuredPort = configuredRelayPort
        )

        if (resolvedRelay == null) {
            relayEndpoint = null
            activeOverlayClient = null
            udpStatus = "DISCOVERY: relay not found; retrying"
            delay(5_000)
            receiverEpoch++
            return@LaunchedEffect
        }

        relayEndpoint = resolvedRelay
        udpStatus = "RELAY: ${resolvedRelay.host}:${resolvedRelay.port}"

        val client = UdpOverlayClient(
            listenPort = receivePort,
            relayHost = resolvedRelay.host,
            relayPort = resolvedRelay.port,
            clientId = localSenderId
        )
        activeOverlayClient = client

        try {
            client.run(
                onMessage = { message ->
                    val result = authoritativeOverlayState.apply(message)

                    when (result.status) {
                        OverlayStateApplyStatus.RESYNC_REQUIRED -> {
                            authoritativeOverlayState.resetPendingSnapshot()
                            client.requestSnapshot()
                            withContext(Dispatchers.Main.immediate) {
                                udpStatus =
                                    "RX: overlay gap at " +
                                        "${authoritativeOverlayState.appliedEpoch}:" +
                                        "${authoritativeOverlayState.appliedVersion}; resync"
                            }
                        }

                        OverlayStateApplyStatus.STALE_IGNORED -> {
                            withContext(Dispatchers.Main.immediate) {
                                udpStatus = "RX: stale overlay packet ignored"
                            }
                        }

                        OverlayStateApplyStatus.SNAPSHOT_INCOMPLETE -> {
                            val chunk = message as? OverlayInboundMessage.SnapshotChunk
                            withContext(Dispatchers.Main.immediate) {
                                udpStatus = if (chunk == null) {
                                    "RX: overlay snapshot incomplete"
                                } else {
                                    "RX: overlay snapshot " +
                                        "${chunk.chunkIndex + 1}/${chunk.chunkCount} " +
                                        "v${chunk.stateVersion}"
                                }
                            }
                        }

                        OverlayStateApplyStatus.SNAPSHOT_APPLIED,
                        OverlayStateApplyStatus.APPLIED -> {
                            if (result.stateChanged ||
                                result.status == OverlayStateApplyStatus.SNAPSHOT_APPLIED
                            ) {
                                withContext(Dispatchers.Main.immediate) {
                                    reconcileAuthoritativeOverlays(
                                        authoritativeOverlayState.snapshot(),
                                        currentCalibrationState.value,
                                        currentSessionState.value
                                    )
                                }
                            }

                            withContext(Dispatchers.Main.immediate) {
                                udpStatus =
                                    "RX: overlays ${authoritativeOverlayState.snapshot().size} " +
                                        "e${authoritativeOverlayState.appliedEpoch} " +
                                        "v${authoritativeOverlayState.appliedVersion}"
                            }
                        }

                        OverlayStateApplyStatus.IGNORED -> Unit
                    }
                },
                onStatus = { status ->
                    withContext(Dispatchers.Main.immediate) {
                        udpStatus = status
                    }
                }
            )
        } finally {
            if (activeOverlayClient === client) {
                activeOverlayClient = null
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .onSizeChanged { viewportSize = it }
    ) {

        ARSceneView(
            modifier = Modifier.fillMaxSize(),
            planeRenderer = true,
            onSessionUpdated = { session, frame ->
                latestSession = session
                latestFrame = frame

                trackingState = when (frame.camera.trackingState) {
                    TrackingState.TRACKING -> "TRACKING"
                    TrackingState.PAUSED -> "PAUSED"
                    TrackingState.STOPPED -> "STOPPED"
                }
            }
        ) {
            renderAnchors.forEach { anchor ->
                key(anchor) {
                    AnchorNode(anchor = anchor) {
                        BillboardNode(
                            bitmap = pingBitmap,
                            widthMeters = 0.18f,
                            heightMeters = 0.18f
                        )
                    }
                }
            }
        }

        Box(
            modifier = Modifier
                .align(Alignment.TopStart)
                .padding(14.dp)
                .background(
                    color = Color.Black.copy(alpha = 0.68f),
                    shape = RoundedCornerShape(12.dp)
                )
                .padding(horizontal = 14.dp, vertical = 10.dp)
        ) {
            Column {
                Text(
                    text = "LYNX  •  PROTOTYPE 0.6",
                    color = Color.White,
                    fontSize = 14.sp
                )
                Text(
                    text = "AR      $trackingState",
                    color = Color.White,
                    fontSize = 13.sp
                )
                Text(
                    text = "CAL     $calibrationStatus",
                    color = if (calibration != null) Color.Green else Color.White,
                    fontSize = 13.sp
                )

                calibrationAttempt?.let { attempt ->
                    val tagText = attempt.detection?.beaconId?.toString() ?: "--"
                    Text(
                        text = "TAG     $tagText    HITS ${attempt.planeHits}/4",
                        color = Color.White,
                        fontSize = 13.sp
                    )
                }

                Text(
                    text = udpStatus,
                    color = Color.White,
                    fontSize = 13.sp
                )
                Text(
                    text = "RELAY   ${relayEndpoint?.let { "${it.host}:${it.port}" } ?: "--"}",
                    color = Color.White,
                    fontSize = 13.sp
                )
                Text(
                    text = "ID      ${localSenderId.toString().takeLast(8)}",
                    color = Color.White,
                    fontSize = 13.sp
                )
                Text(
                    text = "OVERLAYS ${remoteOverlays.size} remote  " +
                        "${remoteAnchors.size} marker(s) rendered",
                    color = Color.White,
                    fontSize = 13.sp
                )

                lastPing?.let { ping ->
                    Text(
                        text = "SHARED  b${ping.beaconId}  " +
                            "(${"%.2f".format(ping.x)}, " +
                            "${"%.2f".format(ping.y)}, " +
                            "${"%.2f".format(ping.z)})",
                        color = Color.White,
                        fontSize = 13.sp
                    )
                }
            }
        }

        // Center aiming reticle
        Box(
            modifier = Modifier
                .align(Alignment.Center)
                .size(12.dp)
                .background(
                    color = Color.Red,
                    shape = CircleShape
                )

        )

        Row(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 32.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Button(
                onClick = {
                    val frame = latestFrame ?: return@Button

                    calibrationStatus = "SCANNING..."

                    try {
                        val attempt = BeaconDetector.attemptCalibration(frame)
                        calibrationAttempt = attempt

                        val built = attempt.calibration

                        if (built == null) {
                            calibrationStatus = attempt.message.uppercase()
                            return@Button
                        }

                        calibration = built
                        calibrationStatus = "READY  •  BEACON ${built.beaconId}"

                        reconcileAuthoritativeOverlays(
                            authoritativeOverlayState.snapshot(),
                            built,
                            latestSession
                        )
                    } catch (e: Exception) {
                        Log.e("LynxCalibration", "Calibration failed", e)
                        calibrationStatus =
                            "ERROR  •  ${e.javaClass.simpleName}"
                    }
                }
            ) {
                Text("CALIBRATE")
            }

            Button(
                onClick = {
                    val currentCalibration = calibration ?: run {
                        udpStatus = "SIM REMOTE: calibrate first"
                        return@Button
                    }

                    val session = latestSession ?: return@Button
                    val now = System.currentTimeMillis()

                    val simulatedRemotePing = Ping(
                        id = now,
                        senderId = 99L,
                        beaconId = currentCalibration.beaconId,
                        x = 0.50f,
                        y = 0.00f,
                        z = 1.00f,
                        createdAt = now
                    )

                    val projection = RemotePingProjector.project(
                        ping = simulatedRemotePing,
                        calibration = currentCalibration
                    ) ?: run {
                        udpStatus = "SIM REMOTE: frame mismatch"
                        return@Button
                    }

                    val local = projection.localPosition
                    localAnchors.add(
                        registerRenderAnchor(
                            session.createAnchor(
                                Pose.makeTranslation(
                                    local.x,
                                    local.y,
                                    local.z
                                )
                            )
                        )
                    )

                    udpStatus = "SIM REMOTE: rendered shared (0.50, 0.00, 1.00)"
                }
            ) {
                Text("SIM REMOTE")
            }

            Button(
                onClick = {
                    val frame = latestFrame ?: return@Button

                    if (viewportSize.width == 0 || viewportSize.height == 0) {
                        return@Button
                    }

                    val centerX = viewportSize.width / 2f
                    val centerY = viewportSize.height / 2f

                    val hits = frame.hitTest(centerX, centerY)
                    val bestHit = pickBestHit(hits) ?: return@Button
                    val pose = bestHit.hitPose

                    localAnchors.add(registerRenderAnchor(bestHit.createAnchor()))

                    val currentCalibration = calibration

                    if (currentCalibration == null) {
                        udpStatus = "LOCAL ONLY: calibrate beacon first"
                        return@Button
                    }

                    val shared = currentCalibration.localToShared(
                        Vec3(
                            x = pose.tx(),
                            y = pose.ty(),
                            z = pose.tz()
                        )
                    )

                    val now = System.currentTimeMillis()
                    val ping = Ping(
                        id = now,
                        senderId = localSenderId,
                        beaconId = currentCalibration.beaconId,
                        x = shared.x,
                        y = shared.y,
                        z = shared.z,
                        createdAt = now
                    )

                    lastPing = ping

                    val json = overlayCreateToJson(
                        overlayId = ping.id,
                        senderId = ping.senderId,
                        beaconId = ping.beaconId,
                        kind = OverlayKinds.MARKER,
                        geometry = listOf(
                            SharedPoint(ping.x, ping.y, ping.z)
                        ),
                        createdAt = ping.createdAt
                    )
                    lastPingJson = json

                    coroutineScope.launch {
                        val client = activeOverlayClient
                        if (client == null) {
                            udpStatus = "UDP: overlay client unavailable"
                            return@launch
                        }

                        try {
                            client.sendCommand(json)
                            udpStatus = "UDP: marker overlay command sent"
                        } catch (e: Exception) {
                            udpStatus = "UDP ERROR: ${e.message}"
                        }
                    }

                    pings.add(ping)
                }
            ) {
                Text("PING")
            }

            Button(
                onClick = {
                    val pingsToRemove = pings.toList()

                    localAnchors.toList().forEach(::retireAnchor)
                    localAnchors.clear()
                    pings.clear()
                    lastPing = null
                    lastPingJson = null

                    coroutineScope.launch {
                        val client = activeOverlayClient
                        if (client == null) {
                            udpStatus = "UDP: overlay client unavailable"
                            return@launch
                        }

                        try {
                            var commands = 0
                            pingsToRemove.forEach { ping ->
                                val overlay = authoritativeOverlayState.get(ping.id)
                                    ?: return@forEach
                                client.sendCommand(
                                    overlayRemoveToJson(
                                        overlayId = overlay.overlayId,
                                        senderId = localSenderId,
                                        expectedRevision = overlay.revision
                                    )
                                )
                                commands++
                            }

                            udpStatus = "UDP: remove commands $commands/${pingsToRemove.size}"
                        } catch (e: Exception) {
                            udpStatus = "UDP ERROR: ${e.message}"
                        }
                    }
                }
            ) {
                Text("CLEAR")
            }
        }
    }
}