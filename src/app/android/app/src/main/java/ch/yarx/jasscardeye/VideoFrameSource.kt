package ch.yarx.jasscardeye

import android.content.Context
import android.graphics.Bitmap
import android.media.MediaMetadataRetriever
import android.os.SystemClock
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.LifecycleOwner
import java.io.File
import java.util.concurrent.ExecutorService
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Port of src/app/ios/Sources/VideoFrameSource.swift.

/**
 * Emulator-only stand-in for the camera: plays a video file in a loop and hands its frames to the same
 * detection path the camera feeds. An emulated camera films a virtual room with no cards in it, so this is
 * the only way to exercise the live loop - stability rule, pile, score - without a device.
 *
 * The file is **never bundled**. Push it onto the emulator and point the build at it (see src/app/android/README.md):
 * the shipping app is not touched by it, and a release build never takes this path at all.
 */
class VideoFrameSource(private val context: Context, private val executor: ExecutorService) : FrameSource {

    /** The frame shown where the camera preview would be - the same square that is analysed. */
    var frame by mutableStateOf<Bitmap?>(null); private set

    @Volatile override var onFrame: ((Bitmap) -> Unit)? = null
    override var onTorchChanged: ((Boolean) -> Unit)? = null
    override val needsCameraPermission: Boolean = false
    /** Left out for store screenshots (`-Pjasscardeye.screenshots=true`), like `JASSCARDEYE_SCREENSHOTS` on iOS. */
    override val notice: String? = if (BuildConfig.SCREENSHOTS) null else "Emulator: Testvideo statt Kamera (Endlosschleife)."
    override val hasTorch: Boolean = false
    override fun setTorch(on: Boolean) {}
    override suspend fun availableLenses(): List<CameraLens> = emptyList()

    /**
     * The switch of the loop that is playing, null when none is. Every start gets a switch of its own, so a
     * loop still winding down from the last session can never be switched back on by the next one.
     */
    private var playing: AtomicBoolean? = null

    private val path: String
        get() = BuildConfig.TEST_VIDEO.ifEmpty { File(context.getExternalFilesDir(null), DEFAULT_NAME).path }

    override suspend fun start(owner: LifecycleOwner, lens: CameraLens): FrameSource.Problem? {
        stop()
        val file = File(path)
        if (!file.exists()) return FrameSource.Problem.Other("Testvideo nicht gefunden: $path")
        val retriever = MediaMetadataRetriever()
        val (count, duration) = withContext(Dispatchers.IO) {
            try {
                retriever.setDataSource(file.path)
                val count = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_FRAME_COUNT)?.toIntOrNull() ?: 0
                val duration = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toDoubleOrNull() ?: 0.0
                count to duration
            } catch (_: Exception) {
                0 to 0.0
            }
        }
        if (count <= 0 || duration <= 0) {
            retriever.release()
            return FrameSource.Problem.Other("Testvideo enthält keine Videospur.")
        }
        val active = AtomicBoolean(true)
        playing = active
        Thread({ play(retriever, count, count / (duration / 1000.0), active) }, "jasscardeye-video").start()
        return null
    }

    override fun stop() {
        playing?.set(false)
        playing = null
    }

    /**
     * The loop of one session, on its own thread until [active] is switched off. It releases the retriever
     * it was given when it ends - nothing else touches that retriever, so nothing can release it under a
     * frame that is still being decoded.
     */
    private fun play(retriever: MediaMetadataRetriever, frameCount: Int, frameRate: Double, active: AtomicBoolean) {
        val busy = AtomicBoolean(false)
        val begin = SystemClock.elapsedRealtime()
        try {
            while (active.get()) {
                // Follows the clock, not a frame counter: frames that arrive while the detector is busy are
                // simply not fetched - the same backpressure the camera applies.
                val index = (((SystemClock.elapsedRealtime() - begin) / 1000.0) * frameRate).toInt() % frameCount
                val decoded = try { retriever.getFrameAtIndex(index) } catch (_: Exception) { null }
                if (decoded != null) {
                    val area = FrameGeometry.square(decoded.width, decoded.height)
                    val square = Bitmap.createBitmap(decoded, area.left, area.top, area.width(), area.height())
                    frame = square
                    if (busy.compareAndSet(false, true)) {
                        executor.execute {
                            try { if (active.get()) onFrame?.invoke(square) } finally { busy.set(false) }
                        }
                    }
                }
                SystemClock.sleep(FRAME_INTERVAL_MS)
            }
        } catch (_: RejectedExecutionException) {
            // The analysis thread is gone with its model: the app is closing this screen for good.
        } finally {
            retriever.release()
        }
    }

    companion object {
        private const val DEFAULT_NAME = "test-video.mp4"
        private const val FRAME_INTERVAL_MS = 20L

        /**
         * Whether this run feeds the video instead of the camera: a debug build on an emulator. A release
         * build never does, which is the compile-time guarantee `#if targetEnvironment(simulator)` gives iOS.
         */
        fun shouldUse(): Boolean = BuildConfig.DEBUG && AppInfo.isEmulator && !BuildConfig.EMULATOR_CAMERA
    }
}
