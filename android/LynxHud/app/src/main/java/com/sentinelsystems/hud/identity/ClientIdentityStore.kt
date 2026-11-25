package com.sentinelsystems.hud.identity

import android.content.Context
import java.security.SecureRandom

class ClientIdentityStore(context: Context) {
    private val preferences = context.getSharedPreferences(
        "lynx_identity",
        Context.MODE_PRIVATE
    )

    fun getOrCreate(): Long {
        val existing = preferences.getLong(KEY_CLIENT_ID, 0L)
        if (existing > 0L) {
            return existing
        }

        val random = SecureRandom()
        var generated: Long

        do {
            generated = random.nextLong() and Long.MAX_VALUE
        } while (generated <= 0L)

        preferences.edit()
            .putLong(KEY_CLIENT_ID, generated)
            .apply()

        return generated
    }

    companion object {
        private const val KEY_CLIENT_ID = "client_id"
    }
}