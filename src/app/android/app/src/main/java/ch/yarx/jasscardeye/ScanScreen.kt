package ch.yarx.jasscardeye

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.view.PreviewView
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed as gridItemsIndexed
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.FlashlightOff
import androidx.compose.material.icons.filled.FlashlightOn
import androidx.compose.material.icons.outlined.SaveAlt
import androidx.compose.material.icons.outlined.RadioButtonChecked
import androidx.compose.material.icons.outlined.Speed
import androidx.compose.material.icons.outlined.VideocamOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView

// Port of src/app/ios/Sources/ScanView.swift.

/**
 * The sizes the scan screen is laid out from. Named, because they all decide the same thing: how much room is
 * left for the picture. The viewfinder is a square that takes whatever the two bars leave over, so a bar that
 * grows when a card is recognised would resize the camera square - hence a floor under each bar.
 */
private object ScanMetrics {
    val margin = 10.dp
    val statusMark = 24.dp
    val chipMark = 22.dp
    val chipPadding = 5.dp
    val stripHeight = chipMark + chipPadding * 2
}

/**
 * A counting session: camera preview, detection band, the virtual pile and its score. The screen exists only
 * while counting - it starts camera and inference when it appears and stops them when it closes, so nothing
 * expensive runs while the app sits on the home screen.
 *
 * Split into small composables on purpose, as on iOS: a composable only recomposes when state it actually
 * reads changes, so the fast-moving values (frame rate, detection box) invalidate their own part and leave the
 * controls below alone.
 */
@Composable
fun ScanScreen(model: LiveDetectionModel, onFinish: (CountResult) -> Unit) {
    val owner = LocalLifecycleOwner.current
    var showPicker by remember { mutableStateOf(false) }
    var showPurchase by remember { mutableStateOf(false) }

    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) model.start(owner) else model.cameraRefused()
    }
    LaunchedEffect(Unit) {
        if (model.hasCameraPermission()) model.start(owner) else permission.launch(Manifest.permission.CAMERA)
    }
    DisposableEffect(Unit) { onDispose { model.stop() } }

    // The screen stays on for as long as a count runs: nobody touches the phone while cards are laid down, and a
    // display that dims or locks halfway through interrupts the scan. The flag belongs to the view the session is
    // drawn in, so it is cleared the moment the session closes and the system setting applies again.
    val view = LocalView.current
    DisposableEffect(view) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }

    val finish = { onFinish(model.finishCounting()) }
    // The system back gesture closes the session the way "Fertig" does - iOS has no back, only "Fertig".
    BackHandler(onBack = finish)

    Box(Modifier.fillMaxSize().background(Color.Black)) {
        Column(
            Modifier
                .fillMaxSize()
                .safeDrawingPadding()
                // The same narrow margin on all four sides: it sets how wide the picture can be.
                .padding(ScanMetrics.margin),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            StatusBar(model)
            // The viewfinder takes whatever is left between the bar and the controls and centres a square in it.
            Viewfinder(model, Modifier.weight(1f))
            ScoreBar(model, onShowPicker = { showPicker = true }, onShowPurchase = { showPurchase = true }, onFinish = finish)
        }
    }

    // Bought in the middle of a count, the sheet closes and the score turns sharp - computed from the pile already
    // lying there, nothing has to be scanned again.
    if (showPurchase) {
        PurchaseSheet { showPurchase = false }
    }

    if (showPicker) {
        CardPicker(model.missingCards, onPick = { label ->
            model.addCard(label)
            showPicker = false
        }, onDismiss = { showPicker = false })
    }
}

// MARK: - Manual card picker

/** Adds a card the model will not recognise. Only the cards not yet on the pile are offered, so no duplicate. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CardPicker(missing: List<String>, onPick: (String) -> Unit, onDismiss: () -> Unit) {
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(), containerColor = Color(0xFF1C1C1E)) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 8.dp)) {
            TextButton(onClick = onDismiss, modifier = Modifier.align(Alignment.CenterStart)) {
                Text(stringResource(R.string.common_cancel), style = JassType.body, color = JassColors.Green)
            }
            Text(stringResource(R.string.scan_add_card), style = JassType.headline, color = Color.White, modifier = Modifier.align(Alignment.Center))
        }
        LazyVerticalGrid(
            columns = GridCells.Fixed(3),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.padding(16.dp),
        ) {
            gridItemsIndexed(missing, key = { _, label -> label }) { _, label ->
                Box(
                    Modifier
                        .clip(RoundedCornerShape(8.dp))
                        .background(JassColors.Grey30)
                        .clickable { onPick(label) }
                        .padding(vertical = 12.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    CardChip(label, markSize = 24.dp, style = JassType.callout.copy(fontWeight = FontWeight.Medium))
                }
            }
        }
    }
}

// MARK: - Viewfinder (recomposes per frame)

/**
 * The camera, cropped to exactly the square that is analysed, centred in the space the bar and the controls
 * leave over. Preview and analysis share one square (see `CameraService`), so a detection box normalised to it
 * maps straight onto the picture with no letterbox arithmetic in between.
 */
