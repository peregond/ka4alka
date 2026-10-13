"use client";

import { useState } from "react";
import { useSiteLanguage } from "@/components/site-language";
import { ArrowUpRight, Film, Download, Sun, Rows3 } from "lucide-react";

const screens = [
  { id: "catalog", label: "Каталог", icon: Film, file: "catalog-dark.png", alt: "Каталог Качалки: подборки фильмов, постеры, рейтинги и фильтры в тёмной теме", caption: "Подборки, поиск и фильтры — чтобы найти кино под настроение." },
  { id: "detail", label: "Карточка фильма", icon: Rows3, file: "detail-wide-dark.png", alt: "Карточка фильма в Качалке с описанием, рейтингами и вариантами загрузки", caption: "Описание, оценки и варианты раздач — рядом с выбранным фильмом." },
  { id: "downloads", label: "Загрузки", icon: Download, file: "downloads-dark.png", alt: "Очередь загрузок в Качалке с прогрессом, скоростью и управлением задачами", caption: "Очередь, прогресс и управление загрузками — в отдельном разделе." },
  { id: "light", label: "Светлая тема", icon: Sun, file: "catalog-light.png", alt: "Каталог Качалки в светлой теме", caption: "Предпочитаешь светлую тему? Она тоже есть — и выбор сохраняется." },
];

export function ProductPreview() {
  const { t } = useSiteLanguage();
  const [selected, setSelected] = useState(0);
  const screen = screens[selected];
  function selectWithKeyboard(event: React.KeyboardEvent<HTMLButtonElement>, index: number) {
    const next = event.key === "ArrowRight" ? (index + 1) % screens.length : event.key === "ArrowLeft" ? (index + screens.length - 1) % screens.length : event.key === "Home" ? 0 : event.key === "End" ? screens.length - 1 : null;
    if (next === null) return;
    event.preventDefault(); setSelected(next);
    document.getElementById(`preview-tab-${screens[next].id}`)?.focus();
  }
  return <div className="product-preview" id="interface">
    <div className="preview-toolbar">
      <div className="preview-tabs" role="tablist" aria-label={t("Скриншоты приложения")}>
        {screens.map((item, index) => <button key={item.id} id={`preview-tab-${item.id}`} role="tab" type="button" aria-selected={selected === index} aria-controls="preview-panel" tabIndex={selected === index ? 0 : -1} onClick={() => setSelected(index)} onKeyDown={event => selectWithKeyboard(event, index)}><item.icon size={16} aria-hidden="true" />{t(item.label)}</button>)}
      </div>
      <span className="preview-live"><span />{t("Реальный интерфейс")}</span>
    </div>
    <div id="preview-panel" role="tabpanel" aria-labelledby={`preview-tab-${screen.id}`} tabIndex={0} className="preview-image">
      <img src={`/app/${screen.file}`} alt={t(screen.alt)} width="1744" height="911" fetchPriority={selected === 0 ? "high" : "auto"} />
    </div>
    <div className="preview-caption"><p>{t(screen.caption)}</p><a href="https://github.com/peregond/ka4alka/tree/v0.40.4/distribution/assets/0.40" target="_blank" rel="noreferrer">{t("Скриншоты из GitHub")} <ArrowUpRight size={14} aria-hidden="true" /></a></div>
  </div>;
}
