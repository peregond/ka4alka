package cc.ka4alka.tv.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
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
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cc.ka4alka.core.Library
import cc.ka4alka.core.Person
import cc.ka4alka.core.Release
import cc.ka4alka.core.Section
import cc.ka4alka.core.Title
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.Releases
import cc.ka4alka.tv.Screen
import coil.compose.AsyncImage

@Composable
fun DetailScreen(vm: AppViewModel, library: Library, id: String) {
    val title = library[id]
    if (title == null) { Message("Не нашли карточку", "Возможно, библиотека обновилась. Вернись назад и попробуй ещё раз."); return }
    val backdrops by vm.backdrops.collectAsStateWithLifecycleCompat()
    val releases by vm.releases.collectAsStateWithLifecycleCompat()
    val saved by vm.saved.collectAsStateWithLifecycleCompat()
    LaunchedEffect(title.id) { vm.loadBackdrop(title); vm.loadReleases(title) }
    val loaded = releases[title.id]
    var season by remember(title.id) { mutableStateOf<Int?>(null) }

    Box(Modifier.fillMaxSize()) {
        val wide = backdrops[title.id]
        if (wide != null) AsyncImage(wide, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
        else if (title.poster != null) AsyncImage(title.poster, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize().blur(40.dp))
        Box(Modifier.fillMaxSize().background(Brush.horizontalGradient(listOf(Color(0xF2000000), Color(0xCC000000), Color(0x80000000)))))
        Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0x00000000), Color(0x66000000), Color.Black))))

        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 40.dp, bottom = 80.dp), verticalArrangement = Arrangement.spacedBy(28.dp)) {
            item { Header(vm, title, title.id in saved, loaded) }
            if (title.cast.isNotEmpty()) item { Cast(title.directors.take(2) + title.cast.take(14)) }
            item {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text("Раздачи", color = Color.White, style = MaterialTheme.typography.headlineSmall)
                    if (loaded is Releases.Loaded) RoundButton(Icons.Filled.Refresh, "Обновить список", onClick = { vm.loadReleases(title, force = true) })
                }
            }
            when (loaded) {
                null, Releases.Loading -> item { Text("Ищем раздачи на трекерах…", color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge) }
                is Releases.Loaded -> {
                    val seasons = loaded.items.mapNotNull { it.season }.distinct().sorted()
                    if (title.section == Section.Series && seasons.isNotEmpty()) item {
                        LazyRow(Modifier.bleed(56.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 10.dp, bottom = 10.dp)) {
                            item { Chip("Все сезоны", season == null, { season = null }) }
                            items(seasons) { s -> Chip("Сезон $s", season == s, { season = s }) }
                        }
                    }
                    val rows = loaded.items.filter { season == null || it.season == season }
                    if (rows.isEmpty()) item {
                        Text(
                            if (loaded.failed.isNotEmpty()) "Источники сейчас не отвечают (${loaded.failed.joinToString(", ")}). Проверь интернет и обнови список."
                            else "Раздач для этого названия пока не нашлось.",
                            color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge,
                        )
                    }
                    items(rows, key = { it.id }) { release -> ReleaseRow(release, onWatch = { vm.watch(title, release) }, onDownload = { vm.download(title, release) }) }
                }
            }
        }
    }
}

@Composable
private fun Header(vm: AppViewModel, title: Title, saved: Boolean, releases: Releases?) {
    val best = (releases as? Releases.Loaded)?.items?.firstOrNull { it.recommended } ?: (releases as? Releases.Loaded)?.items?.firstOrNull { !it.poor && it.seeds > 0 }
    Row(horizontalArrangement = Arrangement.spacedBy(36.dp)) {
        Poster(title.poster, Modifier.width(240.dp).height(360.dp), RoundedCornerShape(18.dp))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(title.title, color = Color.White, style = MaterialTheme.typography.displayMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (title.originalTitle != null) Text(title.originalTitle!!, color = KD.textSecondary, style = MaterialTheme.typography.titleLarge, maxLines = 1)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                title.kinopoisk?.let { RatingBadge(it, "КП") }
                title.imdb?.let { RatingBadge(it, "IMDb") }
                Text(listOfNotNull(title.year.takeIf { it > 0 }?.toString(), title.genres.take(4).joinToString(", ").ifBlank { null }, title.country.ifBlank { null }).joinToString(" · "), color = KD.textSecondary, style = MaterialTheme.typography.titleMedium, maxLines = 1)
            }
            Text(title.description.ifBlank { "Описание пока не найдено." }, color = Color(0xD9FFFFFF), style = MaterialTheme.typography.bodyLarge, maxLines = 6, overflow = TextOverflow.Ellipsis, modifier = Modifier.fillMaxWidth(0.85f))
            Row(Modifier.padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                if (best != null) PillButton("Смотреть · ${best.qualityLabel}", onClick = { vm.watch(title, best) }, icon = Icons.Filled.PlayArrow, primary = true)
                else PillButton(if (releases is Releases.Loaded) "Нет раздач" else "Ищем раздачи…", onClick = {}, icon = Icons.Filled.PlayArrow, primary = true, enabled = false)
                RoundButton(if (saved) Icons.Filled.Favorite else Icons.Filled.FavoriteBorder, if (saved) "Убрать из сохранённого" else "В сохранённое", onClick = { vm.toggleSaved(title.id) }, active = saved)
            }
        }
    }
}

@Composable
private fun Cast(people: List<Person>) {
    Column {
        Text("В ролях", color = Color.White, style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(bottom = 14.dp))
        LazyRow(Modifier.bleed(56.dp), horizontalArrangement = Arrangement.spacedBy(22.dp), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 8.dp, bottom = 8.dp)) {
            items(people, key = { it.role + it.name }) { person ->
                Column(Modifier.width(110.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Poster(person.photoUrl, Modifier.size(96.dp), CircleShape)
                    Text(person.name, color = Color.White, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 8.dp))
                    if (person.role.startsWith("Режисс")) Text("Режиссёр", color = KD.textTertiary, fontSize = 13.sp)
                }
            }
        }
    }
}

@Composable
private fun ReleaseRow(release: Release, onWatch: () -> Unit, onDownload: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0xCC1C1C1E)).padding(horizontal = 22.dp, vertical = 16.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(release.title, color = Color.White, style = MaterialTheme.typography.titleMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                if (release.recommended) TagText("Рекомендуем", color = KD.accent)
                TagText(release.qualityLabel, color = if (release.poor) KD.warning else Color.White)
                if (release.hdr) TagText("HDR")
                if (release.russianAudio) TagText(if (release.dubbed) "Дубляж" else "Рус. звук")
                if (release.subtitles) TagText("Субтитры")
                Text(listOfNotNull(release.size?.let { formatBytes(it) }, "↑ ${release.seeds}", release.source).joinToString("  ·  "), color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
            }
        }
        PillButton("Смотреть", onClick = onWatch, icon = Icons.Filled.PlayArrow, primary = release.recommended)
        PillButton("Скачать", onClick = onDownload, icon = Icons.Filled.Download)
    }
}
