package com.sentinelsystems.hud.identity

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ClientIdentityStoreInstrumentedTest {
    @Test
    fun identityIsPositiveAndStableAcrossStoreInstances() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()

        val first = ClientIdentityStore(context).getOrCreate()
        val second = ClientIdentityStore(context).getOrCreate()

        assertTrue(first > 0L)
        assertEquals(first, second)
    }
}