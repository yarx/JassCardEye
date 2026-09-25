package ch.yarx.jasscardeye

import android.os.Build
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/ScoreRow.swift.

/**
 * The three-column score line, shared by the home screen and a running session so both read the same way:
 * own points left, what is being counted in the middle, the opponents' right.
 *
 * Each column takes exactly one third of the width, so the middle stays on the centre line and each number
 * is anchored to its own side while the digits change with every card.
 */
@Composable
fun ScoreRow(
    points: Int,
    pointsCaption: String,
    opponentPoints: Int,
    cards: Int,
    modeName: String,
    modifier: Modifier = Modifier,
    modeSuit: JassSuit? = null,
    note: String? = null,
    valueStyle: TextStyle = JassType.largeTitle,
    /** The demo: both numbers are drawn and blurred. The caller keeps the points out of the captions. */
    locked: Boolean = false,
) {
    val digits = valueStyle.copy(fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    Row(modifier = modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Column(Modifier.weight(1f), horizontalAlignment = Alignment.Start, verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Value(points, digits, JassColors.Green, locked)
            Caption(pointsCaption)
        }
        Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(pluralStringResource(R.plurals.score_cards, cards, cards), style = JassType.callout.copy(fontFeatureSettings = "tnum"), color = JassColors.Secondary, maxLines = 1)
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                if (modeSuit != null) SuitMark(modeSuit, 17.dp)
                Caption(modeName)
            }
            if (note != null) Caption(note)
        }
        Column(Modifier.weight(1f), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Value(opponentPoints, digits, JassColors.Orange, locked)
            Caption(stringResource(R.string.score_opponents))
        }
    }
}

/**
 * A score. Locked, it is drawn blurred - it still moves with every card, but cannot be read - and TalkBack hears that
 * it is locked instead of the number. Blur needs Android 12; below that the digits are drawn as dots of the same
 * count, which gives away no more.
 */
@Composable
private fun Value(number: Int, style: TextStyle, color: Color, locked: Boolean) {
    if (!locked) {
        Text("$number", style = style, color = color, maxLines = 1)
        return
    }
    val blurs = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
    val lockedLabel = stringResource(R.string.score_locked)
    Text(
        if (blurs) "$number" else "$number".replace(Regex("\\d"), "•"),
        style = style,
        color = color,
        maxLines = 1,
        modifier = Modifier
            .then(if (blurs) Modifier.blur(10.dp, BlurredEdgeTreatment.Unbounded) else Modifier)
            .clearAndSetSemantics { contentDescription = lockedLabel },
    )
}

/** Captions stay on one line rather than wrap - a column that grows a line would push the whole bar around. */
@Composable
private fun Caption(text: String) {
    Text(text, style = JassType.caption, color = JassColors.Secondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
}
