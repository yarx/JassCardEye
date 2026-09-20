package ch.yarx.jasscardeye

import android.Manifest
import android.app.Application
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableDoubleStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.core.content.edit
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.viewModelScope
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.asCoroutineDispatcher
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

// Port of src/app/ios/Sources/LiveDetectionModel.swift - same members in the same order, so a change made to both
// apps lands in the same place on each.

/**
 * Drives the live loop: camera frames -> detection inside the framing square -> stability rule -> virtual
 * pile. All published state is Compose state mutated on the main thread; the heavy lifting happens on the
 * analysis thread.
 */
class LiveDetectionModel(application: Application) : AndroidViewModel(application) {

    private val context: Context get() = getApplication()
    private val prefs = application.getSharedPreferences("settings", Context.MODE_PRIVATE)
    private val main = Handler(Looper.getMainLooper())

    // MARK: - Published state

    /** Best detection of the current frame (box normalised to the framing square), null = no card. */
    var current by mutableStateOf<Detection?>(null); private set
    /** Cards committed to the virtual pile, oldest first. */
    var pile by mutableStateOf<List<String>>(emptyList()); private set
    /** Analysed frames per second (the real rate - dropped frames don't count). */
    var fps by mutableDoubleStateOf(0.0); private set
    /** Model inference time of the last frame, in milliseconds. */
    var inferenceMs by mutableDoubleStateOf(0.0); private set
    /** Set when camera or model are unavailable. */
    var statusMessage by mutableStateOf<String?>(null); private set

    /**
     * True when the camera was refused. Kept apart from [statusMessage] because it is the one failure with
     * a remedy: the scan screen then offers the way into the system settings instead of leaving a black
     * square and a sentence.
     */
    var cameraDenied by mutableStateOf(false); private set

    /** Whether this phone can light the table. The scan screen hides the button rather than offering one that does nothing. */
    var hasTorch by mutableStateOf(false); private set

    /**
     * Whether the torch is on right now. Not persisted and not carried between sessions: a room that was
     * dark an hour ago says nothing about this one.
     */
    var torchOn by mutableStateOf(false); private set

    /**
     * Switches the torch, and takes the device's word for the result: the camera reports the torch state
     * back (it can refuse, or switch off on its own when the phone runs hot), and [torchOn] follows that
     * report rather than the request.
     */
    fun toggleTorch() = frames.setTorch(!torchOn)

    private val catalog = ModelCatalog(application)

    /** The recognition variants whose models are in the APK, in the order C, A, B. */
    val availableVariants: List<RecognitionVariant> = catalog.availableVariants()

    /**
     * Which variant is currently running. Switching it rebuilds the recogniser and clears the pile - a
     * score must not mix two models.
     */
    var variant by mutableStateOf(if (RecognitionVariant.C in availableVariants) RecognitionVariant.C
        else availableVariants.firstOrNull() ?: RecognitionVariant.C); private set

    private var framesState by mutableIntStateOf(DEFAULT_FRAMES)

    /** How many frames have to agree on the top card before it is committed - see [StabilityRule]. Persisted. */
    var requiredFrames: Int
        get() = framesState
        set(value) {
            framesState = value
            updateSettings { it.copy(frames = value) }
            prefs.edit { putInt(FRAMES_KEY, value) }
        }

    private var ruleState by mutableStateOf(StabilityRule.RUN)

    /** How a card earns its place on the pile. Switchable so both can be tried against real piles. */
    var stabilityRule: StabilityRule
        get() = ruleState
        set(value) {
            ruleState = value
            updateSettings { it.copy(rule = value) }
            prefs.edit { putString(RULE_KEY, value.id) }
        }

    /** Which lenses this phone has. Empty or single-entry means the picker stays hidden. Asked of the camera stack once, at launch. */
    var availableLenses by mutableStateOf<List<CameraLens>>(emptyList()); private set

    private var lensState by mutableStateOf(CameraLens.WIDE)

    /** Which lens films the pile. Takes effect at the next session. */
    var cameraLens: CameraLens
        get() = lensState
        set(value) {
            lensState = value
            prefs.edit { putString(LENS_KEY, value.id) }
        }

    private var deckState by mutableStateOf(JassDeck.FRENCH)

