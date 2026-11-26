package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Overlay
import com.sentinelsystems.hud.model.OverlayKinds
import com.sentinelsystems.hud.model.SharedPoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AuthoritativeOverlayStateTest {
    private fun marker(id: Long, revision: Long = 1) = Overlay(
        overlayId = id,
        ownerId = 9,
        beaconId = 0,
        kind = OverlayKinds.MARKER,
        revision = revision,
        geometry = listOf(SharedPoint(id.toFloat(), 0f, id.toFloat())),
        createdAt = 1000 + id,
        updatedAt = 1000 + id + revision - 1
    )

    @Test
    fun snapshotThenContiguousDiffApplies() {
        val state = AuthoritativeOverlayState()
        val snapshot = OverlayInboundMessage.SnapshotChunk(
            snapshotId = 1,
            stateEpoch = 100,
            stateVersion = 1,
            chunkIndex = 0,
            chunkCount = 1,
            overlays = listOf(marker(1))
        )
        assertEquals(OverlayStateApplyStatus.SNAPSHOT_APPLIED, state.apply(snapshot).status)

        val diff = OverlayInboundMessage.Diff(
            operation = "UPSERT",
            overlayId = 2,
            revision = 1,
            overlay = marker(2),
            stateEpoch = 100,
            stateVersion = 2
        )
        assertEquals(OverlayStateApplyStatus.APPLIED, state.apply(diff).status)
        assertEquals(listOf(1L, 2L), state.snapshot().map { it.overlayId })
    }

    @Test
    fun removeTombstoneBlocksOlderRevision() {
        val state = AuthoritativeOverlayState()
        state.apply(
            OverlayInboundMessage.SnapshotChunk(
                1, 100, 1, 0, 1, listOf(marker(1, 1))
            )
        )
        state.apply(
            OverlayInboundMessage.Diff(
                "REMOVE", 1, 2, null, 100, 2
            )
        )
        assertTrue(state.snapshot().isEmpty())
        assertEquals(2, state.latestRevision(1))

        val delayed = state.apply(
            OverlayInboundMessage.Diff(
                "UPSERT", 1, 1, marker(1, 1), 100, 3
            )
        )
        assertEquals(OverlayStateApplyStatus.APPLIED, delayed.status)
        assertFalse(delayed.stateChanged)
        assertTrue(state.snapshot().isEmpty())
    }

    @Test
    fun globalVersionGapRequiresResync() {
        val state = AuthoritativeOverlayState()
        state.apply(OverlayInboundMessage.SnapshotChunk(1, 100, 1, 0, 1, emptyList()))
        val result = state.apply(
            OverlayInboundMessage.Diff(
                "UPSERT", 1, 1, marker(1), 100, 3
            )
        )
        assertEquals(OverlayStateApplyStatus.RESYNC_REQUIRED, result.status)
        assertEquals(1, state.appliedVersion)
    }
}
