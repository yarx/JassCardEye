import SwiftUI
#if os(iOS)
import UIKit
#endif
import AVFoundation

/// The sizes the scan screen is laid out from.
///
/// Named, because they all decide the same thing: how much room is left for the picture. The
/// viewfinder is a square that takes whatever the two bars leave over, so a bar that grows by a few
/// points when a card is recognised resizes the camera square - and detections come and go many
/// times a second. Hence a floor under each bar, and a margin kept deliberately small.
private enum ScanMetrics {
    /// The margin around the whole screen. Small on purpose: the viewfinder is a square and on a
    /// phone it is the leftover height that limits it, so this margin is what the picture is worth
    /// on the two other sides.
    static let margin: CGFloat = 10

    /// The suit mark on the current card in the status bar.
    static let statusMark: CGFloat = 24

    /// The suit mark on a pile chip.
    static let chipMark: CGFloat = 22

    /// A chip's padding above and below its content.
    static let chipPadding: CGFloat = 5

    /// What the chip strip reserves, with or without cards on it. Derived rather than typed out, so
    /// a larger mark cannot leave the reserved space behind.
    static let stripHeight: CGFloat = chipMark + 2 * chipPadding
}

/// Keeps the display from dimming and locking while `awake` is true. Only iOS has an idle timer to
/// hold off; the macOS build, which exists to keep the sources compiling, leaves the display alone.
@MainActor
private func keepScreenAwake(_ awake: Bool) {
    #if os(iOS)
    UIApplication.shared.isIdleTimerDisabled = awake
    #endif
}

/// A counting session: camera preview, detection band, the virtual pile and its score. The view
/// exists only while counting - it starts camera and inference when it appears and stops them when
/// it closes, so nothing expensive runs while the app sits on the home screen.
///
/// The screen is split into small views on purpose. With @Observable, a view is only re-rendered
/// when a property it actually reads changes - so the fast-moving values (frame rate, detection
/// box) invalidate their own view and leave the controls below alone. Reading them in one big body
/// would rebuild the whole tree thirty times a second and make the controls feel unresponsive on a
/// device, where the camera and the model already keep the main thread busy.
struct ScanView: View {
    @Bindable var model: LiveDetectionModel
    /// Called with the result when the user is done; the presenter closes the session.
    let onFinish: (CountResult) -> Void

    @State private var showPicker = false
    @State private var showPurchase = false

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            VStack(spacing: 8) {
                StatusBar(model: model)
                // The viewfinder takes whatever is left between the bar and the controls and centres
                // a square in it. Nothing outside that square is drawn at all - it is the picture.
                //
                // The square is as wide as the screen allows, which means everything above and below
                // it has to stay lean: the square is limited by the height left over, not by the
                // width, so every point spent on a bar is a point taken off all four sides of the
                // picture. Notes and the recording light are therefore drawn on top of the picture
                // rather than given a row of their own.
                Viewfinder(model: model)
                ScoreBar(model: model, showPicker: $showPicker, showPurchase: $showPurchase,
                         onFinish: { onFinish(model.finishCounting()) })
            }
            // Narrower than the usual margin, and the same on all four sides of the screen: this
            // margin sets how wide the picture can be, and the picture is what the screen is for.
            .padding(ScanMetrics.margin)
        }
        .task { await model.start() }
        // The screen stays on for as long as a count runs: nobody touches the phone while cards are
        // laid down, and a display that dims or locks halfway through interrupts the scan. The idle
        // timer belongs to the whole app, so it goes back to the system the moment the session closes.
        .onAppear { keepScreenAwake(true) }
        .onDisappear {
            model.stop()
            keepScreenAwake(false)
        }
        // Bought in the middle of a count, the page closes and the score turns sharp - computed from
        // the pile already lying there, nothing has to be scanned again.
        .sheet(isPresented: $showPurchase) {
            PurchaseView()
        }
        .sheet(isPresented: $showPicker) {
            CardPicker(missing: model.missingCards) { label in
                model.addCard(label)
                showPicker = false
            }
            .presentationDetents([.medium, .large])
        }
    }
}

