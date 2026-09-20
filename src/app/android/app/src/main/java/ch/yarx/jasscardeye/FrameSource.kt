package ch.yarx.jasscardeye

import android.graphics.Bitmap
import androidx.lifecycle.LifecycleOwner

// Android only. iOS picks its frame source at compile time with `#if targetEnvironment(simulator)`; here the
// choice is made once at launch, and everything that differs between the two sources sits behind this type.

/**
 * Where the analysed squares come from: the camera on a phone, or - in a debug build on the emulator, which
 * films an empty virtual room - a looping test video. The live loop drives either the same way and never
 * learns which one it has; only the viewfinder, which has to draw it, tells them apart.
 */
sealed interface FrameSource {

    /** Called on the analysis thread with the upright framing square of every analysed frame. */
    var onFrame: ((Bitmap) -> Unit)?

    /** Called with the torch state the device reports - which is not always the one asked for. */
    var onTorchChanged: ((Boolean) -> Unit)?

    /** Whether the camera permission is needed before [start]. */
    val needsCameraPermission: Boolean

    /** A sentence for the viewfinder while this source runs, null when there is nothing to say. */
    val notice: String?

    /** Whether this source can light the table. False until it has started. */
    val hasTorch: Boolean

    /** Asks for the torch; the answer arrives through [onTorchChanged]. */
    fun setTorch(on: Boolean)

    /** The lenses to offer, in order. Empty or a single one means the picker stays hidden. */
    suspend fun availableLenses(): List<CameraLens>

    /** Starts delivering frames. Returns why it could not, null on success. Safe to call again. */
    suspend fun start(owner: LifecycleOwner, lens: CameraLens): Problem?

    fun stop()

    /** Why a session could not be set up. Refusal is its own case because the person can do something about it. */
    sealed class Problem {
        data object Denied : Problem()
        data class Other(val text: String) : Problem()

        val message: String get() = when (this) {
            Denied -> "Kein Kamerazugriff."
            is Other -> text
        }
        val isDenied: Boolean get() = this is Denied
    }

    companion object {
        /** The camera, except in a debug build on the emulator - see [VideoFrameSource.shouldUse]. */
        fun make(context: android.content.Context, executor: java.util.concurrent.ExecutorService): FrameSource =
            if (VideoFrameSource.shouldUse()) VideoFrameSource(context, executor) else CameraService(context, executor)
    }
}
