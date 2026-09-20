package ch.yarx.jasscardeye

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import kotlin.math.PI

/**
 * Holds the LiteRT decoding to what the Core ML export does for iOS. The detector's suppression is the part
 * that is easy to get subtly wrong: suppressing across classes, or at a different overlap, still produces a
 * plausible box on screen - just not the one iOS would have shown.
 *
 *     ./gradlew :app:testDebugUnitTest
 */
class TensorDecodingTest {

    /** A `[1, channels, anchors]` tensor from one column of values per anchor. */
    private fun tensor(vararg anchors: FloatArray): AnchorTensor {
        val channels = anchors.first().size
        val values = FloatArray(channels * anchors.size) { i -> anchors[i % anchors.size][i / anchors.size] }
        return AnchorTensor(values, channels, anchors.size)
    }

    private fun box(left: Float, top: Float, right: Float, bottom: Float, classIndex: Int, score: Float) =
        ScoredBox(classIndex, score, Box(left, top, right, bottom))

    @Test
    fun tensorIsReadChannelByChannel() {
        val t = AnchorTensor(floatArrayOf(1f, 2f, 3f, 4f, 5f, 6f), channels = 2, anchors = 3)
        assertEquals(1f, t[0, 0])
        assertEquals(3f, t[0, 2])
        assertEquals(4f, t[1, 0])
        assertEquals(6f, t[1, 2])
    }

    @Test
    fun detectorTakesEachAnchorsBestClassAboveTheFloor() {
        val t = tensor(
            floatArrayOf(0.5f, 0.5f, 0.2f, 0.4f, 0.1f, 0.9f, 0.3f),   // class 1 at 0.9
            floatArrayOf(0.2f, 0.2f, 0.1f, 0.1f, 0.2f, 0.1f, 0.2f),   // nothing above 0.25
        )
        val candidates = TensorDecoding.detectorCandidates(t, floor = 0.25f)
        assertEquals(listOf(ScoredBox(1, 0.9f, Box(0.4f, 0.3f, 0.6f, 0.7f))), candidates)
    }

    @Test
    fun overlappingBoxesOfDifferentClassesBothSurvive() {
        val kept = TensorDecoding.suppressPerClass(
            listOf(box(0f, 0f, 1f, 1f, classIndex = 0, score = 0.9f), box(0f, 0f, 1f, 1f, classIndex = 1, score = 0.8f)),
            iouThreshold = 0.7f, limit = 20,
        )
        assertEquals(listOf(0, 1), kept.map { it.classIndex })
    }

    @Test
    fun theWeakerOfTwoOverlappingBoxesOfOneClassIsDropped() {
        val kept = TensorDecoding.suppressPerClass(
            listOf(box(0f, 0f, 1f, 1f, 0, 0.6f), box(0f, 0f, 1f, 0.9f, 0, 0.9f)),   // IoU 0.9
            iouThreshold = 0.7f, limit = 20,
        )
        assertEquals(listOf(0.9f), kept.map { it.score })
    }

    @Test
    fun anOverlapAtOrBelowTheThresholdIsKept() {
        // IoU 0.5 - below Core ML's 0.7, so both boxes stay.
        val kept = TensorDecoding.suppressPerClass(
            listOf(box(0f, 0f, 1f, 1f, 0, 0.9f), box(0f, 0f, 1f, 0.5f, 0, 0.8f)),
            iouThreshold = 0.7f, limit = 20,
        )
        assertEquals(2, kept.size)
    }

    @Test
    fun suppressionSortsStrongestFirstAndStopsAtTheLimit() {
        val candidates = (0 until 5).map { box(it.toFloat(), 0f, it + 0.5f, 0.5f, 0, it / 10f) }
        val kept = TensorDecoding.suppressPerClass(candidates, iouThreshold = 0.7f, limit = 3)
        assertEquals(listOf(0.4f, 0.3f, 0.2f), kept.map { it.score })
    }

    @Test
    fun argmaxLooksOnlyAtTheClasses() {
        assertEquals(2, TensorDecoding.argmax(floatArrayOf(0.1f, 0.2f, 0.7f)))
        assertEquals(1, TensorDecoding.argmax(floatArrayOf(0.1f, 0.2f, 0.7f), count = 2))
    }

    @Test
    fun locatorPicksTheMostConfidentCardWithItsAngle() {
        val t = tensor(
            floatArrayOf(0.5f, 0.5f, 0.2f, 0.4f, 0.6f, 0.1f),
            floatArrayOf(0.3f, 0.4f, 0.1f, 0.2f, 0.8f, 0.5f),
        )
        assertEquals(OrientedBox(0.3f, 0.4f, 0.1f, 0.2f, 0.5f, 0.8f), TensorDecoding.mostConfidentCard(t, floor = 0.25f))
        assertNull(TensorDecoding.mostConfidentCard(t, floor = 0.9f))
    }

    @Test
    fun cornersRunClockwiseFromTopLeftAndTurnWithTheAngle() {
        val upright = OrientedBox(0.5f, 0.5f, 0.4f, 0.2f, angle = 0f, confidence = 1f)
        assertArrayEquals(floatArrayOf(30f, 40f, 70f, 40f, 70f, 60f, 30f, 60f), upright.corners(100f, 100f), 1e-4f)

        // A quarter turn stands the card on its side: the hull on the square now runs down, while the crop,
        // measured along the card's own edges, comes out the same size as before.
        val turned = upright.copy(angle = (PI / 2).toFloat()).corners(100f, 100f)
        assertEquals(40 to 20, TensorDecoding.rectifiedSize(turned))
        assertEquals(Box(0.4f, 0.3f, 0.6f, 0.7f).toString(), roundedBox(TensorDecoding.hull(turned, 100f, 100f)).toString())
    }

    private fun roundedBox(b: Box) = Box(round(b.left), round(b.top), round(b.right), round(b.bottom))
    private fun round(v: Float) = Math.round(v * 1000) / 1000f
}
