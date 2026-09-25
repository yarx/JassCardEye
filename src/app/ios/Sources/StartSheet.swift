import SwiftUI

/// Everything that has to be settled before a card is looked at, on one screen that fits.
///
/// It comes *before* the scanner rather than on top of it, and that is the point: the camera and
/// the model are the most expensive things the app does, and starting them behind a question that
/// might be cancelled spends a battery on nothing. Here nothing runs. The session - camera,
/// inference, pile - begins when "Zählen starten" is tapped, and never before.
///
/// Four facts, none of which a pile of cards can reveal:
///
/// 1. **Which deck.** A property of the evening, so the last answer is kept and usually just
///    confirmed. It decides which 36 of the model's 72 classes count as cards at all.
/// 2. **What was played.** The discipline decides what every card is worth.
/// 3. **The last trick.** Its five points belong to a party, and no arrangement of cards shows
///    which - including the case where the table does not award them at all.
/// 4. **The factor.** What the table agreed the result is multiplied by. ×1 unless somebody picks
///    another, because that is what most rounds are.
///
/// Everything is a row of marks rather than a list of rows, because the whole screen has to be
/// visible at once: a choice that has to be scrolled to is one that gets made badly, and this
/// screen is opened after every single game. The hint shows for the selected entry only - nine of
/// them at once would be a wall nobody reads, and would make the screen too tall.
struct StartSheet: View {
    @Bindable var model: LiveDetectionModel
    let onStart: (CountingMode, LastTrick, Int) -> Void

    @Environment(\.dismiss) private var dismiss

    /// Deliberately empty at first. Nothing is preselected, so the discipline is always a decision
    /// somebody made for this round rather than one left over from the last.
    @State private var mode: CountingMode?
    @State private var lastTrick: LastTrick = .mine
    /// Unlike the discipline, preselected: ×1 is the common case, and an empty factor would only add
    /// a tap to every round.
    @State private var multiplier = 1

    /// The one ground colour of this screen. Named because it is used twice - behind the content
    /// and behind the pinned button - and two literals that have to match are two that will not.
    static let ground = Color(white: 0.07)

    private var trumpModes: [CountingMode] { JassModes.all.filter { $0.trumpRole != nil } }
    private var openModes: [CountingMode] { JassModes.all.filter { $0.trumpRole == nil } }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    group(String(localized: "start.deck", defaultValue: "Blatt")) {
                        Picker(String(localized: "start.deck_picker.ios", defaultValue: "Kartenblatt"), selection: $model.deck) {
                            ForEach(JassDeck.allCases) { deck in
                                Text(deck.name).tag(deck)
                            }
                        }
                        .pickerStyle(.segmented)

                        // The four marks of whatever was just chosen. A segmented control has no
                        // room for them, and the name alone is the slower way to check that the
                        // deck on the table is the deck the app is set to.
                        HStack(spacing: 10) {
                            ForEach(model.deck.suits, id: \.token) { suit in
                                SuitMark(suit: suit, size: 26)
                            }
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.top, 2)
                        .accessibilityHidden(true)
                    }

                    group(String(localized: "start.trump", defaultValue: "Trumpf")) {
                        row(trumpModes)
                    }

                    group(String(localized: "start.no_trump", defaultValue: "Ohne Trumpf")) {
                        row(openModes)
                    }

