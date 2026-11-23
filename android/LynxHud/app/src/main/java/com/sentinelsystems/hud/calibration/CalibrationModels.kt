package com.sentinelsystems.hud.calibration

import kotlin.math.sqrt

data class Vec3(
    val x: Float,
    val y: Float,
    val z: Float
) {
    operator fun plus(other: Vec3) =
        Vec3(x + other.x, y + other.y, z + other.z)

    operator fun minus(other: Vec3) =
        Vec3(x - other.x, y - other.y, z - other.z)

    operator fun times(scale: Float) =
        Vec3(x * scale, y * scale, z * scale)

    fun dot(other: Vec3): Float =
        x * other.x + y * other.y + z * other.z

    fun cross(other: Vec3): Vec3 =
        Vec3(
            y * other.z - z * other.y,
            z * other.x - x * other.z,
            x * other.y - y * other.x
        )

    fun normalized(): Vec3 {
        val length = sqrt(dot(this))
        require(length > 1e-5f) { "Cannot normalize near-zero vector." }
        return this * (1f / length)
    }
}

data class Calibration(
    val beaconId: Int,
    val originLocal: Vec3,
    val xAxisLocal: Vec3,
    val yAxisLocal: Vec3,
    val zAxisLocal: Vec3
) {
    fun localToShared(local: Vec3): Vec3 {
        val delta = local - originLocal

        return Vec3(
            delta.dot(xAxisLocal),
            delta.dot(yAxisLocal),
            delta.dot(zAxisLocal)
        )
    }

    fun sharedToLocal(shared: Vec3): Vec3 {
        return originLocal +
            xAxisLocal * shared.x +
            yAxisLocal * shared.y +
            zAxisLocal * shared.z
    }
}

object CalibrationBuilder {

    fun fromMarkerCorners(
        beaconId: Int,
        topLeft: Vec3,
        topRight: Vec3,
        bottomRight: Vec3,
        bottomLeft: Vec3
    ): Calibration {
        val origin =
            (topLeft + topRight + bottomRight + bottomLeft) * 0.25f

        val leftCenter = (topLeft + bottomLeft) * 0.5f
        val rightCenter = (topRight + bottomRight) * 0.5f
        val topCenter = (topLeft + topRight) * 0.5f
        val bottomCenter = (bottomLeft + bottomRight) * 0.5f

        val xAxis = (rightCenter - leftCenter).normalized()
        val yApprox = (topCenter - bottomCenter).normalized()

        val zAxis = xAxis.cross(yApprox).normalized()
        val yAxis = zAxis.cross(xAxis).normalized()

        return Calibration(
            beaconId = beaconId,
            originLocal = origin,
            xAxisLocal = xAxis,
            yAxisLocal = yAxis,
            zAxisLocal = zAxis
        )
    }
}
