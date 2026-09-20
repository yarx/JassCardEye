package ch.yarx.jasscardeye

import android.content.Context
import android.graphics.Bitmap
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.MediaMuxer
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.util.Log
import android.view.Surface
import java.io.OutputStream
import java.util.Locale
import java.util.TreeMap

// Port of src/app/ios/Sources/SessionRecorder.swift.

/**
 * Records a whole counting session as a square video.
 *
 * What it writes is not the camera stream but the **analysed square, frame by frame as the model received
 * it** - same crop, including the frames dropped while an inference was running. Replayed later it follows
 * the run rather than the camera, which is what makes it usable as emulator input and as the basis of a
 * test case. It is not a perfect copy: encoding costs time inside the frame loop, so a recorded session runs
 * at a slightly lower analysis rate than the same session unrecorded.
 *
 * H.264 in MP4 through `MediaCodec`, written straight into Movies/JassCardEye as a pending file that only
 * becomes visible when the session closes (see [PendingMedia]) - so a half-written file never shows up where
 * the captures are, and a long session is not written twice.
 *
 * With the video it writes the **recognition log**, a CSV of the same name in Documents/JassCardEye -
 * MediaStore keeps it out of Movies: one row per frame in the video, saying what the model made of it, row n for
 * frame n. With it goes [sessionInfo], the session info ([SessionInfo]) as JSON: device, app, model and the settings the
 * pile counted with. The encoder may drop a frame it was given, so a row is written only
 * when a frame comes out of the encoder, matched to its analysis by the presentation time [RecordingSurface]
 * stamped on it.
 *
 * When the session ends, the three are packed into one ZIP in Documents/JassCardEye ([SessionArchive]) and the
 * loose files are discarded before anyone sees them, so one file per session is what gets copied off the phone.
 */
class SessionRecorder(private val context: Context, val fileName: String, private val sessionInfo: String? = null) {

    /** How a recording ended. A failure is reported rather than swallowed. */
    sealed interface Outcome {
        /** Packed into the ZIP of this name; [problem] says why packing failed - the files are then published on their own. */
        data class Saved(val name: String, val frames: Int, val problem: String? = null) : Outcome
        data object NothingRecorded : Outcome
        data class Failed(val reason: String) : Outcome
    }

    /** What the model made of one recorded frame: one row of the recognition log, with the same columns as on iOS. */
    data class FrameResult(val detection: Detection? = null, val committed: Boolean = false) {
        /** The row for frame [index] of the video, presented [timeUs] after the first. The box is normalised to the square, origin top-left. */
        fun row(index: Int, timeUs: Long): String {
            val head = "$index,${"%.3f".format(Locale.US, timeUs / 1000.0)},"
            val flag = if (committed) 1 else 0
            val detection = detection ?: return "$head,,,,,,$flag\n"
            val numbers = with(detection) { listOf(confidence, box.left, box.top, box.width, box.height) }
                .joinToString(",") { "%.4f".format(Locale.US, it) }
            return "$head${detection.label},$numbers,$flag\n"
        }

        companion object {
            /** The columns, described under "Session recordings" in context/architecture/data-pipeline.md. */
            const val HEADER = "frame,t_ms,label,confidence,x,y,w,h,committed\n"
        }
    }

    private var renderer: RecordingSurface? = null
    private var csvMedia: PendingMedia? = null
    private var infoMedia: PendingMedia? = null
    private var csv: OutputStream? = null
    private var startedAt: Long? = null
    private var lastSubmitted = -1L
    private var lastWritten = -1L
    private var firstWritten: Long? = null
    private val pending = TreeMap<Long, FrameResult>()
    private val lock = Any()
    private var codec: MediaCodec? = null
    private var surface: Surface? = null
    private var muxer: MediaMuxer? = null
    private var track = -1
    private var muxing = false
    private var side = 0
    private var frameCount = 0
    private var closed = false
    private var failure: String? = null
    private var media: PendingMedia? = null
    private var descriptor: ParcelFileDescriptor? = null
    private val info = MediaCodec.BufferInfo()

    /** Appends the analysed square of this frame. Does nothing once the recording is closed or has failed. */
    fun append(square: Bitmap, result: FrameResult = FrameResult()): Unit = synchronized(lock) {
        // A recording that has already broken must not keep paying for itself inside the inference loop.
        if (closed || failure != null) return
        if (square.width < 16) return
        if (codec == null && !start(minOf(square.width, MAX_SIDE))) return
        val renderer = renderer ?: return
        try {
            drain(endOfStream = false)
            // Bound the queue when an encoder stalls. No submission means no CSV row.
            if (pending.size >= 120) return
            val now = SystemClock.elapsedRealtimeNanos() / 1000
            if (startedAt == null) startedAt = now
            // At least a millisecond apart, so every frame can be found again by its time alone - the Dataset Tool
            // seeks to a tenth of a millisecond before it.
            val time = maxOf(now - checkNotNull(startedAt), lastSubmitted + MIN_SPACING_US)
            pending[time] = result
            renderer.draw(square, time)
            lastSubmitted = time
            drain(endOfStream = false)
        } catch (error: Exception) {
            fail("Aufnahme abgebrochen: ${describe(error)}")
        }
    }

