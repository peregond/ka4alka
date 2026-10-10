# Посредник для России и Беларуси

Сайт «Ка4алка Онл@йн» размещён на chatgpt.site. Хостинг отвечает посетителям из России и Беларуси страницей блокировки Cloudflare (HTTP 403), поэтому приложение там не получает от сайта ни раздач, ни torrent-файлов. Одновременно в России заблокированы RuTor, NNM-Club, MegaPeer, BigFanGroup, Nyaa и EZTV. С версии 0.40.9 приложение из России и Беларуси сначала обращается к полным копиям сайта из поля `mirrors` того же списка; посредник остаётся запасным путём, когда не отвечает ни одна копия.

Этот Cloudflare Worker выполняет тот же код, что и сайт, но на отдельном адресе:

- `GET /api/search?id=movies:<slug>&title=<название>&original=<оригинальное>&year=<год>` — поиск раздач по всем источникам сайта; ответ в формате `/api/releases`. Базы данных нет: карточку передаёт приложение, оригинальное название при необходимости уточняется в Zona.
- `GET /api/torrent?url=<адрес скачивания>` — torrent-файл NNM-Club, MegaPeer, BigFanGroup или Nyaa. Принимаются только точные адреса скачивания этих трекеров; ответ проверяется как torrent-файл.

Приложение читает список посредников из [`distribution/relays.json`](../distribution/relays.json) на GitHub (GitHub из России доступен) и обращается к ним, только когда не ответила ни одна копия сайта: при открытии карточки и при скачивании torrent-файла. Массовые проверки качества для постеров каталога посредника не используют.

## Развёртывание

Нужна бесплатная учётная запись Cloudflare.

1. В Cloudflare откройте Workers & Pages и один раз выберите поддомен `*.workers.dev`, если он ещё не выбран.
2. Создайте API-токен по шаблону «Edit Cloudflare Workers» и скопируйте Account ID.
3. В GitHub: Settings → Secrets and variables → Actions → добавьте `CLOUDFLARE_API_TOKEN` и `CLOUDFLARE_ACCOUNT_ID`.
4. Actions → «Deploy relay for Russia and Belarus» → Run workflow.

Workflow проверяет код, разворачивает Worker `ka4alka-relay`, проверяет поиск и torrent-файл с раннера GitHub и с зондов Globalping в российских сетях, затем добавляет адрес в `distribution/relays.json` основной ветки. Установленные приложения подхватят адрес в течение нескольких часов без обновления.

Вручную из этой папки:

```sh
npx wrangler@4.92.0 deploy
```

после чего добавьте выданный адрес вида `https://ka4alka-relay.<поддомен>.workers.dev/` в `distribution/relays.json`.

## Если адрес заблокируют

В реестре Роскомнадзора на октябрь 2025 года домен `pages.dev` заблокирован целиком, `workers.dev` — нет (только отдельные поддомены). Если адрес посредника перестанет открываться из России, подключите к Worker собственный домен (Workers → Settings → Domains & Routes), запустите workflow с параметром `relay_url` и новым адресом. Старый адрес можно удалить из `distribution/relays.json`. Приложение пробует посредников по порядку, их может быть до пяти.

## Проверки

Из корня репозитория:

```sh
(cd web-index && npm ci && node tests/torrent-relay.mjs)
web-index/node_modules/.bin/tsc -p relay-worker/tsconfig.json
(cd relay-worker && ../web-index/node_modules/.bin/wrangler dev --local --port 8798)
```

Доступность источников, сайта и посредника из России проверяет workflow «Russia reachability probe» (запуск вручную): Globalping из российских сетей, сводка OONI и сравнение ответа сайта по странам.