// MARK: - Manual card picker

/// Adds a card the model will not recognise - typically one lying at a very flat angle. Only the
/// cards not yet on the pile are offered, so it cannot produce a duplicate.
private struct CardPicker: View {
    let missing: [String]
    let onPick: (String) -> Void

    @Environment(\.dismiss) private var dismiss

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 8), count: 3)

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVGrid(columns: columns, spacing: 8) {
                    ForEach(missing, id: \.self) { label in
                        Button { onPick(label) } label: {
                            CardChip(label: label, rankColor: .primary, markSize: 24)
                                .font(.callout.weight(.medium))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 12)
                                .background(Color(white: 0.30), in: RoundedRectangle(cornerRadius: 8))
                                .contentShape(RoundedRectangle(cornerRadius: 8))
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding()
            }
            .navigationTitle(String(localized: "scan.add_card", defaultValue: "Karte hinzufügen"))
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(String(localized: "common.cancel", defaultValue: "Abbrechen")) { dismiss() }
                }
            }
        }
    }
}

// MARK: - Viewfinder (re-renders per frame)

/// The camera, cropped to exactly the square Vision analyses, centred in the space the bar and the
/// controls leave over.
///
/// Because the preview fills a square view, it shows precisely the centred square of the camera
/// image - which is what `LiveDetectionModel.region` hands Vision. Picture and analysis therefore
/// share one coordinate system: a detection box, normalised to that region, maps straight onto the
/// square with no letterbox arithmetic in between.
private struct Viewfinder: View {
    let model: LiveDetectionModel

