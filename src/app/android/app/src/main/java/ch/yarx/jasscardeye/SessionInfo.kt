package ch.yarx.jasscardeye

import android.content.Context
import android.os.Build
import org.json.JSONObject
import java.math.BigDecimal
import java.time.Instant

// Port of src/app/ios/Sources/SessionInfo.swift.

/**
 * What a session was recorded with, written as JSON beside its recording and recognition log: device,
 * system, app and model, and the settings the pile was counting with. Sessions from different phones can only be
 * compared - in the dataset tool's *Analyse sessions* - when each says what it was recorded on and with.
 *
 * The keys are the same on iOS and described under "Session recordings" in context/architecture/data-pipeline.md.
 */
data class SessionInfo(
    val platform: String,
    val appVersion: String,
    val appBuild: String,
    val device: String,
    val system: String,
    val modelVariant: String,
    val modelRun: String?,
    val compute: String,
    val confidenceThreshold: Double,
    val stabilityRule: String,
    val stabilityFrames: Int,
    val deck: String,
    val cameraLens: String,
    val discipline: String,
    val startedAt: String,
) {
    /**
     * Snake case, sorted and indented as iOS's `JSONEncoder` writes it, so a file reads the same whichever app wrote
     * it. Written out by hand because org.json is only a stub in a unit test.
     */
    fun json(): String {
        val fields = sortedMapOf(
            "app_build" to quote(appBuild),
            "app_version" to quote(appVersion),
            "camera_lens" to quote(cameraLens),
            "compute" to quote(compute),
            "confidence_threshold" to BigDecimal.valueOf(confidenceThreshold).stripTrailingZeros().toPlainString(),
            "deck" to quote(deck),
            "device" to quote(device),
            "discipline" to quote(discipline),
            "model_run" to modelRun?.let(::quote),
            "model_variant" to quote(modelVariant),
            "platform" to quote(platform),
            "stability_frames" to stabilityFrames.toString(),
            "stability_rule" to quote(stabilityRule),
            "started_at" to quote(startedAt),
            "system" to quote(system),
        )
        return fields.filterValues { it != null }.entries
            .joinToString(separator = ",\n", prefix = "{\n", postfix = "\n}") { "  \"${it.key}\" : ${it.value}" }
    }

    companion object {
        const val PLATFORM = "Android"

        /** Manufacturer and model, "Google Pixel 8" - what tells two phones of the same year apart. */
        val deviceModel: String get() = "${Build.MANUFACTURER} ${Build.MODEL}"

        val systemVersion: String get() = "${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"

        /** A threshold as it was meant, not as a `Float` stores it: 0.6 rather than 0.6000000238. */
        fun rounded(value: Float): Double = Math.round(value * 1000.0) / 1000.0

        fun timestamp(): String = Instant.now().toString()

        /** The training run a bundled model came from, out of the models.json the export writes beside it. */
        fun modelRun(context: Context, variant: String): String? = runCatching {
            val text = context.assets.open("${ModelCatalog.FOLDER}/models.json").bufferedReader().use { it.readText() }
            JSONObject(text).getJSONObject("models").getJSONObject(variant).optString("run_id").ifEmpty { null }
        }.getOrNull()

        private fun quote(value: String): String = buildString {
            append('"')
            for (char in value) {
                when {
                    char == '"' -> append("\\\"")
                    char == '\\' -> append("\\\\")
                    char < ' ' -> append("\\u%04x".format(char.code))
                    else -> append(char)
                }
            }
            append('"')
        }
    }
}
