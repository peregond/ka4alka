package cc.ka4alka.tv.ui

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.unit.dp
import cc.ka4alka.core.Section
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.LibraryState
import cc.ka4alka.tv.Screen
import cc.ka4alka.tv.isTopLevel
import kotlinx.coroutines.delay

private data class Destination(val screen: Screen, val label: String, val icon: ImageVector)

private val destinations = listOf(
    Destination(Screen.Home, "Главная", Icons.Filled.Home),
    Destination(Screen.Movies, "Фильмы", Icons.Filled.Movie),
    Destination(Screen.Series, "Сериалы", Icons.Filled.Tv),
    Destination(Screen.Search, "Поиск", Icons.Filled.Search),
    Destination(Screen.Saved, "Сохранённое", Icons.Filled.Favorite),
    Destination(Screen.Downloads, "Загрузки", Icons.Filled.Download),
    Destination(Screen.Computer, "Компьютер", Icons.Filled.Computer),
    Destination(Screen.Settings, "Настройки", Icons.Filled.Settings),
)

/** Navigation rail on the left (icons, opening to labels while it has focus) and the current screen to the right of it. */
@Composable
fun AppShell(vm: AppViewModel) {
    val library by vm.library.collectAsStateWithLifecycleCompat()
    val screen = vm.current
    if (screen is Screen.Player) { PlayerScreen(vm); return }
    // Focus goes into the page whenever it changes, so the rail stays collapsed until the user steps left.
    val focusManager = LocalFocusManager.current
    var railFocused by remember { mutableStateOf(true) }
    val railProgress by androidx.compose.animation.core.animateFloatAsState(if (railFocused) 1f else 0f, label = "railProgress")
    val ready = library is LibraryState.Ready
    LaunchedEffect(screen, ready) { delay(500); if (railFocused) focusManager.moveFocus(FocusDirection.Right) }
    Box(Modifier.fillMaxSize().background(Color.Black).background(Brush.radialGradient(listOf(Color(0x2E5BE3B5), Color.Transparent), center = androidx.compose.ui.geometry.Offset(0f, 0f), radius = 1500f))) {
        // While the menu is open the page slides aside, shrinks a little and dims.
        Box(
            Modifier.fillMaxSize().padding(start = 84.dp).graphicsLayer {
                val shift = 70.dp.toPx() * railProgress
                translationX = shift; alpha = 1f - 0.55f * railProgress
                scaleX = 1f - 0.04f * railProgress; scaleY = scaleX
                transformOrigin = androidx.compose.ui.graphics.TransformOrigin(1f, 0.5f)
            },
        ) {
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
                    Screen.Computer -> ComputerScreen(vm)
                    else -> Message("Нет связи с библиотекой", state.reason + "\nПроверь интернет — каталог загрузится сам, когда связь появится. Перезапусти приложение, чтобы попробовать сразу.")
                }
                is LibraryState.Ready -> when (screen) {
                    Screen.Home -> HomeScreen(vm, state.library)
                    Screen.Movies -> BrowseScreen(vm, state.library, Section.Movies)
                    Screen.Series -> BrowseScreen(vm, state.library, Section.Series)
                    Screen.Search -> SearchScreen(vm, state.library)
                    Screen.Saved -> SavedScreen(vm, state.library)
                    Screen.Downloads -> DownloadsScreen(vm)
                    Screen.Computer -> ComputerScreen(vm)
                    Screen.Settings -> SettingsScreen(vm)
                    is Screen.Detail -> DetailScreen(vm, state.library, screen.id)
                    Screen.Player -> Unit
                }
            }
        }
        val tip by vm.linkTip.collectAsStateWithLifecycleCompat()
        if (tip) LinkTipDialog(vm)
        val computer by vm.computer.collectAsStateWithLifecycleCompat()
        val note = computer.message
        if (note != null && screen !is Screen.Computer) {
            LaunchedEffect(note) { delay(5000); vm.clearComputerMessage() }
            Box(Modifier.align(Alignment.BottomCenter).padding(bottom = 36.dp).clip(RoundedCornerShape(24.dp)).background(Color(0xF2232B27)).padding(horizontal = 28.dp, vertical = 16.dp)) {
                Text(note, color = Color.White, style = MaterialTheme.typography.titleSmall)
            }
        }
        // The rail stays on every page (also on a title page), so the left strip is never empty.
        Rail(vm, screen, railFocused) { railFocused = it }
    }
}

@Composable
private fun Rail(vm: AppViewModel, current: Screen, open: Boolean, onFocus: (Boolean) -> Unit) {
    val width by animateDpAsState(if (open) 250.dp else 72.dp, label = "railWidth")
    Box(
        Modifier.padding(start = 12.dp, top = 12.dp, bottom = 12.dp).fillMaxHeight().width(width)
            .clip(RoundedCornerShape(32.dp))
            .background(if (open) Color(0xF2171D1A) else Color.Transparent)
            .onFocusChanged { onFocus(it.hasFocus) },
    ) {
        Column(Modifier.fillMaxHeight().padding(horizontal = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterVertically)) {
            for (item in destinations) {
                val selected = item.screen == current
                Focusable(onClick = { vm.open(item.screen) }, modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(26.dp), focusedScale = 1.0f, ring = false) { focused ->
                    Row(
                        Modifier.fillMaxSize().background(if (focused) KD.accent else if (selected && open) KD.secondaryContainer else Color.Transparent).padding(horizontal = 14.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp),
                    ) {
                        Icon(item.icon, item.label, tint = if (focused) KD.accentInk else if (selected) KD.accent else Color.White, modifier = Modifier.size(28.dp))
                        if (open) Text(item.label, color = if (focused) KD.accentInk else if (selected) KD.onSecondaryContainer else Color.White, style = MaterialTheme.typography.titleSmall, maxLines = 1)
                    }
                }
            }
        }
    }
}
