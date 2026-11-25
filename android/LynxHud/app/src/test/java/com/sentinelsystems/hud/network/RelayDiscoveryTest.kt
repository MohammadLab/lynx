package com.sentinelsystems.hud.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.net.InetAddress

class RelayDiscoveryTest {
    @Test
    fun parsesValidRelayAnnouncement() {
        val endpoint = RelayDiscovery.parseResponse(
            "LYNX_RELAY_V1|7777",
            InetAddress.getByName("192.168.0.5")
        )

        requireNotNull(endpoint)
        assertEquals("192.168.0.5", endpoint.host)
        assertEquals(7777, endpoint.port)
    }

    @Test
    fun rejectsInvalidRelayAnnouncement() {
        assertNull(
            RelayDiscovery.parseResponse(
                "LYNX_RELAY_V1|70000",
                InetAddress.getLoopbackAddress()
            )
        )
    }
}