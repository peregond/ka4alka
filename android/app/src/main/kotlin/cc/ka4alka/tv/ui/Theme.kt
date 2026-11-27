package cc.ka4alka.tv.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/** The brand tokens of the desktop apps (mint accent on black), expressed as a Material 3 dark scheme for the TV. */
object KD {
    val accent = Color(0xFF5BE3B5)
    val accentFill = Color(0xFF1ABF62)
    val accentInk = Color(0xFF06140F)
    val background = Color(0xFF000000)
    // Tonal surfaces tinted toward the brand mint, as Material 3 derives them from the accent colour.
    val surface = Color(0xFF171D1A)
    val surfaceHigh = Color(0xFF232B27)
    val secondaryContainer = Color(0xFF1B4A3B)
    val onSecondaryContainer = Color(0xFFB8F5DF)
    val hairline = Color(0xFF3A3A3C)
    val text = Color.White
    val textSecondary = Color(0xB3FFFFFF)
    val textTertiary = Color(0x80FFFFFF)
    val danger = Color(0xFFFF8A7A)
    val warning = Color(0xFFF2B35B)
    val success = Color(0xFF5FD98F)
    val kinopoisk = Color(0xFFFF5500)
    val imdb = Color(0xFFF5C518)
}

private val tvTypography = Typography(
    displayLarge = TextStyle(fontSize = 56.sp, fontWeight = FontWeight.Bold, lineHeight = 62.sp),
    displayMedium = TextStyle(fontSize = 48.sp, fontWeight = FontWeight.Bold, lineHeight = 54.sp),
    headlineLarge = TextStyle(fontSize = 36.sp, fontWeight = FontWeight.Bold, lineHeight = 42.sp),
    headlineMedium = TextStyle(fontSize = 30.sp, fontWeight = FontWeight.SemiBold, lineHeight = 36.sp),
    headlineSmall = TextStyle(fontSize = 26.sp, fontWeight = FontWeight.SemiBold, lineHeight = 32.sp),
    titleLarge = TextStyle(fontSize = 24.sp, fontWeight = FontWeight.SemiBold, lineHeight = 30.sp),
    titleMedium = TextStyle(fontSize = 20.sp, fontWeight = FontWeight.Medium, lineHeight = 26.sp),
    titleSmall = TextStyle(fontSize = 18.sp, fontWeight = FontWeight.Medium, lineHeight = 24.sp),
    bodyLarge = TextStyle(fontSize = 20.sp, lineHeight = 29.sp),
    bodyMedium = TextStyle(fontSize = 18.sp, lineHeight = 26.sp),
    bodySmall = TextStyle(fontSize = 16.sp, lineHeight = 22.sp),
    labelLarge = TextStyle(fontSize = 20.sp, fontWeight = FontWeight.SemiBold),
    labelMedium = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.Medium),
    labelSmall = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
)

@Composable
fun KachalkaTheme(content: @Composable () -> Unit) {
    // Designed for a 55-65" screen seen from a sofa: everything is drawn a fifth smaller than the 320 dpi default, so more fits on screen.
    val base = LocalDensity.current
    CompositionLocalProvider(LocalDensity provides Density(base.density * 0.8f, base.fontScale)) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = KD.accent, onPrimary = KD.accentInk,
            secondary = KD.accentFill, onSecondary = KD.accentInk,
            background = KD.background, onBackground = KD.text,
            surface = KD.surface, onSurface = KD.text,
            surfaceVariant = KD.surfaceHigh, secondaryContainer = KD.secondaryContainer, onSecondaryContainer = KD.onSecondaryContainer, onSurfaceVariant = KD.textSecondary,
            outline = KD.hairline, error = KD.danger,
        ),
        typography = tvTypography,
        content = content,
    )
    }
}
