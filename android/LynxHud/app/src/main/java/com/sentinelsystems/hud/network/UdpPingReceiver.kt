package com.sentinelsystems.hud.network

import android.util.Log
import com.sentinelsystems.hud.protocol.InboundMessage
import com.sentinelsystems.hud.protocol.clientHeartbeatToJson
import com.sentinelsystems.hud.protocol.clientRegisterToJson
import com.sentinelsystems.hud.protocol.parseInboundMessage
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

            val buffer = ByteArray(maxPayloadBytes + 1)

            while (currentCoroutineContext().isActive) {
                if (System.currentTimeMillis() >= nextHeartbeatAt) {
                    sendJson(
                        socket = socket,
                        json = clientHeartbeatToJson(clientId),
                        address = relayAddress,
                        port = relayPort
                    )
                    nextHeartbeatAt =
                        System.currentTimeMillis() + heartbeatIntervalMs
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

                    onMessage(message)
                } catch (_: java.net.SocketTimeoutException) {
                    // Wake periodically for heartbeat/cancellation.
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