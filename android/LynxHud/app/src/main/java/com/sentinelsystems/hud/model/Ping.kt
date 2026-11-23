package com.sentinelsystems.hud.model

data class Ping(
    val id: Long,
    val senderId: Long,
    val beaconId: Int,
    val x: Float,
    val y: Float,
    val z: Float,
    val createdAt: Long
)