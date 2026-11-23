package com.sentinelsystems.hud.calibration

import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.opencv.android.OpenCVLoader
import org.opencv.core.CvType
import org.opencv.core.Mat

@RunWith(AndroidJUnit4::class)
class BeaconDetectorInstrumentedTest {

    @Test
    fun extractsFloatAprilTagCornersWithoutCrashing() {
        assertTrue(OpenCVLoader.initLocal())

        val mat = Mat(1, 4, CvType.CV_32FC2)

        try {
            val expected = floatArrayOf(
                10f, 20f,
                30f, 40f,
                50f, 60f,
                70f, 80f
            )

            mat.put(0, 0, expected)

            val actual = BeaconDetector.extractFourCorners(mat)

            requireNotNull(actual)
            assertArrayEquals(expected, actual, 0.0001f)
        } finally {
            mat.release()
        }
    }
}
