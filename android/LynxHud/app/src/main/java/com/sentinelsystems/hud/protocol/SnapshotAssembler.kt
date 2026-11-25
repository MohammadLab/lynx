package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping

enum class SnapshotAssemblyStatus {
    INCOMPLETE,
    COMPLETE,
    STALE,
    CONFLICT
}

data class SnapshotAssemblyResult(
    val status: SnapshotAssemblyStatus,
    val stateEpoch: Long? = null,
    val stateVersion: Long? = null,
    val pings: List<Ping>? = null
)

class SnapshotAssembler {
    private var snapshotId: Long? = null
    private var stateEpoch: Long = 0
    private var stateVersion: Long = 0
    private var chunkCount: Int = 0
    private val chunks = mutableMapOf<Int, List<Ping>>()

    fun add(
        chunk: InboundMessage.SnapshotChunk,
        minimumStateEpoch: Long,
        minimumStateVersion: Long
    ): SnapshotAssemblyResult {
        if (
            chunk.stateEpoch < minimumStateEpoch ||
            (chunk.stateEpoch == minimumStateEpoch &&
                chunk.stateVersion < minimumStateVersion)
        ) {
            return SnapshotAssemblyResult(
                SnapshotAssemblyStatus.STALE,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        val currentId = snapshotId
        if (
            currentId == null ||
            chunk.stateEpoch > stateEpoch ||
            (chunk.stateEpoch == stateEpoch &&
                chunk.stateVersion > stateVersion) ||
            (chunk.stateEpoch == stateEpoch &&
                chunk.stateVersion == stateVersion &&
                chunk.snapshotId > currentId)
        ) {
            start(chunk)
        } else if (
            chunk.stateEpoch != stateEpoch ||
            chunk.snapshotId != snapshotId ||
            chunk.stateVersion != stateVersion
        ) {
            return SnapshotAssemblyResult(
                SnapshotAssemblyStatus.STALE,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        if (chunk.chunkCount != chunkCount) {
            return SnapshotAssemblyResult(
                SnapshotAssemblyStatus.CONFLICT,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        val existing = chunks[chunk.chunkIndex]
        if (existing != null && existing != chunk.pings) {
            return SnapshotAssemblyResult(
                SnapshotAssemblyStatus.CONFLICT,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        chunks[chunk.chunkIndex] = chunk.pings

        if (chunks.size != chunkCount) {
            return SnapshotAssemblyResult(
                SnapshotAssemblyStatus.INCOMPLETE,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        val combined = buildList {
            for (index in 0 until chunkCount) {
                addAll(chunks[index] ?: return SnapshotAssemblyResult(
                    SnapshotAssemblyStatus.INCOMPLETE,
                    chunk.stateEpoch,
                    chunk.stateVersion
                ))
            }
        }

        val completedEpoch = stateEpoch
        val completedVersion = stateVersion
        reset()
        return SnapshotAssemblyResult(
            SnapshotAssemblyStatus.COMPLETE,
            completedEpoch,
            completedVersion,
            combined
        )
    }

    fun reset() {
        snapshotId = null
        stateEpoch = 0
        stateVersion = 0
        chunkCount = 0
        chunks.clear()
    }

    private fun start(chunk: InboundMessage.SnapshotChunk) {
        snapshotId = chunk.snapshotId
        stateEpoch = chunk.stateEpoch
        stateVersion = chunk.stateVersion
        chunkCount = chunk.chunkCount
        chunks.clear()
    }
}