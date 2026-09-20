package ch.yarx.jasscardeye

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

// Port of src/app/ios/Sources/AboutView.swift.

/**
 * The page a person reads rather than changes: who publishes the app, under which licence it is, whose work it
 * carries and what it does with data. Opened from the home screen next to the gear - Settings is for things you
 * change, this is for things you read, and the version a tester quotes is here too.
 *
 * The one thing it changes is [developerTools]: five taps on *Firma* flip it, see `LiveDetectionModel.developerTools`.
 * Nothing hints at it - the tools are for looking into a wrong count, not for playing.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AboutSheet(developerTools: Boolean, onDeveloperToolsChange: (Boolean) -> Unit, unlocked: Boolean, onDismiss: () -> Unit) {
    // Taps on *Firma* since the page opened; the fifth flips the tools.
    var publisherTaps by remember { mutableIntStateOf(0) }
    // Set by the fifth tap, so the footer can say what it did - nothing else on screen would.
    var toolsSwitched by remember { mutableStateOf(false) }

    fun publisherTapped() {
        publisherTaps += 1
        if (publisherTaps < UNLOCK_TAPS) return
        publisherTaps = 0
        onDeveloperToolsChange(!developerTools)
        toolsSwitched = true
    }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = false),
        containerColor = Color.Black,
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
            Text("Über", style = JassType.headline, color = Color.White, modifier = Modifier.align(Alignment.Center))
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
            Column(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text("JassCardEye", style = JassType.title3.copy(fontWeight = FontWeight.Bold), color = Color.White)
                Text("Version ${AppInfo.versionLine}", style = JassType.callout, color = JassColors.Secondary)
                // Under the version, so a tester's report says which app they were looking at.
                Text(if (unlocked) "Freigeschaltet" else "Demo", style = JassType.callout,
                    color = if (unlocked) JassColors.Green else JassColors.Secondary)
            }

            FormSection(
                "Herausgeber",
                footer = if (toolsSwitched) "Bild und Session aufzeichnen sind ${if (developerTools) "eingeblendet" else "ausgeblendet"}." else null,
            ) {
                ValueRow("Firma", About.PUBLISHER,
                    Modifier.clickable(interactionSource = null, indication = null, onClick = ::publisherTapped))
                FormDivider()
                ValueRow("E-Mail") { LinkText(About.EMAIL, "mailto:${About.EMAIL}") }
                FormDivider()
                ValueRow("Website") { LinkText(About.WEBSITE_LABEL, About.WEBSITE) }
            }

            FormSection("Lizenz") {
                BodyRow(About.LICENCE_TEXT)
                FormDivider()
                LinkRow("Lizenztext (AGPL-3.0)", About.LICENCE)
                FormDivider()
                LinkRow("Quellcode", About.SOURCE)
            }

            FormSection("Erkennung") {
                BodyRow(About.MODEL_TEXT)
                FormDivider()
                LinkRow("Ultralytics YOLO", About.ULTRALYTICS)
            }

            FormSection("Bildnachweis", footer = About.CREDITS_NOTE) {
                for (suit in JassSuit.all) {
                    CreditRow(suit)
                    FormDivider()
                }
                LinkRow("Lizenz CC BY-SA 4.0", About.CC_BY_SA)
            }

            FormSection("Datenschutz") {
                BodyRow(About.PRIVACY_TEXT)
            }

            FormSection("Bibliotheken") {
                BodyRow(About.LIBRARIES_TEXT)
                FormDivider()
                LinkRow("Apache License 2.0", About.APACHE)
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

private const val UNLOCK_TAPS = 5

/** The facts on the page, in one place. iOS's `AboutView.swift` says the same in the same order. */
internal object About {
    const val PUBLISHER = "YARX GmbH"
    const val EMAIL = "support@yarx.ch"
    const val WEBSITE_LABEL = "yarx.ch"
    const val WEBSITE = "https://yarx.ch"
    const val LICENCE = "https://www.gnu.org/licenses/agpl-3.0.html"
    const val SOURCE = "https://github.com/yarx/JassCardEye"
    const val ULTRALYTICS = "https://github.com/ultralytics/ultralytics"
    const val CC_BY_SA = "https://creativecommons.org/licenses/by-sa/4.0/deed.de"
    const val APACHE = "https://www.apache.org/licenses/LICENSE-2.0"

    const val LICENCE_TEXT = "JassCardEye ist freie Software unter der GNU Affero General Public License 3.0. Für den Vertrieb über den App Store und Google Play gilt eine zusätzliche Erlaubnis, beschrieben in der Lizenzdatei."
    const val MODEL_TEXT = "Das Erkennungsmodell wurde mit Ultralytics YOLO11 (AGPL-3.0) trainiert, auf je einem französischen und einem Deutschschweizer Jassblatt."
    const val CREDITS_NOTE = "Die Farbzeichen stammen von Wikimedia Commons und sind unverändert übernommen."
    const val PRIVACY_TEXT = "Die App sammelt keine Daten. Die Kamerabilder werden auf dem Gerät ausgewertet und nicht gespeichert, die App hat keine Internetverbindung, kein Konto und keine Werbung. Auf dem Gerät bleiben nur deine Einstellungen."
    // Android's own line: unlike the iOS app, which uses nothing but Apple's frameworks, this one ships libraries.
    const val LIBRARIES_TEXT = "Die App verwendet AndroidX mit Jetpack Compose und CameraX, LiteRT von Google und die Kotlin-Standardbibliothek, alle unter der Apache License 2.0."
}

/** The mark as it appears in the app, its name linking to the Commons page, then author and licence. */
@Composable
private fun CreditRow(suit: JassSuit) {
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(Modifier.clearAndSetSemantics { }) { SuitMark(suit, 24.dp) }
        Box(Modifier.weight(1f)) { LinkText(suit.name, suit.markCredit.page) }
        Text(suit.markCredit.attribution, style = JassType.footnote, color = JassColors.Secondary, textAlign = TextAlign.End)
    }
}
