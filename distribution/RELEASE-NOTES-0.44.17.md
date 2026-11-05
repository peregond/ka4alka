## Плавнее запуск на Windows

Чтение сохранённых данных о странах и подготовка региональных подборок перенесены в фоновый поток. Эти операции больше не блокируют обработку нажатий и перерисовку окна при запуске, в том числе после автоматического обновления, когда файлы ещё не прогреты в системном кэше.

Добавлена проверка отзывчивости окна при задержке загрузки региональных данных и проверка отмены этой загрузки. Все изменения версии 0.44.16 сохранены. Пакеты Windows и Mac выпускаются с общим номером 0.44.17; исправление интерфейса относится к Windows.

## Smoother Windows startup

Cached country metadata and regional catalog preparation now run in the background, keeping window input and painting responsive during those operations. This also helps the first launch after an automatic update, when Windows has not yet cached the files.

Regression coverage checks window responsiveness while regional loading is delayed, as well as cancellation. This release retains all changes from 0.44.16. Windows and Mac packages share version 0.44.17; the UI fix applies to Windows.
