package com.sentinelsystems.hud.calibration

import com.sentinelsystems.hud.model.Ping

data class RemotePingProjection(
    val pingId: Long,
    val beaconId: Int,
    val localPosition: Vec3
)

object RemotePingProjector {
    fun project(
        ping: Ping,
        calibration: Calibration
    ): RemotePingProjection? {
        if (ping.beaconId != calibration.beaconId) {
            return null
        }

        val local = calibration.sharedToLocal(
            Vec3(
                x = ping.x,
                y = ping.y,
                z = ping.z
            )
        )

        return RemotePingProjection(
            pingId = ping.id,
            beaconId = ping.beaconId,
            localPosition = local
        )
    }
}