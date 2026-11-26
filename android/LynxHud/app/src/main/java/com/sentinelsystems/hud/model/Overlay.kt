package com.sentinelsystems.hud.model

data class SharedPoint(
    val x: Float,
    val y: Float,
    val z: Float
)

object OverlayKinds {
    const val MARKER = "MARKER"
    const val PATH = "PATH"
    const val ZONE = "ZONE"
}

object OverlayContract {
    const val MAX_GEOMETRY_POINTS = 16

    fun validateGeometry(
        kind: String,
        geometry: List<SharedPoint>
    ): Boolean {
        val minimum = when (kind) {
            OverlayKinds.MARKER -> 1
            OverlayKinds.PATH -> 2
            OverlayKinds.ZONE -> 3
            else -> return false
        }

        if (kind == OverlayKinds.MARKER && geometry.size != 1) return false
        if (geometry.size < minimum || geometry.size > MAX_GEOMETRY_POINTS) return false

        return geometry.all { point ->
            point.x.isFinite() &&
                point.y.isFinite() &&
                point.z.isFinite()
        }
    }
}

data class Overlay(
    val overlayId: Long,
    val ownerId: Long,
    val beaconId: Int,
    val kind: String,
    val revision: Long,
    val geometry: List<SharedPoint>,
    val createdAt: Long,
    val updatedAt: Long
)
