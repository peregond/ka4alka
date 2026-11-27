package cc.ka4alka.tv.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.BuildConfig
import cc.ka4alka.tv.KachalkaApp
import cc.ka4alka.tv.UpdateUi

@Composable
fun SettingsScreen(vm: AppViewModel) {
    val context = LocalContext.current
    val prefs = remember { KachalkaApp.of(context).prefs }
    val update by vm.update.collectAsStateWithLifecycleCompat()
    var folder by remember { mutableStateOf(prefs.downloadDir().parentFile?.absolutePath) }
    var downloadLimit by remember { mutableIntStateOf(prefs.downloadLimitKbps) }
    var uploadLimit by remember { mutableIntStateOf(prefs.uploadLimitKbps) }
    var autoCheck by remember { mutableStateOf(prefs.autoCheckUpdates) }

    // Back from the system page where the user allows installing updates: carry on with the install.
    val owner = LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_RESUME) vm.resumeAfterPermission() }
        owner.lifecycle.addObserver(observer)
        onDispose { owner.lifecycle.removeObserver(observer) }
    }

    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(start = 56.dp, end = 56.dp, top = 36.dp, bottom = 80.dp), verticalArrangement = Arrangement.spacedBy(22.dp)) {
        item { Text("Настройки", color = Color.White, style = MaterialTheme.typography.headlineLarge) }

        item {
            Card("Обновление приложения", "Установлена версия ${BuildConfig.VERSION_NAME}. Новые версии проверяются по подписи и ставятся прямо из приложения.") {
                UpdateControls(vm, update)
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Chip("Проверять при запуске: " + if (autoCheck) "да" else "нет", selected = autoCheck, onClick = { autoCheck = !autoCheck; prefs.autoCheckUpdates = autoCheck })
                }
            }
        }

        item {
            Card("Куда скачивать", "Папка Ka4alka создаётся внутри выбранного накопителя. USB-диск или карта памяти появятся в списке, когда подключены.") {
                LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(vertical = 8.dp)) {
                    items(prefs.folders()) { (name, dir) ->
                        Chip("$name · ${formatBytes(dir.usableSpace)} свободно", selected = folder == dir.absolutePath, onClick = { prefs.folder = dir.absolutePath; folder = dir.absolutePath })
                    }
                }
                Text("Сейчас: ${prefs.downloadDir().absolutePath}", color = KD.textTertiary, style = MaterialTheme.typography.bodySmall)
            }
        }

        item {
            Card("Скорость", "Ограничения действуют сразу. Раздача помогает другим и ускоряет загрузку.") {
                Text("Загрузка", color = KD.textSecondary, style = MaterialTheme.typography.titleSmall)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(vertical = 8.dp)) {
                    items(listOf(0 to "Без ограничений", 2048 to "2 МБ/с", 5120 to "5 МБ/с", 10240 to "10 МБ/с", 20480 to "20 МБ/с")) { (kb, label) ->
                        Chip(label, downloadLimit == kb, { downloadLimit = kb; prefs.downloadLimitKbps = kb; vm.applyLimits() })
                    }
                }
                Text("Отдача", color = KD.textSecondary, style = MaterialTheme.typography.titleSmall)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(vertical = 8.dp)) {
                    items(listOf(0 to "Без ограничений", 256 to "256 КБ/с", 1024 to "1 МБ/с", 5120 to "5 МБ/с")) { (kb, label) ->
                        Chip(label, uploadLimit == kb, { uploadLimit = kb; prefs.uploadLimitKbps = kb; vm.applyLimits() })
                    }
                }
            }
        }

        item {
            Card("О приложении", "Качалка для Android TV · ${BuildConfig.VERSION_NAME}\nНовости: t.me/ka4alkacc · Чат поддержки: t.me/+6sT2ocaKvLxlNmRi\nФильмы можно смотреть, пока они ещё скачиваются: приложение берёт нужные части файла в первую очередь.") {}
        }
    }
}

@Composable
private fun UpdateControls(vm: AppViewModel, update: UpdateUi) {
    when (update) {
        UpdateUi.Idle -> PillButton("Проверить обновления", onClick = { vm.checkForUpdates() }, icon = Icons.Filled.Refresh, primary = true)
        UpdateUi.Checking -> Text("Проверяем…", color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge)
        UpdateUi.UpToDate -> {
            Text("У тебя последняя версия.", color = KD.success, style = MaterialTheme.typography.bodyLarge)
            PillButton("Проверить ещё раз", onClick = { vm.checkForUpdates() }, icon = Icons.Filled.Refresh)
        }
        is UpdateUi.Available -> {
            Text("Доступна версия ${update.manifest.version}${if (update.manifest.notes.isNotBlank()) ": ${update.manifest.notes}" else ""}", color = Color.White, style = MaterialTheme.typography.bodyLarge)
            PillButton("Скачать и установить", onClick = { vm.installUpdate(update.manifest) }, icon = Icons.Filled.Download, primary = true)
        }
        is UpdateUi.Downloading -> {
            Text("Скачиваем ${update.manifest.version}: ${formatBytes(update.done)} из ${formatBytes(update.total)}", color = Color.White, style = MaterialTheme.typography.bodyLarge)
            LinearProgressIndicator(progress = { if (update.total > 0) update.done.toFloat() / update.total else 0f }, modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), color = KD.accent, trackColor = KD.surfaceHigh)
        }
        is UpdateUi.NeedsPermission -> {
            Text("Разреши Качалке устанавливать приложения: откроется системная страница, включи переключатель и вернись назад.", color = KD.warning, style = MaterialTheme.typography.bodyLarge)
            PillButton("Продолжить установку", onClick = { vm.continueInstall(update.manifest, update.apk) }, primary = true)
        }
        is UpdateUi.Installing -> Text("Подтверди установку в окне системы. Приложение перезапустится само, когда она закончится.", color = Color.White, style = MaterialTheme.typography.bodyLarge)
        is UpdateUi.Failed -> {
            Text("Не получилось: ${update.reason}", color = KD.danger, style = MaterialTheme.typography.bodyLarge)
            PillButton("Повторить", onClick = { vm.checkForUpdates() }, icon = Icons.Filled.Refresh, primary = true)
        }
    }
}

@Composable
private fun Card(title: String, subtitle: String, content: @Composable () -> Unit) {
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(KD.surface).padding(28.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Text(title, color = Color.White, style = MaterialTheme.typography.headlineSmall)
        Text(subtitle, color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
        content()
    }
}
