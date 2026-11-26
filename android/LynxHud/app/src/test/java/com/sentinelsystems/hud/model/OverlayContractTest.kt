package com.sentinelsystems.hud.model

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class OverlayContractTest {
    private fun points(count: Int) =
        List(count) { index -> SharedPoint(index.toFloat(), 0f, index.toFloat()) }

    @Test
    fun markerPathZoneCardinalityMatchesProtocolContract() {
        assertTrue(OverlayContract.validateGeometry(OverlayKinds.MARKER, points(1)))
        assertFalse(OverlayContract.validateGeometry(OverlayKinds.MARKER, points(2)))
        assertFalse(OverlayContract.validateGeometry(OverlayKinds.PATH, points(1)))
        assertTrue(OverlayContract.validateGeometry(OverlayKinds.PATH, points(2)))
        assertFalse(OverlayContract.validateGeometry(OverlayKinds.ZONE, points(2)))
        assertTrue(OverlayContract.validateGeometry(OverlayKinds.ZONE, points(3)))
    }

    @Test
    fun geometryOverSixteenPointsIsRejected() {
        assertTrue(OverlayContract.validateGeometry(OverlayKinds.PATH, points(16)))
        assertFalse(OverlayContract.validateGeometry(OverlayKinds.PATH, points(17)))
    }
}
