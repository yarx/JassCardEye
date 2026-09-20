import AVFoundation

/// The soft tick that sounds together with the tap for a counted card.
///
/// A bundled file played through `AVAudioPlayer`, not a system sound. A system sound would be the
/// obvious choice, but it has no volume of its own - it follows the ringer - and the settings offer
/// a volume slider. The file is a synthesised wooden tick of a tenth of a second
/// (`src/tools/make_card_sound.py`): it plays up to 36 times in half a minute, so it has to stay pleasant
/// on the tenth card, which rules out anything with a sharp attack or a pitch that reads as an alert.
///
/// The audio session is `.playback` with `.mixWithOthers`. The tick is part of the counting, like the
/// sound of a video, not an alert: the volume buttons set it and it sounds with the silent switch on -
/// a phone at a card table is almost always on silent, and `.ambient`, which the switch mutes, would
/// leave it quiet exactly there. Mixing keeps music that is already playing at the table going underneath
/// instead of pausing it.
@MainActor
enum CardSound {

    /// How loud the tick is, from 0 (off) to 1 (the file as recorded). Set from the settings; the
    /// middle is what a fresh install starts with.
    static var volume: Double = 0.5

    /// One player for the whole run. Built on first use and kept, so a tick never waits for a file
    /// to be read.
    private static let player: AVAudioPlayer? = {
        guard let url = Bundle.main.url(forResource: "card-tick", withExtension: "caf") else { return nil }
        return try? AVAudioPlayer(contentsOf: url)
    }()

    #if os(iOS)
    private static var sessionReady = false
    #endif

    /// Sets up the audio session once and loads the tick into the audio hardware, at the start of a
    /// session - like the haptics warm-up, so the first card is not the late one.
    static func prepare() {
        #if os(iOS)
        if !sessionReady {
            do {
                let session = AVAudioSession.sharedInstance()
                try session.setCategory(.playback, mode: .default, options: [.mixWithOthers])
                try session.setActive(true)
                sessionReady = true
            } catch {
                // Tried again at the next prepare. Until then the tick may not sound - the tap still
                // confirms the card, so this is not worth interrupting anything for.
            }
        }
        #endif
        player?.prepareToPlay()
    }

    /// One card recognised and committed to the pile.
    static func cardCounted() {
        play()
    }

    /// The tick once at the current volume, so the slider can be judged by ear.
    static func preview() {
        prepare()
        play()
    }

    private static func play() {
        guard volume > 0, let player else { return }
        player.volume = Float(min(volume, 1))
        // From the start even if the previous tick is still ringing out: two cards in quick
        // succession should give two ticks, not one that was already half over.
        player.currentTime = 0
        player.play()
    }
}