    /** Closes the file and says what became of it. Safe to call more than once. */
    fun finish(): Outcome = synchronized(lock) {
        if (closed) return Outcome.NothingRecorded
        closed = true
        failure?.let { return Outcome.Failed(it) }
        val codec = codec
        if (codec == null || lastSubmitted < 0) {
            release()
            media?.discard(); csvMedia?.discard(); infoMedia?.discard()
            return Outcome.NothingRecorded
        }
        return try {
            codec.signalEndOfInputStream()
            drain(endOfStream = true)
            check(frameCount > 0) { "Der Encoder hat kein Frame geschrieben" }
            csv?.flush(); csv?.close(); csv = null
            // stop writes the MP4 index; a failure here must not publish a broken pair.
            if (muxing) { muxer?.stop(); muxing = false }
            release()
            // One file per session to copy off the phone. A recording that cannot be packed is still a recording:
            // its files are published on their own, and the note says why.
            val problem = pack()
            if (problem == null) {
                Outcome.Saved("$baseName.zip", frameCount)
            } else {
                checkNotNull(csvMedia).commit()
                infoMedia?.commit()
                checkNotNull(media).commit()
                Outcome.Saved(fileName, frameCount, "nicht gepackt: $problem")
            }
        } catch (error: Exception) {
            release()
            media?.discard(); csvMedia?.discard(); infoMedia?.discard()
            Outcome.Failed("Aufnahme nicht abgeschlossen: ${describe(error)}")
        }
    }

    private val baseName: String get() = fileName.substringBeforeLast('.')

    /**
     * Copies the still pending video, log and info into one ZIP in Documents/JassCardEye, publishes it and discards
     * the three. Returns null when that worked, and why not otherwise - with the three left pending.
     */
    private fun pack(): String? {
        val parts = listOfNotNull(
            media?.let { fileName to it },
            csvMedia?.let { "$baseName.csv" to it },
            infoMedia?.let { "$baseName.json" to it },
        )
        var archive: PendingMedia? = null
        return try {
            val entry = PendingMedia.insert(context, PendingMedia.Kind.SESSION_ARCHIVE, "$baseName.zip")
            archive = entry
            val out = context.contentResolver.openOutputStream(entry.uri) ?: error("ZIP konnte nicht geöffnet werden")
            SessionArchive.write(out, baseName, parts.map { (name, part) ->
                name to { context.contentResolver.openInputStream(part.uri) ?: error("$name ist nicht lesbar") }
            })
            entry.commit()
            parts.forEach { (_, part) -> part.discard() }
            null
        } catch (error: Exception) {
            archive?.discard()
            describe(error)
        }
    }

