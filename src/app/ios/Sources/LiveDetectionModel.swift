import Foundation
import CoreVideo
import CoreGraphics
import Observation
import os

/// Drives the live loop: camera frames -> detection inside the framing square -> stability rule ->
/// virtual pile. All published state is mutated on the main actor; the heavy lifting happens on the
/// camera queue.
@Observable @MainActor
final class LiveDetectionModel {

    // MARK: - Published state

    /// Best detection of the current frame (box normalised to the framing square), nil = no card.
    private(set) var current: Detection?
    /// Cards committed to the virtual pile, oldest first.
    private(set) var pile: [String] = []
    /// Analysed frames per second (the real rate - dropped frames don't count).
    private(set) var fps: Double = 0
    /// How long the last frame's Vision request took - crop, scaling, the model and its NMS - in
    /// milliseconds.
    private(set) var inferenceMs: Double = 0
    /// Width/height of the camera image, needed to place the framing square over the preview.
    private(set) var imageAspect: CGFloat = 9.0 / 16.0
    /// Set when camera or model are unavailable.
    private(set) var statusMessage: String?

    /// True when the camera was refused. Kept apart from `statusMessage` because it is the one
    /// failure with a remedy: the scan screen then offers the way into the system settings instead
    /// of leaving a black square and a sentence.
    private(set) var cameraDenied = false

    /// Whether this phone can light the table. Read once the camera is wired up; the scan screen
    /// hides the button rather than offering one that does nothing.
    private(set) var hasTorch = false

    /// Whether the torch is on right now.
    ///
    /// Not persisted and not carried between sessions: a room that was dark an hour ago says
    /// nothing about this one, and a light that came on by itself when the scanner opened would be
    /// a surprise rather than a convenience.
    private(set) var torchOn = false

    /// Switches the torch, and takes the device's word for the result. iOS refuses the torch while
    /// the phone is too warm and turns it off again on its own, so trusting the request instead of
    /// the answer would leave the button claiming a light that is not there.
    func toggleTorch() {
        #if targetEnvironment(simulator)
        torchOn = false
        #else
        torchOn = camera.setTorch(!torchOn)
        #endif
    }

    /// The recognition variants whose models are in the bundle, in the order C, A, B.
    let availableVariants: [RecognitionVariant]
    /// Which variant is currently running. Switching it rebuilds the recogniser and clears the pile -
    /// a score must not mix two models.
    private(set) var variant: RecognitionVariant

    /// A card must be the top detection in this many consecutive frames before it is committed.
    /// Persisted: it is tuned once for how someone holds the phone, not per session.
    var requiredFrames = LiveDetectionModel.defaultFrames {
        didSet {
            let value = requiredFrames
            settings.withLock { $0.frames = value }
            UserDefaults.standard.set(value, forKey: Self.framesKey)
        }
    }

    /// Nonisolated because the camera queue's copy of the settings is seeded with it, from a
    /// property initialiser that is not on the main actor.
    nonisolated static let defaultFrames = 3
    /// The range the settings stepper offers - also what a stored value is checked against.
    static let frameRange = 1...10
    private static let framesKey = "requiredFrames"

    /// How a card earns its place on the pile. Switchable so both can be tried against real piles -
    /// the second is expected to be the better trade, but that is reasoning, not a measurement.
    var stabilityRule: StabilityRule = .run {
        didSet {
            let value = stabilityRule
            settings.withLock { $0.rule = value }
            UserDefaults.standard.set(value.rawValue, forKey: Self.ruleKey)
        }
    }

    private static let ruleKey = "stabilityRule"

    /// Which lenses this phone has. Empty or single-entry means the picker stays hidden.
    let availableLenses = CameraLens.available()

    /// Which lens films the pile. Takes effect at the next session - the setting lives on the home
    /// screen, where no camera is running, so nothing has to be rebuilt mid-count.
    var cameraLens: CameraLens = .wide {
        didSet {
            UserDefaults.standard.set(cameraLens.rawValue, forKey: Self.lensKey)
        }
    }

