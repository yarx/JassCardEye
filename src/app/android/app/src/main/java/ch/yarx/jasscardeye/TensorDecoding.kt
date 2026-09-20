package ch.yarx.jasscardeye

import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.sin

// Android only. On iOS, Core ML's NMS stage and Vision do this work; LiteRT hands over the raw tensors, so
// the app decodes them itself. Kept free of Android types so TensorDecodingTest runs on the plain JVM.

/** One output of a YOLO head, `[1, channels, anchors]`, laid out channel by channel as LiteRT writes it. */
class AnchorTensor(private val values: FloatArray, val channels: Int, val anchors: Int) {
    init {
        require(values.size >= channels * anchors) { "tensor of ${values.size} values is not $channels x $anchors" }
    }

    operator fun get(channel: Int, anchor: Int): Float = values[channel * anchors + anchor]
}

/** A detector candidate: the class its anchor scores highest, that score, and the box it proposes. */
data class ScoredBox(val classIndex: Int, val score: Float, val box: Box)

/** The locator's card: centre and size normalised to the input, rotation in radians, and its confidence. */
data class OrientedBox(val cx: Float, val cy: Float, val width: Float, val height: Float, val angle: Float, val confidence: Float) {

    /**
     * The four corners TL, TR, BR, BL as `x0, y0, x1, y1, ...` in the pixels of an image [imageWidth] by
     * [imageHeight] - the layout `Matrix.setPolyToPoly` takes.
     */
    fun corners(imageWidth: Float, imageHeight: Float): FloatArray {
        val cx = cx * imageWidth
        val cy = cy * imageHeight
        val w = width * imageWidth
        val h = height * imageHeight
        val cosA = cos(angle)
        val sinA = sin(angle)
        val offsets = floatArrayOf(-w / 2, -h / 2, w / 2, -h / 2, w / 2, h / 2, -w / 2, h / 2)
        return FloatArray(8) { i ->
            val dx = offsets[i - i % 2]
            val dy = offsets[i - i % 2 + 1]
            if (i % 2 == 0) dx * cosA - dy * sinA + cx else dx * sinA + dy * cosA + cy
        }
    }
}

object TensorDecoding {

    /**
     * Reads a detector head: per anchor `cx, cy, w, h` normalised to the input, then one score per class.
     * An anchor becomes a candidate for its best class when that score is above [floor].
     */
    fun detectorCandidates(tensor: AnchorTensor, floor: Float): List<ScoredBox> {
        val classes = tensor.channels - 4
        val candidates = ArrayList<ScoredBox>()
        for (anchor in 0 until tensor.anchors) {
            var best = -1
            var bestScore = floor
            for (c in 0 until classes) {
                val score = tensor[4 + c, anchor]
                if (score > bestScore) { bestScore = score; best = c }
            }
            if (best < 0) continue
            val cx = tensor[0, anchor]
            val cy = tensor[1, anchor]
            val w = tensor[2, anchor]
            val h = tensor[3, anchor]
            candidates += ScoredBox(best, bestScore, Box(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2))
        }
        return candidates
    }

    /**
     * Non-maximum suppression **per class**, as the NMS stage of the Core ML export runs it for iOS: a box is
     * dropped only when a stronger box of the *same* class overlaps it by more than [iouThreshold]. Strongest
     * first, at most [limit].
     */
    fun suppressPerClass(candidates: List<ScoredBox>, iouThreshold: Float, limit: Int): List<ScoredBox> {
        val kept = ArrayList<ScoredBox>()
        for (candidate in candidates.sortedByDescending { it.score }) {
            if (kept.size == limit) break
            val overlapped = kept.any { it.classIndex == candidate.classIndex && it.box.iou(candidate.box) > iouThreshold }
            if (!overlapped) kept += candidate
        }
        return kept
    }

    /** Index of the largest of the first [count] values - a classifier's answer. */
    fun argmax(values: FloatArray, count: Int = values.size): Int {
        var best = 0
        for (i in 1 until count) if (values[i] > values[best]) best = i
        return best
    }

    /**
     * The single most confident card of a locator head, `[1, 6, anchors]`: `cx, cy, w, h`, confidence, angle.
     * The app reads one top card, so this is an argmax over confidence rather than NMS - as iOS's
     * `CardLocator` does. Null when nothing is above [floor].
     */
    fun mostConfidentCard(tensor: AnchorTensor, floor: Float): OrientedBox? {
        var bestAnchor = -1
        var bestConfidence = floor
        for (anchor in 0 until tensor.anchors) {
            val confidence = tensor[4, anchor]
            if (confidence > bestConfidence) { bestConfidence = confidence; bestAnchor = anchor }
        }
        if (bestAnchor < 0) return null
        return OrientedBox(tensor[0, bestAnchor], tensor[1, bestAnchor], tensor[2, bestAnchor], tensor[3, bestAnchor],
            tensor[5, bestAnchor], bestConfidence)
    }

    /** The axis-aligned hull of [corners] (as from [OrientedBox.corners]), normalised to the image. */
    fun hull(corners: FloatArray, imageWidth: Float, imageHeight: Float): Box {
        val xs = corners.filterIndexed { i, _ -> i % 2 == 0 }
        val ys = corners.filterIndexed { i, _ -> i % 2 == 1 }
        return Box(xs.min() / imageWidth, ys.min() / imageHeight, xs.max() / imageWidth, ys.max() / imageHeight)
    }

    /**
     * The size of the upright image a card with these [corners] rectifies to - the longer of each pair of
     * opposite edges, as CoreImage's perspective correction sizes it on iOS.
     */
    fun rectifiedSize(corners: FloatArray): Pair<Int, Int> {
        fun edge(a: Int, b: Int) = hypot(corners[2 * b] - corners[2 * a], corners[2 * b + 1] - corners[2 * a + 1])
        val width = max(edge(0, 1), edge(3, 2))
        val height = max(edge(0, 3), edge(1, 2))
        return width.toInt() to height.toInt()
    }
}
