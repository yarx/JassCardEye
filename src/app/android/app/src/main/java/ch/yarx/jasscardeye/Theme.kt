package ch.yarx.jasscardeye

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/**
 * The colours and type sizes of the iOS app, named after what iOS calls them, so a view ported from Swift
 * reads the same: `.green` is [JassColors.Green], `.callout` is [JassType.callout].
 *
 * The app is dark on both platforms (`preferredColorScheme(.dark)`), so these are the dark-mode system
 * colours of iOS.
 */
object JassColors {
    val Green = Color(0xFF30D158)
    val Orange = Color(0xFFFF9F0A)
    val Yellow = Color(0xFFFFD60A)
    val Red = Color(0xFFFF453A)
    /** `Color(white: 0.07)` - the ground of home and start screen. */
    val Ground = Color(0xFF121212)
    /** `Color(white: 0.30)` - status bar, picker cells; the grey family of the app. */
    val Grey30 = Color(0xFF4D4D4D)
    /** `.secondary` on a dark ground. */
    val Secondary = Color(0x99EBEBF5)
    /** A grouped form's row on a dark ground. */
    val FormRow = Color(0xFF1C1C1E)
}

/** The iOS text styles at their default sizes. */
object JassType {
    val largeTitle = TextStyle(fontSize = 34.sp, lineHeight = 41.sp)
    val title = TextStyle(fontSize = 28.sp, lineHeight = 34.sp)
    val title3 = TextStyle(fontSize = 20.sp, lineHeight = 25.sp)
    val headline = TextStyle(fontSize = 17.sp, lineHeight = 22.sp, fontWeight = FontWeight.SemiBold)
    val body = TextStyle(fontSize = 17.sp, lineHeight = 22.sp)
    val callout = TextStyle(fontSize = 16.sp, lineHeight = 21.sp)
    val footnote = TextStyle(fontSize = 13.sp, lineHeight = 18.sp)
    val caption = TextStyle(fontSize = 12.sp, lineHeight = 16.sp)
    val caption2 = TextStyle(fontSize = 11.sp, lineHeight = 13.sp)
}

@Composable
fun JassCardEyeTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = JassColors.Green,
            onPrimary = Color.Black,
            secondary = JassColors.Green,
            background = JassColors.Ground,
            surface = JassColors.Ground,
            onBackground = Color.White,
            onSurface = Color.White,
            error = JassColors.Red,
        ),
        content = content,
    )
}
