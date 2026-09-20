package ch.yarx.jasscardeye

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CenterFocusStrong
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.RadioButtonChecked
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.Remove
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.UnfoldMore
import androidx.compose.material.icons.outlined.Vibration
import androidx.compose.material.icons.automirrored.outlined.VolumeUp
import androidx.compose.material.icons.automirrored.outlined.VolumeOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/HomeView.swift.

/**
 * The app's entry screen. Counting is a session that gets opened, used for half a minute and closed again -
 * so this screen is what the app looks like most of the time, and nothing expensive runs here: no camera, no
 * inference.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(model: LiveDetectionModel, lastResult: CountResult?, onStart: () -> Unit) {
    val store = LocalStore.current
    var showSettings by remember { mutableStateOf(false) }
    var showAbout by remember { mutableStateOf(false) }
    var showStart by remember { mutableStateOf(false) }
    var showPurchase by remember { mutableStateOf(false) }

    Box(
        Modifier
            .fillMaxSize()
            .background(JassColors.Ground)
            .safeDrawingPadding(),
    ) {
        Column(
            Modifier
                .fillMaxSize()
                .padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp),
        ) {
            Spacer(Modifier.weight(1f))
            Header(model)
            LastResultCard(lastResult, locked = !store.unlocked, onUnlock = { showPurchase = true })
            Spacer(Modifier.weight(1f))
            StartButton(model, locked = !store.unlocked) { showStart = true }
        }
        // Two doors: the page to read next to the sheet to change things in.
        Row(
            Modifier
                .align(Alignment.TopEnd)
                .padding(end = 12.dp, top = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            RoundIconButton(Icons.Outlined.Info, "Über") { showAbout = true }
            RoundIconButton(Icons.Outlined.Settings, "Einstellungen") { showSettings = true }
        }
    }

    if (showSettings) {
        SettingsSheet(model) { showSettings = false }
    }
    if (showAbout) {
        AboutSheet(model.developerTools, { model.developerTools = it }, store.unlocked) { showAbout = false }
    }
    if (showPurchase) {
        PurchaseSheet { showPurchase = false }
    }
    // Deck, discipline, last trick and factor - asked here, with nothing running behind them.
    if (showStart) {
        StartSheet(
            model,
            onStart = { mode, lastTrick, multiplier ->
                model.beginCounting(mode, lastTrick, multiplier)
                showStart = false
                onStart()
            },
            onCancel = { showStart = false },
        )
    }
}

/** A round toolbar button on the home screen, like the iOS toolbar items. */
@Composable
private fun RoundIconButton(icon: ImageVector, description: String, onClick: () -> Unit) {
    IconButton(
        onClick = onClick,
        modifier = Modifier
            .size(48.dp)
            .background(Color.White.copy(alpha = 0.08f), CircleShape)
            .semantics { contentDescription = description },
    ) {
        Icon(icon, contentDescription = null, tint = Color.White, modifier = Modifier.size(26.dp))
    }
}