    /**
     * Which deck is on the table. Chosen before the session starts, not read off the frames.
     *
     * The model knows all 72 cards, so on a French evening it can and does offer a Schilte - a card that is
     * not in the game. With the deck fixed, everything from the other one is dropped in the frame loop
     * before it can reach the pile, and the app names cards and trump the way that deck does. Changing it
     * clears the pile.
     */
    var deck: JassDeck
        get() = deckState
        set(value) {
            if (value == deckState) return
            deckState = value
            updateSettings { it.copy(deck = value) }
            prefs.edit { putString(DECK_KEY, value.id) }
            reset()
        }

    // MARK: - Feedback per counted card

    private var hapticState by mutableDoubleStateOf(DEFAULT_FEEDBACK)

    /** How firm the tap for a counted card is, from 0 (off) to 1. Persisted. */
    var hapticStrength: Double
        get() = hapticState
        set(value) {
            hapticState = value
            Haptics.strength = value
            prefs.edit { putFloat(HAPTIC_KEY, value.toFloat()) }
        }

    private var soundState by mutableDoubleStateOf(DEFAULT_FEEDBACK)

    /** How loud the tick that sounds with the tap is, from 0 (off) to 1. */
    var soundVolume: Double
        get() = soundState
        set(value) {
            soundState = value
            CardSound.volume = value
            prefs.edit { putFloat(SOUND_KEY, value.toFloat()) }
        }

    /**
     * The discipline this round is counted in. Settled before the camera starts, so by the time a card is
     * committed it can no longer change.
     */
    var mode by mutableStateOf(JassModes.standard); private set

    /** Who took the last trick, or that the round is counted without it. */
    var lastTrick by mutableStateOf(LastTrick.MINE); private set

    /** What the written result is multiplied by. Settled in the start questions with the rest of the round. */
    var multiplier by mutableIntStateOf(1); private set

    /** True once the session has been opened and cards may be committed. */
    var counting by mutableStateOf(false); private set

    // MARK: - Session

    /** Opens a counting session with what the start questions established. The camera is started by the screen afterwards. */
    fun beginCounting(chosen: CountingMode, trick: LastTrick, factor: Int) {
        mode = chosen
        lastTrick = trick
        multiplier = factor
        reset()
        counting = true
        // Warms the feedback now rather than on the first card, which would otherwise be the one card
        // whose confirmation arrives late.
        Haptics.prepare()
        CardSound.prepare()
        capturedCount = 0
        captureNote = null
        val name = chosen.displayName(deck)
        updateSettings { it.copy(counting = true, modeName = name) }

        // Only with the developer tools on: a switch nobody can see must not record.
        if (developerTools && recordSession) {
            recorder.set(SessionRecorder(context, FrameCapture.sessionFileName(name), sessionInfo(name).json()))
            recording = true
        }
    }

    /** What this session is recorded with, for the JSON beside the recording - see [SessionInfo]. */
    private fun sessionInfo(discipline: String) = SessionInfo(
        platform = SessionInfo.PLATFORM,
        appVersion = AppInfo.version,
        appBuild = AppInfo.build,
        device = SessionInfo.deviceModel,
        system = SessionInfo.systemVersion,
        modelVariant = variant.id,
        modelRun = SessionInfo.modelRun(context, variant.id),
        // The recogniser's own word for where it runs: GPU, or CPU where LiteRT does not vouch for the GPU.
        compute = loaded?.takeIf { it.variant == variant }?.recognizer?.backend ?: "not loaded yet",
        confidenceThreshold = SessionInfo.rounded(minConfidence),
        stabilityRule = stabilityRule.id,
        stabilityFrames = requiredFrames,
        deck = deck.id,
        cameraLens = cameraLens.id,
        discipline = discipline,
        startedAt = SessionInfo.timestamp(),
    )

    /** Ends the session and reports what was counted. The camera is stopped by the screen that owns the session. */
    fun finishCounting(): CountResult {
        counting = false
        updateSettings { it.copy(counting = false) }
        return CountResult(mode.displayName(deck), mode.markSuit(deck)?.token, pile.size, points, opponentPoints, multiplier)
    }

