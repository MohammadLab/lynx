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
import com.sentinelsystems.hud.identity.ClientIdentityStore
import com.sentinelsystems.hud.calibration.BeaconDetector
import com.sentinelsystems.hud.calibration.Calibration
import com.sentinelsystems.hud.calibration.CalibrationAttempt
import com.sentinelsystems.hud.calibration.Vec3
import com.sentinelsystems.hud.calibration.RemotePingProjector
import com.sentinelsystems.hud.protocol.pingCreateToJson
import com.sentinelsystems.hud.protocol.pingRemoveToJson
import androidx.compose.runtime.rememberCoroutineScope
import com.sentinelsystems.hud.network.UdpPingSender
import com.sentinelsystems.hud.network.UdpPingReceiver
import com.sentinelsystems.hud.network.RelayDiscovery
import com.sentinelsystems.hud.network.RelayEndpoint
import com.sentinelsystems.hud.protocol.InboundMessage
import com.sentinelsystems.hud.protocol.SnapshotAssembler
import com.sentinelsystems.hud.protocol.SnapshotAssemblyStatus
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

    val remotePings = remember {
        mutableStateMapOf<Long, Ping>()
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
    val snapshotAssembler = remember { SnapshotAssembler() }
    var appliedStateEpoch by remember { mutableStateOf(0L) }
    var appliedStateVersion by remember { mutableStateOf(0L) }
    var receiverEpoch by remember { mutableStateOf(0) }

    LaunchedEffect(receiverEpoch) {
        val resolvedRelay = RelayDiscovery.resolve(
            configuredHost = configuredRelayHost,
            configuredPort = configuredRelayPort
        )

        if (resolvedRelay == null) {
            relayEndpoint = null
            udpStatus = "DISCOVERY: relay not found; retrying"
            delay(5_000)
            receiverEpoch++
            return@LaunchedEffect
        }

        relayEndpoint = resolvedRelay
        udpStatus = "RELAY: ${resolvedRelay.host}:${resolvedRelay.port}"

        val receiver = UdpPingReceiver(
            listenPort = receivePort,
            relayHost = resolvedRelay.host,
            relayPort = resolvedRelay.port,
            clientId = localSenderId
        )

        receiver.run(
            onMessage = { message ->
                withContext(Dispatchers.Main.immediate) {
                    when (message) {
                        is InboundMessage.PingCreate -> {
                            val epoch = message.stateEpoch
                            val version = message.stateVersion
                            if (epoch != null && version != null) {
                                if (
                                    epoch < appliedStateEpoch ||
                                    (epoch == appliedStateEpoch &&
                                        version <= appliedStateVersion)
                                ) {
                                    udpStatus = "RX: stale create $epoch:$version ignored"
                                    return@withContext
                                }
                                if (
                                    epoch > appliedStateEpoch ||
                                    version > appliedStateVersion + 1
                                ) {
                                    udpStatus =
                                        "RX: version gap ${appliedStateEpoch}:${appliedStateVersion}" +
                                            "->$epoch:$version; resync"
                                    snapshotAssembler.reset()
                                    receiverEpoch++
                                    return@withContext
                                }

                                appliedStateEpoch = epoch
                                appliedStateVersion = version
                            }

                            val ping = message.ping
                            val isLiveLocalPing =
                                ping.senderId == localSenderId &&
                                    pings.any { it.id == ping.id }

                            if (isLiveLocalPing) {
                                udpStatus = "RX: own ping ${ping.id} confirmed v${version ?: "-"}"
                                return@withContext
                            }

                            val existing = remotePings[ping.id]
                            if (existing == ping) {
                                if (version != null) appliedStateVersion = version
                                udpStatus = "RX: duplicate ${ping.id} ignored"
                                return@withContext
                            }

                            remotePings[ping.id] = ping

                            val cal = currentCalibrationState.value
                            val session = currentSessionState.value
                            if (cal == null || session == null) {
                                udpStatus = "RX: ping ${ping.id} pending calibration v${version ?: "-"}"
                                return@withContext
                            }

                            val projection = RemotePingProjector.project(ping, cal)
                            if (projection == null) {
                                udpStatus = "RX: frame mismatch b${ping.beaconId}"
                                return@withContext
                            }

                            retireRemoteAnchor(ping.id)
                            val local = projection.localPosition
                            remoteAnchors[ping.id] = registerRenderAnchor(
                                session.createAnchor(
                                    Pose.makeTranslation(local.x, local.y, local.z)
                                )
                            )
                            udpStatus = "RX: rendered ping ${ping.id} v${version ?: "-"}"
                        }

                        is InboundMessage.PingRemove -> {
                            val epoch = message.stateEpoch
                            val version = message.stateVersion
                            if (epoch != null && version != null) {
                                if (
                                    epoch < appliedStateEpoch ||
                                    (epoch == appliedStateEpoch &&
                                        version <= appliedStateVersion)
                                ) {
                                    udpStatus = "RX: stale remove $epoch:$version ignored"
                                    return@withContext
                                }
                                if (
                                    epoch > appliedStateEpoch ||
                                    version > appliedStateVersion + 1
                                ) {
                                    udpStatus =
                                        "RX: version gap ${appliedStateEpoch}:${appliedStateVersion}" +
                                            "->$epoch:$version; resync"
                                    snapshotAssembler.reset()
                                    receiverEpoch++
                                    return@withContext
                                }

                                appliedStateEpoch = epoch
                                appliedStateVersion = version
                            }

                            val isLiveLocalPing =
                                message.senderId == localSenderId &&
                                    pings.any { it.id == message.pingId }

                            if (isLiveLocalPing) {
                                udpStatus = "RX: own remove ${message.pingId} confirmed v${version ?: "-"}"
                                return@withContext
                            }

                            remotePings.remove(message.pingId)
                            retireRemoteAnchor(message.pingId)
                            udpStatus = "RX: removed ping ${message.pingId} v${version ?: "-"}"
                        }

                        is InboundMessage.SnapshotChunk -> {
                            val result = snapshotAssembler.add(
                                chunk = message,
                                minimumStateEpoch = appliedStateEpoch,
                                minimumStateVersion = appliedStateVersion
                            )

                            when (result.status) {
                                SnapshotAssemblyStatus.STALE -> {
                                    udpStatus = "RX: stale snapshot v${message.stateVersion} ignored"
                                    return@withContext
                                }
                                SnapshotAssemblyStatus.CONFLICT -> {
                                    udpStatus = "RX: snapshot conflict; resync"
                                    snapshotAssembler.reset()
                                    receiverEpoch++
                                    return@withContext
                                }
                                SnapshotAssemblyStatus.INCOMPLETE -> {
                                    udpStatus = "RX: snapshot ${message.chunkIndex + 1}/${message.chunkCount} v${message.stateVersion}"
                                    return@withContext
                                }
                                SnapshotAssemblyStatus.COMPLETE -> Unit
                            }

                            retireAllRemoteAnchors()
                            remotePings.clear()

                            val cal = currentCalibrationState.value
                            val session = currentSessionState.value
                            result.pings.orEmpty().forEach { ping ->
                                val isLiveLocalPing =
                                    ping.senderId == localSenderId &&
                                        pings.any { it.id == ping.id }
                                if (isLiveLocalPing) return@forEach

                                remotePings[ping.id] = ping
                                if (cal != null && session != null) {
                                    val projection = RemotePingProjector.project(ping, cal)
                                    if (projection != null) {
                                        val local = projection.localPosition
                                        remoteAnchors[ping.id] = registerRenderAnchor(
                                            session.createAnchor(
                                                Pose.makeTranslation(local.x, local.y, local.z)
                                            )
                                        )
                                    }
                                }
                            }

                            appliedStateEpoch =
                                result.stateEpoch ?: appliedStateEpoch
                            appliedStateVersion =
                                result.stateVersion ?: appliedStateVersion
                            udpStatus =
                                "RX: snapshot ${remotePings.size} ping(s) " +
                                    "e$appliedStateEpoch v$appliedStateVersion"
                        }
                    }
                }
            },
            onStatus = { status ->
                withContext(Dispatchers.Main.immediate) {
                    udpStatus = status
                }
            }
        )
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
                    text = "LYNX  •  PROTOTYPE 0.4",
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
                    text = "REMOTE  ${remotePings.size} ping(s)  " +
                        "${remotePings.keys.count { remoteAnchors.containsKey(it) }} rendered",
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

                        retireAllRemoteAnchors()

                        val session = latestSession
                        if (session != null) {
                            remotePings.values.forEach { ping ->
                                val projection = RemotePingProjector.project(ping, built)
                                    ?: return@forEach

                                val local = projection.localPosition
                                remoteAnchors[ping.id] = registerRenderAnchor(
                                    session.createAnchor(
                                        Pose.makeTranslation(local.x, local.y, local.z)
                                    )
                                )
                            }
                        }
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

                    val json = pingCreateToJson(ping)
                    lastPingJson = json

                    coroutineScope.launch {
                        val endpoint = relayEndpoint
                        if (endpoint == null) {
                            udpStatus = "UDP: relay unavailable"
                            return@launch
                        }

                        try {
                            UdpPingSender.send(
                                json = json,
                                host = endpoint.host,
                                port = endpoint.port
                            )

                            udpStatus = "UDP: shared ping sent"
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
                        val endpoint = relayEndpoint
                        if (endpoint == null) {
                            udpStatus = "UDP: relay unavailable"
                            return@launch
                        }

                        try {
                            pingsToRemove.forEach { ping ->
                                UdpPingSender.send(
                                    json = pingRemoveToJson(
                                        pingId = ping.id,
                                        senderId = ping.senderId
                                    ),
                                    host = endpoint.host,
                                    port = endpoint.port
                                )
                            }

                            udpStatus = "UDP: removed ${pingsToRemove.size}"
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