@Composable
private fun Viewfinder(model: LiveDetectionModel, modifier: Modifier) {
    BoxWithConstraints(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        val side = minOf(maxWidth, maxHeight)
        Box(
            Modifier
                .size(side)
                .clip(RoundedCornerShape(14.dp))
                .background(Color.Black)
                .border(1.dp, Color.White.copy(alpha = 0.22f), RoundedCornerShape(14.dp)),
        ) {
            when (val source = model.frames) {
                is CameraService -> CameraPreview(source)
                is VideoFrameSource -> source.frame?.let { frame ->
                    Image(frame.asImageBitmap(), contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                }
            }

            DetectionBox(model)

            // Both of these belong to the picture and are drawn on it, the way a camera shows its recording light
            // in the frame: a note that comes and goes cannot resize the square underneath it.
            RecordingLight(model, Modifier.align(Alignment.TopEnd))
            StatusMessage(model, Modifier.align(Alignment.BottomCenter).padding(10.dp))
        }
    }
}

/** The camera's own preview, drawn into a view the camera is handed when it appears and lets go of when it leaves. */
@Composable
private fun CameraPreview(camera: CameraService) {
    AndroidView(
        factory = { context ->
            PreviewView(context).apply {
                // TextureView-backed, so the preview is clipped to the rounded square like everything else on it.
                implementationMode = PreviewView.ImplementationMode.COMPATIBLE
                scaleType = PreviewView.ScaleType.FILL_CENTER
                camera.attachPreview(this)
            }
        },
        onRelease = camera::detachPreview,
        modifier = Modifier.fillMaxSize(),
    )
}

@Composable
private fun DetectionBox(model: LiveDetectionModel) {
    val detection = model.current ?: return
    Canvas(Modifier.fillMaxSize()) {
        val box = detection.box
        val stroke = 3.dp.toPx()
        drawRoundRect(
            color = JassColors.Green,
            topLeft = Offset(box.left * size.width, box.top * size.height),
            size = Size(box.width * size.width, box.height * size.height),
            cornerRadius = CornerRadius(4.dp.toPx()),
            style = Stroke(width = stroke),
        )
    }
}

/**
 * The red light that says a session is being recorded - on the picture rather than in the status bar, where it
 * would have shifted the frame rate sideways whenever recording started.
 */
@Composable
private fun RecordingLight(model: LiveDetectionModel, modifier: Modifier) {
    if (!model.recording) return
    Box(modifier.padding(8.dp).background(Color.Black.copy(alpha = 0.45f), CircleShape).padding(5.dp)
        .semantics { contentDescription = "Aufnahme läuft" }) {
        Icon(Icons.Outlined.RadioButtonChecked, contentDescription = null, tint = JassColors.Red, modifier = Modifier.size(22.dp))
    }
}

// MARK: - Status (recomposes a few times per second)

@Composable
private fun StatusBar(model: LiveDetectionModel) {
    val digits = JassType.callout.copy(fontFeatureSettings = "tnum")
    Row(
        Modifier
            .fillMaxWidth()
            // Grey rather than near-black: a black Kreuz or Schaufel would vanish on a dark bar.
            .background(JassColors.Grey30, RoundedCornerShape(14.dp))
            .padding(horizontal = 12.dp, vertical = 6.dp)
            // A floor under the row, so the bar does not grow and shrink with every detection and the square
            // under it does not breathe along.
            .heightIn(min = ScanMetrics.statusMark),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Icon(Icons.Outlined.Speed, contentDescription = null, tint = Color.White, modifier = Modifier.size(20.dp))
            Text("%.0f FPS".format(model.fps), style = digits, color = Color.White)
        }
        Text("%.0f ms".format(model.inferenceMs), style = digits, color = JassColors.Secondary)
        Spacer(Modifier.weight(1f))
        val current = model.current
        if (current != null) {
            // The card stays neutral; the percentage carries the state - yellow means it is already on the pile.
            val captured = current.label in model.pile
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("${(current.confidence * 100).toInt()}%", style = digits.copy(fontWeight = FontWeight.SemiBold),
                    color = if (captured) JassColors.Yellow else JassColors.Green)
                // The mark last, so it holds still while the reading under it changes.
                CardChip(current.label, markSize = ScanMetrics.statusMark, markSide = MarkSide.TRAILING,
                    style = digits.copy(fontWeight = FontWeight.SemiBold))
            }
        } else {
            Text("—", style = digits, color = JassColors.Secondary)
        }
    }
}

