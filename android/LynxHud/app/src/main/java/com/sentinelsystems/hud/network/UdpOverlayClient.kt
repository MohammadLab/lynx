package com.sentinelsystems.hud.network

import android.util.Log
import com.sentinelsystems.hud.protocol.OverlayInboundMessage
import com.sentinelsystems.hud.protocol.clientHeartbeatToJson
import com.sentinelsystems.hud.protocol.clientRegisterToJson
import com.sentinelsystems.hud.protocol.overlaySnapshotRequestToJson
import com.sentinelsystems.hud.protocol.parseOverlayInbound
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class UdpOverlayClient(
    private val listenPort: Int,
    private val relayHost: String,
    private val relayPort: Int,
    private val clientId: Long,
    private val heartbeatIntervalMs: Long = 5_000,
    private val snapshotTimeoutMs: Long = 3_000,
    private val maxPayloadBytes: Int = 4096
) {
    private val sendMutex = Mutex()
    private val socketLock = Any()
    private var activeSocket: DatagramSocket? = null
    private var relayAddress: InetAddress? = null
    private var lastOverlayEpoch = 0L
    private var lastLegacyEpochRequested = 0L

    suspend fun run(
        onMessage: suspend (OverlayInboundMessage) -> Unit,
        onStatus: suspend (String) -> Unit
    ) = withContext(Dispatchers.IO) {
        val address = InetAddress.getByName(relayHost)

        DatagramSocket(listenPort).use { socket ->
            socket.soTimeout = 1000
            synchronized(socketLock) {
                activeSocket = socket
                relayAddress = address
            }

            try {
                sendJsonInternal(socket, clientRegisterToJson(clientId), address)
                sendJsonInternal(socket, overlaySnapshotRequestToJson(clientId), address)
                onStatus("RX: overlay client registered $clientId")

                var nextHeartbeatAt =
                    System.currentTimeMillis() + heartbeatIntervalMs
                var pendingSnapshotId: Long? = null
                var pendingChunkCount = 0
                val receivedChunkIndexes = mutableSetOf<Int>()
                var snapshotDeadlineAt = 0L
                val buffer = ByteArray(maxPayloadBytes + 1)

                while (currentCoroutineContext().isActive) {
                    val now = System.currentTimeMillis()
                    if (now >= nextHeartbeatAt) {
                        sendJsonInternal(socket, clientHeartbeatToJson(clientId), address)
                        nextHeartbeatAt = now + heartbeatIntervalMs
                    }

                    if (pendingSnapshotId != null && now >= snapshotDeadlineAt) {
                        sendJsonInternal(socket, overlaySnapshotRequestToJson(clientId), address)
                        onStatus("RX: overlay snapshot timeout; resync requested")
                        pendingSnapshotId = null
                        pendingChunkCount = 0
                        receivedChunkIndexes.clear()
                    }

                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        socket.receive(packet)
                        if (packet.length > maxPayloadBytes) {
                            onStatus("RX: dropped oversize packet")
                            continue
                        }

                        val json = packet.data.decodeToString(
                            startIndex = packet.offset,
                            endIndex = packet.offset + packet.length
                        )
                        val type = try { JSONObject(json).optString("type") } catch (_: Exception) { "" }

                        if (type == "SNAPSHOT") {
                            val epoch = try { JSONObject(json).optLong("stateEpoch", 0) } catch (_: Exception) { 0 }
                            if (lastOverlayEpoch > 0 && epoch > lastOverlayEpoch && epoch > lastLegacyEpochRequested) {
                                lastLegacyEpochRequested = epoch
                                sendJsonInternal(socket, overlaySnapshotRequestToJson(clientId), address)
                                onStatus("RX: relay epoch signal; overlay resync requested")
                            }
                            continue
                        }

                        val message = try { parseOverlayInbound(json) } catch (e: Exception) {
                            Log.w("LynxOverlayClient", "Bad overlay payload", e)
                            null
                        } ?: continue

                        when (message) {
                            is OverlayInboundMessage.Diff -> {
                                lastOverlayEpoch = maxOf(lastOverlayEpoch, message.stateEpoch)
                            }
                            is OverlayInboundMessage.SnapshotChunk -> {
                                lastOverlayEpoch = maxOf(lastOverlayEpoch, message.stateEpoch)
                                if (pendingSnapshotId != message.snapshotId) {
                                    pendingSnapshotId = message.snapshotId
                                    pendingChunkCount = message.chunkCount
                                    receivedChunkIndexes.clear()
                                    snapshotDeadlineAt =
                                        System.currentTimeMillis() + snapshotTimeoutMs
                                }
                                receivedChunkIndexes.add(message.chunkIndex)
                                if (receivedChunkIndexes.size >= pendingChunkCount) {
                                    pendingSnapshotId = null
                                    pendingChunkCount = 0
                                    receivedChunkIndexes.clear()
                                }
                            }
                        }

                        onMessage(message)
                    } catch (_: java.net.SocketTimeoutException) {
                        // Wake periodically for heartbeat/cancellation.
                    } catch (e: java.net.SocketException) {
                        Log.w("LynxOverlayClient", "Transient UDP receive error", e)
                        onStatus("RX: transient network error; retrying")
                    }
                }
            } finally {
                synchronized(socketLock) {
                    if (activeSocket === socket) {
                        activeSocket = null
                        relayAddress = null
                    }
                }
            }
        }
    }

    suspend fun sendCommand(json: String) = withContext(Dispatchers.IO) {
        val socket: DatagramSocket
        val address: InetAddress
        synchronized(socketLock) {
            socket = activeSocket
                ?: throw IllegalStateException("Overlay client is not connected")
            address = relayAddress
                ?: throw IllegalStateException("Relay address is unavailable")
        }
        sendMutex.withLock {
            sendJsonInternal(socket, json, address)
        }
    }

    suspend fun requestSnapshot() =
        sendCommand(overlaySnapshotRequestToJson(clientId))

    private fun sendJsonInternal(
        socket: DatagramSocket,
        json: String,
        address: InetAddress
    ) {
        val bytes = json.toByteArray(Charsets.UTF_8)
        socket.send(DatagramPacket(bytes, bytes.size, address, relayPort))
    }
}
