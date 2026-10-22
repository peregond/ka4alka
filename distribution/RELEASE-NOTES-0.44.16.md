## Подключение телефона по QR

В настройках Windows и Mac появился QR-код: отсканируй его камерой телефона в той же домашней Wi-Fi сети. Страница откроется уже подключённой, вводить код не нужно. QR действует две минуты и используется один раз. Обычная ссылка и ручной код остаются доступны.

В браузере телефона можно поставить загрузку или раздачу на паузу и продолжить её. Если файлы удалены с компьютера, появится отдельная кнопка «Скачать заново» с подтверждением. Повторный сетевой запрос не переключает состояние обратно и не создаёт дубликат. Проверено в мобильных Chromium и WebKit с профилем iPhone.

## Поиск и рекомендации дубляжа

Рекомендованная раздача теперь пересчитывается после выбора озвучки, студии или качества. Кнопка скачивания на Mac использует ту же рекомендацию, что и список. Русский дубляж в HD и выше с небольшим положительным числом отдающих тоже может получить рекомендацию — с честной пометкой «мало отдающих». Раздачи без отдающих, экранки и неподтверждённое HD автоматически не рекомендуются.

Backrooms / «Закулисье реальности» объединены проверенным IMDb ID и годом. Поиск учитывает русские и оригинальные названия, слова в другом порядке, случайную раскладку и небольшую опечатку. Более точные совпадения показываются первыми; нерелевантные карточки отбрасываются до ограничения выдачи.

Медленный запрос по одному из названий больше не отменяет уже найденные раздачи. Для игр собираются результаты по всем известным названиям. Knaben переведён на API v2: до 150 результатов с фильтрацией трекеров на стороне индекса. Число параллельных запросов по названиям ограничено двумя.

Сокращены временные выделения памяти при поиске по большому каталогу. Исправлен случай, когда пауза сразу после последнего скачанного блока могла потерять защиту от автоматического повторного скачивания удалённых файлов.

## Connect your phone by QR

Scan the QR code in Windows or Mac settings while connected to the same home Wi-Fi. Your phone opens an already paired browser page without entering a code. The QR expires in two minutes and can be used once. A plain link and manual code remain available.

Pause or resume downloads and seeding from your phone browser. Missing files get a separate **Download again** button with confirmation. Network retries neither toggle the state back nor create duplicates. Tested with mobile Chromium and WebKit using an iPhone profile.

## Search and dubbed release recommendations

Recommendations now follow your audio, studio and quality filters. The Mac download button and release list use the same selection. Russian HD dubs with a small positive seeder count can be recommended, with a clear “few seeders” label. Releases without seeders, cam recordings and unverified HD quality are not automatically recommended.

Backrooms and its Russian title share a verified IMDb identity and year. Search supports aliases, reordered words, accidental keyboard layout and small typos. Relevant results are ranked before the result limit. Slow aliases no longer discard successful source replies; game searches collect every known title. Knaben now uses API v2 with up to 150 results and tracker filtering. Alias concurrency remains limited to two.

Large catalog searches allocate less temporary memory. Pausing immediately after the last downloaded piece now retains missing-file protection.