@Composable
private fun StatusMessage(model: LiveDetectionModel, modifier: Modifier) {
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (model.cameraDenied) {
            DeniedNotice()
        } else {
            model.statusMessage?.let { Note(it, Color.White) }
        }
        // What the last save did, briefly - the file name, so it can be found again later.
        model.captureNote?.let { Note(it, JassColors.Green) }
    }
}

/**
 * The one failure the person can undo. A sentence over a black square leaves them stuck, so this says what
 * happened, why the app needs the camera, and offers the way into the settings.
 */
@Composable
private fun DeniedNotice() {
    val context = LocalContext.current
    Column(
        Modifier
            .padding(horizontal = 24.dp)
            .background(Color.Black.copy(alpha = 0.75f), RoundedCornerShape(14.dp))
            .padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.VideocamOff, contentDescription = null, tint = JassColors.Secondary, modifier = Modifier.size(40.dp))
        Text(stringResource(R.string.scan_camera_denied), style = JassType.headline, color = Color.White)
        Text(stringResource(R.string.scan_camera_denied_text),
            style = JassType.footnote, color = JassColors.Secondary, textAlign = TextAlign.Center)
        Button(
            onClick = {
                context.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", context.packageName, null)))
            },
            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF0A84FF), contentColor = Color.White),
        ) { Text(stringResource(R.string.scan_open_settings), style = JassType.body) }
    }
}

@Composable
private fun Note(text: String, tint: Color) {
    Text(text, style = JassType.footnote, color = tint, textAlign = TextAlign.Center,
        modifier = Modifier.background(Color.Black.copy(alpha = 0.65f), RoundedCornerShape(8.dp)).padding(10.dp))
}

// MARK: - Score and controls (recompose only when a card is committed)

