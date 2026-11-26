package cc.ka4alka.tv.ui

import android.view.View
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.ui.PlayerView
import cc.ka4alka.tv.AppViewModel
import cc.ka4alka.tv.Playback
import kotlinx.coroutines.delay

/** Everything between "Смотреть" and the picture: finding sources, choosing an episode, then the player itself. */
@Composable
fun PlayerScreen(vm: AppViewModel) {
    val playback by vm.playback.collectAsStateWithLifecycleCompat()
    Box(Modifier.fillMaxSize().background(Color.Black)) {
        when (val state = playback) {
            Playback.Idle -> Unit
            is Playback.Preparing -> Preparing(state)
            is Playback.ChooseFile -> ChooseFile(vm, state)
            is Playback.Playing -> VideoPlayer(vm, state)
            is Playback.Failed -> Column(Modifier.align(Alignment.Center).padding(56.dp), verticalArrangement = Arrangement.spacedBy(18.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Не получилось начать просмотр", color = Color.White, style = MaterialTheme.typography.headlineMedium)
                Text(state.message, color = KD.textSecondary, style = MaterialTheme.typography.bodyLarge)
                PillButton("Назад", onClick = { vm.back() }, primary = true)
            }
        }
    }
}

@Composable
private fun Preparing(state: Playback.Preparing) {
    Column(Modifier.fillMaxSize().padding(56.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        CircularProgressIndicator(color = KD.accent, modifier = Modifier.size(64.dp))
        Text(state.title, color = Color.White, style = MaterialTheme.typography.headlineMedium, modifier = Modifier.padding(top = 28.dp))
        Text(state.release, color = KD.textTertiary, style = MaterialTheme.typography.bodyMedium, maxLines = 2, modifier = Modifier.padding(top = 6.dp))
        Text(
            if (state.peers > 0) "Подключились к ${state.peers} пирам. Получаем начало файла…" else "Ищем источники раздачи… ${state.seconds} с",
            color = KD.textSecondary, style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 18.dp),
        )
        Text("Назад — отменить просмотр. Скачивание останется в «Загрузках».", color = KD.textTertiary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 10.dp))
    }
}

@Composable
private fun ChooseFile(vm: AppViewModel, state: Playback.ChooseFile) {
    LazyColumn(Modifier.fillMaxSize().padding(56.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item { Text("Что включить?", color = Color.White, style = MaterialTheme.typography.headlineLarge, modifier = Modifier.padding(bottom = 6.dp)) }
        item { Text(state.title.title, color = KD.textSecondary, style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(bottom = 14.dp)) }
        items(state.files, key = { it.index }) { file ->
            Focusable(onClick = { vm.chooseFile(state, file) }, modifier = Modifier.fillMaxWidth(), focusedScale = 1.02f) { focused ->
                Row(Modifier.fillMaxWidth().background(if (focused) KD.surfaceHigh else KD.surface).padding(horizontal = 22.dp, vertical = 16.dp), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(file.name.substringAfterLast('/'), color = Color.White, style = MaterialTheme.typography.titleMedium, maxLines = 1, modifier = Modifier.weight(1f))
                    Text(formatBytes(file.size), color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
                    if (file.progress >= 0.999f) TagText("Скачано", color = KD.success) else if (file.progress > 0f) TagText("${(file.progress * 100).toInt()}%")
                }
            }
        }
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun BoxScope.VideoPlayer(vm: AppViewModel, state: Playback.Playing) {
    val context = LocalContext.current
    val downloads by vm.downloads.collectAsStateWithLifecycleCompat()
    var buffering by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    val player = remember(state.url) {
        val data = DefaultHttpDataSource.Factory().setConnectTimeoutMs(15_000).setReadTimeoutMs(75_000)
        // The file is still arriving, so the player may ask for a part that is a few seconds away: wait longer before giving up.
        val load = DefaultLoadControl.Builder().setBufferDurationsMs(20_000, 60_000, 2_500, 5_000).build()
        ExoPlayer.Builder(context).setMediaSourceFactory(DefaultMediaSourceFactory(data)).setLoadControl(load).build().apply {
            setMediaItem(MediaItem.fromUri(state.url))
            prepare()
            val resume = vm.position(state.resumeKey)
            if (resume > 15_000) seekTo(resume - 5_000)
            playWhenReady = true
        }
    }
    DisposableEffect(player) {
        val listener = object : Player.Listener {
            override fun onPlaybackStateChanged(playbackState: Int) { buffering = playbackState == Player.STATE_BUFFERING }
            override fun onPlayerError(e: PlaybackException) { error = "Не удалось воспроизвести файл (${e.errorCodeName}). Формат или звук могут не поддерживаться этим телевизором." }
        }
        player.addListener(listener)
        onDispose {
            vm.savePosition(state.resumeKey, if (player.playbackState == Player.STATE_ENDED) 0 else player.currentPosition)
            player.removeListener(listener)
            player.release()
        }
    }
    LaunchedEffect(player) { while (true) { delay(10_000); if (player.isPlaying) vm.savePosition(state.resumeKey, player.currentPosition) } }

    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            PlayerView(ctx).apply {
                this.player = player
                useController = true
                controllerShowTimeoutMs = 4000
                setShowBuffering(PlayerView.SHOW_BUFFERING_NEVER)
                isFocusable = true
                isFocusableInTouchMode = true
                keepScreenOn = true
                post { requestFocus() }
                setBackgroundColor(android.graphics.Color.BLACK)
                visibility = View.VISIBLE
            }
        },
    )
    val download = downloads.firstOrNull { it.record.id == state.downloadId }
    if (buffering && error == null) {
        Column(Modifier.align(Alignment.Center).clip(RoundedCornerShape(20.dp)).background(Color(0xCC000000)).padding(28.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
            CircularProgressIndicator(color = KD.accent, modifier = Modifier.size(48.dp))
            Text("Подгружаем видео…", color = Color.White, style = MaterialTheme.typography.titleMedium)
            if (download != null) Text("${formatSpeed(download.downloadRate)} · ${download.peers} пиров · скачано ${(download.progress * 100).toInt()}%", color = KD.textSecondary, style = MaterialTheme.typography.bodyMedium)
        }
    }
    error?.let {
        Column(Modifier.align(Alignment.Center).clip(RoundedCornerShape(20.dp)).background(Color(0xE6000000)).padding(32.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text(it, color = Color.White, style = MaterialTheme.typography.titleMedium)
            PillButton("Назад", onClick = { vm.back() }, primary = true)
        }
    }
}
