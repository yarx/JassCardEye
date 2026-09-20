package ch.yarx.jasscardeye

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Matrix
import android.graphics.Paint
import android.hardware.display.DisplayManager
import android.util.Log
import android.util.Rational
import android.view.Display
import android.view.Surface
import androidx.camera.core.AspectRatio
import androidx.camera.core.Camera
import androidx.camera.core.CameraInfo
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.core.TorchState
import androidx.camera.core.UseCaseGroup
import androidx.camera.core.ViewPort
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.lifecycle.LifecycleOwner
import java.util.concurrent.ExecutorService
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.roundToInt
import kotlinx.coroutines.suspendCancellableCoroutine
import androidx.core.content.ContextCompat

// Port of src/app/ios/Sources/CameraService.swift.

/**
 * Owns the camera binding and delivers the framing square of every analysed frame on the analysis thread.
 *
 * **One coordinate system.** Preview and analysis are bound as one `UseCaseGroup` under a square `ViewPort`,
 * so CameraX crops both to the same centred square: the preview shows exactly the region that is analysed,
 * and a detection box normalised to it maps straight onto the picture - the property the iOS viewfinder gets
 * from `resizeAspectFill` over the region handed to Vision.
 *
 * **Backpressure** is the analysis use case's `STRATEGY_KEEP_ONLY_LATEST`: while a frame is being analysed,
 * newer frames are dropped rather than queued - so the displayed FPS is the real analysis rate, and latency
 * cannot pile up (iOS: `alwaysDiscardsLateVideoFrames`).
 */
class CameraService(private val context: Context, private val executor: ExecutorService) : FrameSource {

    @Volatile override var onFrame: ((Bitmap) -> Unit)? = null
    override var onTorchChanged: ((Boolean) -> Unit)? = null
    override val needsCameraPermission: Boolean = true
    override val notice: String? = null

    private var provider: ProcessCameraProvider? = null
    private var preview: Preview? = null
    private var surfaceProvider: Preview.SurfaceProvider? = null
    private var camera: Camera? = null
    private var torchObserverOwner: LifecycleOwner? = null

    private suspend fun provider(): ProcessCameraProvider = provider ?: suspendCancellableCoroutine { continuation ->
        val future = ProcessCameraProvider.getInstance(context)
        future.addListener({
            try {
                continuation.resume(future.get().also { provider = it })
            } catch (error: Exception) {
                continuation.resumeWithException(error)
            }
        }, ContextCompat.getMainExecutor(context))
    }

    private fun backCameras(provider: ProcessCameraProvider): List<CameraInfo> =
        provider.availableCameraInfos.filter { it.lensFacing == CameraSelector.LENS_FACING_BACK }

    /**
     * The lenses this phone actually has, in the order they are offered. Asked of the hardware rather than
     * assumed: many phones have no ultra-wide at all. It shows up either as a camera of its own with a
     * zoom ratio below 0.9, or as a logical camera whose minimum zoom ratio is below 0.9.
     */
    override suspend fun availableLenses(): List<CameraLens> = try {
        val backs = backCameras(provider())
        when {
            backs.isEmpty() -> emptyList()
            backs.any { it.intrinsicZoomRatio < 0.9f || (it.zoomState.value?.minZoomRatio ?: 1f) < 0.9f } ->
                listOf(CameraLens.WIDE, CameraLens.ULTRA_WIDE)
            else -> listOf(CameraLens.WIDE)
        }
    } catch (error: Exception) {
        Log.w(TAG, "Lens discovery failed", error)
        emptyList()
    }

    /** Whether this phone can light the table. False until a camera is bound. */
    override val hasTorch: Boolean get() = camera?.cameraInfo?.hasFlashUnit() == true

    /**
     * Switches the torch. The result arrives through [onTorchChanged]: Android refuses the torch while the
     * phone is too warm, and turns it off by itself, so the button follows the camera's report.
     */
    override fun setTorch(on: Boolean) {
        val camera = camera ?: return
        camera.cameraControl.enableTorch(on)
        if (on) {
            // Not full power. The phone is held a hand's width above the pile, and at that range the full beam
            // blows the white of a card out until the pips go with it. Only some phones let the level be set.
            val max = camera.cameraInfo.maxTorchStrengthLevel
            if (max > 1) {
                runCatching { camera.cameraControl.setTorchStrengthLevel((max * TORCH_LEVEL).roundToInt().coerceAtLeast(1)) }
            }
        }
    }

    /**
     * Where the preview is drawn: the scan screen's view, handed over when the view is created. Kept, so a
     * session bound before or after the view appeared draws into it either way.
     */
    fun attachPreview(view: PreviewView) {
        surfaceProvider = view.surfaceProvider
        preview?.setSurfaceProvider(surfaceProvider)
    }

    /** Lets go of a view that is leaving the screen, so it is not held beyond its activity. */
    fun detachPreview(view: PreviewView) {
        if (surfaceProvider !== view.surfaceProvider) return
        surfaceProvider = null
        preview?.setSurfaceProvider(null)
    }

