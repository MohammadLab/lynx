package com.sentinelsystems.hud.network

import android.util.Log
import com.sentinelsystems.hud.protocol.InboundMessage
import com.sentinelsystems.hud.protocol.parseInboundMessage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket

class UdpPingReceiver(
    private val listenPort: Int,
    private val maxPayloadBytes: Int = 4096
) {
    suspend fun run(
        onMessage: suspend (InboundMessage) -> Unit,
        onStatus: suspend (String) -> Unit
    ) = withContext(Dispatchers.IO) {
        DatagramSocket(listenPort).use { socket ->
            socket.soTimeout = 1000
            onStatus("RX: listening :$listenPort")

            val buffer = ByteArray(maxPayloadBytes + 1)

            while (currentCoroutineContext().isActive) {
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
                    // Periodic wake-up so coroutine cancellation is observed.
                }
            }
        }
    }
}