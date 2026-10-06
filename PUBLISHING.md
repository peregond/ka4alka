# Публикация «Качалки» 0.19.0 и следующих версий

Бинарные выпуски размещаются в peregond/ka4alka. До публикации проверьте, что этот репозиторий доступен вашей учётной записи. Файлы в dist/release-<VERSION> являются подготовленным локальным пакетом, а не опубликованным релизом.

## Сборка на Windows

Автоматическая подготовка выпуска доступна в GitHub Actions: workflow `Prepare signed Windows release`. Он проверяет установку и обновление на Windows, собирает EXE/ZIP, подписывает метаданные и загружает проверенные файлы в черновик Release. Для него нужен repository secret `KACHALKA_UPDATE_SIGNING_KEY_PEM` с закрытым PEM-ключом, соответствующим встроенному публичному ключу. Секрет не используется в проверках pull request. Запустите workflow с номером версии, совпадающим с `native/Kachalka.csproj`; опубликованный выпуск он не перезаписывает. Сначала загрузка читается обратно и проверяется по SHA-256 и подписи, затем готовый черновик можно публиковать.

Нужны .NET 10 SDK и NSIS 3.11 или новее. Закрытый ключ выпуска храните отдельно от Git; публичный ключ встроен из update-core/update-public.pem. Для первой 0.19 ключ создан в игнорируемом .tools/update-signing/private.pem текущей облачной рабочей папки. Сохраните его в защищённом хранилище для будущих выпусков; без этого же ключа подписанные обновления не будут приняты. Не коммитьте ключ и не добавляйте его в релиз. Другой публичный ключ требует отдельного плана смены доверия.

```powershell
./build-release.ps1 -Version 0.19.0 -SigningKeyPath <PRIVATE_PEM_PATH> -MakeNsis 'C:/Program Files (x86)/NSIS/makensis.exe'
```

Скрипт выполняет Windows-интеграционные проверки, публикует приложение и автономный установщик обновлений, запускает проверки ядра обновлений, создаёт ZIP, подписанные метаданные и EXE-установщик. Версия DLL должна совпадать с версией выпуска. Пакет нельзя публиковать, если проверки не прошли. Закрытый ключ проверяется на соответствие встроенному публичному до подписания.

Для подготовленного ZIP отдельно:

```powershell
./package-release.ps1 -Version 0.19.0 -ArchivePath dist/Kachalka-0.19.zip -SigningKeyPath <PRIVATE_PEM_PATH>
dotnet run --project update-tests/Kachalka.UpdateTests.csproj -c Release -- --verify-release dist/release-0.19.0
```

## Выпуск на GitHub

1. Проверьте на Windows установку/удаление и запуск приложения, кнопки обновления, сохранение очереди и обновление между двумя тестовыми версиями. Linux-кросс-компиляция этих проверок не заменяет.
2. Создайте черновик релиза с тегом версии и загрузите Kachalka-Setup-<VERSION>.exe, Kachalka-win-x64.zip, latest.json, latest.sig, SHA256SUMS.txt.
3. Проверьте загруженные файлы и подпись, затем опубликуйте релиз и отметьте его последним.
4. Проверьте скачивание метаданных и архива без авторизации. Не меняйте байты JSON после подписания и не заменяйте выпущенный ZIP под старой версией.

```powershell
gh release create v0.19.0 --repo peregond/ka4alka --draft --title 'Качалка 0.19.0' --notes-file distribution/RELEASE-NOTES-0.19.md
gh release upload v0.19.0 --repo peregond/ka4alka dist/release-0.19.0/Kachalka-Setup-0.19.0.exe dist/release-0.19.0/Kachalka-win-x64.zip dist/release-0.19.0/latest.json dist/release-0.19.0/latest.sig dist/release-0.19.0/SHA256SUMS.txt
gh release edit v0.19.0 --repo peregond/ka4alka --draft=false --latest
```

Подпись метаданных защищает автообновления, но не заменяет Authenticode-подпись EXE для Windows SmartScreen. EXE 0.19.0 пока не имеет подписи издателя.