    /**
     * Binds preview and analysis around the chosen lens. Returns the problem for the UI, null on success.
     * Safe to call again: a new session unbinds whatever an earlier one left behind.
     */
    override suspend fun start(owner: LifecycleOwner, lens: CameraLens): FrameSource.Problem? {
        val provider = try { provider() } catch (error: Exception) {
            return FrameSource.Problem.Other("Keine Kamera gefunden - die App braucht ein echtes Gerät.")
        }
        val backs = backCameras(provider)
        if (backs.isEmpty()) return FrameSource.Problem.Other("Keine Kamera gefunden - die App braucht ein echtes Gerät.")

        // The ultra-wide as a camera of its own where the phone exposes one; otherwise the standard camera,
        // zoomed out to its minimum ratio below. Falls back to the standard lens: a setting can outlive its phone.
        val ultra = if (lens == CameraLens.ULTRA_WIDE) backs.minByOrNull { it.intrinsicZoomRatio }
            ?.takeIf { it.intrinsicZoomRatio < 0.9f } else null
        val selector = if (ultra != null) {
            CameraSelector.Builder().addCameraFilter { infos -> infos.filter { it == ultra } }.build()
        } else {
            CameraSelector.DEFAULT_BACK_CAMERA
        }

        val resolution = ResolutionSelector.Builder()
            .setAspectRatioStrategy(AspectRatioStrategy(AspectRatio.RATIO_16_9, AspectRatioStrategy.FALLBACK_RULE_AUTO))
            .setResolutionStrategy(ResolutionStrategy(lens.targetResolution, ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER))
            .build()

        val preview = Preview.Builder().setResolutionSelector(resolution).build()
        preview.setSurfaceProvider(surfaceProvider)
        this.preview = preview

        val analysis = ImageAnalysis.Builder()
            .setResolutionSelector(resolution)
            .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888)
            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
            .build()
        analysis.setAnalyzer(executor, ::analyze)

        // The square both use cases are cropped to - see the class comment.
        val rotation = context.getSystemService(DisplayManager::class.java)
            ?.getDisplay(Display.DEFAULT_DISPLAY)?.rotation ?: Surface.ROTATION_0
        val group = UseCaseGroup.Builder()
            .addUseCase(preview)
            .addUseCase(analysis)
            .setViewPort(ViewPort.Builder(Rational(1, 1), rotation).build())
            .build()

        return try {
            provider.unbindAll()
            val bound = provider.bindToLifecycle(owner, selector, group)
            camera = bound
            if (lens == CameraLens.ULTRA_WIDE && ultra == null) {
                bound.cameraInfo.zoomState.value?.minZoomRatio?.let { bound.cameraControl.setZoomRatio(it) }
            }
            torchObserverOwner = owner
            bound.cameraInfo.torchState.observe(owner) { state -> onTorchChanged?.invoke(state == TorchState.ON) }
            null
        } catch (error: Exception) {
            Log.e(TAG, "Binding the camera failed", error)
            FrameSource.Problem.Other("Kamera konnte nicht eingebunden werden.")
        }
    }

    override fun stop() {
        torchObserverOwner?.let { owner -> camera?.cameraInfo?.torchState?.removeObservers(owner) }
        torchObserverOwner = null
        provider?.unbindAll()
        camera = null
        preview = null
    }

    // Reused between frames: at thirty frames a second, a fresh bitmap per frame is garbage the collector
    // would have to chase through the whole count.
    private var full: Bitmap? = null
    private var square: Bitmap? = null
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG)

    /**
     * Cuts CameraX's square crop out of the RGBA buffer and turns it upright, so image, preview and overlay
     * agree and nothing downstream has to know about sensor orientation.
     */
    private fun analyze(proxy: ImageProxy) {
        try {
            val handler = onFrame ?: return
            val plane = proxy.planes[0]
            val rowStride = plane.rowStride / 4
            val bufferBitmap = full?.takeIf { it.width == rowStride && it.height == proxy.height }
                ?: Bitmap.createBitmap(rowStride, proxy.height, Bitmap.Config.ARGB_8888).also { full = it }
            plane.buffer.rewind()
            bufferBitmap.copyPixelsFromBuffer(plane.buffer)

            val crop = proxy.cropRect
            val side = minOf(crop.width(), crop.height())
            val target = square?.takeIf { it.width == side } ?: Bitmap.createBitmap(side, side, Bitmap.Config.ARGB_8888).also { square = it }
            val matrix = Matrix().apply {
                postTranslate(-(crop.left + (crop.width() - side) / 2f), -(crop.top + (crop.height() - side) / 2f))
                postRotate(proxy.imageInfo.rotationDegrees.toFloat(), side / 2f, side / 2f)
            }
            Canvas(target).drawBitmap(bufferBitmap, matrix, paint)
            handler(target)
        } catch (error: Exception) {
            Log.e(TAG, "Frame conversion failed", error)
        } finally {
            proxy.close()
        }
    }

    companion object {
        private const val TAG = "JassCardEye"

        /** Bright enough for a dark room, dim enough not to burn out a card at arm's length - as on iOS. */
        private const val TORCH_LEVEL = 0.6
    }
}
