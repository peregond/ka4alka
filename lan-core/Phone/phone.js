"use strict";
const en = !navigator.language.toLowerCase().startsWith("ru");
const tr = (ru, eng) => (en ? eng : ru),
  $ = (id) => document.getElementById(id);
const paths = {
  search: ["m21 21-4.4-4.4", "M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0"],
  refresh: ["M20 7v5h-5", "M20 12a8 8 0 1 0-2.3 5.7"],
  plus: ["M12 5v14M5 12h14"],
  close: ["m6 6 12 12M18 6 6 18"],
  back: ["m15 18-6-6 6-6"],
  forward: ["m9 6 6 6-6 6"],
  download: ["M12 3v12m-5-5 5 5 5-5", "M4 16v4h16v-4"],
  computer: ["M3 4h18v13H3z", "M8 21h8m-4-4v4"],
  phone: ["M7 2h10v20H7z", "M10 18h4"],
  wifi: [
    "M2 8a16 16 0 0 1 20 0M5 12a11 11 0 0 1 14 0m-11 4a6 6 0 0 1 8 0M12 20h.01",
  ],
  catalog: ["M3 4h18v16H3z", "M7 4v16M17 4v16M3 9h4m-4 6h4M17 9h4m-4 6h4"],
  disconnect: ["M9 6V3H3v18h6v-3", "M9 12h12m-4-4 4 4-4 4"],
  play: ["m9 5 10 7-10 7z"],
  pause: ["M8 5v14M16 5v14"],
  check: ["m5 12 4 4L19 6"],
  warning: ["m12 3 10 18H2z", "M12 9v4m0 4h.01"],
};
function node(tag, text, cls) {
  const el = document.createElement(tag);
  if (text != null) el.textContent = String(text);
  if (cls) el.className = cls;
  return el;
}
function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  svg.classList.add("icon");
  for (const d of paths[name] || paths.catalog) {
    const path = document.createElementNS(svg.namespaceURI, "path");
    path.setAttribute("d", d);
    svg.append(path);
  }
  return svg;
}
document.documentElement.lang = en ? "en" : "ru";
document.title = tr("Качалка · Пульт", "Kachalka · Remote");
document
  .querySelectorAll("[data-ru]")
  .forEach((el) => (el.textContent = en ? el.dataset.en : el.dataset.ru));
document
  .querySelectorAll("[data-ru-label]")
  .forEach((el) =>
    el.setAttribute("aria-label", en ? el.dataset.enLabel : el.dataset.ruLabel),
  );
document
  .querySelectorAll("[data-icon]")
  .forEach((el) => el.append(icon(el.dataset.icon)));
const storage = {
  get() {
    try {
      return sessionStorage.getItem("phone-token") || "";
    } catch {
      return "";
    }
  },
  set(value) {
    try {
      if (value) sessionStorage.setItem("phone-token", value);
      else sessionStorage.removeItem("phone-token");
    } catch {}
  },
};
let token = storage.get(),
  busy = false,
  mode = "catalog",
  section = "movies",
  selected = null,
  catalogState = null,
  releasesState = [],
  downloadState = [],
  downloadFilter = "all",
  lastAction = null;
const categoryName = (key) =>
  ({
    movies: tr("Фильмы", "Movies"),
    series: tr("Сериалы", "Series"),
    games: tr("Игры", "Games"),
  })[key] || tr("Каталог", "Catalog");
const qrCode = new URLSearchParams(location.hash.slice(1)).get("pair") || "";
if (location.hash)
  history.replaceState(null, "", location.pathname + location.search);
let appearance = "auto";
try {
  const saved = localStorage.getItem("phone-appearance");
  if (["auto", "light", "dark"].includes(saved)) appearance = saved;
} catch {}
const colorPreference = matchMedia("(prefers-color-scheme: dark)");
function applyAppearance() {
  document.documentElement.dataset.appearance = appearance;
  document.querySelector('meta[name="theme-color"]').content =
    appearance === "dark" || (appearance === "auto" && colorPreference.matches)
      ? "#0c0e12"
      : "#f5f5f7";
  document
    .querySelectorAll("[data-theme]")
    .forEach((button) =>
      button.setAttribute(
        "aria-pressed",
        String(button.dataset.theme === appearance),
      ),
    );
}
applyAppearance();
if (colorPreference.addEventListener)
  colorPreference.addEventListener("change", applyAppearance);
