import SwiftUI

/// The app's entry screen. Counting is a session that gets opened, used for half a minute and
/// closed again - so this screen is what the app looks like most of the time, and nothing expensive
/// runs here: no camera, no inference.
struct HomeView: View {
    @Bindable var model: LiveDetectionModel
    @Environment(Store.self) private var store

    @State private var showSettings = false
    @State private var showAbout = false
    @State private var showPurchase = false
    @State private var showStart = false
    @State private var showScanner = false
    /// Set when the start sheet was confirmed rather than cancelled, and read once it has closed.
    ///
    /// The scanner is opened from the sheet's `onDismiss` rather than from the button inside it:
    /// putting a full-screen cover up while a sheet is still going down drops one of the two.
    @State private var startConfirmed = false
    /// What the last finished session counted; replaced by the next one.
    @State private var lastResult: CountResult?

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Spacer()
                header
                lastResultCard
                Spacer()
                startButton
            }
            .padding(24)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(white: 0.07).ignoresSafeArea())
            .toolbar {
                // Two doors: the page to read next to the sheet to change things in.
                ToolbarItemGroup(placement: .primaryAction) {
                    Button { showAbout = true } label: {
                        Image(systemName: "info.circle")
                    }
                    .accessibilityLabel(String(localized: "about.title", defaultValue: "Über"))
                    Button { showSettings = true } label: {
                        Image(systemName: "gearshape")
                    }
                    .accessibilityLabel(String(localized: "settings.title", defaultValue: "Einstellungen"))
                }
            }
            .sheet(isPresented: $showAbout) {
                AboutView(developerTools: $model.developerTools, unlocked: store.unlocked)
                    .presentationDetents([.medium, .large])
                    .presentationDragIndicator(.visible)
            }
            .sheet(isPresented: $showPurchase) {
                PurchaseView()
            }
            .sheet(isPresented: $showSettings) {
                SettingsSheet(model: model)
                    // Large as well: the stability section holds a picker, and half height clips it.
                    .presentationDetents([.medium, .large])
                    .presentationDragIndicator(.visible)
            }
            // Deck, discipline, last trick and factor - asked here, with nothing running behind them.
            .sheet(isPresented: $showStart, onDismiss: {
                guard startConfirmed else { return }   // cancelled: nothing starts
                startConfirmed = false
                showScanner = true
            }) {
                StartSheet(model: model) { mode, lastTrick, multiplier in
                    model.beginCounting(mode: mode, lastTrick: lastTrick, multiplier: multiplier)
                    startConfirmed = true
                    showStart = false
                }
            }
        }
        .preferredColorScheme(.dark)
        // The scanner is a session: it exists only while counting, and takes camera and model with
        // it when it goes.
        #if os(iOS)
        .fullScreenCover(isPresented: $showScanner) {
            ScanView(model: model) { result in
                lastResult = result
                showScanner = false
            }
        }
        #else
        .sheet(isPresented: $showScanner) {
            ScanView(model: model) { result in
                lastResult = result
                showScanner = false
            }
            .frame(minWidth: 420, minHeight: 700)
        }
        #endif
    }

    private var header: some View {
        VStack(spacing: 6) {
            Text(String(localized: "app.name", defaultValue: "JassCardEye"))
                .font(.largeTitle.weight(.bold))
            // The deck, because it stays as the default. What was played belongs to the single round
            // and is asked when a count starts. The marks alone tell the two decks apart at a
            // glance; the name is left to VoiceOver.
            HStack(spacing: 6) {
                ForEach(model.deck.suits, id: \.token) { suit in
                    SuitMark(suit: suit, size: 24)
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(model.deck.name)
        }
        .frame(maxWidth: .infinity)
        // Room between the name and what was counted, so the two do not read as one block.
        .padding(.bottom, 16)
    }

    @ViewBuilder
    private var lastResultCard: some View {
        if let result = lastResult {
            VStack(spacing: 12) {
                Text(String(localized: "home.last_count", defaultValue: "Letzte Zählung"))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                // The same three columns a session shows, so the number stands where it stood a
                // moment ago.
                ScoreRow(points: result.points,
                         pointsCaption: String(localized: "score.mine", defaultValue: "meine Punkte"),
                         opponentPoints: result.opponentPoints,
                         cards: result.cards,
                         modeName: result.modeName,
                         modeSuit: result.modeSuit.flatMap(JassSuit.named),
                         valueFont: .title,
                         locked: !store.unlocked)
                if !store.unlocked {
                    UnlockButton { showPurchase = true }
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity)
            .background(.white.opacity(0.08), in: RoundedRectangle(cornerRadius: 14))
        } else {
            Text(String(localized: "home.nothing_counted", defaultValue: "Noch nichts gezählt."))
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private var startButton: some View {
        VStack(spacing: 12) {
            // Decided here rather than in the start questions: it belongs to the session as a whole,
            // and putting it in the flow would add a tap to every single count. Only with the
            // developer tools on - see `LiveDetectionModel.developerTools`.
            if model.developerTools {
                Toggle(isOn: $model.recordSession) {
                    Label("Session aufzeichnen", systemImage: "record.circle")
                        .font(.callout)
                }
                .tint(.red)
            }

            Button {
                showStart = true
            } label: {
                Label(String(localized: "count.start", defaultValue: "Zählen starten"), systemImage: "camera.viewfinder")
                    .font(.title3.weight(.semibold))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 16)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)

            // Said once, where a count starts: the demo is the whole app, only the result is blurred.
            if !store.unlocked {
                Text(String(localized: "home.demo_note", defaultValue: "Die Demo zählt wie die gekaufte App - nur die Punkte bleiben verwischt."))
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity)
            }
        }
    }
}

// MARK: - Settings

/// Recognition model, lens, stability, feedback and the purchase - the settings of the app itself,
/// not of a round. What belongs to a round (deck, discipline, last trick, factor) is asked when a
/// count is started, so this sheet is opened rarely and holds nothing anyone needs mid-game. The
/// model row only appears when more than one variant was bundled - never in a release, which bundles
/// one - and the lens row only on a phone with an ultra-wide lens.
struct SettingsSheet: View {
    @Bindable var model: LiveDetectionModel
    @Environment(Store.self) private var store
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                if model.availableVariants.count > 1 {
                    Section("Erkennungsmodell") {
                        Picker("Modell", selection: variantBinding) {
                            ForEach(model.availableVariants) { variant in
                                Text(variant.displayName).tag(variant)
                            }
                        }
                        Text("Wechselt das Modell, das die oberste Karte erkennt.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                if model.availableLenses.count > 1 {
                    Section(String(localized: "settings.camera", defaultValue: "Kamera")) {
                        Picker(String(localized: "settings.lens", defaultValue: "Objektiv"), selection: $model.cameraLens) {
                            ForEach(model.availableLenses) { lens in
                                Text(lens.displayName).tag(lens)
                            }
                        }
                        Text(model.cameraLens.explanation)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                Section(String(localized: "settings.stability", defaultValue: "Stabilität")) {
                    Stepper(String(localized: "settings.frames", defaultValue: "\(model.requiredFrames) Bilder müssen zustimmen"),
                            value: $model.requiredFrames, in: LiveDetectionModel.frameRange)
                    Picker(String(localized: "settings.rule", defaultValue: "Zählweise"), selection: $model.stabilityRule) {
                        ForEach(StabilityRule.allCases) { rule in
                            Text(rule.displayName(votes: model.requiredFrames)).tag(rule)
                        }
                    }
                    Text(model.stabilityRule.explanation)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                // Sliders rather than switches: 0 is off, and everything above depends on the table -
                // a phone lying on wood feels different from one on a cloth, and a quiet kitchen
                // sounds different from a restaurant.
                Section(String(localized: "settings.feedback", defaultValue: "Rückmeldung")) {
                    FeedbackSlider(title: String(localized: "settings.vibration", defaultValue: "Vibration"),
                                   lowSymbol: "iphone", highSymbol: "iphone.radiowaves.left.and.right",
                                   value: $model.hapticStrength, preview: Haptics.preview)
                    FeedbackSlider(title: String(localized: "settings.sound", defaultValue: "Ton"),
                                   lowSymbol: "speaker.slash", highSymbol: "speaker.wave.3",
                                   value: $model.soundVolume, preview: CardSound.preview)
                    Text(String(localized: "settings.feedback_note.ios", defaultValue: "Bei jeder gezählten Karte. Ganz links ist aus. Der Ton folgt der Medienlautstärke, auch im Lautlos-Modus, und lässt laufende Musik weiterspielen. Die System-Haptik des iPhones gilt trotzdem."))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                // Apple asks for a visible way to restore a purchase, for a new phone or a reinstall.
                Section(String(localized: "settings.purchase", defaultValue: "Kauf")) {
                    LabeledContent(String(localized: "settings.points", defaultValue: "Punkte"),
                                   value: store.unlocked ? String(localized: "purchase.unlocked", defaultValue: "Freigeschaltet") : String(localized: "purchase.demo", defaultValue: "Demo"))
                    Button(String(localized: "purchase.restore", defaultValue: "Kauf wiederherstellen")) {
                        Task { await store.restore() }
                    }
                    if let problem = store.problem {
                        Text(problem)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .navigationTitle(String(localized: "settings.title", defaultValue: "Einstellungen"))
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button(String(localized: "common.done", defaultValue: "Fertig")) { dismiss() }
                }
            }
        }
    }

    /// Selecting a variant goes through the model, which rebuilds the recogniser and clears the pile.
    private var variantBinding: Binding<RecognitionVariant> {
        Binding(get: { model.variant }, set: { model.selectVariant($0) })
    }
}

/// One slider of the feedback section. Letting go plays the feedback once at the new setting, so it
/// is judged by feel and by ear rather than by a number - on release, not while dragging, which would
/// fire a burst of taps.
private struct FeedbackSlider: View {
    let title: String
    let lowSymbol: String
    let highSymbol: String
    @Binding var value: Double
    let preview: @MainActor () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
            Slider(value: $value, in: LiveDetectionModel.feedbackRange) {
                Text(title)
            } minimumValueLabel: {
                Image(systemName: lowSymbol).foregroundStyle(.secondary)
            } maximumValueLabel: {
                Image(systemName: highSymbol).foregroundStyle(.secondary)
            } onEditingChanged: { editing in
                if !editing { preview() }
            }
            .accessibilityValue(value == 0 ? String(localized: "settings.feedback_off", defaultValue: "Aus")
                                : String(localized: "settings.feedback_percent", defaultValue: "\(Int((value * 100).rounded())) Prozent"))
        }
    }
}
