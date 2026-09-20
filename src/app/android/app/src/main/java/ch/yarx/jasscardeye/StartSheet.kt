package ch.yarx.jasscardeye

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.CallSplit
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.SwapVert
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// Port of src/app/ios/Sources/StartSheet.swift.

/** The arrow drawn for a discipline without trump - the counterpart of the SF Symbol iOS names in `JassModes`. */
fun modeSymbol(mark: String): ImageVector = when (mark) {
    "arrow.down" -> Icons.Outlined.ArrowDownward
    "arrow.up" -> Icons.Outlined.ArrowUpward
    "arrow.up.arrow.down" -> Icons.Outlined.SwapVert
    else -> Icons.AutoMirrored.Outlined.CallSplit
}

/**
 * Everything that has to be settled before a card is looked at, on one screen that fits.
 *
 * It comes *before* the scanner rather than on top of it, and that is the point: camera and model are the
 * most expensive things the app does, and starting them behind a question that might be cancelled spends a
 * battery on nothing. The session begins when "Zählen starten" is tapped, and never before.
 *
 * Four facts, none of which a pile of cards can reveal: which deck, what was played, the last trick, and the factor
 * the table agreed on - ×1 unless somebody picks another, because that is what most rounds are.
 * Everything is a row of marks rather than a list of rows, because the whole screen has to be visible at once.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StartSheet(model: LiveDetectionModel, onStart: (CountingMode, LastTrick, Int) -> Unit, onCancel: () -> Unit) {
    /** Deliberately empty at first: the discipline is always a decision somebody made for this round. */
    var mode by remember { mutableStateOf<CountingMode?>(null) }
    var lastTrick by remember { mutableStateOf(LastTrick.MINE) }
    /** Unlike the discipline, preselected: ×1 is the common case, and an empty factor would only add a tap to every round. */
    var multiplier by remember { mutableIntStateOf(1) }
    val trumpModes = JassModes.all.filter { it.trumpRole != null }
    val openModes = JassModes.all.filter { it.trumpRole == null }

    ModalBottomSheet(
        onDismissRequest = onCancel,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = JassColors.Ground,
        dragHandle = null,
    ) {
        Column(Modifier.fillMaxHeight(0.94f)) {
            Box(Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 6.dp)) {
                // The way out: nothing has started yet.
                TextButton(onClick = onCancel, modifier = Modifier.align(Alignment.CenterStart)) {
                    Text("Abbrechen", style = JassType.body, color = JassColors.Green)
                }
                Text("Neue Zählung", style = JassType.headline, color = Color.White, modifier = Modifier.align(Alignment.Center))
            }

            Column(
                Modifier
                    .weight(1f)
                    .verticalScroll(rememberScrollState())
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(20.dp),
            ) {
                Group("Blatt") {
                    Segmented(JassDeck.entries.map { it.displayName }, JassDeck.entries.indexOf(model.deck)) { index ->
                        model.deck = JassDeck.entries[index]
                    }
                    // The four marks of whatever was just chosen - the fastest way to check the deck on the table
                    // is the deck the app is set to.
                    Row(Modifier.fillMaxWidth().padding(top = 2.dp), horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally)) {
                        for (suit in model.deck.suits) SuitMark(suit, 26.dp)
                    }
                }

                Group("Trumpf") { ModeRow(trumpModes, mode, model.deck) { mode = it } }
                Group("Ohne Trumpf") { ModeRow(openModes, mode, model.deck) { mode = it } }

                // One line, in the place a line always is, so the layout never jumps between chosen and unchosen.
                Text(
                    mode?.hint ?: "Wähle, was gespielt wurde.",
                    style = JassType.footnote,
                    color = if (mode == null) JassColors.Secondary else Color.White,
                    minLines = 2,
                    maxLines = 2,
                    modifier = Modifier.fillMaxWidth(),
                )

                Group("Letzter Stich") {
                    Segmented(LastTrick.entries.map { it.shortName }, LastTrick.entries.indexOf(lastTrick)) { index ->
                        lastTrick = LastTrick.entries[index]
                    }
                }

                Group("Faktor") { MultiplierBar(multiplier, onChange = { multiplier = it }) }
            }

            // Pinned rather than scrolled to: on a phone small enough to need scrolling, the one control that must
            // always be reachable is this one.
            Button(
                onClick = { mode?.let { onStart(it, lastTrick, multiplier) } },
                enabled = mode != null,
                shape = CircleShape,
                colors = ButtonDefaults.buttonColors(
                    containerColor = JassColors.Green, contentColor = Color.White,
                    disabledContainerColor = Color.White.copy(alpha = 0.12f), disabledContentColor = Color.White.copy(alpha = 0.35f),
                ),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 20.dp)
                    .padding(top = 12.dp, bottom = 8.dp)
                    .navigationBarsPadding()
                    .height(52.dp),
            ) {
                Text("Zählen starten", style = JassType.headline)
            }
        }
    }
}

/** A labelled block, so the two rows of marks stay told apart without a box around each. */
@Composable
private fun Group(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title.uppercase(), style = JassType.caption2.copy(fontWeight = FontWeight.SemiBold, letterSpacing = 0.6.sp),
            color = JassColors.Secondary)
        content()
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun Segmented(options: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
        options.forEachIndexed { index, option ->
            SegmentedButton(
                selected = index == selected,
                onClick = { onSelect(index) },
                shape = SegmentedButtonDefaults.itemShape(index, options.size),
                icon = {},
                colors = SegmentedButtonDefaults.colors(
                    activeContainerColor = Color.White.copy(alpha = 0.28f), activeContentColor = Color.White,
                    inactiveContainerColor = Color.White.copy(alpha = 0.10f), inactiveContentColor = Color.White,
                    activeBorderColor = Color.Transparent, inactiveBorderColor = Color.Transparent,
                ),
            ) { Text(option, style = JassType.footnote.copy(fontWeight = FontWeight.Medium), maxLines = 1) }
        }
    }
}

/** The disciplines as one row of equal cells. Equal because they are equal choices - nothing here is a default. */
@Composable
private fun ModeRow(entries: List<CountingMode>, chosenMode: CountingMode?, deck: JassDeck, onPick: (CountingMode) -> Unit) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        for (entry in entries) {
            val chosen = entry == chosenMode
            Column(
                Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(10.dp))
                    .background(if (chosen) JassColors.Green else Color.White.copy(alpha = 0.13f))
                    .clickable { onPick(entry) }
                    .semantics { contentDescription = entry.displayName(deck); selected = chosen }
                    .padding(vertical = 9.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                // The suit as it is printed for a trump discipline, an arrow for one played without trump. Both sit
                // in the same square, so the cells of a row line up whichever kind they hold.
                when (val kind = entry.kind) {
                    is ModeKind.Trump -> entry.markSuit(deck)?.let { SuitMark(it, 34.dp) }
                    is ModeKind.Open -> Box(Modifier.size(34.dp), contentAlignment = Alignment.Center) {
                        Icon(modeSymbol(kind.mark), contentDescription = null,
                            tint = if (chosen) Color.Black else Color.White, modifier = Modifier.size(24.dp))
                    }
                }
                Text(entry.shortName(deck), style = JassType.caption2, color = if (chosen) Color.Black else Color.White,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
    Spacer(Modifier.height(0.dp))
}
