package cc.ka4alka.tv.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import cc.ka4alka.core.DownloadPhase
import cc.ka4alka.core.DownloadState
import cc.ka4alka.tv.AppViewModel

@Composable
fun DownloadsScreen(vm: AppViewModel) {
    val downloads by vm.downloads.collectAsStateWithLifecycleCompat()
    var deleting by remember { mutableStateOf<DownloadState?>(null) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 60.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        item { Text("Загрузки", color = Color.White, style = MaterialTheme.typography.headlineLarge, modifier = Modifier.padding(bottom = 10.dp)) }
        if (downloads.isEmpty()) item { Message("Здесь пусто", "Выбери фильм, нажми «Скачать» или «Смотреть» — раздача появится в этом списке и продолжит скачиваться.") }
        items(downloads, key = { it.record.id }) { state ->
            DownloadRow(
                state,
                onWatch = { vm.watchDownload(state) },
                onToggle = { if (state.phase == DownloadPhase.Paused) vm.resume(state.record.id) else vm.pause(state.record.id) },
                onDelete = { deleting = state },
            )
        }
    }
    deleting?.let { state ->
        Dialog(onDismissRequest = { deleting = null }) {
            Column(Modifier.clip(RoundedCornerShape(24.dp)).background(KD.surface).padding(32.dp), verticalArrangement = Arrangement.spacedBy(18.dp)) {
                Text("Удалить загрузку?", color = Color.White, style = MaterialTheme.typography.headlineSmall)
                Text(state.record.title, color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge, maxLines = 2)
                Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    PillButton("Удалить и файлы", onClick = { vm.remove(state.record.id, true); deleting = null }, primary = true)
                    PillButton("Оставить файлы", onClick = { vm.remove(state.record.id, false); deleting = null })
                    PillButton("Отмена", onClick = { deleting = null })
                }
            }
        }
    }
}

@Composable
private fun DownloadRow(state: DownloadState, onWatch: () -> Unit, onToggle: () -> Unit, onDelete: () -> Unit) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(KD.surface).padding(18.dp), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.CenterVertically) {
        Poster(state.record.poster, Modifier.width(76.dp).height(114.dp), RoundedCornerShape(10.dp))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(state.record.title, color = Color.White, style = MaterialTheme.typography.titleLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(state.record.releaseTitle, color = KD.textTertiary, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
            LinearProgressIndicator(progress = { state.progress }, modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)), color = KD.accent, trackColor = KD.surfaceHigh)
            Text(statusLine(state), color = if (state.phase == DownloadPhase.Error) KD.danger else KD.textSecondary, style = MaterialTheme.typography.bodyMedium, maxLines = 1)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            PillButton("Смотреть", onClick = onWatch, icon = Icons.Filled.PlayArrow, primary = true)
            RoundButton(if (state.phase == DownloadPhase.Paused) Icons.Filled.PlayArrow else Icons.Filled.Pause, if (state.phase == DownloadPhase.Paused) "Продолжить" else "Пауза", onClick = onToggle)
            RoundButton(Icons.Filled.Delete, "Удалить", onClick = onDelete)
        }
    }
}

private fun statusLine(state: DownloadState): String = when (state.phase) {
    DownloadPhase.Starting -> "Получаем список файлов…"
    DownloadPhase.Checking -> "Проверяем файлы…"
    DownloadPhase.Paused -> "На паузе · ${(state.progress * 100).toInt()}% из ${formatBytes(state.totalBytes)}"
    DownloadPhase.Done -> "Готово · ${formatBytes(state.totalBytes)}"
    DownloadPhase.Error -> state.error ?: "Ошибка"
    DownloadPhase.Downloading -> listOf("${(state.progress * 100).toInt()}% из ${formatBytes(state.totalBytes)}", formatSpeed(state.downloadRate), "${state.peers} пиров", formatEta(state.etaSeconds)).filter { it.isNotBlank() }.joinToString(" · ")
}
