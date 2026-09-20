package ch.yarx.jasscardeye

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.RectF
import android.util.Log
import java.io.Closeable
import java.io.FileInputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.MappedByteBuffer
import java.nio.channels.FileChannel
import org.tensorflow.lite.Interpreter
import org.tensorflow.lite.gpu.CompatibilityList
import org.tensorflow.lite.gpu.GpuDelegate
import kotlin.math.min

// Port of src/app/ios/Sources/CardRecognizer.swift.

/**
 * Turns a frame into recognised cards. The three variants differ only behind this one call, so the live
 * loop - framing square, timing, stability rule, pile - is written once and never learns which model it
 * is driving.
 *
 * [square] is the framing square, already cut out of the frame - by CameraX's 1:1 viewport, or by
 * `FrameGeometry.square` for the emulator's test video; the
 * recogniser scales it to the model's 640 pixels, so the square gets the full model resolution and
 * returned boxes are normalised to it - the same property Vision's region of interest gives iOS.
 * Called from the analysis thread only: an interpreter is not safe to share across threads.
 */
interface CardRecognizer : Closeable {
    fun recognize(square: Bitmap, minConfidence: Float): List<Detection>

    /** Which hardware runs the model(s), for the report: "GPU" or "CPU". */
    val backend: String
}

/**
 * One LiteRT model with its class names.
 *
 * The GPU delegate where the device supports it, the CPU (XNNPACK, four threads) otherwise - the
 * counterpart of Core ML's `computeUnits = .all`. Input is NCHW float32 RGB in [0, 1], as exported by
 * `src/training/export.py --format litert`; the class names come from the sidecar written next to the model
 * from the same `names` the training used, so the class-ID contract holds end to end.
 *
 * The shapes are checked when the model is loaded, not trusted: a model and a label file that do not belong
 * together would otherwise decode into plausible nonsense frame after frame. A mismatch fails the load with
 * a sentence the variant picker shows.
 */