    /// Which deck is on the table. Chosen before the session starts, not read off the frames.
    ///
    /// It sits in the start questions rather than in the settings because it is a fact about the
    /// evening, asked where the other facts about the round are asked. The last choice is kept, so
    /// a table that always plays the same deck confirms it and moves on.
    ///
    /// The model knows all 72 cards, so on a French evening it can and does offer a Schilte - a
    /// card that is not in the game. Reading the deck off what was recognised would make that worse,
    /// not better: one wrong detection would switch the whole app over. With the deck fixed,
    /// everything from the other one is dropped in the frame loop before it can reach the pile, and
    /// the app names cards and trump the way that deck does.
    ///
    /// Changing it clears the pile - a count half in one deck and half in the other is meaningless.
    /// In the normal flow there is nothing to clear, because the choice is made before counting.
    var deck: JassDeck = .french {
        didSet {
            guard deck != oldValue else { return }
            let value = deck
            settings.withLock { $0.deck = value }
            UserDefaults.standard.set(value.rawValue, forKey: Self.deckKey)
            reset()
        }
    }

    private static let deckKey = "deck"

    private static let lensKey = "cameraLens"

    // MARK: - Feedback per counted card

    /// How firm the tap for a counted card is, from 0 (off) to 1. Persisted: like the stability
    /// threshold it is set once for a phone and a table, not per round.
    var hapticStrength = LiveDetectionModel.defaultFeedback {
        didSet {
            Haptics.strength = hapticStrength
            UserDefaults.standard.set(hapticStrength, forKey: Self.hapticKey)
        }
    }

    /// How loud the tick that sounds with the tap is, from 0 (off) to 1.
    var soundVolume = LiveDetectionModel.defaultFeedback {
        didSet {
            CardSound.volume = soundVolume
            UserDefaults.standard.set(soundVolume, forKey: Self.soundKey)
        }
    }

    /// Where a fresh install starts: the middle of both sliders. For the tap that is the heavy
    /// generator at half intensity, which is still a firm tap - see `Haptics`.
    nonisolated static let defaultFeedback = 0.5
    /// What the sliders offer - and what a stored value is checked against.
    static let feedbackRange: ClosedRange<Double> = 0...1
    private static let hapticKey = "hapticStrength"
    private static let soundKey = "soundVolume"

    /// A stored strength or volume, or the middle when there is none or it is unusable. Read with
    /// `object(forKey:)` rather than `double(forKey:)`: a missing key must not read as 0, because 0
    /// means off, and a fresh install would start silent.
    private static func restoredFeedback(forKey key: String) -> Double {
        guard let value = UserDefaults.standard.object(forKey: key) as? Double,
              feedbackRange.contains(value) else { return defaultFeedback }
        return value
    }

    /// The discipline this round is counted in. Settled before the camera starts, so by the time a
    /// card is committed it can no longer change - which is why this needs no pile-clearing dance.
    private(set) var mode: CountingMode = JassModes.standard

    /// Who took the last trick, or that the round is counted without it. A pile of cards cannot
    /// show it, so the session asks - by then the game is over and the answer is known.
    private(set) var lastTrick: LastTrick = .mine

    /// What the written result is multiplied by. Settled in the start questions with the rest of
    /// the round - it belongs to the table's agreement, not to the discipline.
    private(set) var multiplier = 1

    /// True once the session has been opened and cards may be committed.
    private(set) var counting = false

    // MARK: - Session

