package com.sentinelsystems.hud.protocol

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.sentinelsystems.hud.model.OverlayKinds
import com.sentinelsystems.hud.model.SharedPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OverlayProtocolInstrumentedTest {
    @Test
    fun sharedPathDiffFixtureParsesOnAndroid() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val json = context.assets.open("overlay-contract-v1.json")
            .bufferedReader()
            .use { it.readText() }

        val message = parseOverlayInbound(json) as OverlayInboundMessage.Diff
        assertEquals("UPSERT", message.operation)
        assertEquals(42L, message.overlayId)
        assertEquals(3L, message.revision)
        assertEquals(900L, message.stateEpoch)
        assertEquals(12L, message.stateVersion)
        assertNotNull(message.overlay)
        assertEquals(OverlayKinds.PATH, message.overlay!!.kind)
        assertEquals(3, message.overlay.geometry.size)
    }

    @Test
    fun createsMarkerPathAndZoneCommands() {
        val marker = overlayCreateToJson(
            1, 99, 0, OverlayKinds.MARKER,
            listOf(SharedPoint(1f, 0f, 2f)),
            1000
        )
        val path = overlayCreateToJson(
            2, 99, 0, OverlayKinds.PATH,
            listOf(
                SharedPoint(0f,0f,0f),
                SharedPoint(1f,0f,1f)
            ),
            1000
        )
        val zone = overlayCreateToJson(
            3, 99, 0, OverlayKinds.ZONE,
            listOf(
                SharedPoint(0f,0f,0f),
                SharedPoint(1f,0f,0f),
                SharedPoint(0f,0f,1f)
            ),
            1000
        )

        assertTrue(marker.contains("\"kind\":\"MARKER\""))
        assertTrue(path.contains("\"kind\":\"PATH\""))
        assertTrue(zone.contains("\"kind\":\"ZONE\""))
    }
}
