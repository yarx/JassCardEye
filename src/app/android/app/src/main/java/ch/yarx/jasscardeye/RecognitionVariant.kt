package ch.yarx.jasscardeye

import android.content.Context

// Port of src/app/ios/Sources/RecognitionVariant.swift.

/**
 * The three approaches of the thesis. Only C is pursued: it is what a release bundles and what the app is
 * built around. A and B stay in the code and remain trainable - a build that bundles their models offers
 * them in the settings.
 *
 *   C  one detector, 72 classes: boxes and labels in a single pass.
 *   A  one classifier over the whole framing square: a label, no localisation.
 *   B  two stages: a locator finds the card, a classifier reads the rectified crop.
 */
enum class RecognitionVariant(val id: String) {
    C("c"),
    A("a"),
    B("b");

    val displayName: String
        get() = when (this) {
            C -> "C · Detektor"
            A -> "A · Klassifikation"
            B -> "B · Zweistufig"
        }

    /** The model(s) this variant needs in the assets, by base name (`JassCardEye-<name>.tflite`). */
    val requiredModels: List<String>
        get() = when (this) {
            C -> listOf("c")
            A -> listOf("a")
            B -> listOf("b1", "b2")
        }
}

/**
 * What is actually in the APK. `src/training/export.py --format litert` writes the models into
 * `assets/models/`, so which variants can run is decided at launch from the files present - a variant
 * whose models were never exported simply does not appear in the picker, exactly as on iOS.
 */
class ModelCatalog(private val context: Context) {

    /** Base names of every bundled model, e.g. `["c", "a", "b1", "b2"]`. */
    fun bundledModelNames(): Set<String> =
        (context.assets.list(FOLDER) ?: emptyArray())
            .filter { it.startsWith(PREFIX) && it.endsWith(".tflite") }
            .map { it.removePrefix(PREFIX).removeSuffix(".tflite") }
            .toSet()

    /** Variants whose every required model is present, in the fixed order C, A, B. */
    fun availableVariants(): List<RecognitionVariant> {
        val present = bundledModelNames()
        return RecognitionVariant.entries.filter { variant -> variant.requiredModels.all { it in present } }
    }

    /**
     * Builds the recogniser for a variant, loading its model(s) from the assets. Throws when a model is
     * missing or fails to load, so the caller can show why a variant is unavailable.
     */
    fun makeRecognizer(variant: RecognitionVariant): CardRecognizer = when (variant) {
        RecognitionVariant.C -> load("c") { DetectorRecognizer(it) }
        RecognitionVariant.A -> load("a") { ClassifierRecognizer(it) }
        RecognitionVariant.B -> load("b1") { locator -> load("b2") { TwoStageRecognizer(locator, it) } }
    }

    /** Loads a model and hands it to [build] - and closes it again if anything after the load fails. */
    private inline fun <T> load(name: String, build: (LiteRtModel) -> T): T {
        val model = LiteRtModel.load(context, name)
        return try { build(model) } catch (error: Throwable) { model.close(); throw error }
    }

    companion object {
        const val FOLDER = "models"
        const val PREFIX = "JassCardEye-"
    }
}
