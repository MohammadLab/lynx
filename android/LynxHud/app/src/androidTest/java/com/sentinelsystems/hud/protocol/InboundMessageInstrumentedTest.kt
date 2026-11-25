package com.sentinelsystems.hud.protocol

import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class InboundMessageInstrumentedTest {
    @Test
    fun parsesPingCreate() {
        val json = """
            {"type":"PING_CREATE","ping":{"id":123,"senderId":9,"beaconId":0,"x":0.5,"y":-0.25,"z":1.5,"createdAt":456}}
        """.trimIndent()

        val message = parseInboundMessage(json)
        require(message is InboundMessage.PingCreate)

        assertEquals(123L, message.ping.id)
        assertEquals(9L, message.ping.senderId)
        assertEquals(0, message.ping.beaconId)
        assertEquals(0.5f, message.ping.x, 0.0001f)
        assertEquals(-0.25f, message.ping.y, 0.0001f)
        assertEquals(1.5f, message.ping.z, 0.0001f)
        assertEquals(456L, message.ping.createdAt)
    }

    @Test
    fun parsesPingRemove() {
        val message = parseInboundMessage(
            """{"type":"PING_REMOVE","pingId":123,"senderId":9}"""
        )

        require(message is InboundMessage.PingRemove)
        assertEquals(123L, message.pingId)
        assertEquals(9L, message.senderId)
    }

    @Test
    fun parsesSnapshot() {
        val message = parseInboundMessage(
            """{"type":"SNAPSHOT","pings":[{"id":1,"senderId":7,"beaconId":0,"x":1.0,"y":2.0,"z":3.0,"createdAt":1000},{"id":2,"senderId":8,"beaconId":0,"x":4.0,"y":5.0,"z":6.0,"createdAt":2000}]}"""
        )

        require(message is InboundMessage.Snapshot)
        assertEquals(2, message.pings.size)
        assertEquals(1L, message.pings[0].id)
        assertEquals(2L, message.pings[1].id)
    }

    @Test
    fun createsRegisterAndHeartbeatJson() {
        val register = clientRegisterToJson(1L)
        val heartbeat = clientHeartbeatToJson(1L)

        assertTrue(register.contains("\"type\":\"CLIENT_REGISTER\""))
        assertTrue(register.contains("\"clientId\":1"))
        assertTrue(heartbeat.contains("\"type\":\"CLIENT_HEARTBEAT\""))
        assertTrue(heartbeat.contains("\"clientId\":1"))
    }

    @Test
    fun rejectsUnknownType() {
        assertNull(parseInboundMessage("""{"type":"NOPE"}"""))
    }
}