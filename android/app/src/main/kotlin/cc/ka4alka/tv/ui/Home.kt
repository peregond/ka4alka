package cc.ka4alka.tv.ui

import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.blur
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cc.ka4alka.core.Library
import cc.ka4alka.core.Shelves
import cc.ka4alka.core.Title
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.Screen
import cc.ka4alka.tv.UpdateUi
import coil.compose.AsyncImage
import kotlinx.coroutines.delay

/** The slides change every 8 seconds, as on the desktop banners. Pressing a direction key on the banner restarts the wait. */
const val HERO_INTERVAL_MS = 8000L

@Composable
fun HomeScreen(vm: AppViewModel, library: Library) {
    val hero = remember(library) { Shelves.hero(library) }
    val shelves = remember(library) { Shelves.home(library) }
    val update by vm.update.collectAsStateWithLifecycleCompat()
    LazyColumn(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(30.dp)) {
        if (update is UpdateUi.Available) item { UpdateBanner(vm, update as UpdateUi.Available) }
        if (hero.isNotEmpty()) item { HeroBanner(vm, hero) }
        items(shelves, key = { it.id }) { shelf ->
            Column {
                SectionTitle(shelf.title)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(20.dp), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 56.dp, vertical = 12.dp)) {
                    items(shelf.items, key = { it.id }) { title -> PosterCard(title, onClick = { vm.open(Screen.Detail(title.id)) }) }
                }
            }
        }
        item { Box(Modifier.height(60.dp)) }
    }
}

@Composable
private fun UpdateBanner(vm: AppViewModel, update: UpdateUi.Available) {
    Row(
        Modifier.fillMaxWidth().padding(start = 56.dp, end = 56.dp, top = 24.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(18.dp)).background(KD.surface).padding(20.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        Icon(Icons.Filled.SystemUpdate, null, tint = KD.accent, modifier = Modifier.size(34.dp))
        Column(Modifier.weight(1f)) {
            Text("Доступна версия ${update.manifest.version}", color = Color.White, style = MaterialTheme.typography.titleLarge)
            if (update.manifest.notes.isNotBlank()) Text(update.manifest.notes, color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
        PillButton("Обновить", onClick = { vm.open(Screen.Settings); vm.installUpdate(update.manifest) }, primary = true)
    }
}

/** The big banner on top of the home page: wide artwork, scrims, title, rating and two actions. */
@Composable
fun HeroBanner(vm: AppViewModel, items: List<Title>) {
    // A 1080p TV is 540 dp tall: the banner takes about two thirds and the first shelf peeks out below it.
    val heroHeight = (androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp * 0.68f).dp.coerceIn(340.dp, 560.dp)
    var index by remember { mutableIntStateOf(0) }
    var restart by remember { mutableIntStateOf(0) }
    val backdrops by vm.backdrops.collectAsStateWithLifecycleCompat()
    val saved by vm.saved.collectAsStateWithLifecycleCompat()
    LaunchedEffect(items) { items.forEach { vm.loadBackdrop(it) } }
    LaunchedEffect(index, restart, items.size) {
        if (items.size > 1) { delay(HERO_INTERVAL_MS); index = (index + 1) % items.size }
    }
    val step: (Int) -> Unit = { delta -> index = Math.floorMod(index + delta, items.size); restart++ }
    Box(
        Modifier.fillMaxWidth().height(heroHeight).onPreviewKeyEvent { event ->
            if (event.type == KeyEventType.KeyDown && items.size > 1) when (event.key) {
                Key.DirectionRight -> { step(1); true }
                Key.DirectionLeft -> if (index > 0) { step(-1); true } else false
                else -> false
            } else false
        },
    ) {
        Crossfade(targetState = index, animationSpec = tween(380), label = "hero") { page ->
            val item = items[page]
            Box(Modifier.fillMaxSize()) {
                val wide = backdrops[item.id]
                if (wide != null) AsyncImage(wide, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                else if (item.poster != null) {
                    AsyncImage(item.poster, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize().blur(60.dp).alpha(0.55f))
                    Poster(item.poster, Modifier.align(Alignment.CenterEnd).padding(end = 130.dp).width(300.dp).height(450.dp), RoundedCornerShape(18.dp))
                }
                Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0x33000000), Color(0x00000000), Color(0xD9000000), Color.Black))))
                Box(Modifier.fillMaxSize().background(Brush.horizontalGradient(listOf(Color(0xE6000000), Color(0x99000000), Color(0x00000000)))))
                HeroText(item)
            }
        }
        // The buttons stay put while slides change, so remote focus is never lost to a slide swap.
        val current = items[index]
        Row(Modifier.align(Alignment.BottomStart).padding(start = 56.dp, bottom = 44.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            PillButton("Подробнее", onClick = { vm.open(Screen.Detail(current.id)) }, primary = true)
            val isSaved = current.id in saved
            RoundButton(if (isSaved) Icons.Filled.Favorite else Icons.Filled.FavoriteBorder, if (isSaved) "Убрать из сохранённого" else "В сохранённое", onClick = { vm.toggleSaved(current.id) }, active = isSaved)
        }
        Row(Modifier.align(Alignment.BottomCenter).padding(bottom = 14.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items.forEachIndexed { i, _ ->
                Box(Modifier.height(8.dp).width(if (i == index) 26.dp else 8.dp).clip(CircleShape).background(if (i == index) Color.White else Color(0xB3FFFFFF).copy(alpha = 0.45f)))
            }
        }
    }
}

@Composable
private fun HeroText(item: Title) {
    Column(Modifier.fillMaxSize().padding(start = 56.dp, end = 56.dp, bottom = 108.dp), verticalArrangement = Arrangement.Bottom) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Filled.AutoAwesome, null, tint = KD.accent, modifier = Modifier.size(22.dp))
            Text("Актуальное", color = KD.accent, style = MaterialTheme.typography.titleMedium)
        }
        Text(item.title, color = Color.White, style = MaterialTheme.typography.headlineLarge, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 4.dp).fillMaxWidth(0.62f))
        Row(Modifier.padding(top = 10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            item.rating?.let { RatingBadge(it, item.ratingSource ?: "") }
            Text(listOfNotNull(item.year.takeIf { it > 0 }?.toString(), item.genres.take(3).joinToString(", ").ifBlank { null }, item.country.ifBlank { null }).joinToString(" · "), color = KD.textSecondary, style = MaterialTheme.typography.titleMedium, maxLines = 1)
        }
        Text(item.description, color = Color(0xD9FFFFFF), style = MaterialTheme.typography.bodyLarge, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 8.dp).fillMaxWidth(0.5f))
    }
}
