package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Holds the session info to the JSON both apps write beside a recording. iOS encodes it with
 * `JSONEncoder` (snake case, sorted, indented); the dataset tool reads the keys by name, so a key spelt differently
 * here would leave a device or a setting out of every comparison.
 */
class SessionInfoTest {

    private val info = SessionInfo(
        platform = "Android", appVersion = "1.0.1", appBuild = "1109", device = "Google Pixel 8",
        system = "15 (API 35)", modelVariant = "c", modelRun = "20260101-1200-abc1234", compute = "GPU",
        confidenceThreshold = 0.6, stabilityRule = "run", stabilityFrames = 3, deck = "german", cameraLens = "wide",
        discipline = "Obenabe", startedAt = "2026-01-01T12:00:00.000Z",
    )

    @Test
    fun keysAreSortedInSnakeCaseLikeIos() {
        val json = info.json()
        assertTrue(json.startsWith("{\n  \"app_build\" : \"1109\",\n  \"app_version\" : \"1.0.1\","))
        assertTrue(json.contains("\n  \"confidence_threshold\" : 0.6,\n"))
        assertTrue(json.contains("\n  \"stability_frames\" : 3,\n"))
        assertTrue(json.endsWith("\n  \"system\" : \"15 (API 35)\"\n}"))
    }

    @Test
    fun aModelWithoutARunIsLeftOut() {
        assertFalse(info.copy(modelRun = null).json().contains("model_run"))
    }

    @Test
    fun quotesAndBackslashesAreEscaped() {
        assertTrue(info.copy(discipline = "Trumpf \"Herz\" \\ 1").json().contains("\"discipline\" : \"Trumpf \\\"Herz\\\" \\\\ 1\""))
    }

    @Test
    fun aThresholdIsWrittenAsMeantNotAsTheFloatStoresIt() {
        assertEquals(0.6, SessionInfo.rounded(0.6f), 0.0)
    }
}
