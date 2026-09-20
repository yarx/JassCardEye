package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * Holds a row of the recognition log to the format both apps share. The iOS side is checked by
 * src/tools/check_recorder.swift against a real recording; the Dataset Tool reads these rows strictly, so a
 * column that drifts on one platform would make every session from it unreadable.
 */
class SessionRecorderTest {

    @Test
    fun aFrameWithoutADetectionKeepsEveryColumn() {
        assertEquals("0,0.000,,,,,,,0\n", SessionRecorder.FrameResult().row(0, 0))
    }

    @Test
    fun aDetectionIsWrittenTopLeftInTheSquare() {
        val detection = Detection("spades_6", 0.85f, Box(left = 0.1f, top = 0.2f, right = 0.4f, bottom = 0.6f))
        assertEquals(
            "4,126.667,spades_6,0.8500,0.1000,0.2000,0.3000,0.4000,1\n",
            SessionRecorder.FrameResult(detection, committed = true).row(4, 126_667),
        )
    }

    @Test
    fun theHeaderNamesTheSameColumnsAsTheRows() {
        assertEquals(9, SessionRecorder.FrameResult.HEADER.trim().split(',').size)
        assertEquals(9, SessionRecorder.FrameResult().row(0, 0).trim().split(',').size)
    }
}
