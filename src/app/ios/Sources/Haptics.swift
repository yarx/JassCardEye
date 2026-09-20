#if os(iOS)
import UIKit
#endif

/// The short tap that says a card has been counted.
///
/// During a count the phone lies over the pile and the eyes are on the cards, not on the screen -
/// so the confirmation that a card actually landed has to arrive somewhere other than the display.
/// A tap does that without asking anyone to look up, and it is the difference between lifting the
/// next card confidently and lifting it twice.
///
/// Always the `.heavy` generator, scaled by the strength set in the settings. A gentler generator
/// such as `.soft` would keep 36 taps in half a minute quiet - but on a real phone lying on a table,
/// under a hand that is busy lifting cards, it is barely there. So the slider scales the firm
/// generator rather than picking a gentler one: its middle is still a tap you notice, and 0 switches
/// it off.
///
/// Nothing here overrides the phone's own settings - a phone with system haptics off stays silent,
/// whatever the slider says.
@MainActor
enum Haptics {

    /// How firm the tap is, from 0 (off) to 1 (the firmest single impact there is). Set from the
    /// settings; the middle is what a fresh install starts with.
    static var strength: Double = 0.5

    #if os(iOS)
    /// Kept alive across a session so the engine stays warm. A generator built per tap arrives
    /// late, which for a confirmation is worse than not arriving at all.
    private static let counted = UIImpactFeedbackGenerator(style: .heavy)
    #endif

    /// Warms the Taptic Engine at the start of a session, so the first card is not the slow one.
    static func prepare() {
        #if os(iOS)
        counted.prepare()
        #endif
    }

    /// One card recognised and committed to the pile.
    static func cardCounted() {
        tap()
    }

    /// The tap once at the current strength, so the slider can be judged by feel.
    static func preview() {
        prepare()
        tap()
    }

    private static func tap() {
        #if os(iOS)
        guard strength > 0 else { return }
        counted.impactOccurred(intensity: CGFloat(min(strength, 1)))
        // Straight back to ready: the next card is usually a second away, and the engine goes cold
        // on its own after a short while.
        counted.prepare()
        #endif
    }
}
