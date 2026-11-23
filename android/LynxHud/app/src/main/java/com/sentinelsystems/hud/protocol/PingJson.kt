package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping
import org.json.JSONObject

private const val TYPE_PING_CREATE = "PING_CREATE"
private const val TYPE_PING_REMOVE = "PING_REMOVE"

fun pingCreateToJson(ping: Ping): String {
    val pingJson = JSONObject()
        .put("id", ping.id)
        .put("senderId", ping.senderId)
        .put("beaconId", ping.beaconId)
        .put("x", ping.x)
        .put("y", ping.y)
        .put("z", ping.z)
        .put("createdAt", ping.createdAt)

    return JSONObject()
        .put("type", TYPE_PING_CREATE)
        .put("ping", pingJson)
        .toString()
}

fun pingRemoveToJson(
    pingId: Long,
    senderId: Long
): String {
    return JSONObject()
        .put("type", TYPE_PING_REMOVE)
        .put("pingId", pingId)
        .put("senderId", senderId)
        .toString()
}
