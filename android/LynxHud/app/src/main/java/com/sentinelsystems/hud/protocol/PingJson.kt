package com.sentinelsystems.hud.protocol

import com.sentinelsystems.hud.model.Ping
import org.json.JSONObject

fun pingToJson(ping: Ping): String {
    return JSONObject()
        .put("id", ping.id)
        .put("senderId", ping.senderId)
        .put("x", ping.x)
        .put("y", ping.y)
        .put("z", ping.z)
        .put("createdAt", ping.createdAt)
        .toString()
}