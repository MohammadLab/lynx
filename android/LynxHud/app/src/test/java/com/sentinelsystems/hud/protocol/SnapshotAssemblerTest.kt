package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping
import org.junit.Assert.assertEquals
import org.junit.Test

class SnapshotAssemblerTest {
    private fun ping(id: Long) = Ping(
        id = id,
        senderId = 9,
        beaconId = 0,
        x = id.toFloat(),
        y = 0f,
        z = 0f,
        createdAt = 1000 + id
    )

    @Test
    fun assemblesOutOfOrderChunksAndIgnoresDuplicateChunk() {
        val assembler = SnapshotAssembler()
        val chunk1 = InboundMessage.SnapshotChunk(10, 100, 5, 1, 2, listOf(ping(2)))
        val chunk0 = InboundMessage.SnapshotChunk(10, 100, 5, 0, 2, listOf(ping(1)))

        assertEquals(
            SnapshotAssemblyStatus.INCOMPLETE,
            assembler.add(chunk1, 0, 0).status
        )
        assertEquals(
            SnapshotAssemblyStatus.INCOMPLETE,
            assembler.add(chunk1, 0, 0).status
        )
        val complete = assembler.add(chunk0, 0, 0)
        assertEquals(SnapshotAssemblyStatus.COMPLETE, complete.status)
        assertEquals(100L, complete.stateEpoch)
        assertEquals(5L, complete.stateVersion)
        assertEquals(listOf(1L, 2L), complete.pings!!.map { it.id })
    }

    @Test
    fun newerEpochCanResetVersion() {
        val assembler = SnapshotAssembler()
        val chunk = InboundMessage.SnapshotChunk(11, 101, 0, 0, 1, emptyList())
        val result = assembler.add(chunk, 100, 35)

        assertEquals(SnapshotAssemblyStatus.COMPLETE, result.status)
        assertEquals(101L, result.stateEpoch)
        assertEquals(0L, result.stateVersion)
    }

    @Test
    fun olderEpochIsStaleEvenWithHigherVersion() {
        val assembler = SnapshotAssembler()
        val chunk = InboundMessage.SnapshotChunk(12, 99, 999, 0, 1, listOf(ping(1)))
        assertEquals(
            SnapshotAssemblyStatus.STALE,
            assembler.add(chunk, 100, 1).status
        )
    }
}