package ch.yarx.jasscardeye

import android.content.Context
import android.os.Build
import android.os.VibrationAttributes
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.provider.Settings

// Port of src/app/ios/Sources/Haptics.swift.

/**
 * The short tap that says a card has been counted.
 *
 * During a count the phone lies over the pile and the eyes are on the cards, not on the screen - so the
 * confirmation that a card actually landed has to arrive somewhere other than the display. A tap does that
 * without asking anyone to look up, and it is the difference between lifting the next card confidently and
 * lifting it twice.
 *
 * A firm, short pulse scaled by the strength set in the settings - the counterpart of iOS's heavy impact
 * generator at an intensity. The middle is still a tap you notice, and 0 switches it off.
 *
 * Nothing here overrides the phone's own settings: the pulse is sent as touch feedback, which a phone with
 * touch vibration switched off keeps silent, whatever the slider says.
 */
object Haptics {

    /** How firm the tap is, from 0 (off) to 1 (the firmest the vibrator offers). Set from the settings. */
    @Volatile var strength: Double = 0.5

    private var vibrator: Vibrator? = null
    private var appContext: Context? = null

    fun init(context: Context) {
        appContext = context.applicationContext
        vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            (context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
        }
    }

    /**
     * Nothing to warm: a vibrator motor has no latency worth preparing for, unlike the Taptic Engine. Kept
     * so the session start reads the same on both platforms.
     */
    fun prepare() {}

    /** One card recognised and committed to the pile. */
    fun cardCounted() = tap()

    /** The tap once at the current strength, so the slider can be judged by feel. */
    fun preview() = tap()

    private fun tap() {
        val vibrator = vibrator ?: return
        if (strength <= 0 || !vibrator.hasVibrator()) return
        // Short enough to read as a tap rather than a buzz, long enough for a motor to spin up.
        val amplitude = if (vibrator.hasAmplitudeControl()) (strength.coerceIn(0.0, 1.0) * 255).toInt().coerceIn(1, 255)
        else VibrationEffect.DEFAULT_AMPLITUDE
        val effect = VibrationEffect.createOneShot(PULSE_MS, amplitude)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            // Touch usage: the system applies the user's touch-feedback setting itself.
            vibrator.vibrate(effect, VibrationAttributes.createForUsage(VibrationAttributes.USAGE_TOUCH))
        } else {
            // Deprecated from API 33 on in favour of the usage above, which is why it is only read below it.
            @Suppress("DEPRECATION")
            val enabled = appContext?.let {
                Settings.System.getInt(it.contentResolver, Settings.System.HAPTIC_FEEDBACK_ENABLED, 1) != 0
            } ?: true
            if (enabled) vibrator.vibrate(effect)
        }
    }

    private const val PULSE_MS = 30L
}
