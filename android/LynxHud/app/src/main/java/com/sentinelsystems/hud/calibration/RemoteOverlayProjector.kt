package com.sentinelsystems.hud.calibration

import com.sentinelsystems.hud.model.Overlay

object RemoteOverlayProjector {
    fun projectPoints(
        overlay: Overlay,
        calibration: Calibration
    ): List<Vec3>? {
        if (overlay.beaconId != calibration.beaconId) return null

        return overlay.geometry.map { point ->
            calibration.sharedToLocal(
                Vec3(point.x, point.y, point.z)
            )
        }
    }
}
