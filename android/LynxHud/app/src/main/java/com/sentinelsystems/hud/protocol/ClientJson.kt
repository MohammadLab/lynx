package com.sentinelsystems.hud.protocol

import org.json.JSONObject

fun clientRegisterToJson(clientId: Long): String =
    JSONObject()
        .put("type", "CLIENT_REGISTER")
        .put("clientId", clientId)
        .toString()

fun clientHeartbeatToJson(clientId: Long): String =
    JSONObject()
        .put("type", "CLIENT_HEARTBEAT")
        .put("clientId", clientId)
        .toString()

fun snapshotRequestToJson(clientId: Long): String =
    JSONObject()
        .put("type", "SNAPSHOT_REQUEST")
        .put("clientId", clientId)
        .toString()