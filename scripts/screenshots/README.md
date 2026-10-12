# Скриншоты опубликованного приложения

Workflow `Capture published application screenshots` запускается вручную с номером опубликованной версии (по умолчанию 0.42.0). Он проверяет подпись и содержимое пакета и снимает настоящий WPF-интерфейс опубликованной DLL в Windows. Приложение из текущей ветки не пересобирается.

Драйвер открывает фильмы, сериалы, каталог Steam в обеих темах, карточку игры из загруженного каталога, загрузки и настройки. Названия, изображения и описание игры поступают из живых источников. Очередь в изолированном профиле пуста; задания и результаты скачивания не инсценируются.

`capture-info.json` содержит версию, SHA-256 DLL и архива, список снимков и число загруженных постеров. После захвата PNG нужно визуально проверить перед сохранением в документации. Временный черновик `docs-preview-<run-id>` можно удалить после сохранения снимков.

```powershell
dotnet build scripts/screenshots/CaptureReleaseScreenshot.csproj -c Release -p:ReleaseDirectory=C:\Kachalka-0.42
$env:KACHALKA_SCREENSHOT_VERSION='0.42.0'
dotnet scripts/screenshots/bin/Release/net10.0-windows/CaptureReleaseScreenshot.dll C:\Kachalka-0.42 C:\screenshots
```

Нужны Windows и .NET 10 SDK. Профиль изолирован; в GitHub передаются только PNG и очищенные метаданные. Workflow `Publish reviewed release screenshots` загружает проверенные снимки из `distribution/assets/0.42/` в соответствующий опубликованный релиз; установщик и файлы обновления он не меняет.
