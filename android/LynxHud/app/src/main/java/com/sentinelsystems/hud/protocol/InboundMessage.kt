package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping
import org.json.JSONObject

sealed interface InboundMessage {
    data class PingCreate(val ping: Ping) : InboundMessage
    data class PingRemove(val pingId: Long, val senderId: Long) : InboundMessage
}

fun parseInboundMessage(json: String): InboundMessage? {
    val root = JSONObject(json)

    return when (root.optString("type")) {
        "PING_CREATE" -> {
            val obj = root.optJSONObject("ping") ?: return null
            val ping = Ping(
                id = obj.getLong("id"),
                senderId = obj.getLong("senderId"),
                beaconId = obj.getInt("beaconId"),
                x = obj.getDouble("x").toFloat(),
                y = obj.getDouble("y").toFloat(),
                z = obj.getDouble("z").toFloat(),
                createdAt = obj.getLong("createdAt")
            )
            InboundMessage.PingCreate(ping)
        }

        "PING_REMOVE" -> InboundMessage.PingRemove(
            pingId = root.getLong("pingId"),
            senderId = root.getLong("senderId")
        )

        else -> null
    }
}