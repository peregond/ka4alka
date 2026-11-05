"use client";

import { useEffect, useState } from "react";
import { useSiteLanguage } from "@/components/site-language";
import { useSitePlatform } from "@/components/site-platform";
import type { AppRelease } from "@/lib/app-release";
import { appScreenshot, screenshotVersion, screenshotFiles } from "@/lib/app-screenshots";
import { ArrowUpRight, Film, Download, Sun, Rows3, Gamepad2, Tv, Sparkles, Settings2, Smartphone } from "lucide-react";

const screens = [
  { id: "featured", label: "Актуальное", icon: Sparkles, file: "featured-dark.webp", alt: "Актуальное в Качалке: курируемые подборки фильмов, сериалов и игр", caption: "Не знаешь, с чего начать? В «Актуальном» уже собраны подборки фильмов, сериалов и игр." },
  { id: "catalog", label: "Каталог", icon: Film, file: "catalog-dark.webp", alt: "Каталог Качалки: подборки фильмов, постеры, рейтинги и фильтры в тёмной теме", caption: "Подборки, поиск и фильтры — чтобы найти кино под настроение." },
  { id: "series", label: "Сериалы", icon: Tv, file: "series-dark.webp", alt: "Сериалы в Качалке: баннер с описанием и подборки", caption: "Сериалы, описания и подборки — в одном разделе." },
  { id: "games", label: "Игры", icon: Gamepad2, file: "games-dark.webp", alt: "Игры для Windows в Качалке: каталог Steam и поиск раздач", caption: "Каталог игр для Windows — с описаниями и поиском раздач." },
  { id: "detail", label: "Карточка фильма", icon: Rows3, file: "detail-dark.webp", alt: "Карточка фильма в Качалке с описанием, рейтингами и вариантами загрузки", caption: "Описание, оценки и варианты раздач — рядом с выбранным фильмом." },
  { id: "downloads", label: "Загрузки", icon: Download, file: "downloads-dark.webp", alt: "Очередь загрузок в Качалке с прогрессом, скоростью и управлением задачами", caption: "Очередь, прогресс и управление загрузками — в отдельном разделе." },
  { id: "settings", label: "Настройки", icon: Settings2, file: "settings-dark.webp", alt: "Настройки Качалки", caption: "Язык, тема, загрузки и обновления — в настройках приложения." },
  { id: "network", label: "Локальная сеть", icon: Smartphone, file: "settings-lan-dark.webp", alt: "Настройки локальной сети Качалки для Windows: подключение телефона", caption: "Подключи телефон по QR-коду в домашней сети и управляй загрузками из браузера." },
  { id: "light", label: "Светлая тема", icon: Sun, file: "catalog-light.webp", alt: "Каталог Качалки в светлой теме", caption: "Предпочитаешь светлую тему? Она тоже есть — и выбор сохраняется." },
];

export function ProductPreview({ release }: { release: AppRelease }) {
  const { t, language } = useSiteLanguage();
  const [selectedId, setSelectedId] = useState("featured");
  const { platform, ready } = useSitePlatform();
  const [localFallback, setLocalFallback] = useState(false);
  useEffect(() => { setLocalFallback(false); }, [platform, release.version]);
  const platformScreens = screens.filter(item => platform === "windows" || item.id !== "network");
  const selected = Math.max(0, platformScreens.findIndex(item => item.id === selectedId));
  const screen = platformScreens[selected];
  const file = screenshotFiles[platform][selected];
  const remote = !localFallback ? (platform === "macos" ? release.macScreenshots : release.windowsScreenshots)?.[`${language === "ru" ? "ru" : "en"}/${file}`] : undefined;
  const version = remote ? release.version : screenshotVersion(platform);
  function selectWithKeyboard(event: React.KeyboardEvent<HTMLButtonElement>, index: number) {
    const next = event.key === "ArrowRight" ? (index + 1) % platformScreens.length : event.key === "ArrowLeft" ? (index + platformScreens.length - 1) % platformScreens.length : event.key === "Home" ? 0 : event.key === "End" ? platformScreens.length - 1 : null;
    if (next === null) return;
    event.preventDefault(); setSelectedId(platformScreens[next].id);
    document.getElementById(`preview-tab-${platformScreens[next].id}`)?.focus();
  }
  return <div className="product-preview" id="interface">
    <div className="preview-toolbar">
      <div className="preview-tabs" role="tablist" aria-label={t("Скриншоты приложения")}>
        {platformScreens.map((item, index) => <button key={item.id} id={`preview-tab-${item.id}`} role="tab" type="button" aria-selected={selected === index} aria-controls="preview-panel" tabIndex={selected === index ? 0 : -1} onClick={() => setSelectedId(item.id)} onKeyDown={event => selectWithKeyboard(event, index)}><item.icon size={16} aria-hidden="true" />{t(item.label)}</button>)}
      </div>
      <span className="preview-live"><span />{t("Реальный интерфейс")}</span>
    </div>
    <div id="preview-panel" role="tabpanel" aria-labelledby={`preview-tab-${screen.id}`} tabIndex={0} className="preview-image">
      {ready ? <img key={`${platform}-${language}-${screen.id}-${version}`} data-platform={platform} src={remote ?? appScreenshot(language, file, platform)} onError={() => { if (remote) setLocalFallback(true); }} alt={t(screen.alt)} width="1424" height="892" decoding="async" fetchPriority={selected === 0 ? "high" : "auto"} /> : <div className="preview-loading" role="status">{t("Загрузка интерфейса…")}</div>}
    </div>
    <div className="preview-caption"><p>{t(screen.caption)} <span className="capture-version">{platform === "macos" ? "macOS" : "Windows"} · {version}</span></p><a href={`https://github.com/peregond/ka4alka/releases/tag/v${version}`} target="_blank" rel="noreferrer">{t("Скриншоты из GitHub")} <ArrowUpRight size={14} aria-hidden="true" /></a></div>
  </div>;
}