    /** Closes a running recording and reports what became of it. Safe to call when nothing is being recorded. */
    private fun finishRecording() {
        val active = recorder.getAndSet(null) ?: return
        recording = false
        // On the analysis thread it was fed on, so the last frame cannot race it - and on the executor rather
        // than in viewModelScope, which is already cancelled when the screen goes away for good, and would
        // take the file with it.
        analysisExecutor.execute {
            val outcome = active.finish()
            // Also logged: the note lives under the viewfinder, and after Fertig nobody is looking there.
            Log.i(TAG, "Session recording: $outcome")
            main.post {
                when (outcome) {
                    is SessionRecorder.Outcome.Saved -> showNote(
                        "Aufnahme gesichert: ${outcome.name} (${outcome.frames} Bilder)" + (outcome.problem?.let { " – $it" } ?: ""), 6000)
                    // Said out loud. A recording that broke halfway looks exactly like one never started.
                    is SessionRecorder.Outcome.Failed -> showNote(outcome.reason, 6000)
                    SessionRecorder.Outcome.NothingRecorded -> {}
                }
            }
        }
    }

    /**
     * Shows a short note under the viewfinder and clears it again. Identified by a counter rather than by
     * its text: two notes can read the same, and comparing strings would let an older one clear a newer one.
     */
    private fun showNote(text: String, millis: Long) {
        captureNoteID += 1
        val id = captureNoteID
        captureNote = text
        viewModelScope.launch {
            delay(millis)
            if (captureNoteID == id) captureNote = null
        }
    }

    // MARK: - Corrections

    /** Removes a card from the pile - the remedy when the model named the wrong one. Not re-recognised while still on top. */
    fun removeCard(label: String) {
        show(synchronized(tracker) { if (tracker.remove(label)) tracker.snapshot else return })
    }

    /** Adds a card by hand - for one the model will not recognise, typically at a very flat angle. */
    fun addCard(label: String) {
        // The same confirmation as a recognised card: the pile grew, however the card got there.
        if (show(synchronized(tracker) { if (tracker.add(label)) tracker.snapshot else return })) cardCounted()
    }

    /**
     * The pile the frame loop decides on. Everything about it - cards, corrections, the stability window -
     * changes as a whole under this one lock, from the analysis thread and from main alike; see [PileTracker].
     */
    private val tracker = PileTracker()

    /** Which version of the pile is on screen. Main thread only. */
    private var shownPileVersion = 0

    /**
     * Puts a state of the pile on screen - unless a newer one is already there. A frame's commit is posted to
     * main a moment after it was decided, and a correction or a Reset can land in between; the older snapshot
     * is then simply stale. Returns whether it was shown.
     */
    private fun show(snapshot: PileTracker.Snapshot): Boolean {
        if (snapshot.version <= shownPileVersion) return false
        shownPileVersion = snapshot.version
        pile = snapshot.cards
        return true
    }

    /** The tap and the tick. Only where a card really joined the pile - a confirmation for something that did not happen is worse than none. */
    private fun cardCounted() {
        Haptics.cardCounted()
        CardSound.cardCounted()
    }

    /** Cards not yet on the pile, in deck order - the choices of the manual picker. Of the deck chosen before the session. */
    val missingCards: List<String>
        get() {
            val onPile = pile.toSet()
            return JassSuit.all.filter { it.deck == deck }
                .flatMap { suit -> DeckLayout.ranks.map { "${suit.token}_$it" } }
                .filter { it !in onPile }
        }

    /** Card points of the committed pile in the current discipline. */
    val cardPoints: Int get() = pile.sumOf { mode.points(it) }

    /**
     * The pile's arithmetic, which lives in `JassScoring` so it can be checked without the camera. There is
     * no match bonus: a pile of all 36 cards is 152, and with the last trick that is the 157 everyone writes.
     */
    val tally: Tally get() = Tally(mode, lastTrick, multiplier, cardPoints)

    val bonusPoints: Int get() = tally.bonus
    /** What gets written on the slate: card points plus the last trick, times the chosen factor. */
    val points: Int get() = tally.points
    /** The other party's written points - only one party's tricks are counted after a game. */
    val opponentPoints: Int get() = tally.opponentPoints

    var minConfidence: Float = 0.6f
        set(value) { field = value; updateSettings { it.copy(confidence = value) } }

