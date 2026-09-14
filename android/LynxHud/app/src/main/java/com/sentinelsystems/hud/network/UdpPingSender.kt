package com.sentinelsystems.hud.network

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress


object UdpPingSender {

    suspend fun send(
        json: String,
        host: String,
        port: Int
    ) = withContext(Dispatchers.IO) {

        val bytes = json.toByteArray(Charsets.UTF_8)
        val address = InetAddress.getByName(host)

        DatagramSocket().use { socket ->
            val packet = DatagramPacket(
                bytes,
                bytes.size,
                address,
                port
            )

            socket.send(packet)
        }
    }
}