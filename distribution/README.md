# Качалка · Windows и Mac

Нативное приложение для Windows и macOS: фильмы, сериалы и игры для Windows, поиск раздач и общая очередь torrent- и magnet-загрузок. Windows использует WPF, Mac — SwiftUI и AppKit. Общий движок и единый выпуск для обеих платформ.

**[Скачать последний выпуск для своей платформы](https://github.com/peregond/ka4alka/releases/latest)** · [Установка и сборка Mac](MACOS.md)

[Все выпуски](https://github.com/peregond/ka4alka/releases) · [Описание возможностей](../README.md) · [Новости](https://t.me/ka4alkacc) · [Поддержка](https://t.me/+6sT2ocaKvLxlNmRi)

## Скриншоты 0.44.27

Настоящие окна опубликованного приложения. Русский и английский интерфейсы сняты отдельно. Галерея включает «Актуальное» с курируемыми подборками и обновлённые баннеры; версии, SHA-256 пакетов и изображений сохранены в [метаданных Windows](assets/0.44.27/capture-info.json) и [Mac](assets/macos/0.44.27/capture-info.json).

| Раздел | Windows | Mac |
| --- | --- | --- |
| Актуальное | [Русский](assets/0.44.27/ru/featured-dark.webp) · [English](assets/0.44.27/en/featured-dark.webp) | [Русский](assets/macos/0.44.27/ru/featured-dark.webp) · [English](assets/macos/0.44.27/en/featured-dark.webp) |
| Фильмы | [Русский](assets/0.44.27/ru/catalog-dark.webp) · [English](assets/0.44.27/en/catalog-dark.webp) | [Русский](assets/macos/0.44.27/ru/catalog-dark.webp) · [English](assets/macos/0.44.27/en/catalog-dark.webp) |
| Сериалы | [Русский](assets/0.44.27/ru/series-dark.webp) · [English](assets/0.44.27/en/series-dark.webp) | [Русский](assets/macos/0.44.27/ru/series-dark.webp) · [English](assets/macos/0.44.27/en/series-dark.webp) |
| Игры | [Русский](assets/0.44.27/ru/games-dark.webp) · [English](assets/0.44.27/en/games-dark.webp) | [Русский](assets/macos/0.44.27/ru/games-dark.webp) · [English](assets/macos/0.44.27/en/games-dark.webp) |
| Карточка фильма | [Русский](assets/0.44.27/ru/detail-dark.webp) · [English](assets/0.44.27/en/detail-dark.webp) | [Русский](assets/macos/0.44.27/ru/detail-dark.webp) · [English](assets/macos/0.44.27/en/detail-dark.webp) |
| Загрузки | [Русский](assets/0.44.27/ru/downloads-dark.webp) · [English](assets/0.44.27/en/downloads-dark.webp) | [Русский](assets/macos/0.44.27/ru/downloads-dark.webp) · [English](assets/macos/0.44.27/en/downloads-dark.webp) |
| Настройки | [Русский](assets/0.44.27/ru/settings-lite-dark.webp) · [English](assets/0.44.27/en/settings-lite-dark.webp) | [Русский](assets/macos/0.44.27/ru/settings-dark.webp) · [English](assets/macos/0.44.27/en/settings-dark.webp) |
| Локальная сеть и телефон | [Русский](assets/0.44.27/ru/settings-lan-dark.webp) · [English](assets/0.44.27/en/settings-lan-dark.webp) | — |
| Светлая тема | [Русский](assets/0.44.27/ru/catalog-light.webp) · [English](assets/0.44.27/en/catalog-light.webp) | [Русский](assets/macos/0.44.27/ru/catalog-light.webp) · [English](assets/macos/0.44.27/en/catalog-light.webp) |

### Переключатели настроек Windows

- Лёгкий режим: [PNG на русском](assets/0.44.27/ru/settings-lite-dark.png) · [English PNG](assets/0.44.27/en/settings-lite-dark.png).
- Локальная сеть и телефон: [PNG на русском](assets/0.44.27/ru/settings-lan-dark.png) · [English PNG](assets/0.44.27/en/settings-lan-dark.png). Видны кнопка подключения телефона и переключатель загрузок между компьютерами. Подключение для снимка выключено.

[Метаданные оригиналов настроек](assets/0.44.27/settings-capture-info.json).

### Материалы для лендинга

WebP находятся в `assets/0.44.27/{ru,en}/` и `assets/macos/0.44.27/{ru,en}/`. Копии включены в `landing/public/app/` и `web-index/public/app/`: лендинг показывает свою платформу и язык даже при недоступности изображений GitHub. Для испанского интерфейса используются английские снимки приложения. [Текст описания](LANDING-COPY.md) · [Получение скриншотов](../scripts/screenshots/README.md) · [Платформенные галереи](SCREENSHOTS.md).

## Установка

На Windows запустите `.exe`, на Mac откройте DMG и перетащите Ka4alka в Applications. При первом запуске выберите папку для загрузок: приложение создаст в ней `Ka4alka`. Тёмная тема включена по умолчанию, светлая выбирается в настройках. Номер версии, автозапуск и обновления находятся там же.

Приложение проверяет обновления на GitHub и скачивает изменившиеся компоненты. Пакеты проверяются по подписи и SHA-256 до установки; настройки, очередь и скачанные файлы сохраняются. Проверку и автоматическую установку можно отключить.

На Mac требуется macOS 14+. Пакеты для Apple Silicon и Intel включают .NET. В этом выпуске используется ad-hoc подпись без нотариализации Apple; для первого запуска может понадобиться разрешение в «Системные настройки → Конфиденциальность и безопасность».

## Примечания

Наличие карточки не гарантирует подходящую раздачу или активных участников. Доступность Steam и торрент-источников зависит от региона и сети. Игровые установщики не запускаются автоматически. Используйте приложение для материалов, которые вы вправе скачивать и распространять.

[Что изменилось в 0.44.27](RELEASE-NOTES-0.44.27.md), [в 0.44.26](RELEASE-NOTES-0.44.26.md), [в 0.44.1](RELEASE-NOTES-0.44.1.md), [в 0.44.0](RELEASE-NOTES-0.44.0.md), [в 0.43.14](RELEASE-NOTES-0.43.14.md), [в 0.43.13](RELEASE-NOTES-0.43.13.md), [в 0.43.12](RELEASE-NOTES-0.43.12.md), [в 0.43.11](RELEASE-NOTES-0.43.11.md), [в 0.43.10](RELEASE-NOTES-0.43.10.md), [в 0.43.9](RELEASE-NOTES-0.43.9.md), [в 0.43.8](RELEASE-NOTES-0.43.8.md), [в 0.43.7](RELEASE-NOTES-0.43.7.md), [в 0.43.6](RELEASE-NOTES-0.43.6.md), [в 0.43.5](RELEASE-NOTES-0.43.5.md), [в 0.43.4](RELEASE-NOTES-0.43.4.md), [в 0.43.3](RELEASE-NOTES-0.43.3.md), [в 0.43.2](RELEASE-NOTES-0.43.2.md), [в 0.43.1](RELEASE-NOTES-0.43.1.md), [в 0.43.0](RELEASE-NOTES-0.43.0.md), [в 0.42.2](RELEASE-NOTES-0.42.2.md), [в 0.42.1](RELEASE-NOTES-0.42.1.md) и [в 0.42.0](RELEASE-NOTES-0.42.0.md). Лицензии используемых компонентов находятся в папке `licenses` сборки.
