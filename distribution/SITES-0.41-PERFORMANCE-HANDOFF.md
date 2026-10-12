Обновить существующий каталог https://ka4alka-online-new.peregon.chatgpt.site/ из ветки claude/quirky-hopper-1vlmj6 репозитория peregond/ka4alka. Сохранить текущий проект, базу D1, привязку DB, серверные секреты и адрес публикации.

Перенести серверные изменения 0.41:

- web-index/lib/release-cache.ts (новый файл)
- web-index/lib/release-sources.ts
- web-index/lib/release-search.ts
- web-index/app/api/releases/route.ts
- web-index/tests/release-performance.mjs (новый тест)

Это объединение одновременных поисков, короткий ограниченный кэш полезных ответов, параллельные запросы RuTor и начало русскоязычного поиска одновременно с получением метаданных. Контракт API и структура базы не меняются.

Проверить node tests/release-performance.mjs, node tests/title-lookup.mjs, tsc --noEmit и npm run build. Опубликовать существующий сайт и проверить публичные /api/health, /api/title для фильма и сериала и /api/releases. Значения секретов не выводить и не включать в исходники.