    /**
     * The UI-owned settings as the analysis thread reads them, replaced as a whole rather than touched per
     * field. `counting` gates committing; `modeName` rides along so a saved frame can record what was counted.
     */
    private data class FrameSettings(
        val confidence: Float = 0.6f,
        val frames: Int = DEFAULT_FRAMES,
        val counting: Boolean = false,
        val modeName: String = "",
        val rule: StabilityRule = StabilityRule.RUN,
        val deck: JassDeck = JassDeck.FRENCH,
        val variant: RecognitionVariant = RecognitionVariant.C,
    )

    private val settings = AtomicReference(FrameSettings())

    private fun updateSettings(change: (FrameSettings) -> FrameSettings) {
        while (true) {
            val old = settings.get()
            if (settings.compareAndSet(old, change(old))) return
        }
    }

    private var developerToolsState by mutableStateOf(false)

    /**
     * Whether *Bild* and *Session aufzeichnen* are on screen. They are for looking into a wrong count, not for
     * playing, so they stay hidden until someone taps *Firma* on the *Über* page five times - and hide again after
     * five more. Persisted: whoever unlocked them wants them the next time the app opens as well.
     */
    var developerTools: Boolean
        get() = developerToolsState
        set(value) {
            developerToolsState = value
            prefs.edit { putBoolean(DEVELOPER_TOOLS_KEY, value) }
        }

    /** Whether the next session is recorded end to end. Chosen before starting, next to the start button. */
    var recordSession by mutableStateOf(false)

    /** The recorder of the running session, null when nothing is being recorded. */
    private val recorder = AtomicReference<SessionRecorder?>(null)

    /** True while a session is being recorded - the scanner shows it. */
    var recording by mutableStateOf(false); private set

    /** Set by the save button; the next frame on the analysis thread writes itself out and clears it. */
    private val captureRequested = AtomicBoolean(false)

    /** Feedback for the last save: the file name, or why it failed. Clears itself after a moment. */
    var captureNote by mutableStateOf<String?>(null); private set
    /** How many frames were saved in this session - shown on the button as lasting confirmation. */
    var capturedCount by mutableIntStateOf(0); private set
    private var captureNoteID = 0

    /** Saves the square currently being analysed. Takes effect on the next frame. */
    fun captureFrame() = captureRequested.set(true)

    /**
     * One thread for everything a model touches: loading, inference, capture and recording. Not a pool - a
     * GPU delegate has to run on the thread that created it, and one thread also gives the backpressure iOS
     * gets from its serial camera queue.
     */
    private val analysisExecutor = Executors.newSingleThreadExecutor { Thread(it, "jasscardeye-analysis") }
    private val analysisDispatcher = analysisExecutor.asCoroutineDispatcher()

    /** The camera - or, in a debug build on the emulator, a looping test video. Everything downstream is identical. */
    val frames: FrameSource = FrameSource.make(application, analysisExecutor)

    /** A recogniser together with the variant it was built for - one reference, so the two cannot disagree. */
    private class Loaded(val variant: RecognitionVariant, val recognizer: CardRecognizer)

    /** The recogniser in use. Analysis thread only: it is loaded, used and closed there. */
    private var loaded: Loaded? = null

    init {
        Haptics.init(application)
        CardSound.init(application)

        // The discipline is not restored: it belongs to the round, and the round is over.

        // Restore the stability threshold; anything unusable falls back to the default rather than being trusted.
        val savedFrames = prefs.getInt(FRAMES_KEY, 0)
        framesState = if (savedFrames in FRAME_RANGE) savedFrames else DEFAULT_FRAMES
        ruleState = StabilityRule.fromId(prefs.getString(RULE_KEY, null)) ?: StabilityRule.RUN
        lensState = CameraLens.fromId(prefs.getString(LENS_KEY, null)) ?: CameraLens.WIDE
        deckState = JassDeck.fromId(prefs.getString(DECK_KEY, null)) ?: JassDeck.FRENCH
        // The feedback per counted card. Missing or out of range means the middle - `contains` first,
        // because a missing key must not read as 0, and 0 means off.
        hapticState = restoredFeedback(HAPTIC_KEY)
        soundState = restoredFeedback(SOUND_KEY)
        developerToolsState = prefs.getBoolean(DEVELOPER_TOOLS_KEY, false)

        // The analysis thread and the feedback objects read their own copies, so they are seeded explicitly.
        settings.set(FrameSettings(frames = framesState, rule = ruleState, deck = deckState, variant = variant))
        Haptics.strength = hapticState
        CardSound.volume = soundState

        frames.onTorchChanged = { on -> main.post { torchOn = on } }

        // Which lenses exist is a question for the camera stack, not for a guess; asked once, off the main thread.
        viewModelScope.launch {
            val lenses = frames.availableLenses()
            availableLenses = lenses
            if (lensState !in lenses) lensState = CameraLens.WIDE
        }
    }