class LiteRtModel private constructor(
    val name: String,
    private val interpreter: Interpreter,
    private val gpu: GpuDelegate?,
    val names: List<String>,
) : Closeable {

    val backend: String get() = if (gpu != null) "GPU" else "CPU"

    private val inputShape = interpreter.getInputTensor(0).shape()

    init {
        verify(inputShape.size == 4 && inputShape[0] == 1 && inputShape[1] == 3 && inputShape[2] == inputShape[3]) {
            "Eingabe ${inputShape.contentToString()} statt [1, 3, n, n]"
        }
    }

    val inputSize: Int = inputShape[3]

    /** The output tensor's shape, e.g. `[1, 76, 8400]` for the detector. */
    val shape: IntArray = interpreter.getOutputTensor(0).shape()

    private val inputValues = FloatArray(inputShape.fold(1) { a, b -> a * b })
    private val outputValues = FloatArray(shape.fold(1) { a, b -> a * b })
    private val input: ByteBuffer = ByteBuffer.allocateDirect(4 * inputValues.size).order(ByteOrder.nativeOrder())
    private val output: ByteBuffer = ByteBuffer.allocateDirect(4 * outputValues.size).order(ByteOrder.nativeOrder())
    private val pixels = IntArray(inputSize * inputSize)
    private val scaled = Bitmap.createBitmap(inputSize, inputSize, Bitmap.Config.ARGB_8888)
    private val canvas = Canvas(scaled)
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG)

    /** Fails the load, naming the model, when [ok] is false. */
    private fun verify(ok: Boolean, problem: () -> String) {
        if (!ok) throw IllegalStateException("$PREFIX$name: ${problem()}")
    }

    /** A head of `[1, channels, anchors]` - the detector (4 + classes) or the locator (4 + 1 + angle). */
    fun checkAnchorOutput(channels: Int) = verify(shape.size == 3 && shape[0] == 1 && shape[1] == channels) {
        "Ausgabe ${shape.contentToString()} passt nicht zu ${names.size} Klassen (erwartet [1, $channels, n])"
    }

    /** A classifier head of `[1, classes]`. */
    fun checkClassOutput() = verify(shape.contentEquals(intArrayOf(1, names.size))) {
        "Ausgabe ${shape.contentToString()} passt nicht zu ${names.size} Klassen (erwartet [1, ${names.size}])"
    }

    /**
     * Runs the model on [image], scaled to fit the square input - "scale fit", as Vision is told on
     * iOS: a square image fills it, anything else is centred on black. The returned array is reused by
     * the next call.
     */
    fun run(image: Bitmap): FloatArray {
        canvas.drawColor(Color.BLACK)
        val scale = min(inputSize.toFloat() / image.width, inputSize.toFloat() / image.height)
        val w = image.width * scale
        val h = image.height * scale
        val left = (inputSize - w) / 2
        val top = (inputSize - h) / 2
        canvas.drawBitmap(image, null, RectF(left, top, left + w, top + h), paint)
        scaled.getPixels(pixels, 0, inputSize, 0, 0, inputSize, inputSize)

        val plane = inputSize * inputSize
        for (i in 0 until plane) {
            val p = pixels[i]
            inputValues[i] = ((p shr 16) and 0xFF) / 255f
            inputValues[i + plane] = ((p shr 8) and 0xFF) / 255f
            inputValues[i + 2 * plane] = (p and 0xFF) / 255f
        }
        input.rewind()
        input.asFloatBuffer().put(inputValues)
        output.rewind()
        interpreter.run(input, output)
        output.rewind()
        output.asFloatBuffer().get(outputValues)
        return outputValues
    }

    /** [run] with the result read as a head of `[1, channels, anchors]`. */
    fun runAnchors(image: Bitmap): AnchorTensor = AnchorTensor(run(image), shape[1], shape[2])

    override fun close() {
        interpreter.close()
        gpu?.close()
    }

    companion object {
        private const val TAG = "JassCardEye"
        private val PREFIX = ModelCatalog.PREFIX

        fun load(context: Context, name: String): LiteRtModel {
            val file = "${ModelCatalog.FOLDER}/$PREFIX$name.tflite"
            val mapped = context.assets.openFd(file).use { fd ->
                FileInputStream(fd.fileDescriptor).channel.use { channel ->
                    channel.map(FileChannel.MapMode.READ_ONLY, fd.startOffset, fd.declaredLength)
                }
            }
            val names = context.assets.open("${ModelCatalog.FOLDER}/$PREFIX$name.labels.txt")
                .bufferedReader().readLines().map { it.trim() }.filter { it.isNotEmpty() }

            val (interpreter, gpu) = interpreter(mapped, name)
            return try {
                LiteRtModel(name, interpreter, gpu, names)
            } catch (error: Throwable) {
                interpreter.close()
                gpu?.close()
                throw error
            }
        }

        /**
         * The GPU first, where the device vouches for it; a delegate that fails to build on a device that
         * claimed support falls back to the CPU rather than leaving the variant unusable.
         */
        private fun interpreter(model: MappedByteBuffer, name: String): Pair<Interpreter, GpuDelegate?> {
            CompatibilityList().use { compatibility ->
                if (compatibility.isDelegateSupportedOnThisDevice) {
                    var delegate: GpuDelegate? = null
                    try {
                        delegate = GpuDelegate(compatibility.bestOptionsForThisDevice)
                        return Interpreter(model, Interpreter.Options().addDelegate(delegate)) to delegate
                    } catch (error: Throwable) {
                        Log.w(TAG, "GPU delegate for $name failed, using the CPU: $error")
                        delegate?.close()
                    }
                }
            }
            // XNNPACK stays off on the emulator only: on Apple Silicon hosts its CPU detection picks
            // instructions the virtual ARM processor does not have, and the process dies with SIGILL
            // while the tensors are allocated. LiteRT's built-in kernels are slower but run anywhere,
            // and the emulator is for exercising the pipeline, not for measuring it.
            val options = Interpreter.Options().setNumThreads(4).setUseXNNPACK(!AppInfo.isEmulator)
            return Interpreter(model, options) to null
        }
    }
}

// MARK: - C: one-pass detector

/**
 * Variant C. The 72-class detector. Its LiteRT export carries no NMS, so the raw `[1, 4 + classes, 8400]`
 * tensor is decoded here (see [TensorDecoding]).
 *
 * Decoded to match what the NMS stage of the Core ML export hands iOS with its defaults - candidates from
 * 0.25, suppression per class at IoU 0.7, strongest first - and only then held against the caller's
 * threshold. The app reads the first detection that survives the deck filter, so the order and the
 * suppression are what matter, and both are the same as on iOS, which also orders by the best class's score
 * rather than by Vision's sum over all classes.
 */
class DetectorRecognizer(private val model: LiteRtModel) : CardRecognizer {

    init {
        model.checkAnchorOutput(channels = 4 + model.names.size)
    }

    override val backend: String get() = model.backend

