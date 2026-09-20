package ch.yarx.jasscardeye

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withLink
import androidx.compose.ui.unit.dp

// The grouped form both sheets are made of - Einstellungen and Über - as SwiftUI's `Form` draws it on iOS.

/** A titled block of rows, as a grouped `Form` section reads on iOS, with the footer a `Section` can carry. */
@Composable
internal fun FormSection(title: String, footer: String? = null, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(title, style = JassType.headline, color = JassColors.Secondary, modifier = Modifier.padding(start = 16.dp))
        Column(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(20.dp))
                .background(JassColors.FormRow),
        ) { content() }
        footer?.let { FormNote(it) }
    }
}

/** The line between two rows, indented like the iOS separator. */
@Composable
internal fun FormDivider() = HorizontalDivider(Modifier.padding(start = 16.dp), color = Color.White.copy(alpha = 0.12f))

/** Secondary small print: under the rows of a section, or as its footer. */
@Composable
internal fun FormNote(text: String) {
    Text(text, style = JassType.caption, color = JassColors.Secondary, modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp))
}

/** A label on the left, its value on the right. */
@Composable
internal fun ValueRow(label: String, value: String, modifier: Modifier = Modifier) = ValueRow(label, modifier) {
    Text(value, style = JassType.body, color = JassColors.Secondary)
}

@Composable
internal fun ValueRow(label: String, modifier: Modifier = Modifier, value: @Composable () -> Unit) {
    Row(modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = JassType.body, color = Color.White, modifier = Modifier.weight(1f))
        value()
    }
}

/** A paragraph as a row of its own. */
@Composable
internal fun BodyRow(text: String) {
    Text(text, style = JassType.body, color = Color.White, modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp))
}

/** A row that is nothing but a link. */
@Composable
internal fun LinkRow(label: String, url: String) {
    Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp)) { LinkText(label, url) }
}

/**
 * Text that is a link - announced as one by TalkBack, as `Link` is by VoiceOver. Opened through an intent of its
 * own rather than the default handler, so a phone without a browser or a mail app shrugs instead of crashing.
 */
@Composable
internal fun LinkText(label: String, url: String) {
    val context = LocalContext.current
    val link = LinkAnnotation.Url(url, TextLinkStyles(SpanStyle(color = JassColors.Green))) {
        val uri = Uri.parse(url)
        val intent = if (uri.scheme == "mailto") Intent(Intent.ACTION_SENDTO, uri) else Intent(Intent.ACTION_VIEW, uri)
        runCatching { context.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }
    Text(buildAnnotatedString { withLink(link) { append(label) } }, style = JassType.body)
}
