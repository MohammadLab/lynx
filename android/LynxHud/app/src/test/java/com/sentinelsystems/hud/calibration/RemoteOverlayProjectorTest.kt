package com.sentinelsystems.hud.calibration

import com.sentinelsystems.hud.model.Overlay
import com.sentinelsystems.hud.model.OverlayKinds
import com.sentinelsystems.hud.model.SharedPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class RemoteOverlayProjectorTest {
    private val calibration = Calibration(
        beaconId = 0,
        originLocal = Vec3(10f, 2f, -3f),
        xAxisLocal = Vec3(0f, 0f, 1f),
        yAxisLocal = Vec3(0f, 1f, 0f),
        zAxisLocal = Vec3(-1f, 0f, 0f)
    )

    @Test
    fun pathProjectsEveryPointInOrder() {
        val overlay = Overlay(
            1, 9, 0, OverlayKinds.PATH, 1,
            listOf(
                SharedPoint(0f, 0f, 0f),
                SharedPoint(1f, 0f, 2f),
                SharedPoint(2f, 0f, 3f)
            ),
            1000, 1000
        )

        val points = RemoteOverlayProjector.projectPoints(overlay, calibration)!!
        assertEquals(3, points.size)
        assertEquals(Vec3(10f, 2f, -3f), points[0])
        assertEquals(Vec3(8f, 2f, -2f), points[1])
        assertEquals(Vec3(7f, 2f, -1f), points[2])
    }

    @Test
    fun differentBeaconIsRejected() {
        val overlay = Overlay(
            1, 9, 4, OverlayKinds.ZONE, 1,
            listOf(
                SharedPoint(0f,0f,0f),
                SharedPoint(1f,0f,0f),
                SharedPoint(0f,0f,1f)
            ),
            1000,1000
        )
        assertNull(RemoteOverlayProjector.projectPoints(overlay, calibration))
    }
}
