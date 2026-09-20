package ch.yarx.jasscardeye

// Port of src/app/ios/Sources/Detection.swift.

/**
 * One recognised card: class label, confidence and the box normalised to the framing square.
 *
 * One deliberate difference to iOS: the box has a **top-left** origin. iOS keeps Vision's bottom-left
 * origin and flips when drawing; Android's bitmaps, the model's output and Compose all count from the
 * top, so there is nothing to flip here. Every variant produces this same shape, so the overlay and
 * the stability rule stay identical no matter which model is running.
 */
data class Detection(val label: String, val confidence: Float, val box: Box)

/**
 * An axis-aligned box, normalised to the framing square, origin top-left. A plain type rather than
 * `RectF`, so the decoding that produces it runs in a JVM unit test without an Android framework.
 */
data class Box(val left: Float, val top: Float, val right: Float, val bottom: Float) {
    val width: Float get() = right - left
    val height: Float get() = bottom - top

    /** Intersection over union - how much two boxes are the same box. */
    fun iou(other: Box): Float {
        val intersection = maxOf(0f, minOf(right, other.right) - maxOf(left, other.left)) *
            maxOf(0f, minOf(bottom, other.bottom) - maxOf(top, other.top))
        val union = width * height + other.width * other.height - intersection
        return if (union <= 0f) 0f else intersection / union
    }
}
