import SwiftUI

/// The three-column score line, shared by the home screen and a running session so both read the
/// same way: own points left, what is being counted in the middle, the opponents' right.
///
/// Each column takes exactly one third of the width. That matters more than it looks: with plain
/// spacers the middle is only centred in whatever the two numbers leave over, so it drifts sideways
/// as the score grows from 0 to 1'764 - and during a count those numbers change with every card.
/// A third each keeps the middle on the centre line and anchors each number to its own side, so
/// nothing but the digits themselves moves.
struct ScoreRow: View {
    let points: Int
    /// Under the own score: "meine Punkte", or the breakdown "118 Karten +5 ×2".
    let pointsCaption: String
    let opponentPoints: Int
    let cards: Int
    let modeName: String
    /// The trump's mark, drawn next to its name. `nil` for a discipline without trump, which has
    /// no suit to show.
    var modeSuit: JassSuit? = nil
    /// Optional third middle line, e.g. "+5 letzter Stich".
    var note: String? = nil
    var valueFont: Font = .largeTitle
    /// The demo: both numbers are drawn and blurred, so they still move with every card
    /// but cannot be read. The caller keeps the points out of the captions.
    var locked = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            column(alignment: .leading) {
                value(points, color: .green)
                caption(pointsCaption)
            }

            column(alignment: .center) {
                Text(String(localized: "score.cards", defaultValue: "\(cards) Karten"))
                    .font(.callout.monospacedDigit())
                    .foregroundStyle(.secondary)
                HStack(spacing: 4) {
                    if let modeSuit {
                        SuitMark(suit: modeSuit, size: 17)
                    }
                    caption(modeName)
                }
                if let note {
                    caption(note)
                }
            }

            column(alignment: .trailing) {
                value(opponentPoints, color: .orange)
                caption(String(localized: "score.opponents", defaultValue: "Gegner"))
            }
        }
    }

    /// A score. Locked, VoiceOver is told that it is locked rather than read the number the eye is
    /// not meant to see.
    private func value(_ number: Int, color: Color) -> some View {
        Text("\(number)")
            .font(valueFont.weight(.bold).monospacedDigit())
            .foregroundStyle(color)
            .blur(radius: locked ? 9 : 0)
            .accessibilityLabel(locked ? String(localized: "score.locked", defaultValue: "Punkte, freischalten") : "\(number)")
    }

    private func column<Content: View>(alignment: HorizontalAlignment,
                                       @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: alignment, spacing: 1) {
            content()
        }
        // One third each, so the middle sits on the centre line whatever the numbers do.
        .frame(maxWidth: .infinity, alignment: Alignment(horizontal: alignment, vertical: .center))
    }

    /// Captions stay on one line and shrink a little rather than wrap - a column that grows a line
    /// would push the whole bar around while counting.
    private func caption(_ text: String) -> some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(1)
            .minimumScaleFactor(0.75)
    }
}
