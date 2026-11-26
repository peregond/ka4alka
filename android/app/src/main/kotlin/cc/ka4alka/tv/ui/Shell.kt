package cc.ka4alka.tv.ui

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Movie
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Tv
import androidx.compose.material3.CircularProgressIndicator
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
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import cc.ka4alka.core.Section
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.LibraryState
import cc.ka4alka.tv.Screen
import cc.ka4alka.tv.isTopLevel

private data class Destination(val screen: Screen, val label: String, val icon: ImageVector)

private val destinations = listOf(
    Destination(Screen.Home, "Главная", Icons.Filled.Home),
    Destination(Screen.Movies, "Фильмы", Icons.Filled.Movie),
    Destination(Screen.Series, "Сериалы", Icons.Filled.Tv),
    Destination(Screen.Search, "Поиск", Icons.Filled.Search),
    Destination(Screen.Saved, "Сохранённое", Icons.Filled.Favorite),
    Destination(Screen.Downloads, "Загрузки", Icons.Filled.Download),
    Destination(Screen.Settings, "Настройки", Icons.Filled.Settings),
)

/** Navigation rail on the left (icons, opening to labels while it has focus) and the current screen to the right of it. */
@Composable
fun AppShell(vm: AppViewModel) {
    val library by vm.library.collectAsStateWithLifecycleCompat()
    val screen = vm.current
    if (screen is Screen.Player) { PlayerScreen(vm); return }
    Box(Modifier.fillMaxSize().background(Color.Black)) {
        Box(Modifier.fillMaxSize().padding(start = 92.dp)) {
            when (val state = library) {
                LibraryState.Loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(18.dp)) {
                        CircularProgressIndicator(color = KD.accent, modifier = Modifier.size(56.dp))
                        Text("Загружаем библиотеку…", color = KD.textSecondary, style = MaterialTheme.typography.titleMedium)
                    }
                }
                is LibraryState.Failed -> when (screen) {
                    Screen.Settings -> SettingsScreen(vm)
                    Screen.Downloads -> DownloadsScreen(vm)
                    else -> Message("Нет связи с библиотекой", state.reason + "\nПроверь интернет — каталог загрузится сам, когда связь появится. Перезапусти приложение, чтобы попробовать сразу.")
                }
                is LibraryState.Ready -> when (screen) {
                    Screen.Home -> HomeScreen(vm, state.library)
                    Screen.Movies -> BrowseScreen(vm, state.library, Section.Movies)
                    Screen.Series -> BrowseScreen(vm, state.library, Section.Series)
                    Screen.Search -> SearchScreen(vm, state.library)
                    Screen.Saved -> SavedScreen(vm, state.library)
                    Screen.Downloads -> DownloadsScreen(vm)
                    Screen.Settings -> SettingsScreen(vm)
                    is Screen.Detail -> DetailScreen(vm, state.library, screen.id)
                    Screen.Player -> Unit
                }
            }
        }
        if (screen.isTopLevel) Rail(vm, screen)
    }
}

@Composable
private fun Rail(vm: AppViewModel, current: Screen) {
    var expanded by remember { mutableStateOf(false) }
    val width by animateDpAsState(if (expanded) 280.dp else 92.dp, label = "railWidth")
    Row(Modifier.fillMaxHeight().width(width).background(Brush.horizontalGradient(listOf(if (expanded) Color(0xF2000000) else Color(0xB3000000), Color.Transparent))).onFocusChanged { expanded = it.hasFocus }) {
        Column(Modifier.fillMaxHeight().width(280.dp).padding(start = 14.dp, top = 28.dp), verticalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterVertically)) {
            for (item in destinations) {
                val selected = item.screen == current
                Focusable(onClick = { vm.open(item.screen) }, modifier = Modifier.width(if (expanded) 240.dp else 64.dp), shape = RoundedCornerShape(32.dp), focusedScale = 1.0f) { focused ->
                    Row(
                        Modifier.height(60.dp).background(if (focused) Color.White else if (selected) Color(0x33FFFFFF) else Color.Transparent).padding(horizontal = 16.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(18.dp),
                    ) {
                        Icon(item.icon, item.label, tint = if (focused) KD.accentInk else if (selected) KD.accent else Color.White, modifier = Modifier.size(32.dp))
                        if (expanded) Text(item.label, color = if (focused) KD.accentInk else Color.White, style = MaterialTheme.typography.titleMedium, maxLines = 1)
                    }
                }
            }
        }
    }
}
