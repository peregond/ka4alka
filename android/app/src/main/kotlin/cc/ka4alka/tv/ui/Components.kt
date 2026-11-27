package cc.ka4alka.tv.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.layout
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Movie
import androidx.compose.material.icons.filled.Person
import coil.compose.AsyncImage
import cc.ka4alka.core.Title
import java.util.Locale

/** The one focus treatment of the app: the item grows a little and gets a white ring, as on the desktop hover. */
@Composable
fun Focusable(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    shape: Shape = RoundedCornerShape(16.dp),
    focusedScale: Float = 1.06f,
    ring: Boolean = true,
    content: @Composable BoxScope.(focused: Boolean) -> Unit,
) {
    var focused by remember { mutableStateOf(false) }
    val scale by animateFloatAsState(if (focused) focusedScale else 1f, label = "focusScale")
    Box(
        modifier
            .graphicsLayer { scaleX = scale; scaleY = scale }
            .clip(shape)
            .then(if (focused && ring) Modifier.border(BorderStroke(3.dp, Color.White), shape) else Modifier)
            .onFocusChanged { focused = it.isFocused }
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onClick),
    ) { content(focused) }
}

/** A pill button. `primary` is the white call to action; the other is a translucent secondary. */
@Composable
fun PillButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, icon: ImageVector? = null, primary: Boolean = false, enabled: Boolean = true) {
    Focusable(onClick = { if (enabled) onClick() }, modifier = modifier.height(52.dp), shape = CircleShape, focusedScale = 1.05f) { focused ->
        val container = when {
            primary && focused -> Color.White
            primary -> Color(0xF2FFFFFF)
            focused -> Color(0x40FFFFFF)
            else -> Color(0x26FFFFFF)
        }
        val content = if (primary) KD.accentInk.copy(alpha = if (enabled) 1f else 0.5f) else Color.White.copy(alpha = if (enabled) 1f else 0.5f)
        Row(Modifier.background(container).padding(horizontal = 26.dp).height(52.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            if (icon != null) Icon(icon, null, tint = content, modifier = Modifier.size(24.dp))
            Text(text, color = content, style = MaterialTheme.typography.labelLarge, maxLines = 1)
        }
    }
}

@Composable
fun RoundButton(icon: ImageVector, description: String, onClick: () -> Unit, modifier: Modifier = Modifier, active: Boolean = false) {
    Focusable(onClick = onClick, modifier = modifier.size(52.dp), shape = CircleShape, focusedScale = 1.08f) { focused ->
        Box(Modifier.fillMaxSize().background(if (focused) Color(0x59FFFFFF) else Color(0x2EFFFFFF)), contentAlignment = Alignment.Center) {
            Icon(icon, description, tint = if (active) Color(0xFFFF453A) else Color.White, modifier = Modifier.size(26.dp))
        }
    }
}

/** A filter chip (genre, season, limit). Selected chips are filled with the accent. */
@Composable
fun Chip(text: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Focusable(onClick = onClick, modifier = modifier.height(44.dp), shape = RoundedCornerShape(14.dp), focusedScale = 1.06f) { focused ->
        Box(
            Modifier.background(if (selected) KD.secondaryContainer else if (focused) KD.surfaceHigh else KD.surface).padding(horizontal = 22.dp).height(44.dp),
            contentAlignment = Alignment.Center,
        ) {
            Text(text, color = if (selected) KD.onSecondaryContainer else Color.White, style = MaterialTheme.typography.titleSmall, maxLines = 1)
        }
    }
}

@Composable
fun RatingBadge(value: Double, source: String, modifier: Modifier = Modifier) {
    val color = if (source == "КП") KD.kinopoisk else KD.imdb
    Row(modifier.clip(RoundedCornerShape(8.dp)).background(Color(0xB3000000)).padding(horizontal = 10.dp, vertical = 5.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(source, color = color, fontSize = 15.sp, fontWeight = FontWeight.ExtraBold)
        Text(String.format(Locale.US, "%.1f", value), color = Color.White, fontSize = 17.sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
fun TagText(text: String, modifier: Modifier = Modifier, color: Color = Color.White) {
    Text(
        text, color = color, fontSize = 14.sp, fontWeight = FontWeight.Bold,
        modifier = modifier.clip(RoundedCornerShape(6.dp)).background(Color(0x33FFFFFF)).padding(horizontal = 8.dp, vertical = 3.dp),
    )
}

@Composable
fun Poster(url: String?, modifier: Modifier = Modifier, shape: Shape = RoundedCornerShape(12.dp), person: Boolean = false) {
    Box(modifier.clip(shape).background(KD.surface), contentAlignment = Alignment.Center) {
        // The placeholder stays under the picture, so a photo that fails to load still shows an icon instead of an empty circle.
        Icon(if (person) Icons.Filled.Person else Icons.Filled.Movie, null, tint = KD.textTertiary, modifier = Modifier.size(if (person) 40.dp else 48.dp))
        if (url != null) AsyncImage(model = url, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
    }
}

/** A catalog card: poster with the rating in the corner, title and year below. */
@Composable
fun PosterCard(title: Title, onClick: () -> Unit, modifier: Modifier = Modifier, width: Int = 176) {
    Column(modifier.width(width.dp)) {
        Focusable(onClick = onClick, modifier = Modifier.width(width.dp).height((width * 3 / 2).dp), shape = RoundedCornerShape(20.dp)) {
            Poster(title.poster, Modifier.fillMaxSize(), RoundedCornerShape(0.dp))
            val rating = title.rating
            if (rating != null) RatingBadge(rating, title.ratingSource ?: "", Modifier.align(Alignment.TopEnd).padding(8.dp))
        }
        Text(title.title, color = Color.White, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 12.dp, start = 2.dp))
        Text(if (title.year > 0) title.year.toString() else "", color = KD.textTertiary, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(start = 2.dp))
    }
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(text, color = Color.White, style = MaterialTheme.typography.headlineSmall, modifier = modifier.padding(start = 56.dp, bottom = 14.dp))
}

@Composable
fun Message(title: String, text: String, modifier: Modifier = Modifier) {
    Column(modifier.fillMaxWidth().padding(56.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(title, color = Color.White, style = MaterialTheme.typography.headlineMedium)
        Text(text, color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge)
    }
}

fun formatBytes(bytes: Long): String = when {
    bytes >= 1L shl 30 -> String.format(Locale.US, "%.1f ГБ", bytes / 1073741824.0)
    bytes >= 1L shl 20 -> String.format(Locale.US, "%.0f МБ", bytes / 1048576.0)
    bytes > 0 -> String.format(Locale.US, "%.0f КБ", bytes / 1024.0)
    else -> "0"
}

fun formatSpeed(bytesPerSecond: Int): String = when {
    bytesPerSecond >= 1 shl 20 -> String.format(Locale.US, "%.1f МБ/с", bytesPerSecond / 1048576.0)
    bytesPerSecond >= 1024 -> String.format(Locale.US, "%d КБ/с", bytesPerSecond / 1024)
    else -> "0 КБ/с"
}

fun formatEta(seconds: Long?): String = when {
    seconds == null -> ""
    seconds >= 3600 -> "осталось ${seconds / 3600} ч ${seconds % 3600 / 60} мин"
    seconds >= 60 -> "осталось ${seconds / 60} мин"
    else -> "осталось меньше минуты"
}

/** Lets a row run to the screen edges while its first item still lines up with the page margin (the parent clips at its padding). */
fun Modifier.bleed(margin: androidx.compose.ui.unit.Dp): Modifier = this.then(
    Modifier.layout { measurable, constraints ->
        val extra = margin.roundToPx()
        val placeable = measurable.measure(constraints.copy(maxWidth = constraints.maxWidth + 2 * extra))
        layout(constraints.maxWidth, placeable.height) { placeable.place(-extra, 0) }
    },
)
