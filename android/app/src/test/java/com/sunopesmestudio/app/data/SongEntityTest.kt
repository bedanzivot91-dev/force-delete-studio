package com.sunopesmestudio.app.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class SongEntityTest {
    @Test
    fun `defaults are sane for a freshly imported song`() {
        val song = SongEntity(id = "abc123", title = "Test pesma")
        assertEquals("Test pesma", song.title)
        assertFalse(song.isFavorite)
        assertEquals(0, song.rating)
        assertEquals(0.0, song.durationSeconds, 0.0001)
    }
}
