package com.sentinelsystems.hud

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.google.ar.core.Anchor
import com.google.ar.core.Frame
import com.google.ar.core.HitResult
import com.google.ar.core.Plane
import com.google.ar.core.TrackingState
import io.github.sceneview.ar.ARSceneView
import io.github.sceneview.ar.node.AnchorNode
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import io.github.sceneview.node.BillboardNode
import android.graphics.BitmapFactory
import androidx.compose.ui.platform.LocalContext

fun pickBestHit(hits: List<HitResult>): HitResult? {
    return hits.firstOrNull { hit ->
        val trackable = hit.trackable

        trackable is Plane &&
                trackable.trackingState == TrackingState.TRACKING &&
                trackable.isPoseInPolygon(hit.hitPose)
    }
}

@Composable
fun ArScreen() {
    var viewportSize by remember {
        mutableStateOf(IntSize.Zero)
    }

    var trackingState by remember {
        mutableStateOf("STARTING")
    }

    var latestFrame by remember {
        mutableStateOf<Frame?>(null)
    }

    val anchors = remember {
        mutableStateListOf<Anchor>()
    }

    val context = LocalContext.current

    val pingBitmap = remember {
        BitmapFactory.decodeResource(
            context.resources,
            R.drawable.lynx_ping
        )
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .onSizeChanged { viewportSize = it }
    ) {

        ARSceneView(
            modifier = Modifier.fillMaxSize(),
            planeRenderer = true,
            onSessionUpdated = { _, frame ->
                latestFrame = frame

                trackingState = when (frame.camera.trackingState) {
                    TrackingState.TRACKING -> "TRACKING"
                    TrackingState.PAUSED -> "PAUSED"
                    TrackingState.STOPPED -> "STOPPED"
                }
            }
        ) {
            anchors.forEach { anchor ->
                AnchorNode(anchor = anchor) {
                    BillboardNode(
                        bitmap = pingBitmap,
                        widthMeters = 0.18f,
                        heightMeters = 0.18f
                    )
                }
            }
        }

        Text(
            text = "AR: $trackingState",
            color = Color.White,
            modifier = Modifier
                .align(Alignment.TopStart)
                .padding(16.dp)
        )

        // Center aiming reticle
        Box(
            modifier = Modifier
                .align(Alignment.Center)
                .size(12.dp)
                .background(
                    color = Color.Red,
                    shape = CircleShape
                )
        )

        Button(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 32.dp),
            onClick = {
                val frame = latestFrame ?: return@Button

                if (viewportSize.width == 0 || viewportSize.height == 0) {
                    return@Button
                }

                val centerX = viewportSize.width / 2f
                val centerY = viewportSize.height / 2f

                val hits = frame.hitTest(centerX, centerY)
                val bestHit = pickBestHit(hits) ?: return@Button

                anchors.add(bestHit.createAnchor())
            }
        ) {
            Text("PING")
        }
    }
}