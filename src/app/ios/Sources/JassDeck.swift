import Foundation

/// The two decks a Swiss Jass is played with. A game uses one or the other, never both, so the
/// deck is a property of the table, chosen in the start questions (the last choice is kept) - not
/// of a single card.
enum JassDeck: String, CaseIterable, Identifiable {
    case french, german

    var id: String { rawValue }

    /// What it is called: "Französisch", "Deutsch".
    var name: String { self == .french ? "Französisch" : "Deutsch" }

    /// Its four suits, shown next to the name wherever the deck is offered - seeing the marks is
    /// the fastest way to know which deck is meant.
    ///
    /// In role order rather than class order, so this row and the trump row underneath it read the
    /// same way. They differ for the German deck: the model numbers it Eichel, Rosen, Schellen,
    /// Schilten, while by role it is Eichel, Schellen, Rosen, Schilten.
    var suits: [JassSuit] {
        DeckLayout.suits.compactMap { JassSuit.playing($0, in: self) }
    }
}

/// One suit of one deck, and everything the app needs to know about it.
///
/// The eight suits are one table rather than a dictionary per question, so a suit's deck, its role
/// in the rules and its two names cannot drift apart - and so the German names are written once.
struct JassSuit {
    /// As the model emits it in a label: "spades", "shields".
    let token: String

    let deck: JassDeck

    /// The French suit whose rules this one follows. Swiss convention: Eichel ≙ Kreuz,
    /// Rosen ≙ Herz, Schellen ≙ Ecken, Schilten ≙ Schaufel. Scoring and trump therefore only ever
    /// deal with four suits, whichever deck is on the table.
    let role: String

    /// The suit on its own: "Schaufel", "Schilten". What VoiceOver reads for the mark, what a
    /// filename carries, and what stands under the mark in the compact picker.
    ///
    /// The mark itself is not here: it is vector art in the asset catalogue under this suit's
    /// `token`, drawn by `SuitMark`. A string would not do - Unicode has the French suits but no
    /// acorn, rose, bell or shield that belongs on a playing card, and an emoji is somebody else's
    /// drawing at somebody else's size; these are the marks as they are actually printed.
    let name: String

    /// Who drew the mark and under which licence - what the *Über* page credits. A column of this
    /// table, so no suit can be drawn without being credited.
    let markCredit: MarkCredit

    static let all: [JassSuit] = [
        JassSuit(token: "clubs",    deck: .french, role: "clubs",    name: "Kreuz",
                 markCredit: MarkCredit(file: "SuitClubs.svg", author: "F l a n k e r", publicDomain: true)),
        JassSuit(token: "diamonds", deck: .french, role: "diamonds", name: "Ecken",
                 markCredit: MarkCredit(file: "Ecke_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "hearts",   deck: .french, role: "hearts",   name: "Herz",
                 markCredit: MarkCredit(file: "Herz_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "spades",   deck: .french, role: "spades",   name: "Schaufel",
                 markCredit: MarkCredit(file: "Schaufel_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "acorns",   deck: .german, role: "clubs",    name: "Eichel",
                 markCredit: MarkCredit(file: "Eichel_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "roses",    deck: .german, role: "hearts",   name: "Rosen",
                 markCredit: MarkCredit(file: "Rosen_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "bells",    deck: .german, role: "diamonds", name: "Schellen",
                 markCredit: MarkCredit(file: "Schellen_Neu.svg", author: "Jensche", publicDomain: false)),
        JassSuit(token: "shields",  deck: .german, role: "spades",   name: "Schilten",
                 markCredit: MarkCredit(file: "Schilten_Neu.svg", author: "Jensche", publicDomain: false)),
    ]

    private static let byToken = Dictionary(uniqueKeysWithValues: all.map { ($0.token, $0) })

    static func named(_ token: String) -> JassSuit? { byToken[token] }

    /// The suit that plays `role` in `deck` - what the trump menu needs to name the same trump
    /// Herz on a French deck and Rosen on a German one.
    static func playing(_ role: String, in deck: JassDeck) -> JassSuit? {
        all.first { $0.role == role && $0.deck == deck }
    }
}

/// Where a drawn suit mark comes from. All eight are vector drawings from Wikimedia Commons: seven
/// by Jensche under CC BY-SA 4.0, which asks for author, licence and a link; Kreuz by F l a n k e r,
/// dedicated to the public domain.
struct MarkCredit {
    /// The file's name on Wikimedia Commons.
    let file: String
    let author: String
    let publicDomain: Bool

    var page: URL { URL(string: "https://commons.wikimedia.org/wiki/File:\(file)")! }
    var attribution: String { "\(author) · \(publicDomain ? "gemeinfrei" : "CC BY-SA 4.0")" }
}

/// A class label as the model emits it, taken apart.
///
/// The model has 72 classes - both decks, 36 cards each - and writes them as `suit_rank`
/// ("hearts_ace", "roses_9"). The suit tokens are unique across the decks, so splitting at the
/// first underscore is still all it takes to read one.
struct CardLabel {
    let rank: String

    /// Resolved once, when the label is parsed. Nothing downstream has to look a suit up again or
    /// decide what to do when it is unknown - an unknown suit means there is no CardLabel at all.
    let suit: JassSuit

    init?(_ label: String) {
        let parts = label.split(separator: "_", maxSplits: 1)
        guard parts.count == 2, let suit = JassSuit.named(String(parts[0])) else { return nil }
        self.suit = suit
        self.rank = String(parts[1])
    }

    var deck: JassDeck { suit.deck }

    /// The French suit whose rules this card follows.
    var role: String { suit.role }

    /// The rank as a Jass player says it. The model emits English tokens; on screen the Jass names
    /// belong there. They are the same on both decks - a Swiss player calls the jack Under and the
    /// queen Ober whether the card shows a Schilte or a Schaufel.
    var rankName: String { Self.jassRanks[rank] ?? rank }

    /// Whether the suit is printed in a warm colour: Herz and Ecken, and by role Rosen and Schellen.
    /// Not read by the app itself; `src/tools/check_scoring.swift` checks that it follows the role.
    var isRed: Bool { role == "hearts" || role == "diamonds" }

    private static let jassRanks = [
        "jack": "Under", "queen": "Ober", "king": "König", "ace": "Ass",
    ]
}
