package com.sentinelsystems.hud.calibration

import android.util.Log
import com.google.ar.core.Coordinates2d
import com.google.ar.core.Frame
import com.google.ar.core.HitResult
import com.google.ar.core.Plane
import com.google.ar.core.TrackingState
import org.opencv.android.OpenCVLoader
import org.opencv.core.CvType
import org.opencv.core.Mat
import org.opencv.objdetect.ArucoDetector
import org.opencv.objdetect.Objdetect

data class BeaconDetection(
    val beaconId: Int,
    val imageCorners: FloatArray
)

data class CalibrationAttempt(
    val detection: BeaconDetection?,
    val viewCorners: FloatArray? = null,
    val localCorners: List<Vec3> = emptyList(),
    val planeHits: Int = 0,
    val calibration: Calibration? = null,
    val message: String
)

object BeaconDetector {
    const val REQUIRED_BEACON_ID = 0
    private const val LOG_TAG = "LynxCalibration"

    private val openCvReady: Boolean by lazy {
        OpenCVLoader.initLocal()
    }

    private val dictionary by lazy {
        Objdetect.getPredefinedDictionary(
            Objdetect.DICT_APRILTAG_36h11
        )
    }

    private val detector by lazy {
        ArucoDetector(dictionary)
    }

    fun detect(frame: Frame): BeaconDetection? {
        if (!openCvReady) return null

        val image = try {
            frame.acquireCameraImage()
        } catch (_: Exception) {
            return null
        }

        try {
            val gray = imageToGrayMat(image)

            try {
                val corners = mutableListOf<Mat>()
                val ids = Mat()

                try {
                    detector.detectMarkers(gray, corners, ids)

                    if (ids.empty()) return null

                    for (index in 0 until ids.rows()) {
                        val id = ids.get(index, 0)?.firstOrNull()?.toInt()
                            ?: continue

                        if (id != REQUIRED_BEACON_ID) continue

                        val cornerMat = corners.getOrNull(index)
                            ?: continue

                        val points = extractFourCorners(cornerMat)
                            ?: continue

                        return BeaconDetection(
                            beaconId = id,
                            imageCorners = points
                        )
                    }

                    return null
                } finally {
                    ids.release()
                    corners.forEach { it.release() }
                }
            } finally {
                gray.release()
            }
        } finally {
            image.close()
        }
    }

    fun attemptCalibration(
        frame: Frame
    ): CalibrationAttempt {
        val detection = detect(frame)

        if (detection == null) {
            return CalibrationAttempt(
                detection = null,
                message = "Beacon 0 not detected"
            )
        }

        val viewCorners = FloatArray(detection.imageCorners.size)

        frame.transformCoordinates2d(
            Coordinates2d.IMAGE_PIXELS,
            detection.imageCorners,
            Coordinates2d.VIEW,
            viewCorners
        )

        val localCorners = mutableListOf<Vec3>()

        for (i in 0 until 4) {
            val x = viewCorners[i * 2]
            val y = viewCorners[i * 2 + 1]

            val hit = pickPlaneHit(frame.hitTest(x, y))

            if (hit == null) {
                Log.d(
                    LOG_TAG,
                    "Beacon ${detection.beaconId}: corner ${i + 1}/4 " +
                        "has no tracked-plane hit at view=($x,$y)"
                )

                return CalibrationAttempt(
                    detection = detection,
                    viewCorners = viewCorners,
                    localCorners = localCorners.toList(),
                    planeHits = localCorners.size,
                    message = "Tag found; plane hits ${localCorners.size}/4"
                )
            }

            val pose = hit.hitPose
            val local = Vec3(
                pose.tx(),
                pose.ty(),
                pose.tz()
            )

            localCorners += local

            Log.d(
                LOG_TAG,
                "Beacon ${detection.beaconId}: corner ${i + 1}/4 " +
                    "view=($x,$y) local=$local"
            )
        }

        val calibration = CalibrationBuilder.fromMarkerCorners(
            beaconId = detection.beaconId,
            topLeft = localCorners[0],
            topRight = localCorners[1],
            bottomRight = localCorners[2],
            bottomLeft = localCorners[3]
        )

        Log.i(
            LOG_TAG,
            "Beacon ${calibration.beaconId} READY " +
                "origin=${calibration.originLocal} " +
                "x=${calibration.xAxisLocal} " +
                "y=${calibration.yAxisLocal} " +
                "z=${calibration.zAxisLocal}"
        )

        return CalibrationAttempt(
            detection = detection,
            viewCorners = viewCorners,
            localCorners = localCorners,
            planeHits = 4,
            calibration = calibration,
            message = "Beacon ${detection.beaconId} ready"
        )
    }

    private fun pickPlaneHit(hits: List<HitResult>): HitResult? {
        return hits.firstOrNull { hit ->
            val trackable = hit.trackable

            trackable is Plane &&
                trackable.trackingState == TrackingState.TRACKING &&
                trackable.isPoseInPolygon(hit.hitPose)
        }
    }

    private fun imageToGrayMat(
        image: android.media.Image
    ): Mat {
        val width = image.width
        val height = image.height
        val plane = image.planes[0]
        val buffer = plane.buffer
        val rowStride = plane.rowStride
        val pixelStride = plane.pixelStride

        val gray = Mat(height, width, CvType.CV_8UC1)
        val row = ByteArray(width)

        for (y in 0 until height) {
            val rowStart = y * rowStride

            for (x in 0 until width) {
                row[x] = buffer.get(rowStart + x * pixelStride)
            }

            gray.put(y, 0, row)
        }

        return gray
    }

    internal fun extractFourCorners(
        cornerMat: Mat
    ): FloatArray? {
        val flattened = cornerMat.reshape(1, 1)

        return try {
            val values = FloatArray(8)
            val copied = flattened.get(0, 0, values)

            if (copied < 8) null else values
        } finally {
            flattened.release()
        }
    }
}