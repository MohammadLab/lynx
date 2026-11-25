package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping
import org.json.JSONObject

sealed interface InboundMessage {
    data class PingCreate(
        val ping: Ping,
        val stateEpoch: Long?,
        val stateVersion: Long?
    ) : InboundMessage

    data class PingRemove(
        val pingId: Long,
        val senderId: Long,
        val stateEpoch: Long?,
        val stateVersion: Long?
    ) : InboundMessage

    data class SnapshotChunk(
        val snapshotId: Long,
        val stateEpoch: Long,
        val stateVersion: Long,
        val chunkIndex: Int,
        val chunkCount: Int,
        val pings: List<Ping>
    ) : InboundMessage
}

fun parseInboundMessage(json: String): InboundMessage? {
    val root = JSONObject(json)

    return when (root.optString("type")) {
        "PING_CREATE" -> {
            val obj = root.optJSONObject("ping") ?: return null
            InboundMessage.PingCreate(
                ping = parsePing(obj),
                stateEpoch = root.optLongOrNull("stateEpoch"),
                stateVersion = root.optLongOrNull("stateVersion")
            )
        }

        "PING_REMOVE" -> InboundMessage.PingRemove(
            pingId = root.getLong("pingId"),
            senderId = root.getLong("senderId"),
            stateEpoch = root.optLongOrNull("stateEpoch"),
            stateVersion = root.optLongOrNull("stateVersion")
        )

        "SNAPSHOT" -> {
            val array = root.optJSONArray("pings") ?: return null
            val pings = buildList {
                for (index in 0 until array.length()) {
                    add(parsePing(array.getJSONObject(index)))
                }
            }
            InboundMessage.SnapshotChunk(
                snapshotId = root.getLong("snapshotId"),
                stateEpoch = root.getLong("stateEpoch"),
                stateVersion = root.getLong("stateVersion"),
                chunkIndex = root.getInt("chunkIndex"),
                chunkCount = root.getInt("chunkCount"),
                pings = pings
            )
        }

        else -> null
    }
}

private fun JSONObject.optLongOrNull(name: String): Long? =
    if (has(name) && !isNull(name)) getLong(name) else null

private fun parsePing(obj: JSONObject): Ping = Ping(
    id = obj.getLong("id"),
    senderId = obj.getLong("senderId"),
    beaconId = obj.getInt("beaconId"),
    x = obj.getDouble("x").toFloat(),
    y = obj.getDouble("y").toFloat(),
    z = obj.getDouble("z").toFloat(),
    createdAt = obj.getLong("createdAt")
)