@Composable
private fun Header(model: LiveDetectionModel) {
    // Room below, between the name and what was counted, so the two do not read as one block.
    Column(
        Modifier.fillMaxWidth().padding(bottom = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text("JassCardEye", style = JassType.largeTitle.copy(fontWeight = FontWeight.Bold), color = Color.White)
        // The deck, because it stays as the default. What was played belongs to the single round and is
        // asked when a count starts. The marks alone tell the two decks apart at a glance; the name is left
        // to TalkBack.
        Row(
            Modifier.clearAndSetSemantics { contentDescription = model.deck.displayName },
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            for (suit in model.deck.suits) SuitMark(suit, 24.dp)
        }
    }
}

@Composable
private fun LastResultCard(result: CountResult?, locked: Boolean, onUnlock: () -> Unit) {
    if (result == null) {
        Text("Noch nichts gezählt.", style = JassType.callout, color = JassColors.Secondary,
            modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
        return
    }
    Column(
        Modifier
            .fillMaxWidth()
            .background(Color.White.copy(alpha = 0.08f), RoundedCornerShape(14.dp))
            .padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("Letzte Zählung", style = JassType.caption, color = JassColors.Secondary)
        // The same three columns a session shows, so the number stands where it stood a moment ago.
        ScoreRow(
            points = result.points,
            pointsCaption = "meine Punkte",
            opponentPoints = result.opponentPoints,
            cards = result.cards,
            modeName = result.modeName,
            modeSuit = result.modeSuit?.let { JassSuit.named(it) },
            valueStyle = JassType.title,
            locked = locked,
        )
        if (locked) UnlockButton(onUnlock)
    }
}

@Composable
private fun StartButton(model: LiveDetectionModel, locked: Boolean, onTap: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        // Decided here rather than in the start questions: it belongs to the session as a whole, and putting
        // it in the flow would add a tap to every single count. Only with the developer tools on - see
        // `LiveDetectionModel.developerTools`.
        if (model.developerTools) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.RadioButtonChecked, contentDescription = null, tint = Color.White, modifier = Modifier.size(22.dp))
                Spacer(Modifier.size(8.dp))
                Text("Session aufzeichnen", style = JassType.callout, color = Color.White, modifier = Modifier.weight(1f))
                Switch(
                    checked = model.recordSession,
                    onCheckedChange = { model.recordSession = it },
                    // The iOS look: a white knob on a grey track, red when on - no outline.
                    colors = SwitchDefaults.colors(
                        checkedTrackColor = JassColors.Red, checkedThumbColor = Color.White, checkedBorderColor = Color.Transparent,
                        uncheckedTrackColor = Color(0xFF39393D), uncheckedThumbColor = Color.White, uncheckedBorderColor = Color.Transparent,
                    ),
                )
            }
        }
        Button(
            onClick = onTap,
            modifier = Modifier.fillMaxWidth().height(58.dp),
            shape = CircleShape,
            colors = ButtonDefaults.buttonColors(containerColor = JassColors.Green, contentColor = Color.White),
        ) {
            Icon(Icons.Outlined.CenterFocusStrong, contentDescription = null, modifier = Modifier.size(26.dp))
            Spacer(Modifier.size(10.dp))
            Text("Zählen starten", style = JassType.title3.copy(fontWeight = FontWeight.SemiBold))
        }
        // Said once, where a count starts: the demo is the whole app, only the result is blurred.
        if (locked) {
            Text("Die Demo zählt wie die gekaufte App - nur die Punkte bleiben verwischt.",
                style = JassType.footnote, color = JassColors.Secondary, textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth())
        }
    }
}

// MARK: - Settings

/**
 * Recognition model, lens, stability, feedback and the purchase - the settings of the app itself, not of a
 * round. What belongs to a round (deck, discipline, last trick, factor) is asked when a count is started, so
 * this sheet is opened rarely and holds nothing anyone needs mid-game. The model row only appears when more
 * than one variant was bundled.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsSheet(model: LiveDetectionModel, onDismiss: () -> Unit) {
    val store = LocalStore.current
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = false),
        containerColor = Color.Black,
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
            Text("Einstellungen", style = JassType.headline, color = Color.White, modifier = Modifier.align(Alignment.Center))
            TextButton(onClick = onDismiss, modifier = Modifier.align(Alignment.CenterEnd)) {
                Text("Fertig", style = JassType.headline, color = JassColors.Green)
            }
        }
        Column(
            Modifier
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp)
                .navigationBarsPadding(),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            Spacer(Modifier.height(4.dp))
            if (model.availableVariants.size > 1) {
                FormSection("Erkennungsmodell") {
                    PickerRow("Modell", model.variant.displayName, model.availableVariants.map { it.displayName }) { index ->
                        model.selectVariant(model.availableVariants[index])
                    }
                    FormDivider()
                    FormNote("Wechselt das Modell, das die oberste Karte erkennt.")
                }
            }

            if (model.availableLenses.size > 1) {
                FormSection("Kamera") {
                    PickerRow("Objektiv", model.cameraLens.displayName, model.availableLenses.map { it.displayName }) { index ->
                        model.cameraLens = model.availableLenses[index]
                    }
                    FormDivider()
                    FormNote(model.cameraLens.explanation)
                }
            }

            FormSection("Stabilität") {
                Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(if (model.requiredFrames == 1) "1 Bild muss zustimmen" else "${model.requiredFrames} Bilder müssen zustimmen", style = JassType.body, color = Color.White, modifier = Modifier.weight(1f))
                    Stepper(
                        onMinus = { if (model.requiredFrames > LiveDetectionModel.FRAME_RANGE.first) model.requiredFrames -= 1 },
                        onPlus = { if (model.requiredFrames < LiveDetectionModel.FRAME_RANGE.last) model.requiredFrames += 1 },
                    )
                }
                FormDivider()
                PickerRow("Zählweise", model.stabilityRule.displayName(model.requiredFrames),
                    StabilityRule.entries.map { it.displayName(model.requiredFrames) }) { index ->
                    model.stabilityRule = StabilityRule.entries[index]
                }
                FormDivider()
                FormNote(model.stabilityRule.explanation)
            }

            // Sliders rather than switches: 0 is off, and everything above depends on the table.
            FormSection("Rückmeldung") {
                FeedbackSlider("Vibration", { Icon(Icons.Outlined.PhoneAndroid, null, tint = JassColors.Secondary) },
                    { Icon(Icons.Outlined.Vibration, null, tint = JassColors.Secondary) },
                    model.hapticStrength, { model.hapticStrength = it }, Haptics::preview)
                FormDivider()
                FeedbackSlider("Ton", { Icon(Icons.AutoMirrored.Outlined.VolumeOff, null, tint = JassColors.Secondary) },
                    { Icon(Icons.AutoMirrored.Outlined.VolumeUp, null, tint = JassColors.Secondary) },
                    model.soundVolume, { model.soundVolume = it }, CardSound::preview)
                FormDivider()
                FormNote("Bei jeder gezählten Karte. Ganz links ist aus. Der Ton folgt der Medienlautstärke, auch im Lautlos-Modus, und lässt laufende Musik weiterspielen. Die Vibrationseinstellungen des Telefons gelten trotzdem.")
            }

            // A visible way to restore a purchase, for a new phone or a reinstall - as on iOS.
            FormSection("Kauf", footer = store.problem) {
                ValueRow("Punkte", if (store.unlocked) "Freigeschaltet" else "Demo")
                FormDivider()
                Box(Modifier.fillMaxWidth().clickable(onClick = store::restore).padding(horizontal = 16.dp, vertical = 12.dp)) {
                    Text("Kauf wiederherstellen", style = JassType.body, color = JassColors.Green)
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

/** A menu-style picker row: the label on the left, the choice and its chevrons on the right. */
@Composable
private fun PickerRow(label: String, value: String, options: List<String>, onPick: (Int) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Row(
        Modifier
            .fillMaxWidth()
            .clickable { open = true }
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, style = JassType.body, color = Color.White, modifier = Modifier.weight(1f))
        Box {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(value, style = JassType.body, color = JassColors.Secondary)
                Icon(Icons.Outlined.UnfoldMore, contentDescription = null, tint = JassColors.Secondary, modifier = Modifier.size(18.dp))
            }
            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                options.forEachIndexed { index, option ->
                    DropdownMenuItem(text = { Text(option) }, onClick = { open = false; onPick(index) })
                }
            }
        }
    }
}