    /// Opens a counting session with what the start questions established. The camera is started by
    /// the view afterwards - nothing runs before the answers are in.
    func beginCounting(mode chosen: CountingMode, lastTrick trick: LastTrick, multiplier factor: Int) {
        mode = chosen
        lastTrick = trick
        multiplier = factor
        reset()
        counting = true
        // Warms the Taptic Engine and the audio player now rather than on the first card, which
        // would otherwise be the one card whose confirmation arrives late.
        Haptics.prepare()
        CardSound.prepare()
        capturedCount = 0
        captureNote = nil
        let name = chosen.token(deck: deck)
        settings.withLock { $0.counting = true; $0.discipline = name }

        // Only with the developer tools on: a switch nobody can see must not record.
        if developerTools && recordSession {
            let url = FrameCapture.folder.appendingPathComponent(FrameCapture.sessionFileName(mode: name))
            let info = try? sessionInfo(discipline: name).json()
            recorder.withLock { $0 = SessionRecorder(url: url, info: info) }
            recording = true
        }
    }

    /// What this session is recorded with, for the JSON beside the recording - see `SessionInfo`.
    private func sessionInfo(discipline: String) -> SessionInfo {
        SessionInfo(
            platform: SessionInfo.platform,
            appVersion: AppInfo.version,
            appBuild: AppInfo.build,
            device: SessionInfo.deviceModel,
            system: SessionInfo.systemVersion,
            modelVariant: variant.rawValue,
            modelRun: SessionInfo.modelRun(variant: variant.rawValue),
            // Core ML decides per layer between Neural Engine, GPU and CPU; the app allows all three.
            compute: "Core ML, all compute units",
            confidenceThreshold: SessionInfo.rounded(minConfidence),
            stabilityRule: stabilityRule.rawValue,
            stabilityFrames: requiredFrames,
            deck: deck.rawValue,
            cameraLens: cameraLens.rawValue,
            discipline: discipline,
            startedAt: SessionInfo.timestamp())
    }

    /// Ends the session and reports what was counted. The camera is stopped by the view that owns
    /// the session; this only closes the gate and hands back the numbers.
    func finishCounting() -> CountResult {
        counting = false
        settings.withLock { $0.counting = false }
        return CountResult(modeName: mode.displayName(deck: deck),
                           modeSuit: mode.markSuit(deck: deck)?.token, cards: pile.count,
                           points: points, opponentPoints: opponentPoints,
                           multiplier: multiplier)
    }

    /// Closes a running recording and reports what became of it. Safe to call when nothing is being
    /// recorded.
    private func finishRecording() {
        guard let active = recorder.withLock({ let value = $0; $0 = nil; return value }) else { return }
        recording = false
        Task { @MainActor in
            switch await active.finish() {
            case .saved(let name, let frames, let problem):
                self.showNote("Aufnahme gesichert: \(name) (\(frames) Bilder)" + (problem.map { " – \($0)" } ?? ""), for: 6)
            case .failed(let reason):
                // Said out loud. A recording that broke halfway looks exactly like one that was
                // never started, and the tester would keep filming into a file that is gone.
                self.showNote(reason, for: 6)
            case .nothingRecorded:
                break
            }
        }
    }