    var body: some View {
        GeometryReader { geometry in
            let side = min(geometry.size.width, geometry.size.height)

            ZStack(alignment: .topLeading) {
                #if targetEnvironment(simulator)
                VideoPreview(player: model.video.player)
                #else
                CameraPreview(session: model.camera.session)
                #endif

                if let detection = model.current {
                    // Vision reports the box with a bottom-left origin; SwiftUI draws from the top.
                    let box = CGRect(x: detection.box.minX * side,
                                     y: (1 - detection.box.maxY) * side,
                                     width: detection.box.width * side,
                                     height: detection.box.height * side)
                    RoundedRectangle(cornerRadius: 4)
                        .stroke(.green, lineWidth: 3)
                        .frame(width: box.width, height: box.height)
                        .offset(x: box.minX, y: box.minY)
                }
            }
            // The picture takes no taps; what lies on top of it may - the camera notice has a button.
            .allowsHitTesting(false)
            .frame(width: side, height: side)
            .clipShape(RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(.white.opacity(0.22), lineWidth: 1))
            // Both of these belong to the picture and are drawn on it, the way a camera shows its
            // recording light in the frame rather than beside it. It also means they cost no layout:
            // a note that comes and goes cannot resize the square underneath it.
            .overlay(alignment: .topTrailing) { RecordingLight(model: model) }
            .overlay(alignment: .bottom) { StatusMessage(model: model).padding(10) }
            // Centred in whatever space is left.
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

/// The red light that says a session is being recorded.
///
/// On the picture rather than in the status bar. A recording that runs unnoticed would be a nasty
/// surprise on the storage bill, so it has to be visible - but in the bar it would either shift the
/// frame rate sideways whenever recording starts, or, with its place held, leave an unexplained
/// gap to the left of the frame rate for the whole session. Over the picture it needs no place held.
private struct RecordingLight: View {
    let model: LiveDetectionModel

    var body: some View {
        if model.recording {
            Image(systemName: "record.circle")
                .foregroundStyle(.red)
                // Its own small dark disc: the light sits on the live picture and has to read on a
                // red tablecloth as well as on a white one.
                .padding(5)
                .background(.black.opacity(0.45), in: Circle())
                .padding(8)
                .accessibilityLabel("Aufnahme läuft")
        }
    }
}

// MARK: - Status (re-renders a few times per second)

private struct StatusBar: View {
    let model: LiveDetectionModel

    var body: some View {
        HStack(spacing: 12) {
            Label(String(format: "%.0f FPS", model.fps), systemImage: "speedometer")
            Text(String(format: "%.0f ms", model.inferenceMs))
                .foregroundStyle(.secondary)
            Spacer()
            if let current = model.current {
                // The card itself stays neutral; the percentage carries the state instead - yellow
                // means it is already on the pile and will not be added again.
                let captured = model.pile.contains(current.label)
                // The mark last, so it is measured from the right edge of the bar and holds still
                // while the reading under it changes. See `MarkSide`.
                HStack(spacing: 6) {
                    Text("\(Int(current.confidence * 100))%")
                        .foregroundColor(captured ? .yellow : .green)
                    CardChip(label: current.label,
                             markSize: ScanMetrics.statusMark, markSide: .trailing)
                }
                .fontWeight(.semibold)
            } else {
                Text("—").foregroundStyle(.secondary)
            }
        }
        .font(.callout.monospacedDigit())
        // The card on the right is a drawn mark and taller than the dash that stands there when
        // nothing is recognised. Without a floor under the row the whole bar would shrink and grow
        // with every detection, and the viewfinder under it would breathe along. A minimum rather than a
        // fixed height, so a larger text size still fits instead of being clipped.
        .frame(minHeight: ScanMetrics.statusMark)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        // Grey rather than near-black: the suit symbol is printed in its card colour, and a black
        // Kreuz or Schaufel would vanish on a dark bar. This is the same grey the card chips and the
        // picker use, so the greys of the app stay one family.
        .background(Color(white: 0.30), in: RoundedRectangle(cornerRadius: 14))
        .foregroundStyle(.white)
    }
}

private struct StatusMessage: View {
    let model: LiveDetectionModel

    var body: some View {
        VStack(spacing: 6) {
            if model.cameraDenied {
                deniedNotice
            } else if let message = model.statusMessage {
                note(message, tint: .white)
            }
            // What the last save did, briefly - the file name, so it can be found again later.
            if let capture = model.captureNote {
                note(capture, tint: .green)
            }
        }
    }

    /// The one failure the person can undo. A sentence over a black square leaves them stuck, and
    /// it is also the first thing App Review tries - so this says what happened, why the app needs
    /// the camera, and offers the way into the settings.
    private var deniedNotice: some View {
        VStack(spacing: 10) {
            Image(systemName: "video.slash")
                .font(.largeTitle)
                .foregroundStyle(.secondary)
            Text(String(localized: "scan.camera_denied", defaultValue: "Kein Kamerazugriff"))
                .font(.headline)
            Text(String(localized: "scan.camera_denied_text", defaultValue: "JassCardEye erkennt die oberste Karte über die Kamera. Ohne Zugriff kann nicht gezählt werden."))
                .font(.footnote)
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            #if os(iOS)
            Button(String(localized: "scan.open_settings", defaultValue: "Einstellungen öffnen")) {
                guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
                UIApplication.shared.open(url)
            }
            .buttonStyle(.borderedProminent)
            #endif
        }
        .foregroundStyle(.white)
        .padding(20)
        .background(.black.opacity(0.75), in: RoundedRectangle(cornerRadius: 14))
        .padding(.horizontal, 24)
    }

    private func note(_ text: String, tint: Color) -> some View {
        Text(text)
            .font(.footnote)
            .multilineTextAlignment(.center)
            .padding(10)
            .background(.black.opacity(0.65), in: RoundedRectangle(cornerRadius: 8))
            .foregroundStyle(tint)
    }
}

// MARK: - Score and controls (re-renders only when a card is committed)

private struct ScoreBar: View {
    @Bindable var model: LiveDetectionModel
    @Environment(Store.self) private var store
    @Binding var showPicker: Bool
    @Binding var showPurchase: Bool
    let onFinish: () -> Void

    var body: some View {
        VStack(spacing: 10) {
            if !store.unlocked {
                UnlockButton { showPurchase = true }
            }

            // Only one party's tricks get counted after a game: the own score counts up, the
            // opponents' score is the remainder and counts down with every card added. Deck, mode,
            // last trick and factor were settled before the camera started, so they are shown here
            // rather than offered - but shown, so the total stays explainable.
            ScoreRow(points: model.points,
                     pointsCaption: myCaption,
                     opponentPoints: model.opponentPoints,
                     cards: model.pile.count,
                     modeName: model.mode.displayName(deck: model.deck),
                     modeSuit: model.mode.markSuit(deck: model.deck),
                     note: model.lastTrick.note,
                     locked: !store.unlocked)

            HStack(spacing: 8) {
                Button {
                    showPicker = true
                } label: {
                    Text(String(localized: "scan.missing_card", defaultValue: "Karte fehlt?"))
                        .padding(.horizontal, 12)
                        .padding(.vertical, 10)
                        .background(.white.opacity(0.18), in: Capsule())
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .disabled(model.pile.count == DeckLayout.count)

                // Keeps a picture of a situation the model got wrong, for looking at afterwards.
                // The count is lasting confirmation - a toast that has faded leaves you wondering
                // whether the tap registered. Only with the developer tools on.
                if model.developerTools {
                    Button {
                        model.captureFrame()
                    } label: {
                        Label(model.capturedCount > 0 ? "Bild \(model.capturedCount)" : "Bild",
                              systemImage: "square.and.arrow.down")
                            .padding(.horizontal, 12)
                            .padding(.vertical, 10)
                            .background(.white.opacity(0.18), in: Capsule())
                            .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Bild sichern")
                }

                // Light for a dark table. Icon only: the row already carries two labelled buttons
                // and a Reset, and a fourth word would push them off a narrow phone. The state is
                // in the colour, which is the one thing about a torch nobody has to be told.
                if model.hasTorch {
                    Button {
                        model.toggleTorch()
                    } label: {
                        Image(systemName: model.torchOn ? "flashlight.on.fill" : "flashlight.off.fill")
                            .frame(width: 22)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 10)
                            .background(model.torchOn ? Color.yellow : .white.opacity(0.18),
                                        in: Capsule())
                            .foregroundStyle(model.torchOn ? Color.black : Color.white)
                            .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(model.torchOn ? String(localized: "scan.torch_off", defaultValue: "Licht ausschalten")
                                                      : String(localized: "scan.torch_on", defaultValue: "Licht einschalten"))
                    .accessibilityAddTraits(model.torchOn ? [.isSelected] : [])
                }

                Spacer()
                Button(String(localized: "scan.reset", defaultValue: "Reset"), role: .destructive) { model.reset() }
                    .buttonStyle(.bordered)
                    .disabled(model.pile.isEmpty)
            }
            .font(.callout)
            .foregroundStyle(.white)

            // The strip keeps its place from the start. If it existed only once a card had been
            // counted, the first recognition would push the score and the buttons upwards - at the
            // one moment the eye is on the card and not on the layout.
            Group {
                if model.pile.isEmpty {
                    Text(String(localized: "scan.empty", defaultValue: "Erkannte Karten erscheinen hier."))
                        .font(.callout)
                        .foregroundStyle(.white.opacity(0.45))
                        .frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    // Tapping a chip removes that card - the remedy when the model named the wrong
                    // one. The list follows the newest card: during a count the phone lies on the
                    // table and nobody wants to drag the strip along to see what was just recognised.
                    ScrollViewReader { proxy in
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 6) {
                                ForEach(Array(model.pile.enumerated()), id: \.offset) { index, label in
                                    Button { model.removeCard(label) } label: {
                                        HStack(spacing: 5) {
                                            // Neutral grey chip, white text - only the suit mark
                                            // carries colour, as on a printed card.
                                            Text("\(index + 1).").foregroundColor(.white)
                                            CardChip(label: label, markSize: ScanMetrics.chipMark)
                                            Image(systemName: "xmark.circle.fill")
                                                .font(.caption2)
                                                .foregroundStyle(.white.opacity(0.5))
                                        }
                                        .font(.callout.weight(.medium))
                                        .padding(.horizontal, 10)
                                        .padding(.vertical, ScanMetrics.chipPadding)
                                        // The newest card sits a shade lighter, so the eye finds it.
                                        .background(Color(white: index == model.pile.count - 1 ? 0.42 : 0.26),
                                                    in: Capsule())
                                        .contentShape(Capsule())
                                    }
                                    .buttonStyle(.plain)
                                    .id(index)
                                }
                            }
                        }
                        .onChange(of: model.pile.count) { _, count in
                            guard count > 0 else { return }
                            withAnimation(.easeOut(duration: 0.2)) {
                                proxy.scrollTo(count - 1, anchor: .trailing)
                            }
                        }
                    }
                }
            }
            // Both branches reserve the same room, so the swap from the hint to the first chip moves
            // nothing. A minimum, so a larger text size grows the strip instead of clipping it.
            .frame(minHeight: ScanMetrics.stripHeight)

            Button(action: onFinish) {
                Text(String(localized: "common.done", defaultValue: "Fertig"))
                    .font(.headline)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
        }
        .padding(12)
        .background(.black.opacity(0.65), in: RoundedRectangle(cornerRadius: 12))
        .foregroundStyle(.white)
    }

    /// The number is what gets written on the slate; when multiplier or bonuses are in play, this
    /// caption shows where it comes from ("62 Karten +5 ×2").
    private var myCaption: String {
        // In the demo the breakdown would give the points away. The factor stays: it was chosen at
        // the start, not counted.
        let mine = String(localized: "score.mine", defaultValue: "meine Punkte")
        guard store.unlocked else {
            return model.multiplier > 1
                ? String(localized: "score.mine_factor", defaultValue: "meine Punkte ×\(model.multiplier)")
                : mine
        }
        guard model.bonusPoints > 0 || model.multiplier > 1 else { return mine }
        var parts = [String(localized: "score.card_points", defaultValue: "\(model.cardPoints) Karten")]
        if model.bonusPoints > 0 { parts.append("+\(model.bonusPoints)") }
        if model.multiplier > 1 { parts.append("×\(model.multiplier)") }
        return parts.joined(separator: " ")
    }

}

// MARK: - Camera preview (UIKit/AppKit bridge)

#if os(iOS)
struct CameraPreview: UIViewRepresentable {
    let session: AVCaptureSession

    final class PreviewView: UIView {
        override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
        var previewLayer: AVCaptureVideoPreviewLayer { layer as! AVCaptureVideoPreviewLayer }
    }

    func makeUIView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.previewLayer.session = session
        // The preview lives in a square view, and filling it crops to the centred square of the
        // camera image - exactly the region Vision is given, so picture and analysis agree.
        view.previewLayer.videoGravity = .resizeAspectFill
        return view
    }

    func updateUIView(_ view: PreviewView, context: Context) {}
}
#else
struct CameraPreview: NSViewRepresentable {
    let session: AVCaptureSession

    final class PreviewView: NSView {
        let previewLayer = AVCaptureVideoPreviewLayer()

        override init(frame: NSRect) {
            super.init(frame: frame)
            wantsLayer = true
            previewLayer.videoGravity = .resizeAspectFill
            layer?.addSublayer(previewLayer)
        }

        required init?(coder: NSCoder) { fatalError("not used") }

        override func layout() {
            super.layout()
            previewLayer.frame = bounds
        }
    }

    func makeNSView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.previewLayer.session = session
        return view
    }

    func updateNSView(_ view: PreviewView, context: Context) {}
}
#endif