    override fun recognize(square: Bitmap, minConfidence: Float): List<Detection> {
        val candidates = TensorDecoding.detectorCandidates(model.runAnchors(square), CANDIDATE_FLOOR)
        return TensorDecoding.suppressPerClass(candidates, IOU_THRESHOLD, MAX_DETECTIONS)
            .filter { it.score >= minConfidence }
            .map { Detection(model.names[it.classIndex], it.score, it.box) }
    }

    override fun close() = model.close()

    companion object {
        /** The Core ML export's NMS defaults (`conf=0.25`, `iou=0.7`, per class), which iOS never overrides. */
        private const val CANDIDATE_FLOOR = 0.25f
        private const val IOU_THRESHOLD = 0.7f
        private const val MAX_DETECTIONS = 20
    }
}

// MARK: - A: whole-image classifier

/**
 * Variant A. One classifier over the whole framing square - a label, no box. The negatives were trained
 * under a `none` class, so a top prediction of `none` means "no card", the same abstention the detector
 * gets from simply finding nothing.
 */
class ClassifierRecognizer(private val model: LiteRtModel) : CardRecognizer {

    init {
        model.checkClassOutput()
    }

    override val backend: String get() = model.backend

    override fun recognize(square: Bitmap, minConfidence: Float): List<Detection> {
        val scores = model.run(square)
        val best = TensorDecoding.argmax(scores, model.names.size)
        val label = model.names[best]
        if (scores[best] < minConfidence || label == "none") return emptyList()
        // A does not localise: the detection is the whole framing square, so the overlay outlines it.
        return listOf(Detection(label, scores[best], Box(0f, 0f, 1f, 1f)))
    }

    override fun close() = model.close()
}

// MARK: - B: two stages

/**
 * Variant B. Stage one locates the card, stage two classifies the crop rectified from its oriented box.
 * The whole call is timed by the live loop, so B's reported latency already includes both models and the
 * perspective correction between them - the question the variant was built to answer in the thesis. Kept in
 * the code and trainable; a release bundles only C.
 *
 * A caveat carried knowingly: the oriented box has a 180° ambiguity, so a face card can be rectified
 * upside down and misread. It is a real property of the two-stage approach, not a bug to hide.
 */
class TwoStageRecognizer(private val locator: LiteRtModel, private val classifier: LiteRtModel) : CardRecognizer {

    init {
        // cx, cy, w, h, one confidence per class (the locator has one: "card"), angle.
        locator.checkAnchorOutput(channels = 4 + locator.names.size + 1)
        classifier.checkClassOutput()
    }

    override val backend: String get() = "${locator.backend}+${classifier.backend}"

    override fun recognize(square: Bitmap, minConfidence: Float): List<Detection> {
        val card = TensorDecoding.mostConfidentCard(locator.runAnchors(square), LOCATE_FLOOR) ?: return emptyList()
        val corners = card.corners(square.width.toFloat(), square.height.toFloat())
        val crop = rectify(square, corners) ?: return emptyList()
        val scores = try { classifier.run(crop) } finally { crop.recycle() }

        val best = TensorDecoding.argmax(scores, classifier.names.size)
        if (scores[best] < minConfidence) return emptyList()

        // The label is stage two's; the box is the axis-aligned hull of stage one's quad, normalised to
        // the square - the same space variant C reports. Confidence is the weaker of the two.
        return listOf(Detection(classifier.names[best], min(card.confidence, scores[best]),
            TensorDecoding.hull(corners, square.width.toFloat(), square.height.toFloat())))
    }

    /** Rectifies the card to an upright image via its four corners - the counterpart of CoreImage's perspective correction. */
    private fun rectify(image: Bitmap, corners: FloatArray): Bitmap? {
        val (width, height) = TensorDecoding.rectifiedSize(corners)
        if (width < 8 || height < 8) return null

        val matrix = Matrix()
        val target = floatArrayOf(0f, 0f, width.toFloat(), 0f, width.toFloat(), height.toFloat(), 0f, height.toFloat())
        if (!matrix.setPolyToPoly(corners, 0, target, 0, 4)) return null
        val output = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        Canvas(output).drawBitmap(image, matrix, Paint(Paint.FILTER_BITMAP_FLAG))
        return output
    }

    override fun close() {
        locator.close()
        classifier.close()
    }

    companion object {
        /** Finding a card is easier than reading it, so stage one keeps its own low floor. */
        private const val LOCATE_FLOOR = 0.25f
    }
}