    private fun restoredFeedback(key: String): Double {
        if (!prefs.contains(key)) return DEFAULT_FEEDBACK
        val value = prefs.getFloat(key, DEFAULT_FEEDBACK.toFloat()).toDouble()
        return if (value in FEEDBACK_RANGE) value else DEFAULT_FEEDBACK
    }

    // MARK: - Lifecycle

    /**
     * Counts sessions. [start] awaits the model and the camera, and the screen can be gone by the time those
     * return; comparing the token it took at entry against the current one is what makes a late start harmless.
     */
    private var sessionToken = 0

    /** Whether the app may use the camera right now. */
    fun hasCameraPermission(): Boolean =
        !frames.needsCameraPermission || ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED

    /** Called by the scan screen when the permission request was answered with no. */
    fun cameraRefused() {
        cameraDenied = true
        statusMessage = "Kein Kamerazugriff."
    }

    fun start(owner: LifecycleOwner) {
        sessionToken += 1
        val token = sessionToken
        cameraDenied = false
        // Cleared with it, not left standing: a model that failed to load once must not keep its sentence
        // over every later session that worked.
        statusMessage = null

        if (availableVariants.isEmpty()) {
            statusMessage = "Kein Modell in der App - src/training/export.py --format litert ausführen."
            return
        }
        viewModelScope.launch {
            if (!loadRecognizer(variant)) return@launch            // sets statusMessage on failure
            if (token != sessionToken) return@launch                // the session was closed while we waited
            if (!hasCameraPermission()) { cameraRefused(); return@launch }
            frames.onFrame = ::process
            val problem = frames.start(owner, cameraLens)
            if (problem != null) {
                statusMessage = problem.message
                cameraDenied = problem.isDenied
                return@launch
            }
            if (token != sessionToken) { frames.stop(); return@launch }
            statusMessage = frames.notice
            // Only answerable once a camera is bound, and it decides whether the button exists.
            hasTorch = frames.hasTorch
        }
    }

    /** Ends the session, however the screen was left - not only via "Fertig". */
    fun stop() {
        sessionToken += 1
        counting = false
        // A save that was tapped but never reached a frame must not fire into the next session.
        captureRequested.set(false)
        updateSettings { it.copy(counting = false) }
        finishRecording()
        // Before the session goes: a torch left burning after the count is the most expensive mistake this
        // screen could make, and the one nobody would notice until the battery did.
        if (torchOn) frames.setTorch(false)
        torchOn = false
        frames.onFrame = null
        frames.stop()
    }

    /** Switches the active model. The pile is cleared, because a score assembled under one model must not continue under another. */
    fun selectVariant(newVariant: RecognitionVariant) {
        if (newVariant == variant || newVariant !in availableVariants) return
        variant = newVariant
        updateSettings { it.copy(variant = newVariant) }
        reset()
        viewModelScope.launch { loadRecognizer(newVariant) }
    }

    /**
     * Builds a variant's recogniser on the analysis thread and installs it. Kept across sessions: loading a
     * model is slow enough to notice, and a counting app is opened and closed many times an evening. Returns
     * false, with the reason in [statusMessage], only when loading failed.
     *
     * Frames are only analysed by the recogniser of the variant currently selected (see [process]), so a
     * recogniser that finishes loading after the picker has moved on can do no harm: it is replaced by the
     * load that the newer choice queued behind it.
     */
    private suspend fun loadRecognizer(target: RecognitionVariant): Boolean = try {
        withContext(analysisDispatcher) {
            if (loaded?.variant == target || settings.get().variant != target) return@withContext
            val recognizer = catalog.makeRecognizer(target)
            loaded?.recognizer?.close()
            loaded = Loaded(target, recognizer)
            Log.i(TAG, "Model ${target.displayName} loaded on ${recognizer.backend}")
        }
        true
    } catch (error: Throwable) {
        statusMessage = "Modell (${target.displayName}) konnte nicht geladen werden: ${error.message}"
        false
    }