                    // One line, in the place a line always is, so the layout never jumps between
                    // a chosen and an unchosen state.
                    Text(mode?.hint ?? String(localized: "start.choose", defaultValue: "Wähle, was gespielt wurde."))
                        .font(.footnote)
                        .foregroundStyle(mode == nil ? .secondary : .primary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .lineLimit(2, reservesSpace: true)

                    group(String(localized: "start.last_trick", defaultValue: "Letzter Stich")) {
                        Picker(String(localized: "start.last_trick", defaultValue: "Letzter Stich"), selection: $lastTrick) {
                            ForEach(LastTrick.allCases) { choice in
                                Text(choice.shortName).tag(choice)
                            }
                        }
                        .pickerStyle(.segmented)
                    }

                    group(String(localized: "start.factor", defaultValue: "Faktor")) {
                        MultiplierBar(multiplier: $multiplier)
                    }
                }
                .padding(20)
            }
            .background(Self.ground.ignoresSafeArea())
            // Pinned rather than scrolled to: on a phone small enough to need scrolling, the one
            // control that must always be reachable is this one.
            .safeAreaInset(edge: .bottom) {
                Button {
                    guard let mode else { return }
                    onStart(mode, lastTrick, multiplier)
                } label: {
                    Text(String(localized: "count.start", defaultValue: "Zählen starten"))
                        .font(.headline)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 14)
                }
                .buttonStyle(.borderedProminent)
                .tint(.green)
                // Until a discipline is chosen there is nothing to count with, and the camera has
                // no reason to start.
                .disabled(mode == nil)
                .padding(.horizontal, 20)
                .padding(.top, 12)
                // The same ground as the page, carried past the home indicator to the screen edge.
                // A material here would be two mistakes at once: it is lighter than the page, so the
                // bar reads as a strip glued on, and it stops at the safe area, so the last few
                // millimetres fall back to whatever the sheet paints underneath.
                .background(Self.ground.ignoresSafeArea(edges: .bottom))
            }
            .navigationTitle(String(localized: "start.title", defaultValue: "Neue Zählung"))
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    // The way out. Nothing runs behind the questions, so leaving costs nothing.
                    Button(String(localized: "common.cancel", defaultValue: "Abbrechen")) { dismiss() }
                }
            }
        }
        .preferredColorScheme(.dark)
    }

    /// A labelled block, so the two rows of marks stay told apart without a box around each.
    private func group<Content: View>(_ title: String,
                                      @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title.uppercased())
                .font(.caption2.weight(.semibold))
                .foregroundStyle(.secondary)
                .tracking(0.6)
            content()
        }
    }

    /// The disciplines as one row of equal cells. Equal because they are equal choices - nothing
    /// here is a default or a recommendation.
    private func row(_ entries: [CountingMode]) -> some View {
        HStack(spacing: 6) {
            ForEach(entries) { entry in
                cell(entry)
            }
        }
    }

    private func cell(_ entry: CountingMode) -> some View {
        let chosen = entry == mode
        return Button {
            mode = entry
        } label: {
            VStack(spacing: 4) {
                mark(for: entry, chosen: chosen)
                Text(entry.shortName(deck: model.deck))
                    .font(.caption2)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                    .foregroundStyle(chosen ? Color.black : Color.white)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 9)
            .background(chosen ? Color.green : Color.white.opacity(0.13),
                        in: RoundedRectangle(cornerRadius: 10))
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(entry.displayName(deck: model.deck))
        .accessibilityAddTraits(chosen ? [.isSelected] : [])
    }

    /// The mark above the name: the suit as it is printed for a trump discipline, an arrow for one
    /// played without trump. Both sit in the same square, so the cells of a row line up whichever
    /// kind they hold.
    @ViewBuilder
    private func mark(for entry: CountingMode, chosen: Bool) -> some View {
        switch entry.kind {
        case .trump:
            // Drawn art carries its own colours, so nothing is tinted here. `SuitMark` brings the
            // white ground it needs, which is what lets the chosen cell keep the app's green.
            // The suit resolves for both decks, so the fallback is a name, never a missing mark.
            if let suit = entry.markSuit(deck: model.deck) {
                SuitMark(suit: suit, size: 34)
            } else {
                Text(entry.shortName(deck: model.deck))
                    .font(.caption)
                    .frame(width: 30, height: 30)
            }
        case .open(_, let symbol, _):
            Image(systemName: symbol)
                .font(.title3)
                .frame(width: 30, height: 30)
                .foregroundStyle(chosen ? Color.black : Color.white)
        }
    }
}
