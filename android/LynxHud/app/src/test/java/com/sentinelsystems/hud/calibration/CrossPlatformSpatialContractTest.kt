package com.sentinelsystems.hud.calibration

import com.sentinelsystems.hud.model.Ping
import org.junit.Assert.assertEquals
import org.junit.Test
import java.util.Properties

class CrossPlatformSpatialContractTest {
    @Test
    fun sharedPointProjectsToExpectedAndroidLocalCoordinate() {
        val values = loadFixture()
        val shared = parseVec3(values.getProperty("shared.expected"))

        val calibration = Calibration(
            beaconId = values.getProperty("beacon.id").toInt(),
            originLocal = parseVec3(values.getProperty("android.origin")),
            xAxisLocal = parseVec3(values.getProperty("android.xaxis")),
            yAxisLocal = parseVec3(values.getProperty("android.yaxis")),
            zAxisLocal = parseVec3(values.getProperty("android.zaxis"))
        )

        val projection = RemotePingProjector.project(
            ping = Ping(
                id = 9001L,
                senderId = 77L,
                beaconId = calibration.beaconId,
                x = shared.x,
                y = shared.y,
                z = shared.z,
                createdAt = 1L
            ),
            calibration = calibration
        )

        requireNotNull(projection)
        val expected = parseVec3(values.getProperty("android.local.expected"))

        assertEquals(expected.x, projection.localPosition.x, 1e-5f)
        assertEquals(expected.y, projection.localPosition.y, 1e-5f)
        assertEquals(expected.z, projection.localPosition.z, 1e-5f)
    }

    private fun loadFixture(): Properties {
        val properties = Properties()
        val stream = javaClass.classLoader
            ?.getResourceAsStream("spatial-contract-v1.properties")
            ?: error("spatial contract fixture missing")

        stream.use(properties::load)
        return properties
    }

    private fun parseVec3(value: String): Vec3 {
        val parts = value.split(',')
        return Vec3(
            parts[0].toFloat(),
            parts[1].toFloat(),
            parts[2].toFloat()
        )
    }
}