else colorPreference.addListener(applyAppearance);
document.querySelectorAll("[data-theme]").forEach(
  (button) =>
    (button.onclick = () => {
      appearance = button.dataset.theme;
      try {
        localStorage.setItem("phone-appearance", appearance);
      } catch {}
      applyAppearance();
    }),
);
function message(text = "", kind = "info", retry = false) {
  $("status").hidden = !text;
  $("status").dataset.kind = kind;
  $("status-text").textContent = text;
  $("retry").hidden = !retry;
  const sheet = $("magnet-message");
  sheet.textContent = text;
  sheet.dataset.kind = kind;
  sheet.hidden = !text || !$("magnet-dialog").open;
}
async function api(path, args = {}) {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: "Bearer " + token } : {}),
    },
    body: JSON.stringify(args),
    cache: "no-store",
  });
  const data = await response.json();
  if (!response.ok) {
    if (response.status === 401 && path != "/pair") logout();
    throw Error(
      data.error ||
        tr(
          "Не удалось выполнить запрос. Попробуй ещё раз.",
          "The request failed. Please try again.",
        ),
    );
  }
  return data;
}
async function run(action, label = tr("Загрузка…", "Loading…")) {
  if (busy) return;
  busy = true;
  lastAction = action;
  document
    .querySelectorAll("button,input,select,textarea")
    .forEach((b) => (b.disabled = true));
  $("remote").setAttribute("aria-busy", "true");
  message(label);
  try {
    await action();
    message();
  } catch (error) {
    message(error.message, "error", !!token);
  } finally {
    busy = false;
    $("remote").setAttribute("aria-busy", "false");
    document
      .querySelectorAll("button,input,select,textarea")
      .forEach((b) => (b.disabled = b.dataset.unavailable === "true"));
  }
}
$("retry").onclick = () => {
  if (lastAction) run(lastAction);
};
function connected() {
  $("pair").hidden = true;
  $("remote").hidden = false;
  $("session-actions").hidden = false;
  $("device").textContent = tr(
    "Подключено к компьютеру",
    "Connected to your computer",
  );
  $("device-address").textContent = location.host;
}
function closeMagnet() {
  const dialog = $("magnet-dialog");
  if (dialog.close) {
    if (dialog.open) dialog.close();
  } else dialog.removeAttribute("open");
  document.body.classList.remove("sheet-open");
}
function logout() {
  token = "";
  storage.set("");
  catalogState = null;
  selected = null;
  downloadState = [];
  releasesState = [];
  closeMagnet();
  $("pair").hidden = false;
  $("remote").hidden = true;
  $("session-actions").hidden = true;
  $("results").replaceChildren();
  $("device").textContent = tr(
    "Пульт в твоём телефоне",
    "Your phone. Your remote.",
  );
}
async function pair(code) {
  const result = await api("/pair", { code });
  token = result.token;
  storage.set(token);
  $("code").value = "";
  connected();
  lastAction = () => catalog(true);
  await catalog(true);
}
$("pair-form").onsubmit = (e) => {
  e.preventDefault();
  run(() => pair($("code").value), tr("Подключаемся…", "Connecting…"));
};
function view(next) {
  mode = next;
  $("search").hidden = next !== "catalog";
  $("release-tools").hidden = next !== "releases";
  $("download-tools").hidden = next !== "downloads";
  $("device-panel").hidden = next !== "device";
  $("results").hidden = next === "device";
  $("back").hidden = next !== "releases";
  $("more").hidden = true;
  $("results").className = next === "catalog" ? "cards" : "";
  $("view-title").textContent =
    next === "downloads"
      ? tr("Загрузки", "Downloads")
      : next === "device"
        ? tr("Устройство", "Device")
        : next === "releases"
          ? tr("Выбрать раздачу", "Choose a release")
          : categoryName(section);
  $("view-subtitle").textContent =
    next === "downloads"
      ? tr("Управляй очередью на компьютере", "Manage your computer’s queue")
      : next === "device"
        ? tr("Дома, в одной сети", "At home, on the same network")
        : next === "releases"
          ? tr(
              "Скачивание начнётся на компьютере",
              "Your computer will handle the download",
            )
          : section === "games"
            ? tr("Игры для Windows", "Windows games")
            : tr(
                "Найди, что посмотреть сегодня",
                "Find something to watch today",
              );
  for (const [id, active] of [
    ["browse", next === "catalog" || next === "releases"],
    ["transfers", next === "downloads"],
    ["connection", next === "device"],
  ]) {
    if (active) $(id).setAttribute("aria-current", "page");
    else $(id).removeAttribute("aria-current");
  }
}
function loading(text) {
  const state = node("div", null, "loading-state");
  state.append(icon("refresh"), node("p", text));
  $("results").replaceChildren(state);
}
function empty(title, description, symbol = "search") {
  const state = node("div", null, "empty-state");
  state.append(icon(symbol), node("h2", title));
  if (description) state.append(node("p", description));
  return state;
}
function poster(item, cls = "poster-frame") {
  const frame = node("div", null, cls);
  frame.append(icon(item.kind === "games" ? "computer" : "catalog"));
  if (typeof item.poster === "string" && /^https:\/\//i.test(item.poster)) {
    const image = node("img");
    image.src = item.poster;
    image.alt = "";
    image.loading = "lazy";
    image.decoding = "async";
    image.referrerPolicy = "no-referrer";
    image.onerror = () => {
      image.hidden = true;
    };
    frame.append(image);
  }
  return frame;
}
function renderCatalog(restore = false) {
  view("catalog");
  $("results").replaceChildren();
  const items = catalogState?.items || [];
  for (const item of items) {
    const button = node("button", null, "card");
    button.type = "button";
    button.setAttribute(
      "aria-label",
      item.title + (item.year ? " · " + item.year : ""),
    );
    const image = poster(item);
    if (item.rating && item.rating !== "—")
      image.append(
        node(
          "span",
          (item.ratingSource ? item.ratingSource + " " : "") + item.rating,
          "rating-badge",
        ),
      );
    button.append(
      image,
      node("span", item.title, "card-title"),
      node(
        "span",
        [item.year || "", categoryName(item.kind || section)]
          .filter(Boolean)
          .join(" · "),
        "card-meta",
      ),
    );
    button.onclick = () =>
      run(() => releases(item), tr("Ищем раздачи…", "Finding releases…"));
    $("results").append(button);
  }
  if (!items.length)
    $("results").append(
      empty(
        tr("Пока ничего не нашли", "No matches yet"),
        tr(
          "Попробуй другое название или выбери другой раздел.",
          "Try a different title or category.",
        ),
      ),
    );
  $("more").hidden = !catalogState?.hasNext;
  if (restore)
    requestAnimationFrame(() => window.scrollTo(0, catalogState?.scroll || 0));
}
async function catalog(reset) {
  view("catalog");
  const nextPage = reset ? 1 : (catalogState?.page || 1) + 1;
  if (reset) loading(tr("Загружаем каталог…", "Loading the catalog…"));
  const query = $("query").value.trim();
  const data = await api("/api/catalog", { section, query, page: nextPage });
  const previous = reset ? [] : catalogState?.items || [];
  const unique = new Map(
    [...previous, ...data.items].map((item) => [item.id, item]),
  );
  catalogState = {
    items: [...unique.values()],
    page: nextPage,
    hasNext: !!data.hasNext,
    query,
    section,
    scroll: reset ? 0 : window.scrollY,
  };
  selected = null;
  renderCatalog();
}
function returnToCatalog() {
  if (catalogState) {
    section = catalogState.section;
    $("query").value = catalogState.query;
    syncCategories();
    renderCatalog(true);
  } else run(() => catalog(true));
}
function syncCategories() {
  document
    .querySelectorAll("[data-section]")
    .forEach((button) =>
      button.setAttribute(
        "aria-selected",
        String(button.dataset.section === section),
      ),
    );
  $("query").placeholder =
    section === "games"
      ? tr("Название игры", "Game title")
      : tr("Фильм или сериал", "Movie or series");
  $("results").setAttribute("aria-labelledby", "category-" + section);
}
syncCategories();
document.querySelectorAll("[data-section]").forEach((button) => {
  button.onclick = () => {
    section = button.dataset.section;
    syncCategories();
    run(() => catalog(true));
  };
  button.onkeydown = (e) => {
    if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
    e.preventDefault();
    const buttons = [...document.querySelectorAll("[data-section]")],
      index = buttons.indexOf(button),
      next =
        buttons[(index + (e.key === "ArrowRight" ? 1 : 2)) % buttons.length];
    next.focus();
    next.click();
  };
});
function options(control, values, label) {
  control.replaceChildren(new Option(label, ""));
  for (const value of [...new Set(values.filter(Boolean))])
    control.append(new Option(value, value));
}
async function releases(item) {
  if (mode === "catalog" && catalogState) catalogState.scroll = window.scrollY;
  selected = item;
  releasesState = [];
  options($("release-quality"), [], tr("Любое качество", "Any quality"));
  options($("release-audio"), [], tr("Любая озвучка", "Any audio"));
  view("releases");
  window.scrollTo(0, 0);
  loading(tr("Ищем подходящие раздачи…", "Finding matching releases…"));
  const data = await api("/api/releases", { id: item.id });
  releasesState = data.items.map((entry, index) => ({ ...entry, index }));
  options(
    $("release-quality"),
    releasesState.map((x) => x.quality),
    tr("Любое качество", "Any quality"),
  );
  options(
    $("release-audio"),
    releasesState.map((x) => x.audio),
    tr("Любая озвучка", "Any audio"),
  );
  $("release-order").value = "recommended";
  renderReleases();
}
function renderReleases() {
  if (!selected) return;
  const quality = $("release-quality").value,
    audio = $("release-audio").value,
    order = $("release-order").value;
  const rows = releasesState
    .filter(
      (x) =>
        (!quality || x.quality === quality) && (!audio || x.audio === audio),
    )
    .sort((a, b) =>
      order === "seeds"
        ? (Number(b.seeds) || 0) - (Number(a.seeds) || 0) || a.index - b.index
        : order === "size"
          ? (Number(a.size) > 0 ? Number(a.size) : Number.MAX_SAFE_INTEGER) -
              (Number(b.size) > 0 ? Number(b.size) : Number.MAX_SAFE_INTEGER) ||
            a.index - b.index
          : a.index - b.index,
    );
  $("results").replaceChildren();
  const hero = node("article", null, "detail-hero"),
    info = node("div");
  info.append(
    node("h2", selected.title),
    node(
      "p",
      [selected.year, categoryName(selected.kind || section)]
        .filter(Boolean)
        .join(" · "),
    ),
  );
  if (selected.description)
    info.append(node("p", selected.description, "synopsis"));
  hero.append(poster(selected), info);
  $("results").append(hero);
  $("release-count").textContent =
    tr("Показано ", "Showing ") +
    rows.length +
    tr(" из ", " of ") +
    releasesState.length;
  for (const release of rows) {
    const row = node(
      "article",
      null,
      "row" + (release.recommended ? " recommended" : ""),
    );
    if (release.recommended) {
      const badge = node("div", null, "recommended-label");
      badge.append(
        icon("check"),
        node("span", tr("Рекомендуем", "Recommended")),
      );
      row.append(badge);
    }
    row.append(node("h2", release.title, "release-title"));
    const chips = node("div", null, "chips");
    for (const text of [
      release.quality,
      release.audio,
      release.source,
      bytes(release.size),
    ])
      if (text) chips.append(node("span", text, "chip"));
    row.append(chips);
    const footer = node("div", null, "row-footer"),
      button = node("button", null, "primary");
    button.type = "button";
    button.append(icon("download"), node("span", tr("Скачать", "Download")));
    button.disabled = !release.downloadable;
    button.dataset.unavailable = String(!release.downloadable);
    button.onclick = () =>
      run(async () => {
        if (
          !confirm(
            tr("Скачать на компьютере?", "Download on your computer?") +
              "\n" +
              selected.title,
          )
        )
          return;
        await api("/api/add", { release: release.id, card: selected.id });
        await transfers();
      });
    footer.append(
      node(
        "span",
        tr("Раздают: ", "Seeders: ") + (release.seeds ?? "—"),
        "release-seeds",
      ),
      button,
    );
    row.append(footer);
    $("results").append(row);
  }
  if (!rows.length)
    $("results").append(
      empty(
        releasesState.length
          ? tr(
              "Нет раздач с такими фильтрами",
              "No releases match these filters",
            )
          : tr("Раздач пока нет", "No releases yet"),
        releasesState.length
          ? tr(
              "Выбери другое качество или озвучку.",
              "Try another quality or audio option.",
            )
          : tr("Попробуй проверить позже.", "Try checking again later."),
        "catalog",
      ),
    );
}
for (const id of ["release-quality", "release-audio", "release-order"])
  $(id).onchange = renderReleases;
