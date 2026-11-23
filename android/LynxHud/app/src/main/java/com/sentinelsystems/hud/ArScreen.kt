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
import com.sentinelsystems.hud.protocol.InboundMessage
import kotlinx.coroutines.launch
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext


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

    val localSenderId = 1L
    val pcHost = "192.168.0.241"
    val pcPort = 7777
    val receivePort = 7778

    val currentCalibrationState = rememberUpdatedState(calibration)
    val currentSessionState = rememberUpdatedState(latestSession)

    LaunchedEffect(Unit) {
        val receiver = UdpPingReceiver(receivePort)

        receiver.run(
            onMessage = { message ->
                withContext(Dispatchers.Main.immediate) {
                    when (message) {
                        is InboundMessage.PingCreate -> {
                            val ping = message.ping

                            if (ping.senderId == localSenderId) {
                                udpStatus = "RX: own ping ${ping.id} confirmed"
                                return@withContext
                            }

                            val existing = remotePings[ping.id]

                            if (existing == ping) {
                                udpStatus = "RX: duplicate ${ping.id} ignored"
                                return@withContext
                            }

                            remotePings[ping.id] = ping

                            val cal = currentCalibrationState.value
                            val session = currentSessionState.value

                            if (cal == null || session == null) {
                                udpStatus = "RX: ping ${ping.id} pending calibration"
                                return@withContext
                            }

                            val projection = RemotePingProjector.project(ping, cal)

                            if (projection == null) {
                                udpStatus = "RX: frame mismatch b${ping.beaconId}"
                                return@withContext
                            }

                            remoteAnchors.remove(ping.id)?.detach()

                            val local = projection.localPosition
                            val anchor = session.createAnchor(
                                Pose.makeTranslation(local.x, local.y, local.z)
                            )

                            remoteAnchors[ping.id] = anchor
                            udpStatus = "RX: rendered ping ${ping.id}"
                        }

                        is InboundMessage.PingRemove -> {
                            if (message.senderId == localSenderId) {
                                udpStatus = "RX: own remove ${message.pingId} confirmed"
                                return@withContext
                            }

                            remotePings.remove(message.pingId)
                            remoteAnchors.remove(message.pingId)?.detach()
                            udpStatus = "RX: removed ping ${message.pingId}"
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
            localAnchors.forEach { anchor ->
                AnchorNode(anchor = anchor) {
                    BillboardNode(
                        bitmap = pingBitmap,
                        widthMeters = 0.18f,
                        heightMeters = 0.18f
                    )
                }
            }

            remoteAnchors.values.forEach { anchor ->
                AnchorNode(anchor = anchor) {
                    BillboardNode(
                        bitmap = pingBitmap,
                        widthMeters = 0.18f,
                        heightMeters = 0.18f
                    )
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
                    text = "REMOTE  ${remotePings.size} ping(s)  ${remoteAnchors.size} rendered",
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

                        remoteAnchors.values.forEach { it.detach() }
                        remoteAnchors.clear()

                        val session = latestSession
                        if (session != null) {
                            remotePings.values.forEach { ping ->
                                val projection = RemotePingProjector.project(ping, built)
                                    ?: return@forEach

                                val local = projection.localPosition
                                remoteAnchors[ping.id] = session.createAnchor(
                                    Pose.makeTranslation(local.x, local.y, local.z)
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
                        session.createAnchor(
                            Pose.makeTranslation(
                                local.x,
                                local.y,
                                local.z
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

                    localAnchors.add(bestHit.createAnchor())

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
                        try {
                            UdpPingSender.send(
                                json = json,
                                host = pcHost,
                                port = pcPort
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

                    localAnchors.forEach { it.detach() }
                    localAnchors.clear()
                    pings.clear()
                    lastPing = null
                    lastPingJson = null

                    coroutineScope.launch {
                        try {
                            pingsToRemove.forEach { ping ->
                                UdpPingSender.send(
                                    json = pingRemoveToJson(
                                        pingId = ping.id,
                                        senderId = ping.senderId
                                    ),
                                    host = pcHost,
                                    port = pcPort
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