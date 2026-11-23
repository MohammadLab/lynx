package com.sentinelsystems.hud.protocol

import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
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
    fun rejectsUnknownType() {
        assertNull(parseInboundMessage("""{"type":"NOPE"}"""))
    }
}