function bytes(value) {
  const count = Number(value);
  if (!Number.isFinite(count) || count <= 0) return "";
  const units = ["B", "KB", "MB", "GB", "TB"],
    index = Math.min(4, Math.floor(Math.log(count) / Math.log(1024)));
  return (
    new Intl.NumberFormat(en ? "en" : "ru", {
      maximumFractionDigits: index > 1 ? 1 : 0,
    }).format(count / 1024 ** index) +
    " " +
    units[index]
  );
}
const percent = (value) =>
  Number.isFinite(Number(value))
    ? Math.max(0, Math.min(100, Number(value)))
    : 0;
function status(item) {
  if (item.filesMissing) return tr("Файлы не найдены", "Files not found");
  const labels = {
    downloading: tr("Скачивается", "Downloading"),
    waiting: tr("Ожидание участников", "Waiting for peers"),
    checking: tr("Проверка файлов", "Checking files"),
    error: tr("Требует внимания", "Needs attention"),
    paused: tr("На паузе", "Paused"),
    seeding: tr("Раздаётся", "Seeding"),
  };
  return (
    labels[item.statusKind] ||
    (item.paused
      ? tr("На паузе", "Paused")
      : item.complete
        ? tr("Готово", "Complete")
        : item.status)
  );
}
async function transfers() {
  view("downloads");
  loading(tr("Обновляем очередь…", "Updating your queue…"));
  const data = await api("/api/downloads");
  downloadState = data.items;
  renderDownloads();
}
function renderDownloads() {
  view("downloads");
  const matches = (item) =>
    downloadFilter === "active"
      ? !item.paused && !item.complete && !item.filesMissing
      : downloadFilter === "paused"
        ? item.paused || item.filesMissing
        : downloadFilter === "complete"
          ? item.complete
          : true;
  const active = downloadState.filter(
      (x) => !x.paused && !x.complete && !x.filesMissing,
    ).length,
    done = downloadState.filter((x) => x.complete).length;
  $("download-summary").replaceChildren();
  for (const [value, label] of [
    [active, tr("Скачивается сейчас", "Downloading now")],
    [done, tr("Готово на компьютере", "Ready on your computer")],
  ]) {
    const cell = node("div", null, "summary-cell");
    cell.append(node("strong", value), node("span", label));
    $("download-summary").append(cell);
  }
  document
    .querySelectorAll("[data-download-filter]")
    .forEach((button) =>
      button.setAttribute(
        "aria-pressed",
        String(button.dataset.downloadFilter === downloadFilter),
      ),
    );
  $("results").replaceChildren();
  for (const item of downloadState.filter(matches)) {
    const row = node(
      "article",
      null,
      "row" + (item.filesMissing ? " missing" : ""),
    );
    row.dataset.downloadId = item.id;
    const heading = node("div", null, "transfer-heading"),
      name = node("div");
    name.append(
      node("h2", item.title),
      node("span", status(item), "transfer-status"),
    );
    heading.append(poster(item), name);
    row.append(heading);
    const value = percent(item.progress),
      labels = node("div", null, "progress-label");
    labels.append(
      node(
        "span",
        item.complete
          ? tr("Скачано", "Downloaded")
          : tr("Загружено", "Downloaded"),
      ),
      node(
        "strong",
        new Intl.NumberFormat(en ? "en" : "ru", {
          maximumFractionDigits: 1,
        }).format(value) + "%",
      ),
    );
    const progress = node("progress");
    progress.max = 100;
    progress.value = value;
    progress.setAttribute(
      "aria-label",
      tr("Загрузка: ", "Download: ") + item.title,
    );
    row.append(labels, progress);
    const stats = node("div", null, "transfer-stats"),
      rate = bytes(item.downloadRate),
      size = bytes(item.totalBytes);
    if (rate) stats.append(node("span", "↓ " + rate + tr("/с", "/s")));
    if (Number(item.uploadRate) > 0)
      stats.append(
        node("span", "↑ " + bytes(item.uploadRate) + tr("/с", "/s")),
      );
    if (size) stats.append(node("span", size));
    if (stats.childNodes.length) row.append(stats);
    const action = item.filesMissing
        ? "redownload"
        : item.paused
          ? "resume"
          : "pause",
      label = item.filesMissing
        ? tr("Скачать заново", "Download again")
        : item.paused
          ? item.complete
            ? tr("Продолжить раздачу", "Resume seeding")
            : tr("Продолжить", "Resume")
          : item.complete
            ? tr("Остановить раздачу", "Pause seeding")
            : tr("Пауза", "Pause");
    const button = node(
      "button",
      null,
      "transfer-action" + (item.filesMissing ? " primary" : ""),
    );
    button.type = "button";
    button.dataset.action = action;
    button.disabled = !!item.busy;
    button.dataset.unavailable = String(!!item.busy);
    button.append(
      icon(item.filesMissing ? "download" : item.paused ? "play" : "pause"),
      node("span", label),
    );
    button.onclick = () =>
      run(async () => {
        if (
          action === "redownload" &&
          !confirm(
            tr(
              "Скачать отсутствующие файлы заново на компьютере?",
              "Download the missing files again on your computer?",
            ),
          )
        )
          return;
        await api("/api/control", {
          id: item.id,
          action,
          ...(action === "redownload" ? { confirmed: true } : {}),
        });
        await transfers();
        const target = [...$("results").querySelectorAll("[data-download-id]")]
          .find((x) => x.dataset.downloadId === item.id)
          ?.querySelector("button");
        target?.focus({ preventScroll: true });
      });
    row.append(button);
    $("results").append(row);
  }
  if (!$("results").childNodes.length)
    $("results").append(
      empty(
        downloadState.length
          ? tr("Здесь пока пусто", "Nothing here yet")
          : tr("Очередь пока пуста", "Your queue is empty"),
        downloadState.length
          ? tr(
              "Выбери другой фильтр, чтобы увидеть остальные загрузки.",
              "Choose another filter to see your other downloads.",
            )
          : tr(
              "Выбери фильм, сериал или игру в каталоге — они появятся здесь.",
              "Choose a movie, series or game from the catalog to get started.",
            ),
        "download",
      ),
    );
}
document.querySelectorAll("[data-download-filter]").forEach(
  (button) =>
    (button.onclick = () => {
      downloadFilter = button.dataset.downloadFilter;
      renderDownloads();
    }),
);
function device() {
  view("device");
  window.scrollTo(0, 0);
}
function openMagnet() {
  if (!token) return;
  $("magnet-message").hidden = true;
  $("magnet-message").textContent = "";
  const dialog = $("magnet-dialog");
  if (dialog.showModal) dialog.showModal();
  else dialog.setAttribute("open", "");
  document.body.classList.add("sheet-open");
  $("magnet").focus();
}
$("magnet-dialog").addEventListener("close", () =>
  document.body.classList.remove("sheet-open"),
);
$("close-magnet").onclick = closeMagnet;
$("add-magnet").onclick = openMagnet;
$("device-magnet").onclick = openMagnet;
$("search").onsubmit = (e) => {
  e.preventDefault();
  run(() => catalog(true));
};
$("more").onclick = () => run(() => catalog(false));
$("browse").onclick = () => {
  message();
  returnToCatalog();
};
$("back").onclick = returnToCatalog;
$("transfers").onclick = () => run(transfers);
$("connection").onclick = device;
$("refresh").onclick = () =>
  run(
    mode === "downloads"
      ? transfers
      : mode === "releases"
        ? () => releases(selected)
        : mode === "device"
          ? async () => device()
          : () => catalog(true),
  );
$("logout").onclick = () =>
  run(async () => {
    try {
      await api("/api/logout");
    } finally {
      logout();
    }
  });
$("magnet-form").onsubmit = (e) => {
  e.preventDefault();
  run(async () => {
    const source = $("magnet").value.trim();
    if (!/^magnet:\?/i.test(source))
      throw Error(tr("Нужна magnet-ссылка", "Enter a magnet link"));
    if (!confirm(tr("Скачать на компьютере?", "Download on your computer?")))
      return;
    await api("/api/add", { source });
    $("magnet").value = "";
    closeMagnet();
    await transfers();
  });
};
if (qrCode) {
  if (/^\d{6}$/.test(qrCode))
    run(() => pair(qrCode), tr("Подключаемся…", "Connecting…"));
  else
    message(
      tr(
        "QR-код недействителен. Получи новый на компьютере.",
        "Invalid QR code. Get a new one on your computer.",
      ),
      "error",
    );
} else if (token) {
  connected();
  run(() => catalog(true));
}
