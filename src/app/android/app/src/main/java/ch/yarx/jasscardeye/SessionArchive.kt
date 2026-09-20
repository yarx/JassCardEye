package ch.yarx.jasscardeye

import java.io.InputStream
import java.io.OutputStream
import java.util.zip.Deflater
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

// Port of src/app/ios/Sources/SessionArchive.swift.

/**
 * One ZIP per recording: `session_<time>_<discipline>.zip`, holding a folder of the same name with the
 * video, the recognition log and the session info. One file per session is what gets copied off the phone - on
 * Android also the end of the video and the log lying in two different folders - and it is the layout iOS writes,
 * so the dataset tool opens either.
 */
object SessionArchive {

    /**
     * Writes [files] - a name in the archive and a way to open its content - into a ZIP on [out], inside the folder
     * [name], and closes [out]. Nothing is compressed: the video already is, and it is almost all of the archive.
     */
    fun write(out: OutputStream, name: String, files: List<Pair<String, () -> InputStream>>) {
        ZipOutputStream(out).use { zip ->
            zip.setLevel(Deflater.NO_COMPRESSION)
            for ((fileName, open) in files) {
                zip.putNextEntry(ZipEntry("$name/$fileName"))
                open().use { it.copyTo(zip) }
                zip.closeEntry()
            }
        }
    }
}
