package com.sentinelsystems.hud

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.google.ar.core.TrackingState
import io.github.sceneview.ar.ARSceneView

@Composable
fun ArScreen() {
    var trackingState by remember {
        mutableStateOf("STARTING")
    }

    Box(
        modifier = Modifier.fillMaxSize()
    ) {
        ARSceneView(
            modifier = Modifier.fillMaxSize(),
            planeRenderer = true,
            onSessionUpdated = { _, frame ->
                trackingState = when (frame.camera.trackingState) {
                    TrackingState.TRACKING -> "TRACKING"
                    TrackingState.PAUSED -> "PAUSED"
                    TrackingState.STOPPED -> "STOPPED"
                }
            }
        )

        // Tracking status
        Text(
            text = "AR: $trackingState",
            color = Color.White,
            modifier = Modifier.align(Alignment.TopStart)
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
    }
}