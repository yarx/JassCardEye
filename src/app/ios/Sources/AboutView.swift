import SwiftUI

/// The page a person reads rather than changes: who publishes the app, under which licence it is,
/// whose work it carries and what it does with data. Opened from the home screen next to the gear -
/// Settings is for things you change, this is for things you read, and the version a tester quotes
/// is here too.
///
/// The one thing it changes is `developerTools`: five taps on *Firma* flip it, see
/// `LiveDetectionModel.developerTools`. Nothing hints at it - the tools are for looking into a wrong
/// count, not for playing.
struct AboutView: View {
    @Binding var developerTools: Bool
    /// Shown under the version, so a tester's report says which app they were looking at.
    let unlocked: Bool
    @Environment(\.dismiss) private var dismiss

    /// Taps on *Firma* since the page opened; the fifth flips the tools.
    @State private var publisherTaps = 0
    /// Set by the fifth tap, so the footer can say what it did - nothing else on screen would.
    @State private var toolsSwitched = false

    private static let unlockTaps = 5

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    VStack(spacing: 4) {
                        Text("JassCardEye")
                            .font(.title2.weight(.bold))
                        Text("Version \(AppInfo.versionLine)")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                        Text(unlocked ? "Freigeschaltet" : "Demo")
                            .font(.callout)
                            .foregroundStyle(unlocked ? .green : .secondary)
                    }
                    .frame(maxWidth: .infinity)
                }
                .listRowBackground(Color.clear)

                Section {
                    LabeledContent("Firma", value: About.publisher)
                        .contentShape(Rectangle())
                        .onTapGesture(perform: publisherTapped)
                    LabeledContent("E-Mail") {
                        Link(About.email, destination: About.emailURL)
                    }
                    LabeledContent("Website") {
                        Link(About.websiteLabel, destination: About.website)
                    }
                } header: {
                    Text("Herausgeber")
                } footer: {
                    if toolsSwitched {
                        Text("Bild und Session aufzeichnen sind \(developerTools ? "eingeblendet" : "ausgeblendet").")
                    }
                }

                Section("Lizenz") {
                    Text(About.licenceText)
                    Link("Lizenztext (AGPL-3.0)", destination: About.licence)
                    Link("Quellcode", destination: About.source)
                }

                Section("Erkennung") {
                    Text(About.modelText)
                    Link("Ultralytics YOLO", destination: About.ultralytics)
                }

                Section {
                    ForEach(JassSuit.all, id: \.token) { suit in
                        creditRow(suit)
                    }
                    Link("Lizenz CC BY-SA 4.0", destination: About.ccBySa)
                } header: {
                    Text("Bildnachweis")
                } footer: {
                    Text(About.creditsNote)
                }

                Section("Datenschutz") {
                    Text(About.privacyText)
                }

                Section("Bibliotheken") {
                    Text("Ausser den Frameworks von Apple verwendet die App keine fremden Bibliotheken.")
                }
            }
            // Links in the app's green, as on Android - the one accent both apps use for things to tap.
            .tint(.green)
            .navigationTitle("Über")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { dismiss() }
                }
            }
        }
    }

    private func publisherTapped() {
        publisherTaps += 1
        guard publisherTaps == Self.unlockTaps else { return }
        publisherTaps = 0
        developerTools.toggle()
        toolsSwitched = true
    }

    /// The mark as it appears in the app, its name linking to the Commons page, then author and licence.
    private func creditRow(_ suit: JassSuit) -> some View {
        HStack(spacing: 10) {
            SuitMark(suit: suit, size: 24)
                .accessibilityHidden(true)
            Link(suit.name, destination: suit.markCredit.page)
            Spacer(minLength: 8)
            Text(suit.markCredit.attribution)
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.trailing)
        }
    }
}

/// The facts on the page, in one place. Android's `AboutScreen.kt` says the same in the same order.
enum About {
    static let publisher = "YARX GmbH"
    static let email = "support@yarx.ch"
    static let emailURL = URL(string: "mailto:support@yarx.ch")!
    static let websiteLabel = "yarx.ch"
    static let website = URL(string: "https://yarx.ch")!
    static let licence = URL(string: "https://www.gnu.org/licenses/agpl-3.0.html")!
    static let source = URL(string: "https://github.com/yarx/JassCardEye")!
    static let ultralytics = URL(string: "https://github.com/ultralytics/ultralytics")!
    static let ccBySa = URL(string: "https://creativecommons.org/licenses/by-sa/4.0/deed.de")!

    static let licenceText = "JassCardEye ist freie Software unter der GNU Affero General Public License 3.0. Für den Vertrieb über den App Store und Google Play gilt eine zusätzliche Erlaubnis, beschrieben in der Lizenzdatei."
    static let modelText = "Das Erkennungsmodell wurde mit Ultralytics YOLO11 (AGPL-3.0) trainiert, auf je einem französischen und einem Deutschschweizer Jassblatt."
    static let creditsNote = "Die Farbzeichen stammen von Wikimedia Commons und sind unverändert übernommen."
    static let privacyText = "Die App sammelt keine Daten. Die Kamerabilder werden auf dem Gerät ausgewertet und nicht gespeichert, die App hat keine Internetverbindung, kein Konto und keine Werbung. Auf dem Gerät bleiben nur deine Einstellungen."
}
