package cc.ka4alka.tv.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import cc.ka4alka.core.Library
import cc.ka4alka.core.Search
import cc.ka4alka.core.Section
import cc.ka4alka.core.Shelves
import cc.ka4alka.core.Title
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.Screen
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

private val genreChips = listOf<Pair<String?, String>>(
    null to "Все", "boevik" to "Боевик", "komediia" to "Комедия", "triller" to "Триллер", "fantastika" to "Фантастика",
    "uzhasy" to "Ужасы", "drama" to "Драма", "kriminal" to "Криминал", "multfilm" to "Мультфильм",
    "semeinyi" to "Семейный", "fentezi" to "Фэнтези", "detektiv" to "Детектив", "melodrama" to "Мелодрама",
    "prikliucheniia" to "Приключения", "istoriia" to "История", "biografiia" to "Биография", "dokumentalnyi" to "Документальный",
)

/** Movies or Series: the whole library of that kind in a grid, with a genre filter above it. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun BrowseScreen(vm: AppViewModel, library: Library, section: Section) {
    var genre by rememberSaveable(section) { mutableStateOf<String?>(null) }
    val items = remember(library, section, genre) { Shelves.browse(library, section, genre) }
    LazyVerticalGrid(
        columns = GridCells.Adaptive(176.dp), modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 60.dp),
        horizontalArrangement = Arrangement.spacedBy(20.dp), verticalArrangement = Arrangement.spacedBy(26.dp),
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) {
            Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
                Text(section.label, color = Color.White, style = MaterialTheme.typography.headlineLarge)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(vertical = 8.dp)) {
                    items(genreChips) { (key, label) -> Chip(label, selected = genre == key, onClick = { genre = key }) }
                }
            }
        }
        if (items.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) { Message("Ничего не нашлось", "Для этого жанра в библиотеке пока нет названий.") }
        items(items, key = { it.id }) { title -> PosterCard(title, onClick = { vm.open(Screen.Detail(title.id)) }) }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun SavedScreen(vm: AppViewModel, library: Library) {
    val saved by vm.saved.collectAsStateWithLifecycleCompat()
    val items = remember(library, saved) { library.items.filter { it.id in saved } }
    LazyVerticalGrid(
        columns = GridCells.Adaptive(176.dp), modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 60.dp),
        horizontalArrangement = Arrangement.spacedBy(20.dp), verticalArrangement = Arrangement.spacedBy(26.dp),
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) { Text("Сохранённое", color = Color.White, style = MaterialTheme.typography.headlineLarge, modifier = Modifier.padding(bottom = 8.dp)) }
        if (items.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) { Message("Пока пусто", "Нажми на сердечко в карточке фильма или сериала, и он появится здесь.") }
        items(items, key = { it.id }) { title -> PosterCard(title, onClick = { vm.open(Screen.Detail(title.id)) }) }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun SearchScreen(vm: AppViewModel, library: Library) {
    var query by rememberSaveable { mutableStateOf("") }
    var results by remember { mutableStateOf<List<Title>>(emptyList()) }
    var section by rememberSaveable { mutableStateOf<Section?>(null) }
    LaunchedEffect(query, section, library) {
        delay(200)
        results = withContext(Dispatchers.Default) { Search.run(library.items, query, 90, section) }
    }
    LazyVerticalGrid(
        columns = GridCells.Adaptive(176.dp), modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 60.dp),
        horizontalArrangement = Arrangement.spacedBy(20.dp), verticalArrangement = Arrangement.spacedBy(26.dp),
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) {
            Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Text("Поиск", color = Color.White, style = MaterialTheme.typography.headlineLarge)
                Row(
                    Modifier.fillMaxWidth().clip(androidx.compose.foundation.shape.RoundedCornerShape(16.dp)).background(KD.surface).padding(horizontal = 20.dp, vertical = 16.dp),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    Icon(Icons.Filled.Search, null, tint = KD.textSecondary, modifier = Modifier.size(28.dp))
                    Box(Modifier.weight(1f)) {
                        if (query.isEmpty()) Text("Название фильма или сериала", color = KD.textTertiary, style = MaterialTheme.typography.titleLarge)
                        BasicTextField(
                            value = query, onValueChange = { query = it }, singleLine = true,
                            textStyle = MaterialTheme.typography.titleLarge.copy(color = Color.White),
                            cursorBrush = SolidColor(KD.accent),
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                            keyboardActions = KeyboardActions(onSearch = { }),
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Chip("Всё", section == null, { section = null })
                    Chip("Фильмы", section == Section.Movies, { section = Section.Movies })
                    Chip("Сериалы", section == Section.Series, { section = Section.Series })
                }
            }
        }
        if (query.isBlank()) item(span = { GridItemSpan(maxLineSpan) }) { Message("Что смотрим?", "Введи название по-русски или по-английски. Голосовой ввод работает через микрофон на пульте.") }
        else if (results.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) { Message("Ничего не нашлось", "Проверь написание или попробуй другое название.") }
        items(results, key = { it.id }) { title -> PosterCard(title, onClick = { vm.open(Screen.Detail(title.id)) }) }
    }
}
