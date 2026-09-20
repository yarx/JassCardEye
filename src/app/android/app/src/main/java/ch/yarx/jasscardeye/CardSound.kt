package ch.yarx.jasscardeye

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool

// Port of src/app/ios/Sources/CardSound.swift.

/**
 * The soft tick that sounds together with the tap for a counted card.
 *
 * The same synthesised wooden tick as on iOS (`src/tools/make_card_sound.py`, converted to Ogg), played
 * through a `SoundPool`, which has a volume of its own, unlike a system sound.
 *
 * The tick is part of the counting, like the sound of a video, not an alert: it plays on the media
 * stream, so the volume buttons set it and silent or vibrate mode does not mute it - a phone at a card
 * table is almost always on silent. A `SoundPool` takes no audio focus, so music that is already playing
 * keeps playing underneath. That is the iOS `.playback` session with `.mixWithOthers`.
 */
object CardSound {

    /** How loud the tick is, from 0 (off) to 1 (the file as recorded). Set from the settings. */
    @Volatile var volume: Double = 0.5

    // Main thread only: loading, the load listener (delivered on the thread that built the pool) and playing.
    private var pool: SoundPool? = null
    private var soundId = 0
    private var loaded = false
    /** A tick asked for while the file was still loading - played the moment it is in, not dropped. */
    private var playWhenLoaded = false
    private var appContext: Context? = null

    /** Called once at launch. Loads the tick right away, so even the very first preview of the slider sounds. */
    fun init(context: Context) {
        appContext = context.applicationContext
        prepare()
    }

    /** Makes sure the tick is loaded - at launch and again at the start of a session, which retries a failed load. */
    fun prepare() {
        if (pool != null) return
        val context = appContext ?: return
        val attributes = AudioAttributes.Builder()
            .setUsage(AudioAttributes.USAGE_MEDIA)
            .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
            .build()
        val created = SoundPool.Builder().setMaxStreams(2).setAudioAttributes(attributes).build()
        created.setOnLoadCompleteListener { loadedPool, _, status ->
            if (status != 0) {
                // Given up for now and built again at the next prepare(): a pool that failed once stays silent.
                loadedPool.release()
                if (pool === loadedPool) pool = null
                playWhenLoaded = false
                return@setOnLoadCompleteListener
            }
            loaded = true
            if (playWhenLoaded) {
                playWhenLoaded = false
                play()
            }
        }
        loaded = false
        soundId = created.load(context, R.raw.card_tick, 1)
        pool = created
    }

    /** One card recognised and committed to the pile. */
    fun cardCounted() = play()

    /** The tick once at the current volume, so the slider can be judged by ear. */
    fun preview() {
        prepare()
        play()
    }

    private fun play() {
        val pool = pool ?: return
        if (volume <= 0) return
        if (!loaded) { playWhenLoaded = true; return }
        val v = volume.coerceIn(0.0, 1.0).toFloat()
        pool.play(soundId, v, v, 1, 0, 1f)
    }
}
