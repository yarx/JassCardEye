package ch.yarx.jasscardeye

import android.util.Size
import androidx.annotation.StringRes

// Port of src/app/ios/Sources/CameraLens.swift.

/**
 * Which back camera films the pile.
 *
 * The default 1× lens makes a phone held over a table feel too close: to get a whole pile into the
 * framing square you have to hold it uncomfortably high. The ultra-wide sees roughly twice as much at the
 * same height - at the price of the card landing on fewer pixels, which is why choosing it also raises the
 * capture resolution.
 */
enum class CameraLens(val id: String) {
    /** The standard lens. */
    WIDE("wide"),

    /** The 0.5× lens. Not on every phone, so it is only offered where it exists. */
    ULTRA_WIDE("ultraWide");

    @get:StringRes
    val displayName: Int
        get() = when (this) {
            WIDE -> R.string.lens_wide
            ULTRA_WIDE -> R.string.lens_ultra_wide
        }

    @get:StringRes
    val explanation: Int
        get() = when (this) {
            WIDE -> R.string.lens_wide_note
            ULTRA_WIDE -> R.string.lens_ultra_wide_note
        }

    /**
     * Capturing wider means the card covers less of the frame, so the square that reaches the model
     * carries less of it. More capture resolution puts that detail back before the downscale to 640.
     * In sensor orientation (landscape), as CameraX expects it.
     */
    val targetResolution: Size get() = if (this == ULTRA_WIDE) Size(1920, 1080) else Size(1280, 720)

    companion object {
        fun fromId(id: String?): CameraLens? = entries.firstOrNull { it.id == id }
    }
}
