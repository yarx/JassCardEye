import SwiftUI

/// A Jass suit drawn the way it is printed on the card.
///
/// The eight marks are vector art in the asset catalogue, one image set per suit, named exactly
/// like the suit's token - so `JassSuit.token` is both what the model emits and what the drawing is
/// called, and there is no third list to keep in step.
///
/// Always a square frame with `scaledToFit`. The eight drawings share a square viewBox, so a square
/// frame puts every one of them on the same optical centre: in a row of chips the Schilte and the
/// Herz sit on the same line and take the same room, however different their outlines are. Sizing
/// by height alone would let the wide ones drift and the narrow ones look shrunken.
///
/// Each sits on a small white plate, and that is not decoration. The marks carry their own printed
/// colours and cannot be recoloured: Kreuz and Schaufel are near-black, Schilten almost as dark, and
/// on this app's near-black ground they would simply disappear. Tinting them is not an option - a green
/// Herz is not a Herz - so instead they are given the ground they are printed on. It also means a
/// mark reads the same wherever it is put, rather than depending on whatever is behind it.
struct SuitMark: View {
    let suit: JassSuit

    /// Edge length of the plate in points. A little larger than the text beside it: these are
    /// drawings, not letters, and at text size the Eichel and the Schilte stop being tellable apart.
    var size: CGFloat = 26

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.2, style: .continuous)
            .fill(.white)
            .frame(width: size, height: size)
            .overlay {
                Image(suit.token)
                    .resizable()
                    .interpolation(.high)
                    .scaledToFit()
                    // The margin a mark has on a real card, so the drawing never touches the edge.
                    .padding(size * 0.13)
            }
            .accessibilityLabel(suit.name)
    }
}

/// Which side of the rank the suit mark sits on.
///
/// Not decoration: in a right-aligned row the first item is the one that moves, because everything
/// after it changes width. The status bar names a different card several times a second, and with
/// the mark first it would slide left and right with every rank - "9" and "Under" are not the same width.
/// Put last, the mark is measured from the right edge and stays where it is; only the text changes.
enum MarkSide {
    case leading, trailing
}

/// A card as it reads on a real Jass card: the suit's own mark, then the rank in neutral text.
///
/// A drawing cannot be glued onto a `Text`, so this is a small view of its own - which also lets
/// the mark be sized on its own rather than inheriting whatever font the surrounding line happens
/// to use.
struct CardChip: View {
    let label: String
    var rankColor: Color = .white
    var markSize: CGFloat = 24
    var markSide: MarkSide = .leading

    var body: some View {
        if let card = CardLabel(label) {
            HStack(spacing: 5) {
                if markSide == .leading { SuitMark(suit: card.suit, size: markSize) }
                Text(card.rankName).foregroundColor(rankColor)
                if markSide == .trailing { SuitMark(suit: card.suit, size: markSize) }
            }
            // One thing to VoiceOver: "Schilten Under", not a mark and a word.
            .accessibilityElement(children: .combine)
        } else {
            // A label the app cannot parse still has to show something.
            Text(label).foregroundColor(rankColor)
        }
    }
}
