package com.sentinelsystems.hud.calibration

import org.junit.Assert.assertEquals
import org.junit.Test

class CalibrationModelsTest {

    private fun assertVec(
        expected: Vec3,
        actual: Vec3,
        epsilon: Float = 0.0001f
    ) {
        assertEquals(expected.x, actual.x, epsilon)
        assertEquals(expected.y, actual.y, epsilon)
        assertEquals(expected.z, actual.z, epsilon)
    }

    @Test
    fun identityAlignedMarkerRoundTripsPoint() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(-1f, 1f, 0f),
            topRight = Vec3(1f, 1f, 0f),
            bottomRight = Vec3(1f, -1f, 0f),
            bottomLeft = Vec3(-1f, -1f, 0f)
        )

        val local = Vec3(0.4f, -0.2f, 1.7f)
        val shared = calibration.localToShared(local)
        val roundTrip = calibration.sharedToLocal(shared)

        assertVec(local, roundTrip)
    }

    @Test
    fun translatedMarkerUsesBeaconCenterAsSharedOrigin() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(9f, 6f, 2f),
            topRight = Vec3(11f, 6f, 2f),
            bottomRight = Vec3(11f, 4f, 2f),
            bottomLeft = Vec3(9f, 4f, 2f)
        )

        val sharedOrigin = calibration.localToShared(Vec3(10f, 5f, 2f))

        assertVec(Vec3(0f, 0f, 0f), sharedOrigin)
    }

    @Test
    fun sharedToLocalUsesMarkerAxes() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(2f, 1f, 1f),
            topRight = Vec3(2f, 1f, -1f),
            bottomRight = Vec3(2f, -1f, -1f),
            bottomLeft = Vec3(2f, -1f, 1f)
        )

        val local = calibration.sharedToLocal(Vec3(1f, 0f, 0f))

        assertVec(Vec3(2f, 0f, -1f), local)
    }

    @Test
    fun twoDifferentLocalFramesResolveSameSharedPoint() {
        val deviceA = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(-1f, 1f, 0f),
            topRight = Vec3(1f, 1f, 0f),
            bottomRight = Vec3(1f, -1f, 0f),
            bottomLeft = Vec3(-1f, -1f, 0f)
        )

        val deviceB = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(5f, 3f, 3f),
            topRight = Vec3(5f, 3f, 1f),
            bottomRight = Vec3(5f, 1f, 1f),
            bottomLeft = Vec3(5f, 1f, 3f)
        )

        val sharedPoint = Vec3(0.35f, -0.20f, 1.40f)

        val deviceALocal = deviceA.sharedToLocal(sharedPoint)
        val deviceBLocal = deviceB.sharedToLocal(sharedPoint)

        assertVec(
            sharedPoint,
            deviceA.localToShared(deviceALocal)
        )

        assertVec(
            sharedPoint,
            deviceB.localToShared(deviceBLocal)
        )
    }
    @Test
    fun markerCornerOrderDefinesExpectedAxes() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(-1f, 1f, 0f),
            topRight = Vec3(1f, 1f, 0f),
            bottomRight = Vec3(1f, -1f, 0f),
            bottomLeft = Vec3(-1f, -1f, 0f)
        )

        assertVec(Vec3(1f, 0f, 0f), calibration.xAxisLocal)
        assertVec(Vec3(0f, 1f, 0f), calibration.yAxisLocal)
        assertVec(Vec3(0f, 0f, 1f), calibration.zAxisLocal)
    }

    @Test
    fun rotatedMarkerKeepsRightHandedFrame() {
        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = 0,
            topLeft = Vec3(2f, 1f, 1f),
            topRight = Vec3(2f, 1f, -1f),
            bottomRight = Vec3(2f, -1f, -1f),
            bottomLeft = Vec3(2f, -1f, 1f)
        )

        assertVec(Vec3(0f, 0f, -1f), calibration.xAxisLocal)
        assertVec(Vec3(0f, 1f, 0f), calibration.yAxisLocal)
        assertVec(Vec3(1f, 0f, 0f), calibration.zAxisLocal)

        val handedness = calibration.xAxisLocal
            .cross(calibration.yAxisLocal)

        assertVec(calibration.zAxisLocal, handedness)
    }
}
