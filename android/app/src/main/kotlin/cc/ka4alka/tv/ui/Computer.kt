package cc.ka4alka.tv.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
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
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Dialpad
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.Screen

/** One numbered step with an icon, used by the page and by the hint. */
@Composable
fun Step(number: Int, icon: ImageVector, title: String, text: String, compact: Boolean = false) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(if (compact) 44.dp else 52.dp).clip(CircleShape).background(KD.secondaryContainer), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = KD.onSecondaryContainer, modifier = Modifier.size(if (compact) 24.dp else 28.dp))
        }
        Column(Modifier.weight(1f)) {
            Text("$number. $title", color = Color.White, style = if (compact) MaterialTheme.typography.titleSmall else MaterialTheme.typography.titleMedium)
            Text(text, color = KD.textSecondary, style = if (compact) MaterialTheme.typography.bodySmall else MaterialTheme.typography.bodyMedium)
        }
    }
}

/** A text box the remote can focus; the system keyboard opens on OK. */
@Composable
fun FieldBox(value: String, onChange: (String) -> Unit, hint: String, modifier: Modifier = Modifier, numeric: Boolean = false) {
    var focused by remember { mutableStateOf(false) }
    Box(
        modifier.clip(RoundedCornerShape(16.dp)).background(KD.surface)
            .border(if (focused) androidx.compose.foundation.BorderStroke(3.dp, Color.White) else androidx.compose.foundation.BorderStroke(0.dp, Color.Transparent), RoundedCornerShape(16.dp))
            .padding(horizontal = 20.dp, vertical = 14.dp),
    ) {
        if (value.isEmpty()) Text(hint, color = KD.textTertiary, style = MaterialTheme.typography.titleMedium)
        BasicTextField(
            value = value, onValueChange = onChange, singleLine = true,
            textStyle = MaterialTheme.typography.titleMedium.copy(color = Color.White), cursorBrush = SolidColor(KD.accent),
            keyboardOptions = KeyboardOptions(keyboardType = if (numeric) KeyboardType.Number else KeyboardType.Uri),
            modifier = Modifier.fillMaxWidth().onFocusChanged { focused = it.isFocused },
        )
    }
}

@Composable
fun ComputerScreen(vm: AppViewModel) {
    val state by vm.computer.collectAsStateWithLifecycleCompat()
    var address by rememberSaveable { mutableStateOf("") }
    var code by rememberSaveable { mutableStateOf("") }
    LaunchedEffect(Unit) { if (!state.linked) vm.scanForComputers() }
    state.choose?.let { ComputerChoiceDialog(vm, it) }
    LaunchedEffect(state.found) { if (address.isEmpty() && state.found.size == 1) address = state.found[0] }

    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 80.dp), verticalArrangement = Arrangement.spacedBy(22.dp)) {
        item {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Компьютер", color = Color.White, style = MaterialTheme.typography.headlineLarge)
                Text("Скачивайте на компьютер, а смотрите на телевизоре: файлы лягут в его папку загрузок, телевизор не нагружается.", color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge)
            }
        }
        if (state.linked && !state.expired) {
            item { LinkedCard(vm, state.host.orEmpty()) }
            item { Text("Загрузки на компьютере", color = Color.White, style = MaterialTheme.typography.headlineSmall) }
            if (state.downloads.isEmpty()) item { Text("Пока пусто. На карточке фильма нажми «На ПК» у нужной раздачи. Готовые фильмы можно смотреть прямо отсюда.", color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge) }
            items(state.downloads, key = { it.id }) { row ->
                Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(KD.surface).padding(20.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(row.title, color = Color.White, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        LinearProgressIndicator(progress = { (row.progress / 100.0).toFloat().coerceIn(0f, 1f) }, modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)), color = KD.accent, trackColor = KD.surfaceHigh)
                        Text(listOf(row.status, "${row.progress.toInt()}%", if (row.downloadRate > 0) formatSpeed(row.downloadRate.toInt()) else "").filter { it.isNotBlank() }.joinToString(" · "), color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
                    }
                    if (row.complete) PillButton("Смотреть", onClick = { vm.watchOnComputer(row) }, icon = Icons.Filled.PlayArrow, primary = true)
                    else RoundButton(if (row.paused) Icons.Filled.PlayArrow else Icons.Filled.Pause, if (row.paused) "Продолжить" else "Пауза", onClick = { vm.computerControl(row.id, !row.paused) })
                }
            }
        } else {
            if (state.expired) item { Text("Подключение истекло (оно живёт ограниченное время). Покажи на компьютере новый код и введи его ниже.", color = KD.warning, style = MaterialTheme.typography.bodyLarge) }
            item {
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(KD.surface).padding(28.dp), verticalArrangement = Arrangement.spacedBy(18.dp)) {
                    Text("Как подключить", color = Color.White, style = MaterialTheme.typography.headlineSmall)
                    Step(1, Icons.Filled.Settings, "Включите доступ на компьютере", "Откройте Качалку на компьютере: Настройки → Устройства в сети → «Подключить телефон». Появятся адрес и шестизначный код (он действует две минуты).")
                    Step(2, Icons.Filled.Wifi, "Одна сеть", "Телевизор и компьютер должны быть в одной домашней сети Wi-Fi или по кабелю к одному роутеру.")
                    Step(3, Icons.Filled.Dialpad, "Введите код здесь", "Ниже выберите найденный компьютер (или впишите адрес) и введите код с экрана компьютера.")
                }
            }
            item {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(if (state.scanning) "Ищем компьютеры в сети…" else if (state.found.isEmpty()) "Найденные компьютеры" else "Найдено:", color = Color.White, style = MaterialTheme.typography.titleMedium)
                        RoundButton(Icons.Filled.Refresh, "Искать ещё раз", onClick = { vm.scanForComputers() })
                    }
                    if (state.found.isNotEmpty()) LazyRow(Modifier.bleed(56.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(horizontal = 56.dp, vertical = 8.dp)) {
                        items(state.found) { ip -> Chip(ip, selected = address == ip, onClick = { address = ip }) }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                        FieldBox(address, { address = it }, "Адрес, например 192.168.1.25", Modifier.width(420.dp))
                        FieldBox(code, { code = it.filter(Char::isDigit).take(6) }, "Код из 6 цифр", Modifier.width(260.dp), numeric = true)
                        PillButton(if (state.busy) "Подключаем…" else "Подключить", onClick = { if (!state.busy) vm.linkComputer(address, code) }, icon = Icons.Filled.Computer, primary = true)
                    }
                }
            }
        }
        state.message?.let { item { Text(it, color = if (state.linked) KD.success else KD.warning, style = MaterialTheme.typography.bodyLarge) } }
    }
}

