package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.util.zip.ZipInputStream

/**
 * Holds the session ZIP to the layout iOS writes: a folder named after the session, with the video,
 * the log and the info inside. The dataset tool unpacks that folder beside the archive, so a file written at the
 * root instead would land one level off.
 */
class SessionArchiveTest {

    @Test
    fun theFilesLieInAFolderNamedAfterTheSession() {
        val name = "session_20260101-120000-000_obenabe"
        val video = ByteArray(5000) { (it * 31).toByte() }
        val out = ByteArrayOutputStream()
        SessionArchive.write(out, name, listOf(
            "$name.mp4" to { ByteArrayInputStream(video) },
            "$name.csv" to { ByteArrayInputStream("frame,t_ms\n".toByteArray()) },
        ))

        val entries = mutableMapOf<String, ByteArray>()
        ZipInputStream(ByteArrayInputStream(out.toByteArray())).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                entries[entry.name] = zip.readBytes()
            }
        }
        assertEquals(setOf("$name/$name.mp4", "$name/$name.csv"), entries.keys)
        assertEquals(video.toList(), entries.getValue("$name/$name.mp4").toList())
        assertEquals("frame,t_ms\n", String(entries.getValue("$name/$name.csv")))
    }
}