@Composable
private fun ScoreBar(model: LiveDetectionModel, onShowPicker: () -> Unit, onShowPurchase: () -> Unit, onFinish: () -> Unit) {
    val store = LocalStore.current
    Column(
        Modifier
            .fillMaxWidth()
            .background(Color.Black.copy(alpha = 0.65f), RoundedCornerShape(12.dp))
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (!store.unlocked) UnlockButton(onShowPurchase)

        // Only one party's tricks get counted after a game: the own score counts up, the opponents' score is the
        // remainder and counts down. Deck, mode, last trick and factor were settled before the camera started, so
        // they are shown here rather than offered.
        ScoreRow(
            points = model.points,
            pointsCaption = myCaption(model, store.unlocked),
            opponentPoints = model.opponentPoints,
            cards = model.pile.size,
            modeName = stringResource(model.mode.displayName(model.deck)),
            modeSuit = model.mode.markSuit(model.deck),
            note = model.lastTrick.note?.let { stringResource(it, JassRules.LAST_TRICK_BONUS) },
            locked = !store.unlocked,
        )

        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Capsule(enabled = model.pile.size < DeckLayout.COUNT, onClick = onShowPicker) {
                Text(stringResource(R.string.scan_missing_card), style = JassType.callout, color = Color.White)
            }
            // Keeps a picture of a situation the model got wrong. The count is lasting confirmation. Only with the
            // developer tools on.
            if (model.developerTools) {
                Capsule(onClick = model::captureFrame, description = "Bild sichern") {
                    Icon(Icons.Outlined.SaveAlt, contentDescription = null, tint = Color.White, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text(if (model.capturedCount > 0) "Bild ${model.capturedCount}" else "Bild", style = JassType.callout, color = Color.White)
                }
            }
            // Light for a dark table. Icon only; the state is in the colour.
            if (model.hasTorch) {
                Capsule(
                    onClick = model::toggleTorch,
                    background = if (model.torchOn) JassColors.Yellow else Color.White.copy(alpha = 0.18f),
                    description = stringResource(if (model.torchOn) R.string.scan_torch_off else R.string.scan_torch_on),
                ) {
                    Icon(if (model.torchOn) Icons.Filled.FlashlightOn else Icons.Filled.FlashlightOff, contentDescription = null,
                        tint = if (model.torchOn) Color.Black else Color.White, modifier = Modifier.size(22.dp))
                }
            }
            Spacer(Modifier.weight(1f))
            OutlinedButton(
                onClick = model::reset,
                enabled = model.pile.isNotEmpty(),
                border = null,
                colors = ButtonDefaults.outlinedButtonColors(
                    containerColor = Color.White.copy(alpha = 0.12f), contentColor = Color.White,
                    disabledContainerColor = Color.White.copy(alpha = 0.06f), disabledContentColor = Color.White.copy(alpha = 0.3f),
                ),
            ) { Text(stringResource(R.string.scan_reset), style = JassType.callout) }
        }

        // The strip keeps its place from the start, so the first recognition does not push the controls upwards.
        Box(Modifier.fillMaxWidth().defaultMinSize(minHeight = ScanMetrics.stripHeight), contentAlignment = Alignment.CenterStart) {
            if (model.pile.isEmpty()) {
                Text(stringResource(R.string.scan_empty), style = JassType.callout, color = Color.White.copy(alpha = 0.45f))
            } else {
                val listState = rememberLazyListState()
                // The list follows the newest card: during a count nobody wants to drag the strip along.
                LaunchedEffect(model.pile.size) {
                    if (model.pile.isNotEmpty()) listState.animateScrollToItem(model.pile.size - 1)
                }
                LazyRow(state = listState, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    itemsIndexed(model.pile) { index, label ->
                        val newest = index == model.pile.size - 1
                        Row(
                            Modifier
                                .clip(CircleShape)
                                // The newest card sits a shade lighter, so the eye finds it.
                                .background(if (newest) Color(0xFF6B6B6B) else Color(0xFF424242))
                                .clickable { model.removeCard(label) }
                                .padding(horizontal = 10.dp, vertical = ScanMetrics.chipPadding),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(5.dp),
                        ) {
                            Text("${index + 1}.", style = JassType.callout.copy(fontWeight = FontWeight.Medium), color = Color.White)
                            CardChip(label, markSize = ScanMetrics.chipMark, style = JassType.callout.copy(fontWeight = FontWeight.Medium))
                            Icon(Icons.Filled.Cancel, contentDescription = null, tint = Color.White.copy(alpha = 0.5f), modifier = Modifier.size(13.dp))
                        }
                    }
                }
            }
        }

        Button(
            onClick = onFinish,
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(containerColor = JassColors.Green, contentColor = Color.White),
        ) {
            Text(stringResource(R.string.common_done), style = JassType.headline, modifier = Modifier.padding(vertical = 4.dp))
        }
    }
}

@Composable
private fun Capsule(
    onClick: () -> Unit,
    enabled: Boolean = true,
    background: Color = Color.White.copy(alpha = 0.18f),
    description: String? = null,
    content: @Composable () -> Unit,
) {
    Row(
        Modifier
            .clip(CircleShape)
            .background(if (enabled) background else background.copy(alpha = 0.08f))
            .clickable(enabled = enabled, onClick = onClick)
            .then(if (description != null) Modifier.semantics { contentDescription = description } else Modifier)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) { content() }
}

/**
 * The number is what gets written on the slate; when multiplier or bonuses are in play, this caption shows
 * where it comes from ("62 Karten +5 ×2").
 */
@Composable
private fun myCaption(model: LiveDetectionModel, unlocked: Boolean): String {
    val mine = stringResource(R.string.score_mine)
    // In the demo the breakdown would give the points away. The factor stays: it was chosen at the start, not counted.
    if (!unlocked) return if (model.multiplier > 1) stringResource(R.string.score_mine_factor, model.multiplier) else mine
    if (model.bonusPoints == 0 && model.multiplier <= 1) return mine
    val parts = mutableListOf(stringResource(R.string.score_card_points, model.cardPoints))
    if (model.bonusPoints > 0) parts += "+${model.bonusPoints}"
    if (model.multiplier > 1) parts += "×${model.multiplier}"
    return parts.joinToString(" ")
}
