package com.sentinelsystems.hud.network

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface

data class RelayEndpoint(
    val host: String,
    val port: Int
)

object RelayDiscovery {
    private const val REQUEST = "LYNX_DISCOVER_V1"
    private const val RESPONSE_PREFIX = "LYNX_RELAY_V1|"

    suspend fun resolve(
        configuredHost: String,
        configuredPort: Int,
        timeoutMs: Int = 1_500
    ): RelayEndpoint? = withContext(Dispatchers.IO) {
        if (!configuredHost.equals("auto", ignoreCase = true)) {
            return@withContext RelayEndpoint(
                host = configuredHost,
                port = configuredPort
            )
        }

        discover(
            relayPort = configuredPort,
            timeoutMs = timeoutMs
        )
    }

    internal fun parseResponse(
        payload: String,
        sourceAddress: InetAddress
    ): RelayEndpoint? {
        if (!payload.startsWith(RESPONSE_PREFIX)) return null

        val port = payload
            .removePrefix(RESPONSE_PREFIX)
            .toIntOrNull()
            ?.takeIf { it in 1..65535 }
            ?: return null

        return RelayEndpoint(
            host = sourceAddress.hostAddress ?: return null,
            port = port
        )
    }

    private fun discover(
        relayPort: Int,
        timeoutMs: Int
    ): RelayEndpoint? {
        val request = REQUEST.toByteArray(Charsets.UTF_8)

        DatagramSocket().use { socket ->
            socket.broadcast = true

            for (target in broadcastTargets()) {
                sendProbe(socket, request, target, relayPort)
            }

            receiveResponse(socket, timeoutMs = 300)?.let {
                return it
            }

            for (target in boundedLocalSubnetTargets()) {
                sendProbe(socket, request, target, relayPort)
            }

            return receiveResponse(
                socket,
                timeoutMs = (timeoutMs - 300).coerceAtLeast(300)
            )
        }
    }

    private fun sendProbe(
        socket: DatagramSocket,
        request: ByteArray,
        target: InetAddress,
        relayPort: Int
    ) {
        try {
            socket.send(
                DatagramPacket(
                    request,
                    request.size,
                    target,
                    relayPort
                )
            )
        } catch (_: Exception) {
            // Discovery is best effort. Other targets may still succeed.
        }
    }

    private fun receiveResponse(
        socket: DatagramSocket,
        timeoutMs: Int
    ): RelayEndpoint? {
        socket.soTimeout = timeoutMs
        val buffer = ByteArray(256)
        val packet = DatagramPacket(buffer, buffer.size)

        return try {
            socket.receive(packet)
            val payload = packet.data.decodeToString(
                startIndex = packet.offset,
                endIndex = packet.offset + packet.length
            )
            parseResponse(payload, packet.address)
        } catch (_: java.net.SocketTimeoutException) {
            null
        }
    }

    private fun broadcastTargets(): Set<InetAddress> {
        val targets = linkedSetOf<InetAddress>()
        targets.add(InetAddress.getByName("255.255.255.255"))

        val interfaces = NetworkInterface.getNetworkInterfaces()
            ?: return targets

        while (interfaces.hasMoreElements()) {
            val networkInterface = interfaces.nextElement()
            if (!networkInterface.isUp || networkInterface.isLoopback) continue

            for (interfaceAddress in networkInterface.interfaceAddresses) {
                interfaceAddress.broadcast?.let(targets::add)
            }
        }

        return targets
    }

    internal fun boundedLocalSubnetTargets(): Set<InetAddress> {
        val targets = linkedSetOf<InetAddress>()
        val interfaces = NetworkInterface.getNetworkInterfaces()
            ?: return targets

        while (interfaces.hasMoreElements()) {
            val networkInterface = interfaces.nextElement()
            if (!networkInterface.isUp || networkInterface.isLoopback) continue

            for (interfaceAddress in networkInterface.interfaceAddresses) {
                val address = interfaceAddress.address as? Inet4Address
                    ?: continue
                val bytes = address.address
                val ownLastOctet = bytes[3].toInt() and 0xff

                // Keep discovery bounded even on a /16 or larger network.
                for (lastOctet in 1..254) {
                    if (lastOctet == ownLastOctet) continue
                    targets.add(
                        InetAddress.getByAddress(
                            byteArrayOf(
                                bytes[0],
                                bytes[1],
                                bytes[2],
                                lastOctet.toByte()
                            )
                        )
                    )
                }
            }
        }

        return targets
    }
}