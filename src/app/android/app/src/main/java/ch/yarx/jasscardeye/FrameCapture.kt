package ch.yarx.jasscardeye

import android.content.ContentValues
import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

// Port of src/app/ios/Sources/FrameCapture.swift.

/**
 * Saves the square the model was actually given, for looking at afterwards.
 *
 * Real sessions throw up cards the detector will not name, and the useful evidence is not a photo of the
 * table: it is *what the model saw* - the centred square, at the resolution the camera delivered. That
 * square is the same thing a dataset image is, so a capture can go straight into the labelling tool and
 * become a validation case for the very situation that failed.
 *
 * Files land in **Pictures/JassCardEye** (recordings in Movies/JassCardEye, their recognition logs in
 * Documents/JassCardEye), written through MediaStore:
 * the Files app and any gallery list them, and a USB connection shows them, with no permission asked and
 * no adb needed - the counterpart of the Documents folder the iOS Files app lists.
 */
object FrameCapture {

    class Failure(message: String) : Exception(message)

    const val FOLDER = "JassCardEye"

    /** Writes [square] as JPEG and returns the file name. Called on the analysis thread. */
    fun save(context: Context, square: Bitmap, label: String?, confidence: Float, mode: String): String {
        val name = fileName(label, confidence, mode)
        val media = PendingMedia.insert(context, PendingMedia.Kind.IMAGE, name)
        try {
            val written = context.contentResolver.openOutputStream(media.uri)?.use { stream ->
                // High quality: these are diagnostic images, and compression artefacts would be one more
                // thing to argue about when a card turns out to be unrecognisable.
                square.compress(Bitmap.CompressFormat.JPEG, 95, stream)
            } ?: false
            if (!written) throw Failure("Bild konnte nicht erzeugt werden.")
            media.commit()
            return name
        } catch (error: Exception) {
            media.discard()
            throw error as? Failure ?: Failure("Datei konnte nicht geschrieben werden.")
        }
    }

    /** Name for a recorded session, in the same shape of name as a still. */
    fun sessionFileName(mode: String): String = "session_${stamp()}_${modeToken(mode)}.mp4"

    /**
     * The whole context travels in the name, so it survives sharing and a copy into any folder: when it
     * was taken, what the model made of it (or that it saw nothing), and in which mode.
     */
    private fun fileName(label: String?, confidence: Float, mode: String): String {
        val verdict = label?.let { "$it-${(confidence * 100).toInt()}" } ?: "nichts"
        return "capture_${stamp()}_${verdict}_${modeToken(mode)}.jpg"
    }

    /**
     * A discipline's token - see [CountingMode.token] - may carry a dot ("slalom.obe"), which would read as a
     * second file extension: nothing but letters and digits belongs in a file name.
     */
    private fun modeToken(mode: String): String = mode.lowercase().filter { it.isLetterOrDigit() }

    /** Down to milliseconds: two taps in the same second must not overwrite each other. */
    private fun stamp(): String = SimpleDateFormat("yyyyMMdd-HHmmss-SSS", Locale.US).format(Date())
}

/**
 * A file in the shared media folders that nobody sees until it is complete: inserted as pending, written, and
 * then either committed or discarded - so a half-written capture or recording never shows up in a gallery.
 * The one way stills, session recordings and their logs reach Pictures, Movies and Documents.
 */
class PendingMedia private constructor(private val context: Context, val uri: Uri) {

    enum class Kind(val collection: Uri, val directory: String, val mimeType: String) {
        IMAGE(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, Environment.DIRECTORY_PICTURES, "image/jpeg"),
        VIDEO(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, Environment.DIRECTORY_MOVIES, "video/mp4"),
        // The recognition log of a recording. Not beside the video: MediaStore takes a file that is neither
        // picture, video nor audio only into Download or Documents, and refuses Movies.
        SESSION_CSV(MediaStore.Files.getContentUri("external"), Environment.DIRECTORY_DOCUMENTS, "text/csv"),
        // What the session was recorded with ([SessionInfo]), beside its log.
        SESSION_INFO(MediaStore.Files.getContentUri("external"), Environment.DIRECTORY_DOCUMENTS, "application/json"),
        // The three packed into one ZIP when the recording ends ([SessionArchive]) - what normally remains of it.
        SESSION_ARCHIVE(MediaStore.Files.getContentUri("external"), Environment.DIRECTORY_DOCUMENTS, "application/zip"),
    }

    /** Makes the file visible. */
    fun commit() {
        context.contentResolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
    }

    /** Removes the file again. Never throws: it is what runs when something else already went wrong. */
    fun discard() {
        runCatching { context.contentResolver.delete(uri, null, null) }
    }

    companion object {
        fun insert(context: Context, kind: Kind, name: String): PendingMedia {
            val values = ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, name)
                put(MediaStore.MediaColumns.MIME_TYPE, kind.mimeType)
                put(MediaStore.MediaColumns.RELATIVE_PATH, "${kind.directory}/${FrameCapture.FOLDER}")
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }
            val uri = context.contentResolver.insert(kind.collection, values)
                ?: throw FrameCapture.Failure("Datei konnte nicht geschrieben werden.")
            return PendingMedia(context, uri)
        }
    }
}
