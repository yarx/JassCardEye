import Foundation

/// How a detection earns its place on the pile.
///
/// The problem both rules exist for: a wrong reading is worse than a missed one, because it slips
/// past unnoticed. The detector gives no help there: a wrong answer can be as confident as a right
/// one, so no threshold on a single frame separates them; the evidence has to come from several
/// frames.
enum StabilityRule: String, CaseIterable, Identifiable, Codable {
    /// The plain rule and the default: the same card on top of N frames in a row.
    case run
    /// A majority of a window instead of an unbroken run - see `windowSize`.
    case majority

    var id: String { rawValue }

    /// How many of the last frames are looked at. A majority of `2N-1` is exactly N, so the same
    /// number the user already tunes keeps its meaning: "this many frames have to agree".
    func windowSize(votes: Int) -> Int { max(1, votes * 2 - 1) }

    func displayName(votes: Int) -> String {
        switch self {
        case .run:      return "Serie – \(votes) hintereinander"
        case .majority: return "Mehrheit – \(votes) von \(windowSize(votes: votes))"
        }
    }

    var explanation: String {
        switch self {
        case .run:
            return """
                Eine Karte wird gezählt, wenn sie in so vielen Bildern hintereinander zuoberst liegt. \
                Ein einziges abweichendes Bild setzt den Zähler zurück.
                """
        case .majority:
            return """
                Eine Karte wird gezählt, wenn sie die Mehrheit der letzten Bilder für sich hat und \
                keine andere darin mehr als einmal vorkommt. Einzelne Aussetzer brechen nichts mehr \
                ab, aber eine Fehlerkennung während einer Bewegung braucht jetzt eine echte Mehrheit \
                statt drei zufällig benachbarter Bilder.
                """
        }
    }
}
