package ch.yarx.jasscardeye

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/SuitMark.swift.

/**
 * The drawn mark of each suit, named like the suit's token - the same art as iOS's asset catalogue. Spelled
 * out rather than looked up by name, and without a fallback: a suit with no mark of its own must not quietly
 * wear another suit's. `SuitMarkTest` checks every suit of [JassSuit.all] against this list.
 */
internal fun suitDrawable(suit: JassSuit): Int = when (suit.token) {
    "clubs" -> R.drawable.suit_clubs
    "diamonds" -> R.drawable.suit_diamonds
    "hearts" -> R.drawable.suit_hearts
    "spades" -> R.drawable.suit_spades
    "acorns" -> R.drawable.suit_acorns
    "roses" -> R.drawable.suit_roses
    "bells" -> R.drawable.suit_bells
    "shields" -> R.drawable.suit_shields
    else -> error("No drawn mark for suit '${suit.token}'")
}

/**
 * A Jass suit drawn the way it is printed on the card.
 *
 * Always a square frame, so every drawing sits on the same optical centre. Each sits on a small white plate,
 * and that is not decoration: the marks carry their own printed colours and cannot be recoloured - Kreuz and
 * Schaufel are near-black and would disappear on this app's near-black ground.
 */
@Composable
fun SuitMark(suit: JassSuit, size: Dp = 26.dp, modifier: Modifier = Modifier) {
    val name = stringResource(suit.name)
    Box(
        modifier = modifier
            .size(size)
            .background(Color.White, RoundedCornerShape(size * 0.2f))
            .semantics { contentDescription = name },
        contentAlignment = Alignment.Center,
    ) {
        Image(
            painter = painterResource(suitDrawable(suit)),
            contentDescription = null,
            contentScale = ContentScale.Fit,
            // The margin a mark has on a real card, so the drawing never touches the edge.
            modifier = Modifier.padding(size * 0.13f),
        )
    }
}

/** Which side of the rank the suit mark sits on - see iOS `MarkSide`: last, it holds still while the rank changes. */
enum class MarkSide { LEADING, TRAILING }

/** A card as it reads on a real Jass card: the suit's own mark, then the rank in neutral text. */
@Composable
fun CardChip(
    label: String,
    modifier: Modifier = Modifier,
    rankColor: Color = Color.White,
    markSize: Dp = 24.dp,
    markSide: MarkSide = MarkSide.LEADING,
    style: androidx.compose.ui.text.TextStyle = JassType.callout,
) {
    val card = CardLabel.parse(label)
    if (card == null) {
        // A label the app cannot parse still has to show something.
        Text(label, color = rankColor, style = style, modifier = modifier)
        return
    }
    val suitName = stringResource(card.suit.name)
    val rankName = card.rankName?.let { stringResource(it) } ?: card.rank
    Row(
        modifier = modifier.clearAndSetSemantics { contentDescription = "$suitName $rankName" },
        horizontalArrangement = Arrangement.spacedBy(5.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (markSide == MarkSide.LEADING) SuitMark(card.suit, markSize)
        Text(rankName, color = rankColor, style = style)
        if (markSide == MarkSide.TRAILING) SuitMark(card.suit, markSize)
    }
}
