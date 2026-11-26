package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Overlay

enum class OverlaySnapshotAssemblyStatus {
    INCOMPLETE, COMPLETE, STALE, CONFLICT
}

data class OverlaySnapshotAssemblyResult(
    val status: OverlaySnapshotAssemblyStatus,
    val stateEpoch: Long? = null,
    val stateVersion: Long? = null,
    val overlays: List<Overlay>? = null
)

class OverlaySnapshotAssembler {
    private var snapshotId: Long? = null
    private var stateEpoch = 0L
    private var stateVersion = 0L
    private var chunkCount = 0
    private val chunks = mutableMapOf<Int, List<Overlay>>()

    fun add(
        chunk: OverlayInboundMessage.SnapshotChunk,
        minimumStateEpoch: Long,
        minimumStateVersion: Long
    ): OverlaySnapshotAssemblyResult {
        if (
            chunk.stateEpoch < minimumStateEpoch ||
            (chunk.stateEpoch == minimumStateEpoch && chunk.stateVersion < minimumStateVersion)
        ) {
            return OverlaySnapshotAssemblyResult(
                OverlaySnapshotAssemblyStatus.STALE,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        val currentId = snapshotId
        if (
            currentId == null ||
            chunk.stateEpoch > stateEpoch ||
            (chunk.stateEpoch == stateEpoch && chunk.stateVersion > stateVersion) ||
            (chunk.stateEpoch == stateEpoch &&
                chunk.stateVersion == stateVersion &&
                chunk.snapshotId > currentId)
        ) {
            snapshotId = chunk.snapshotId
            stateEpoch = chunk.stateEpoch
            stateVersion = chunk.stateVersion
            chunkCount = chunk.chunkCount
            chunks.clear()
        } else if (
            chunk.stateEpoch != stateEpoch ||
            chunk.stateVersion != stateVersion ||
            chunk.snapshotId != snapshotId
        ) {
            return OverlaySnapshotAssemblyResult(
                OverlaySnapshotAssemblyStatus.STALE,
                chunk.stateEpoch,
                chunk.stateVersion
            )
        }

        if (chunk.chunkCount != chunkCount)
            return OverlaySnapshotAssemblyResult(
                OverlaySnapshotAssemblyStatus.CONFLICT,
                chunk.stateEpoch,
                chunk.stateVersion
            )

        val existing = chunks[chunk.chunkIndex]
        if (existing != null && existing != chunk.overlays)
            return OverlaySnapshotAssemblyResult(
                OverlaySnapshotAssemblyStatus.CONFLICT,
                chunk.stateEpoch,
                chunk.stateVersion
            )

        chunks[chunk.chunkIndex] = chunk.overlays
        if (chunks.size != chunkCount)
            return OverlaySnapshotAssemblyResult(
                OverlaySnapshotAssemblyStatus.INCOMPLETE,
                chunk.stateEpoch,
                chunk.stateVersion
            )

        val combined = buildList {
            for (index in 0 until chunkCount) {
                addAll(chunks[index] ?: return OverlaySnapshotAssemblyResult(
                    OverlaySnapshotAssemblyStatus.INCOMPLETE,
                    chunk.stateEpoch,
                    chunk.stateVersion
                ))
            }
        }

        val epoch = stateEpoch
        val version = stateVersion
        reset()
        return OverlaySnapshotAssemblyResult(
            OverlaySnapshotAssemblyStatus.COMPLETE,
            epoch,
            version,
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
}

enum class OverlayStateApplyStatus {
    APPLIED, SNAPSHOT_INCOMPLETE, SNAPSHOT_APPLIED, STALE_IGNORED, RESYNC_REQUIRED, IGNORED
}

data class OverlayStateApplyResult(
    val status: OverlayStateApplyStatus,
    val stateChanged: Boolean = false
)

class AuthoritativeOverlayState {
    private val overlays = mutableMapOf<Long, Overlay>()
    private val latestRevision = mutableMapOf<Long, Long>()
    private val snapshotAssembler = OverlaySnapshotAssembler()

    var appliedEpoch: Long = 0
        private set
    var appliedVersion: Long = 0
        private set

    fun snapshot(): List<Overlay> = overlays.values.sortedBy { it.overlayId }
    fun get(overlayId: Long): Overlay? = overlays[overlayId]
    fun latestRevision(overlayId: Long): Long = latestRevision[overlayId] ?: 0
    fun resetPendingSnapshot() = snapshotAssembler.reset()

    fun apply(message: OverlayInboundMessage): OverlayStateApplyResult = when (message) {
        is OverlayInboundMessage.Diff -> applyDiff(message)
        is OverlayInboundMessage.SnapshotChunk -> applySnapshot(message)
    }

    private fun applyDiff(message: OverlayInboundMessage.Diff): OverlayStateApplyResult {
        if (
            message.stateEpoch < appliedEpoch ||
            (message.stateEpoch == appliedEpoch && message.stateVersion <= appliedVersion)
        ) return OverlayStateApplyResult(OverlayStateApplyStatus.STALE_IGNORED)

        if (
            appliedEpoch == 0L ||
            message.stateEpoch > appliedEpoch ||
            message.stateVersion != appliedVersion + 1
        ) return OverlayStateApplyResult(OverlayStateApplyStatus.RESYNC_REQUIRED)

        val knownRevision = latestRevision(message.overlayId)
        var changed = false
        if (message.revision > knownRevision) {
            when (message.operation) {
                "UPSERT" -> {
                    val overlay = message.overlay
                        ?: return OverlayStateApplyResult(OverlayStateApplyStatus.RESYNC_REQUIRED)
                    overlays[message.overlayId] = overlay
                    changed = true
                }
                "REMOVE" -> changed = overlays.remove(message.overlayId) != null
                else -> return OverlayStateApplyResult(OverlayStateApplyStatus.RESYNC_REQUIRED)
            }
            latestRevision[message.overlayId] = message.revision
        }

        appliedEpoch = message.stateEpoch
        appliedVersion = message.stateVersion
        return OverlayStateApplyResult(OverlayStateApplyStatus.APPLIED, changed)
    }

    private fun applySnapshot(
        message: OverlayInboundMessage.SnapshotChunk
    ): OverlayStateApplyResult {
        val result = snapshotAssembler.add(
            message, appliedEpoch, appliedVersion
        )

        return when (result.status) {
            OverlaySnapshotAssemblyStatus.STALE ->
                OverlayStateApplyResult(OverlayStateApplyStatus.STALE_IGNORED)
            OverlaySnapshotAssemblyStatus.CONFLICT ->
                OverlayStateApplyResult(OverlayStateApplyStatus.RESYNC_REQUIRED)
            OverlaySnapshotAssemblyStatus.INCOMPLETE ->
                OverlayStateApplyResult(OverlayStateApplyStatus.SNAPSHOT_INCOMPLETE)
            OverlaySnapshotAssemblyStatus.COMPLETE -> {
                val next = result.overlays.orEmpty().associateBy { it.overlayId }
                val changed = next != overlays
                overlays.clear()
                overlays.putAll(next)
                latestRevision.clear()
                next.values.forEach { latestRevision[it.overlayId] = it.revision }
                appliedEpoch = result.stateEpoch ?: appliedEpoch
                appliedVersion = result.stateVersion ?: appliedVersion
                OverlayStateApplyResult(
                    OverlayStateApplyStatus.SNAPSHOT_APPLIED,
                    changed
                )
            }
        }
    }
}
