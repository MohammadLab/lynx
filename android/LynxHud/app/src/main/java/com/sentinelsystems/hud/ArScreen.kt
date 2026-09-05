package com.sentinelsystems.hud

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
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

        Text(
            text = "AR: $trackingState"
        )
    }
}