/** The files of a finished download, when there is more than one to play (a season, say). */
@Composable
fun ComputerChoiceDialog(vm: AppViewModel, choice: cc.ka4alka.tv.ComputerChoice) {
    Dialog(onDismissRequest = { vm.dismissComputerChoice() }) {
        Column(Modifier.clip(RoundedCornerShape(28.dp)).background(KD.surface).padding(28.dp).width(720.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Что включить?", color = Color.White, style = MaterialTheme.typography.headlineSmall)
            Text(choice.title, color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium, maxLines = 1)
            LazyColumn(Modifier.height(340.dp), verticalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(vertical = 6.dp)) {
                items(choice.files, key = { it.index }) { file ->
                    Focusable(onClick = { vm.chooseComputerFile(choice, file) }, modifier = Modifier.fillMaxWidth(), focusedScale = 1.02f) { focused ->
                        Row(Modifier.fillMaxWidth().background(if (focused) KD.surfaceHigh else KD.background).padding(horizontal = 18.dp, vertical = 14.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                            Text(file.name.substringAfterLast('/').substringAfterLast('\\'), color = Color.White, style = MaterialTheme.typography.titleSmall, maxLines = 1, modifier = Modifier.weight(1f))
                            Text(formatBytes(file.size), color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun LinkedCard(vm: AppViewModel, host: String) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(KD.surface).padding(24.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(20.dp)) {
        Box(Modifier.size(56.dp).clip(CircleShape).background(KD.secondaryContainer), contentAlignment = Alignment.Center) { Icon(Icons.Filled.Computer, null, tint = KD.onSecondaryContainer, modifier = Modifier.size(30.dp)) }
        Column(Modifier.weight(1f)) {
            Text("Компьютер подключён", color = Color.White, style = MaterialTheme.typography.titleLarge)
            Text(host, color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
        }
        PillButton("Отключить", onClick = { vm.unlinkComputer() })
    }
}

/** The one-time hint: you can download on a computer instead. Easy to close, never repeated once dismissed for good. */
@Composable
fun LinkTipDialog(vm: AppViewModel) {
    Dialog(onDismissRequest = { vm.closeLinkTip() }) {
        Column(Modifier.clip(RoundedCornerShape(28.dp)).background(KD.surface).padding(32.dp).width(640.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                Icon(Icons.Filled.Download, null, tint = KD.accent, modifier = Modifier.size(32.dp))
                Text("Скачивайте на компьютер", color = Color.White, style = MaterialTheme.typography.headlineSmall)
            }
            Text("Если рядом есть компьютер с Качалкой, фильм можно скачать на него, а на телевизоре только смотреть. Это экономит место и не нагружает телевизор.", color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
            Step(1, Icons.Filled.Settings, "На компьютере", "Настройки → Устройства в сети → «Подключить телефон».", compact = true)
            Step(2, Icons.Filled.Dialpad, "На телевизоре", "Раздел «Компьютер» → выберите найденный компьютер и введите код.", compact = true)
            Step(3, Icons.Filled.Download, "Дальше просто", "У раздачи появится кнопка «На ПК».", compact = true)
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                PillButton("Настроить", onClick = { vm.closeLinkTip(setup = true) }, primary = true)
                PillButton("Не сейчас", onClick = { vm.closeLinkTip() })
                PillButton("Больше не показывать", onClick = { vm.closeLinkTip(hideForever = true) })
            }
        }
    }
}
