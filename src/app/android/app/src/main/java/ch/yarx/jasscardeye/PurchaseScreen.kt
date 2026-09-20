package ch.yarx.jasscardeye

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.History
import androidx.compose.material.icons.outlined.LockOpen
import androidx.compose.material.icons.outlined.Verified
import androidx.compose.material.icons.outlined.Visibility
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/PurchaseView.swift.

/**
 * The purchase page: what the purchase unlocks, its price, buying and restoring.
 *
 * Deliberately nothing else - no other way to pay, no link out of the app. It closes by itself once the points are
 * unlocked, so a purchase made in the middle of a count lands straight back on the score, which is now readable.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PurchaseSheet(onDismiss: () -> Unit) {
    val store = LocalStore.current
    val context = LocalContext.current
    LaunchedEffect(Unit) { store.loadProduct() }
    LaunchedEffect(store.unlocked) { if (store.unlocked) onDismiss() }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = Color.Black,
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
            Text("Freischalten", style = JassType.headline, color = Color.White, modifier = Modifier.align(Alignment.Center))
            TextButton(onClick = onDismiss, modifier = Modifier.align(Alignment.CenterStart)) {
                Text("Schliessen", style = JassType.headline, color = JassColors.Green)
            }
        }
        Column(
            Modifier
                .verticalScroll(rememberScrollState())
                .padding(24.dp)
                .navigationBarsPadding(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(24.dp),
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Outlined.LockOpen, contentDescription = null, tint = JassColors.Green, modifier = Modifier.size(44.dp))
                Text("Punkte freischalten", style = JassType.title3.copy(fontWeight = FontWeight.Bold), color = Color.White)
            }

            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Benefit(Icons.Outlined.Visibility, "Die gezählten Punkte werden lesbar - deine und die des Gegners, mit der Aufschlüsselung.")
                Benefit(Icons.Outlined.History, "Auch «Letzte Zählung» auf dem Startbildschirm zeigt die Punkte.")
                Benefit(Icons.Outlined.Verified, "Einmal kaufen, für immer. Kein Abo, kein Konto.")
            }

            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(
                    onClick = { store.purchase(context) },
                    enabled = store.price != null && !store.purchasing && !store.pending,
                    modifier = Modifier.fillMaxWidth().height(52.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = JassColors.Green, contentColor = Color.White),
                ) {
                    Text(
                        store.price?.let { "Kaufen für $it" } ?: if (store.productUnavailable) "Nicht verfügbar" else "Preis wird geladen …",
                        style = JassType.headline,
                    )
                }
                if (store.pending) Note("Der Kauf wartet auf die Zahlung. Die Punkte werden frei, sobald sie eingegangen ist.")
                if (store.productUnavailable) Note("Google Play ist gerade nicht erreichbar. Die Demo zählt weiter wie bisher.")
                store.problem?.let { Note(it) }
                TextButton(onClick = store::restore) {
                    Text("Kauf wiederherstellen", style = JassType.callout, color = JassColors.Green)
                }
            }
            Spacer(Modifier.height(8.dp))
        }
    }
}

/**
 * The way to the purchase page wherever the points are blurred: "Punkte freischalten – CHF 5.00", with the price from
 * Google Play and without one until it has loaded.
 */
@Composable
fun UnlockButton(onClick: () -> Unit, modifier: Modifier = Modifier) {
    val store = LocalStore.current
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.fillMaxWidth(),
        border = null,
        colors = ButtonDefaults.outlinedButtonColors(containerColor = JassColors.Green.copy(alpha = 0.18f), contentColor = JassColors.Green),
    ) {
        Icon(Icons.Outlined.LockOpen, contentDescription = null, modifier = Modifier.size(18.dp))
        Spacer(Modifier.width(6.dp))
        Text(
            store.price?.let { "Punkte freischalten – $it" } ?: "Punkte freischalten",
            style = JassType.callout.copy(fontWeight = FontWeight.SemiBold),
        )
    }
}

@Composable
private fun Benefit(icon: ImageVector, text: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.Top) {
        Icon(icon, contentDescription = null, tint = JassColors.Green, modifier = Modifier.size(24.dp))
        Text(text, style = JassType.body, color = Color.White)
    }
}

@Composable
private fun Note(text: String) {
    Text(text, style = JassType.footnote, color = JassColors.Secondary, textAlign = TextAlign.Center)
}
