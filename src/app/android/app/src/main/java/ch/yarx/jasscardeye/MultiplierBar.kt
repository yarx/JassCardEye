package ch.yarx.jasscardeye

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/MultiplierBar.swift.

/**
 * The factor the written result is multiplied by, as one row of buttons.
 *
 * Asked in the start questions, next to the discipline, and ×1 unless somebody picks another. It is not a property
 * of the discipline - the same Obenabe counts triple at one table and single at the next - but it is known before the
 * first card is laid down, and settling it there keeps the counting screen to the pile and the score.
 */
@Composable
fun MultiplierBar(multiplier: Int, onChange: (Int) -> Unit, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        for (factor in JassRules.multipliers) {
            val chosen = factor == multiplier
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(8.dp))
                    .background(if (chosen) JassColors.Green else Color.White.copy(alpha = 0.14f))
                    .clickable { onChange(factor) }
                    .semantics { contentDescription = "Faktor $factor"; selected = chosen }
                    .padding(vertical = 8.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    "×$factor",
                    style = JassType.callout.copy(
                        fontWeight = if (chosen) FontWeight.Bold else FontWeight.Normal,
                        fontFeatureSettings = "tnum",
                    ),
                    color = if (chosen) Color.Black else Color.White,
                )
            }
        }
    }
}
