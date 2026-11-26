package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Overlay
import com.sentinelsystems.hud.model.OverlayContract
import com.sentinelsystems.hud.model.SharedPoint
import org.json.JSONArray
import org.json.JSONObject

sealed interface OverlayInboundMessage {
    data class Diff(
        val operation: String,
        val overlayId: Long,
        val revision: Long,
        val overlay: Overlay?,
        val stateEpoch: Long,
        val stateVersion: Long
    ) : OverlayInboundMessage

    data class SnapshotChunk(
        val snapshotId: Long,
        val stateEpoch: Long,
        val stateVersion: Long,
        val chunkIndex: Int,
        val chunkCount: Int,
        val overlays: List<Overlay>
    ) : OverlayInboundMessage
}

fun parseOverlayInbound(json: String): OverlayInboundMessage? {
    val root = JSONObject(json)
    return when (root.optString("type")) {
        "OVERLAY_DIFF" -> OverlayInboundMessage.Diff(
            operation = root.getString("operation"),
            overlayId = root.getLong("overlayId"),
            revision = root.getLong("revision"),
            overlay = root.optJSONObject("overlay")?.let(::parseOverlay),
            stateEpoch = root.getLong("stateEpoch"),
            stateVersion = root.getLong("stateVersion")
        )

        "OVERLAY_SNAPSHOT" -> {
            val array = root.getJSONArray("overlays")
            val overlays = buildList {
                for (i in 0 until array.length())
                    add(parseOverlay(array.getJSONObject(i)))
            }
            OverlayInboundMessage.SnapshotChunk(
                snapshotId = root.getLong("snapshotId"),
                stateEpoch = root.getLong("stateEpoch"),
                stateVersion = root.getLong("stateVersion"),
                chunkIndex = root.getInt("chunkIndex"),
                chunkCount = root.getInt("chunkCount"),
                overlays = overlays
            )
        }

        else -> null
    }
}

fun overlayCreateToJson(
    overlayId: Long,
    senderId: Long,
    beaconId: Int,
    kind: String,
    geometry: List<SharedPoint>,
    createdAt: Long
): String {
    require(overlayId > 0)
    require(senderId > 0)
    require(beaconId >= 0)
    require(createdAt > 0)
    require(OverlayContract.validateGeometry(kind, geometry))

    return JSONObject()
    .put("type", "OVERLAY_CREATE")
    .put("overlayId", overlayId)
    .put("senderId", senderId)
    .put("beaconId", beaconId)
    .put("kind", kind)
    .put("geometry", pointsToJson(geometry))
    .put("createdAt", createdAt)
    .toString()
}

fun overlayUpdateToJson(
    overlay: Overlay,
    senderId: Long,
    geometry: List<SharedPoint>
): String {
    require(senderId > 0)
    require(overlay.revision > 0)
    require(OverlayContract.validateGeometry(overlay.kind, geometry))

    return JSONObject()
    .put("type", "OVERLAY_UPDATE")
    .put("overlayId", overlay.overlayId)
    .put("senderId", senderId)
    .put("expectedRevision", overlay.revision)
    .put("kind", overlay.kind)
    .put("geometry", pointsToJson(geometry))
    .toString()
}

fun overlayRemoveToJson(
    overlayId: Long,
    senderId: Long,
    expectedRevision: Long
): String = JSONObject()
    .put("type", "OVERLAY_REMOVE")
    .put("overlayId", overlayId)
    .put("senderId", senderId)
    .put("expectedRevision", expectedRevision)
    .toString()

fun overlaySnapshotRequestToJson(clientId: Long): String = JSONObject()
    .put("type", "OVERLAY_SNAPSHOT_REQUEST")
    .put("clientId", clientId)
    .toString()

private fun parseOverlay(obj: JSONObject): Overlay {
    val geometryArray = obj.getJSONArray("geometry")
    val geometry = buildList {
        for (i in 0 until geometryArray.length()) {
            val point = geometryArray.getJSONObject(i)
            add(
                SharedPoint(
                    point.getDouble("x").toFloat(),
                    point.getDouble("y").toFloat(),
                    point.getDouble("z").toFloat()
                )
            )
        }
    }

    return Overlay(
        overlayId = obj.getLong("overlayId"),
        ownerId = obj.getLong("ownerId"),
        beaconId = obj.getInt("beaconId"),
        kind = obj.getString("kind"),
        revision = obj.getLong("revision"),
        geometry = geometry,
        createdAt = obj.getLong("createdAt"),
        updatedAt = obj.getLong("updatedAt")
    )
}

private fun pointsToJson(points: List<SharedPoint>): JSONArray = JSONArray().apply {
    points.forEach { point ->
        put(
            JSONObject()
                .put("x", point.x)
                .put("y", point.y)
                .put("z", point.z)
        )
    }
}