@Composable
private fun Stepper(onMinus: () -> Unit, onPlus: () -> Unit) {
    Row(
        Modifier
            .clip(CircleShape)
            .background(Color.White.copy(alpha = 0.12f)),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onMinus, modifier = Modifier.size(width = 52.dp, height = 36.dp)) {
            Icon(Icons.Outlined.Remove, contentDescription = "Weniger", tint = Color.White)
        }
        Box(Modifier.size(width = 1.dp, height = 20.dp).background(Color.White.copy(alpha = 0.25f)))
        IconButton(onClick = onPlus, modifier = Modifier.size(width = 52.dp, height = 36.dp)) {
            Icon(Icons.Outlined.Add, contentDescription = "Mehr", tint = Color.White)
        }
    }
}

/**
 * One slider of the feedback section. Letting go plays the feedback once at the new setting, so it is judged
 * by feel and by ear rather than by a number - on release, not while dragging, which would fire a burst of taps.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun FeedbackSlider(
    title: String,
    low: @Composable () -> Unit,
    high: @Composable () -> Unit,
    value: Double,
    onChange: (Double) -> Unit,
    preview: () -> Unit,
) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(title, style = JassType.body, color = Color.White)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            low()
            // The iOS slider: a round white knob on one continuous track, blue up to the knob.
            Slider(
                value = value.toFloat(),
                onValueChange = { onChange(it.toDouble()) },
                onValueChangeFinished = preview,
                valueRange = 0f..1f,
                thumb = {
                    Box(Modifier.size(28.dp).shadow(3.dp, CircleShape).background(Color.White, CircleShape))
                },
                track = { state ->
                    val fraction = ((state.value - state.valueRange.start) / (state.valueRange.endInclusive - state.valueRange.start)).coerceIn(0f, 1f)
                    Canvas(Modifier.fillMaxWidth().height(4.dp)) {
                        val y = size.height / 2
                        drawLine(Color.White.copy(alpha = 0.18f), Offset(0f, y), Offset(size.width, y), size.height, StrokeCap.Round)
                        drawLine(Color(0xFF0A84FF), Offset(0f, y), Offset(size.width * fraction, y), size.height, StrokeCap.Round)
                    }
                },
                modifier = Modifier
                    .weight(1f)
                    .semantics { stateDescription = if (value == 0.0) "Aus" else "${(value * 100).toInt()} Prozent" },
            )
            high()
        }
    }
}
