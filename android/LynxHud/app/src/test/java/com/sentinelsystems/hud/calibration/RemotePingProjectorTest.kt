package com.sentinelsystems.hud.calibration

import com.sentinelsystems.hud.model.Ping
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class RemotePingProjectorTest {
    @Test
    fun projectsSharedPingIntoReceiverLocalFrame() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(5f, 3f, 3f),
            topRight = Vec3(5f, 3f, 1f),
            bottomRight = Vec3(5f, 1f, 1f),
            bottomLeft = Vec3(5f, 1f, 3f)
        )

        val ping = Ping(
            id = 42L,
            senderId = 7L,
            beaconId = 0,
            x = 0.35f,
            y = -0.20f,
            z = 1.40f,
            createdAt = 1000L
        )

        val projected = RemotePingProjector.project(ping, calibration)
        requireNotNull(projected)

        val expected = calibration.sharedToLocal(
            Vec3(0.35f, -0.20f, 1.40f)
        )

        assertEquals(expected.x, projected.localPosition.x, 0.0001f)
        assertEquals(expected.y, projected.localPosition.y, 0.0001f)
        assertEquals(expected.z, projected.localPosition.z, 0.0001f)
    }

    @Test
    fun rejectsPingFromDifferentBeaconFrame() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(-1f, 1f, 0f),
            topRight = Vec3(1f, 1f, 0f),
            bottomRight = Vec3(1f, -1f, 0f),
            bottomLeft = Vec3(-1f, -1f, 0f)
        )

        val ping = Ping(
            id = 99L,
            senderId = 1L,
            beaconId = 9,
            x = 0f,
            y = 0f,
            z = 0f,
            createdAt = 1000L
        )

        assertNull(RemotePingProjector.project(ping, calibration))
    }
}