    /// Shows a short note under the viewfinder and clears it again.
    ///
    /// Identified by a counter rather than by its text: two notes can read the same, and comparing
    /// strings would let an older one clear a newer one early.
    private func showNote(_ text: String, for seconds: Double) {
        captureNoteID &+= 1
        let id = captureNoteID
        captureNote = text
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(seconds))
            if self.captureNoteID == id { self.captureNote = nil }
        }
    }

    // MARK: - Corrections

    /// Removes a card from the pile - the remedy when the model named the wrong one. The card is
    /// free to be recognised again, but not while it is still the top detection.
    func removeCard(_ label: String) {
        guard let state = tracker.withLock({ $0.remove(label) ? $0 : nil }) else { return }
        show(state)
    }

    /// Adds a card by hand - for one the model will not recognise, typically at a very flat angle.
    func addCard(_ label: String) {
        // The same confirmation as a recognised card: the pile grew, however the card got there.
        guard let state = tracker.withLock({ $0.add(label) ? $0 : nil }), show(state) else { return }
        cardCounted()
    }

    /// The pile the frame loop decides on. Everything about it - cards, corrections, the stability
    /// window - changes as a whole under this one lock, from the camera queue and from the main actor
    /// alike; see `PileTracker`.
    private nonisolated let tracker = OSAllocatedUnfairLock(initialState: PileTracker())

    /// Which version of the pile is on screen.
    @ObservationIgnored private var shownPileVersion = 0

    /// Puts a state of the pile on screen - unless a newer one is already there. A frame's commit
    /// reaches the main actor a moment after it was decided, and a correction or a Reset can land in
    /// between; the older state is then simply stale. Returns whether it was shown.
    @discardableResult
    private func show(_ state: PileTracker) -> Bool {
        guard state.version > shownPileVersion else { return false }
        shownPileVersion = state.version
        pile = state.cards
        return true
    }

    /// The tap and the tick. Only where a card really joined the pile - not on a detection, which
    /// happens many times a second for the same card, and not for a state that was already stale: a
    /// confirmation for something that did not happen is worse than none at all.
    private func cardCounted() {
        Haptics.cardCounted()
        CardSound.cardCounted()
    }

    /// Cards not yet on the pile, in deck order - the choices of the manual picker.
    ///
    /// Of the deck that was chosen before the session, so the picker offers exactly the 36 cards
    /// that can be on the table - never a Schilte on a French evening.
    var missingCards: [String] {
        let onPile = Set(pile)
        return JassSuit.all.filter { $0.deck == deck }.flatMap { suit in
            DeckLayout.ranks.map { "\(suit.token)_\($0)" }
        }.filter { !onPile.contains($0) }
    }

    /// Card points of the committed pile in the current discipline.
    var cardPoints: Int {
        pile.reduce(0) { $0 + mode.points(forLabel: $1) }
    }

    /// The pile's arithmetic, which lives in `JassScoring` so it can be checked without the camera.
    /// There is no match bonus: a pile of all 36 cards is 152, and with the last trick that is the
    /// 157 everyone writes down, without the app needing to be told about a match.
    var tally: Tally {
        Tally(mode: mode, lastTrick: lastTrick, multiplier: multiplier, cardPoints: cardPoints)
    }

    var bonusPoints: Int { tally.bonus }
    /// What gets written on the slate: card points plus the last trick, times the chosen factor.
    var points: Int { tally.points }
    /// The other party's written points - only one party's tricks are counted after a game.
    var opponentPoints: Int { tally.opponentPoints }
    var minConfidence: Float = 0.6 {
        didSet { let value = minConfidence; settings.withLock { $0.confidence = value } }
    }

    /// Hands the UI-owned settings to the camera queue without touching the main actor per frame.
    /// `counting` gates committing: a frame that arrives after the session has closed must not land
    /// on the pile. `discipline` rides along so a saved frame can record what was being counted.
    private nonisolated let settings = OSAllocatedUnfairLock(
        initialState: (confidence: Float(0.6), frames: LiveDetectionModel.defaultFrames,
                       counting: false, discipline: "", rule: StabilityRule.run,
                       deck: JassDeck.french, variant: RecognitionVariant.c))

    /// Whether *Bild* and *Session aufzeichnen* are on screen. They are for looking into a wrong
    /// count, not for playing, so they stay hidden until someone taps *Firma* on the *Über* page
    /// five times - and hide again after five more. Persisted: whoever unlocked them wants them the
    /// next time the app opens as well.
    var developerTools = false {
        didSet { UserDefaults.standard.set(developerTools, forKey: Self.developerToolsKey) }
    }

    private static let developerToolsKey = "developerTools"

    /// Whether the next session is recorded end to end. Chosen before starting, next to the start
    /// button - a decision about the session, not something to fiddle with while counting.
    var recordSession = false

    /// The recorder of the running session, nil when nothing is being recorded. Read on the camera
    /// queue for every frame and swapped from the main actor at the session boundaries.
    private nonisolated let recorder = OSAllocatedUnfairLock<SessionRecorder?>(initialState: nil)

    /// True while a session is being recorded - the scanner shows it, since a recording that runs
    /// unnoticed is a nasty surprise.
    private(set) var recording = false

    /// Set by the save button; the next frame on the camera queue writes itself out and clears it.
    /// Requesting rather than handing a buffer to the main actor is deliberate - a capture buffer
    /// held outside the callback is one the camera cannot reuse.
    private nonisolated let captureRequested = OSAllocatedUnfairLock(initialState: false)

    /// Feedback for the last save: the file name, or why it failed. Clears itself after a moment.
    private(set) var captureNote: String?
    /// How many frames were saved in this session - shown on the button as lasting confirmation.
    private(set) var capturedCount = 0
    @ObservationIgnored private var captureNoteID = 0

    /// Saves the square currently being analysed, for looking at after the game. Takes effect on the
    /// next frame, so a card that is on screen right now is the one that gets written.
    func captureFrame() {
        captureRequested.withLock { $0 = true }
    }

    // The simulator has no camera, so there a looping video file stands in as the frame source.
    // Everything downstream - detection, stability rule, pile, score - is identical.
    #if targetEnvironment(simulator)
    let video = VideoFrameSource()
    #else
    let camera = CameraService()
    #endif

    /// A recogniser together with the variant it was built for - one value, so the two cannot disagree.
    private struct LoadedRecognizer: Sendable {
        let variant: RecognitionVariant
        let recognizer: CardRecognizer
    }

    // The active recogniser is read on the camera queue and swapped from the main actor when the
    // variant changes, so it lives behind a lock rather than in an actor-isolated property.
    private nonisolated let activeRecognizer = OSAllocatedUnfairLock<LoadedRecognizer?>(initialState: nil)

    init() {
        let available = ModelCatalog.availableVariants()
        availableVariants = available
        // C when present, the variant a release ships; otherwise the first variant that has its
        // models.
        variant = available.contains(.c) ? .c : (available.first ?? .c)

        // The discipline is not restored: it belongs to the round, and the round is over. It is
        // asked before every session, which is also where the pile is cleared.

        // Restore the stability threshold. A missing key reads as 0, and a stored value can sit
        // outside the stepper's range, so anything unusable falls back to the default rather than
        // being trusted.
        let savedFrames = UserDefaults.standard.integer(forKey: Self.framesKey)
        let frames = Self.frameRange.contains(savedFrames) ? savedFrames : Self.defaultFrames
        requiredFrames = frames

        // Same for the counting rule; an unknown stored value falls back to the default, *Serie*.
        let savedRule = UserDefaults.standard.string(forKey: Self.ruleKey)
        let restoredRule = savedRule.flatMap(StabilityRule.init(rawValue:)) ?? .run
        stabilityRule = restoredRule

        // The lens too - but only if this phone still has it; a setting can outlive its device.
        let savedLens = UserDefaults.standard.string(forKey: Self.lensKey).flatMap(CameraLens.init(rawValue:))
        cameraLens = savedLens.flatMap { availableLenses.contains($0) ? $0 : nil } ?? .wide

        // The deck last played with. Restored before anything else reads it, and seeded into the
        // camera queue's copy below.
        let savedDeck = UserDefaults.standard.string(forKey: Self.deckKey).flatMap(JassDeck.init(rawValue:))
        let restoredDeck = savedDeck ?? .french
        deck = restoredDeck

        // The feedback per counted card. Missing or out of range means the middle.
        let restoredHaptic = Self.restoredFeedback(forKey: Self.hapticKey)
        let restoredSound = Self.restoredFeedback(forKey: Self.soundKey)
        hapticStrength = restoredHaptic
        soundVolume = restoredSound

        // A missing key reads as false, which is where a fresh install starts.
        developerTools = UserDefaults.standard.bool(forKey: Self.developerToolsKey)

        // Assigning in init may not run the observers, and the camera queue reads its own copies -
        // so they are seeded explicitly instead of relying on that. The same goes for the feedback
        // types, which hold the strengths they play with.
        let selected = variant
        settings.withLock {
            $0.frames = frames; $0.rule = restoredRule; $0.deck = restoredDeck; $0.variant = selected
        }
        Haptics.strength = restoredHaptic
        CardSound.volume = restoredSound
    }

    // MARK: - Framing square

    /// The square's side as a fraction of the image width (portrait: the narrow side). Full width:
    /// the model gets every pixel the sensor delivers across the frame, and the square matches the
    /// centre-crop the training and validation images were normalised with.
    private nonisolated static let squareFraction: CGFloat = 1.0

    /// Framing square in Vision's normalised image coordinates (origin bottom-left). Centred, side
    /// = the full image width - matches what the overlay draws.
    nonisolated static func region(forAspect aspect: CGFloat) -> CGRect {
        guard aspect > 0 else { return CGRect(x: 0, y: 0, width: 1, height: 1) }

        // Largest centred square that still fits the frame. In portrait it spans the full width;
        // once the frame is square or wider the height becomes the limit instead. Without that
        // second case the region overshoots [0,1] - and Vision then rejects every single frame,
        // which looks exactly like "no card in sight". A source that is square to within one pixel
        // is enough to trigger it.
        var width = squareFraction
        var height = width * aspect   // aspect = w/h; equal side length in pixels needs w·(w/h) in v
        if height > 1 {
            height = 1
            width = 1 / aspect
        }
        return CGRect(x: (1 - width) / 2, y: (1 - height) / 2, width: width, height: height)
    }

    var region: CGRect { Self.region(forAspect: imageAspect) }

    // MARK: - Lifecycle

    /// Counts sessions. `start()` is async - it awaits the model and the camera - and the view can be
    /// gone by the time those return: SwiftUI cancels the `.task` but the awaits do not throw, so the
    /// function would run on and switch the camera on with nothing on screen. Comparing the token it
    /// took at entry against the current one is what makes a late start harmless.
    @ObservationIgnored private var sessionToken = 0

    func start() async {
        sessionToken &+= 1
        let token = sessionToken
        cameraDenied = false
        // Cleared with it, not left standing. Nothing else ever sets this back to nil, so a model
        // that failed to load once would keep its sentence over the picture for the rest of the
        // app's life - including over every later session that works perfectly well.
        statusMessage = nil

        guard !availableVariants.isEmpty else {
            statusMessage = "Kein Modell im Bundle - src/training/export.py ausführen."
            return
        }
        guard await loadRecognizer(variant) else { return }   // sets statusMessage on failure
        #if targetEnvironment(simulator)
        if let problem = await video.configure() {
            statusMessage = problem
            return
        }
        // Store screenshots are taken on the simulator too, and the hint is not part of the app a buyer sees:
        // `JASSCARDEYE_SCREENSHOTS=1` leaves it out.
        if ProcessInfo.processInfo.environment["JASSCARDEYE_SCREENSHOTS"] == nil {
            statusMessage = "Simulator: Testvideo statt Kamera (Endlosschleife)."
        }
        guard token == sessionToken else { return }   // the session was closed while we waited
        video.onFrame = { [weak self] buffer in self?.process(buffer) }
        video.start()
        #else
        if let problem = await camera.configure(lens: cameraLens) {
            statusMessage = problem.message
            cameraDenied = problem.isDenied
            return
        }
        guard token == sessionToken else { return }   // the session was closed while we waited
        // Only answerable once a device is attached, and it decides whether the button exists.
        hasTorch = camera.hasTorch
        camera.onFrame = { [weak self] buffer in self?.process(buffer) }
        camera.start()
        #endif
    }

    /// Ends the session, however the view was dismissed - not only via "Fertig". Bumping the token
    /// also disarms a `start()` still waiting on the camera.
    func stop() {
        sessionToken &+= 1
        counting = false
        // A save that was tapped but never reached a frame must not fire into the next session.
        captureRequested.withLock { $0 = false }
        settings.withLock { $0.counting = false }
        finishRecording()
        #if targetEnvironment(simulator)
        video.stop()
        #else
        // Before the session goes: a torch left burning after the count is the most expensive
        // mistake this screen could make, and the one nobody would notice until the battery did.
        if torchOn {
            camera.setTorch(false)
            torchOn = false
        }
        camera.stop()
        #endif
    }

    /// Switches the active model. The pile is cleared, because a score assembled under one model must
    /// not continue under another. Frames are not analysed for the moment the new one takes to load:
    /// a card read by the old model would land on the pile the switch has just cleared.
    func selectVariant(_ newVariant: RecognitionVariant) {
        guard newVariant != variant, availableVariants.contains(newVariant) else { return }
        variant = newVariant
        settings.withLock { $0.variant = newVariant }
        reset()
        Task { await loadRecognizer(newVariant) }
    }

    /// Builds a variant's recogniser off the main thread and installs it. Returns false, with the
    /// reason in the status message, only when a model is missing or fails to load.
    ///
    /// Kept across sessions: loading a CoreML model is slow enough to notice, and a counting app is
    /// opened and closed many times an evening. Memory is the cheaper side of that trade.
    @discardableResult
    private func loadRecognizer(_ target: RecognitionVariant) async -> Bool {
        // Already loaded from an earlier session - reuse it rather than pay the load again.
        if activeRecognizer.withLock({ $0?.variant }) == target { return true }
        do {
            let recognizer = try await Task.detached(priority: .userInitiated) {
                try ModelCatalog.makeRecognizer(target)
            }.value
            // The picker can have moved on while this loaded. A recogniser for a variant that is no
            // longer selected must not replace the one that is; the newer choice loads its own.
            guard target == variant else { return true }
            activeRecognizer.withLock { $0 = LoadedRecognizer(variant: target, recognizer: recognizer) }
            return true
        } catch {
            statusMessage = String(localized: "scan.model_failed", defaultValue: "Modell (\(target.displayName)) konnte nicht geladen werden: \(error.localizedDescription)")
            return false
        }
    }

    /// Clears the counted pile. The last trick is deliberately kept: it was answered for this game
    /// before the first scan, and rescanning the cards does not change who took it.
    func reset() {
        show(tracker.withLock { $0.reset(); return $0 })
    }

    // MARK: - Frame processing (camera queue)

    // Serial-queue state: only ever touched from the camera queue.
    //
    // `@ObservationIgnored` is not tidiness here. Without it the `@Observable` macro wraps each of
    // these in observation tracking, so every write - thirty times a second, from the camera queue -
    // goes through the registrar on a thread that is not the main actor, for a value no view ever
    // reads. It also makes the macro rewrite them as computed properties, which is why the compiler
    // then reports the `nonisolated(unsafe)` on them as having no effect.
    @ObservationIgnored private nonisolated(unsafe) var frameStamps: [CFAbsoluteTime] = []
    @ObservationIgnored private nonisolated(unsafe) var lastStatsPublished: CFAbsoluteTime = 0
    @ObservationIgnored private nonisolated(unsafe) var lastAspect: CGFloat = 0
    @ObservationIgnored private nonisolated(unsafe) var lastWasEmpty = true
    private nonisolated(unsafe) static var errorReported = false

    /// How often the frame rate and inference time are pushed to the UI. They are read by a human,
    /// so five times a second is plenty - publishing them per frame would invalidate the views
    /// thirty times a second and eat main-thread time the controls need to stay responsive.
    private nonisolated static let statsInterval: CFAbsoluteTime = 0.2

    private nonisolated func process(_ buffer: CVPixelBuffer) {
        let width = CGFloat(CVPixelBufferGetWidth(buffer))
        let height = CGFloat(CVPixelBufferGetHeight(buffer))
        let aspect = width / height
        let region = Self.region(forAspect: aspect)

        let (confidence, framesNeeded, counting, discipline, rule, deck, variant) = settings.withLock {
            ($0.confidence, $0.frames, $0.counting, $0.discipline, $0.rule, $0.deck, $0.variant)
        }

        // The recogniser of the variant the picker selected - not one still in place from before a
        // switch. Nil while the selected one loads.
        guard let loaded = activeRecognizer.withLock({ $0 }), loaded.variant == variant else { return }
        let recognizer = loaded.recognizer

        let started = CFAbsoluteTimeGetCurrent()
        var detections: [Detection] = []
        do {
            detections = try recognizer.recognize(.pixelBuffer(buffer),
                                                  regionOfInterest: region,
                                                  minConfidence: confidence)
        } catch {
            // Reported once instead of swallowed - a frame source Vision cannot read looks exactly
            // like "no card in sight", which is the hardest kind of failure to notice.
            if !Self.errorReported {
                Self.errorReported = true
                Logger(subsystem: "ch.yarx.JassCardEye", category: "detect")
                    .error("Erkennung fehlgeschlagen: \(String(describing: error), privacy: .public)")
            }
        }
        let elapsed = (CFAbsoluteTimeGetCurrent() - started) * 1000

        // A card of the other deck is not a card at all tonight. Dropped here rather than ignored
        // later, so it cannot win the stability window, cannot land on the pile, and does not even
        // draw a box - the same nothing an empty table gets.
        detections.removeAll { CardLabel($0.label)?.deck != deck }

        // A requested save happens here, on this frame: the detections are known, so the file name
        // can record what the model made of the very picture being written - including that it saw
        // nothing, which is the case worth collecting.
        if captureRequested.withLock({ let was = $0; $0 = false; return was }) {
            let top = detections.first
            let note: String
            var saved = false
            do {
                let name = try FrameCapture.save(buffer, region: region, label: top?.label,
                                                 confidence: top?.confidence ?? 0, mode: discipline)
                note = "Gesichert: \(name)"
                saved = true
            } catch {
                note = "Nicht gesichert: \(error.localizedDescription)"
            }
            Task { @MainActor in
                if saved { self.capturedCount += 1 }
                self.showNote(note, for: 4)
            }
        }

        // Analysis rate over a one-second sliding window.
        let now = CFAbsoluteTimeGetCurrent()
        frameStamps.append(now)
        frameStamps.removeAll { now - $0 > 1 }
        let rate = Double(frameStamps.count)

        // The stability rule and the pile, decided in one step. The two decks have separate labels,
        // so a card counted once stays counted whichever deck is on the table.
        let best = detections.first
        let committed = tracker.withLock {
            $0.observe(best?.label, rule: rule, votes: framesNeeded, counting: counting) ? $0 : nil
        }

        // This exact inference and commit decision travel with the accepted video frame.
        recorder.withLock { $0 }?.append(buffer, region: region, result: .init(
            label: best?.label, confidence: best?.confidence ?? 0,
            box: best?.box ?? .zero, committed: committed != nil))

        // Publish only what actually changed. Pointing the camera at nothing is the common case
        // and then there is nothing to send at all; the box needs every frame, the counters don't.
        let stats: (fps: Double, ms: Double)?
        if now - lastStatsPublished >= Self.statsInterval {
            lastStatsPublished = now
            stats = (rate, elapsed)
        } else {
            stats = nil
        }

        let isEmpty = best == nil
        let boxChanged = !isEmpty || !lastWasEmpty
        lastWasEmpty = isEmpty

        let aspectChanged = aspect != lastAspect
        lastAspect = aspect

        guard stats != nil || boxChanged || aspectChanged || committed != nil else { return }

        Task { @MainActor in
            if let stats {
                self.fps = stats.fps
                self.inferenceMs = stats.ms
            }
            if boxChanged { self.current = best }
            if aspectChanged { self.imageAspect = aspect }
            if let committed, self.show(committed) { self.cardCounted() }
        }
    }
}