    /** Clears the counted pile. The last trick is deliberately kept: rescanning the cards does not change who took it. */
    fun reset() {
        show(synchronized(tracker) { tracker.reset(); tracker.snapshot })
    }

    override fun onCleared() {
        stop()
        analysisExecutor.execute {
            loaded?.recognizer?.close()
            loaded = null
        }
        analysisExecutor.shutdown()
    }

    // MARK: - Frame processing (analysis thread)

    // Thread-confined state: only ever touched from the analysis thread.
    private val frameStamps = ArrayDeque<Long>()
    private var lastStatsPublished = 0L
    private var lastWasEmpty = true
    private var errorReported = false

    private fun process(square: Bitmap) {
        val frame = settings.get()
        // The recogniser of the variant the picker selected - not one still in place from before a switch, whose
        // reading would land on the pile the switch has just cleared. Null while the selected one loads.
        val recognizer = loaded?.takeIf { it.variant == frame.variant }?.recognizer ?: return

        val started = SystemClock.elapsedRealtimeNanos()
        var detections: List<Detection> = try {
            recognizer.recognize(square, frame.confidence)
        } catch (error: Throwable) {
            // Reported once instead of swallowed - a frame the model cannot read looks exactly like "no card".
            if (!errorReported) { errorReported = true; Log.e(TAG, "Erkennung fehlgeschlagen", error) }
            emptyList()
        }
        val elapsed = (SystemClock.elapsedRealtimeNanos() - started) / 1_000_000.0

        // A card of the other deck is not a card at all tonight: dropped here, so it cannot win the stability
        // window, cannot land on the pile, and does not even draw a box.
        detections = detections.filter { CardLabel.parse(it.label)?.deck == frame.deck }

        // A requested save happens on this frame: the detections are known, so the name records what the model
        // made of the very picture being written - including that it saw nothing.
        if (captureRequested.getAndSet(false)) {
            val top = detections.firstOrNull()
            val (note, saved) = try {
                "Gesichert: ${FrameCapture.save(context, square, top?.label, top?.confidence ?: 0f, frame.modeName)}" to true
            } catch (error: Exception) {
                "Nicht gesichert: ${error.message}" to false
            }
            main.post {
                if (saved) capturedCount += 1
                showNote(note, 4000)
            }
        }

        // Analysis rate over a one-second sliding window.
        val now = SystemClock.elapsedRealtime()
        frameStamps.addLast(now)
        while (frameStamps.isNotEmpty() && now - frameStamps.first() > 1000) frameStamps.removeFirst()
        val rate = frameStamps.size.toDouble()

        val best = detections.firstOrNull()
        val committed = synchronized(tracker) {
            if (tracker.observe(best?.label, frame.rule, frame.frames, frame.counting)) tracker.snapshot else null
        }

        recorder.get()?.append(square, SessionRecorder.FrameResult(best, committed != null))

        // Publish only what actually changed. The counters are read by a human, five times a second is plenty.
        val publishStats = now - lastStatsPublished >= STATS_INTERVAL_MS
        if (publishStats) lastStatsPublished = now
        val isEmpty = best == null
        val boxChanged = !isEmpty || !lastWasEmpty
        lastWasEmpty = isEmpty
        if (!publishStats && !boxChanged && committed == null) return

        main.post {
            if (publishStats) { fps = rate; inferenceMs = elapsed }
            if (boxChanged) current = best
            if (committed != null && show(committed)) cardCounted()
        }
    }

    companion object {
        private const val TAG = "JassCardEye"
        const val DEFAULT_FRAMES = 3
        /** The range the settings stepper offers - also what a stored value is checked against. */
        val FRAME_RANGE = 1..10
        /** Where a fresh install starts: the middle of both sliders. */
        const val DEFAULT_FEEDBACK = 0.5
        /** What the sliders offer - and what a stored value is checked against. */
        val FEEDBACK_RANGE = 0.0..1.0
        private const val STATS_INTERVAL_MS = 200L
        private const val FRAMES_KEY = "requiredFrames"
        private const val RULE_KEY = "stabilityRule"
        private const val LENS_KEY = "cameraLens"
        private const val DECK_KEY = "deck"
        private const val HAPTIC_KEY = "hapticStrength"
        private const val SOUND_KEY = "soundVolume"
        private const val DEVELOPER_TOOLS_KEY = "developerTools"
    }
}
