package com.sentinelsystems.hud.network

import android.util.Log
import com.sentinelsystems.hud.protocol.InboundMessage
import com.sentinelsystems.hud.protocol.clientHeartbeatToJson
import com.sentinelsystems.hud.protocol.clientRegisterToJson
import com.sentinelsystems.hud.protocol.parseInboundMessage
import com.sentinelsystems.hud.protocol.snapshotRequestToJson
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class UdpPingReceiver(
    private val listenPort: Int,
    private val relayHost: String,
    private val relayPort: Int,
    private val clientId: Long,
    private val heartbeatIntervalMs: Long = 5_000,
    private val snapshotTimeoutMs: Long = 3_000,
    private val maxPayloadBytes: Int = 4096
) {
    suspend fun run(
        onMessage: suspend (InboundMessage) -> Unit,
        onStatus: suspend (String) -> Unit
    ) = withContext(Dispatchers.IO) {
        val relayAddress = InetAddress.getByName(relayHost)

        DatagramSocket(listenPort).use { socket ->
            socket.soTimeout = 1000

            sendJson(
                socket = socket,
                json = clientRegisterToJson(clientId),
                address = relayAddress,
                port = relayPort
            )
            onStatus("RX: registered client $clientId")

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
                    sendJson(
                        socket = socket,
                        json = clientHeartbeatToJson(clientId),
                        address = relayAddress,
                        port = relayPort
                    )
                    nextHeartbeatAt = now + heartbeatIntervalMs
                }

                if (pendingSnapshotId != null && now >= snapshotDeadlineAt) {
                    sendJson(
                        socket = socket,
                        json = snapshotRequestToJson(clientId),
                        address = relayAddress,
                        port = relayPort
                    )
                    onStatus("RX: snapshot timeout; resync requested")
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

                    val message = try {
                        parseInboundMessage(json)
                    } catch (e: Exception) {
                        Log.w("LynxReceiver", "Bad inbound payload", e)
                        null
                    }

                    if (message == null) {
                        onStatus("RX: dropped malformed packet")
                        continue
                    }

                    if (message is InboundMessage.SnapshotChunk) {
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

                    onMessage(message)
                } catch (_: java.net.SocketTimeoutException) {
                    // Wake periodically for heartbeat/cancellation.
                } catch (e: java.net.SocketException) {
                    Log.w("LynxReceiver", "Transient UDP receive error", e)
                    onStatus("RX: transient network error; retrying")
                }
            }
        }
    }

    private fun sendJson(
        socket: DatagramSocket,
        json: String,
        address: InetAddress,
        port: Int
    ) {
        val bytes = json.toByteArray(Charsets.UTF_8)
        socket.send(
            DatagramPacket(bytes, bytes.size, address, port)
        )
    }
}