    /** Builds the encoder once the square's size is known - it depends on the lens. */
    private fun start(requested: Int): Boolean {
        var created: MediaCodec? = null
        return try {
            created = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            // CONFIGURE_FLAG_ENCODE is not optional. Without it the codec is set up in the decoder role and refuses
            // every format with an error that names nothing - the emulator's Codec2 encoder and a Qualcomm OMX
            // encoder alike.
            created.configure(format(created, requested), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            surface = created.createInputSurface()
            renderer = RecordingSurface(checkNotNull(surface), side)
            created.start()
            codec = created
            val entry = PendingMedia.insert(context, PendingMedia.Kind.VIDEO, fileName)
            media = entry
            csvMedia = PendingMedia.insert(context, PendingMedia.Kind.SESSION_CSV, fileName.substringBeforeLast('.') + ".csv")
            csv = context.contentResolver.openOutputStream(checkNotNull(csvMedia).uri)
                ?: error("Protokoll konnte nicht geöffnet werden")
            checkNotNull(csv).write(FrameResult.HEADER.toByteArray(Charsets.UTF_8))
            sessionInfo?.let { json ->
                val entry = PendingMedia.insert(context, PendingMedia.Kind.SESSION_INFO, fileName.substringBeforeLast('.') + ".json")
                infoMedia = entry
                context.contentResolver.openOutputStream(entry.uri)?.use { it.write(json.toByteArray(Charsets.UTF_8)) }
                    ?: error("Session-Info konnte nicht geschrieben werden")
            }
            val file = context.contentResolver.openFileDescriptor(entry.uri, "rw")
                ?: throw IllegalStateException("Datei konnte nicht geöffnet werden.")
            descriptor = file
            muxer = MediaMuxer(file.fileDescriptor, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4)
            Log.i("JassCardEye", "Recording ${side}x$side with ${created.name}")
            true
        } catch (error: Exception) {
            // Once it is `codec`, fail() releases it; before that it is still ours to release.
            if (codec == null) runCatching { created?.release() }
            Log.w("JassCardEye", "Encoder setup failed at ${side}x$side", error)
            fail("Aufnahme konnte nicht angelegt werden: ${describe(error)}")
            false
        }
    }

    private fun format(encoder: MediaCodec, requested: Int): MediaFormat {
        val video = encoder.codecInfo.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC).videoCapabilities
            ?: throw IllegalStateException("kein H.264-Encoder")
        // Not every encoder takes every square: a software encoder on a slow processor stops well below 1080 lines,
        // and hardware encoders have alignments of their own. So the square is the largest the encoder says it can
        // take at the frame rate, never larger than what was asked for.
        val align = maxOf(2, video.widthAlignment, video.heightAlignment)
        side = maxOf(16, requested - requested % align)
        while (side > 16 && !video.areSizeAndRateSupported(side, side, FRAME_RATE.toDouble())) side -= align
        return MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, side, side).apply {
            setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
            setInteger(MediaFormat.KEY_BIT_RATE, video.bitrateRange.clamp(BIT_RATE))
            setInteger(MediaFormat.KEY_FRAME_RATE, FRAME_RATE)
            setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
            setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
        }
    }

    /** An exception as a person can read it - a codec exception often comes with an empty message. */
    private fun describe(error: Exception): String = error.message?.takeIf { it.isNotBlank() } ?: error.javaClass.simpleName

    private fun drain(endOfStream: Boolean) {
        val codec = codec ?: return
        val muxer = muxer ?: return
        val deadline = SystemClock.elapsedRealtime() + 10_000
        while (true) {
            check(!endOfStream || SystemClock.elapsedRealtime() < deadline) { "Encoder beendet die Aufnahme nicht" }
            val index = codec.dequeueOutputBuffer(info, if (endOfStream) 10_000 else 0)
            when {
                index == MediaCodec.INFO_TRY_AGAIN_LATER -> if (!endOfStream) return
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    track = muxer.addTrack(codec.outputFormat)
                    muxer.start()
                    muxing = true
                }
                index >= 0 -> {
                    val data = codec.getOutputBuffer(index)
                    if (data != null && info.size > 0 && muxing && (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
                        data.position(info.offset)
                        data.limit(info.offset + info.size)
                        val pts = info.presentationTimeUs
                        check(pts > lastWritten) { "Encoder liefert Frames in anderer Reihenfolge" }
                        val result = pending.remove(pts) ?: error("Encoder-Frame ohne passenden Analyse-Zeitstempel")
                        pending.headMap(pts).clear() // earlier inputs missing from output were dropped
                        if (firstWritten == null) firstWritten = pts
                        info.presentationTimeUs = pts - checkNotNull(firstWritten)
                        muxer.writeSampleData(track, data, info)
                        checkNotNull(csv).write(result.row(frameCount, info.presentationTimeUs).toByteArray(Charsets.UTF_8))
                        lastWritten = pts
                        frameCount++
                    }
                    codec.releaseOutputBuffer(index, false)
                    if ((info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) return
                }
            }
        }
    }

    /** Gives up on this recording and takes the half-written file with it. Call with the lock held. */
    private fun fail(reason: String) {
        Log.w("JassCardEye", reason)
        failure = reason
        release()
        media?.discard(); csvMedia?.discard(); infoMedia?.discard()
    }

    private fun release() {
        runCatching { renderer?.close() }; renderer = null
        runCatching { csv?.close() }; csv = null
        pending.clear()
        runCatching { codec?.stop() }
        runCatching { codec?.release() }
        runCatching { surface?.release() }
        if (muxing) runCatching { muxer?.stop() }
        runCatching { muxer?.release() }
        // After the muxer, which writes the file's index on stop and needs the descriptor open for it.
        runCatching { descriptor?.close() }
        descriptor = null
        codec = null
        surface = null
        muxer = null
        muxing = false
    }

    companion object {
        private const val FRAME_RATE = 30
        private const val BIT_RATE = 6_000_000
        /** A recording is evidence, not footage: 1080 px is plenty and keeps the encoder within budget. */
        private const val MAX_SIDE = 1080
        private const val MIN_SPACING_US = 1_000L
    }
}
