"use client";

import { ArrowDown, ArrowDownToLine, ArrowRight, ArrowUpRight, Check, Code2, Laptop, Monitor, Moon, Network, Plus, Search, Settings2, ShieldCheck } from "lucide-react";
import { ScrollHeader } from "@/components/scroll-header";
import { ProductPreview } from "@/components/product-preview";
import { SitePlatformProvider, PlatformSwitcher, useSitePlatform } from "@/components/site-platform";
import { PlatformDownload, DownloadMeta } from "@/components/platform-download";
import type { AppRelease } from "@/lib/app-release";
import { LanguageSwitcher, LocalizedMetadata, useSiteLanguage } from "@/components/site-language";

function SavedHeartIcon() {
  return <svg width="23" height="23" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 21 C9 18 3 14 3 8 C3 2 10 2 12 7 C14 2 21 2 21 8 C21 14 15 18 12 21 Z" /></svg>;
}

function TelegramIcon() {
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M21.4 3.3 2.9 10.5c-1.3.5-1.3 1.2-.2 1.5l4.7 1.5 1.8 5.4c.2.6.1.8.7.8.4 0 .6-.2.8-.4l2.3-2.2 4.8 3.5c.9.5 1.6.2 1.8-.8l3.1-14.6c.3-1.3-.5-1.9-1.3-1.4ZM8.2 13.1l10.5-6.6c.5-.3.9-.1.6.2l-8.5 7.7-.3 3.2-2.3-4.5Z" /></svg>;
}

export function LandingPage({ release }: { release: AppRelease }) { return <SitePlatformProvider><LandingContent release={release} /></SitePlatformProvider>; }
function LandingContent({ release }: { release: AppRelease }) {
  const { t } = useSiteLanguage();
  const { platform } = useSitePlatform();
  const mac = platform === "macos";
  return <>
    <LocalizedMetadata title="Качалка — фильмы, сериалы, игры и загрузки для Windows и macOS" description="Твой вечер. Играй, смотри, качай. Фильмы, сериалы и игры, поиск раздач и управление загрузками в одном приложении. Скачать Качалку для Windows и macOS." />
    <a className="skip-link" href="#main">{t("Перейти к содержимому")}</a>
    <ScrollHeader />
    <main id="main">
      <section className="hero section-wrap" aria-labelledby="hero-title">
        <div className="hero-language"><LanguageSwitcher /></div>
        <a className="release-badge" href={release.releaseUrl} target="_blank" rel="noreferrer"><span className="status-dot" />{t("Версия")} {release.version}<span className="badge-divider" />{t("Уже на GitHub")}<ArrowUpRight size={13} aria-hidden="true" /></a>
        <h1 id="hero-title">{t("Твой вечер.")}<br /><span>{t("Играй, смотри, качай.")}</span></h1>
        <p className="hero-description">{t("Фильмы, сериалы, игры и загрузки — в одном приложении для Windows и Mac.")}<br className="desktop-break" /> {t("Выбери, во что играть или что смотреть, и добавь загрузку в очередь.")}</p>
        <PlatformSwitcher />
        <div className="hero-actions"><PlatformDownload release={release} /><a className="button button-telegram" href="https://t.me/ka4alkacc" target="_blank" rel="noopener noreferrer"><TelegramIcon />{t("Мы в Telegram")}<ArrowUpRight size={16} aria-hidden="true" /></a></div>
        <div className="hero-feature-link"><a className="button-secondary" href="#features">{t("Что умеет Качалка")}<ArrowDown size={17} aria-hidden="true" /></a></div>
        <DownloadMeta release={release} />
        <ProductPreview release={release} />
        <div className="product-facts"><span><Check size={17} aria-hidden="true" />{t("Бесплатно и без рекламы")}</span><span><Code2 size={16} aria-hidden="true" />{t("Открытый исходный код")}</span><span><Check size={17} aria-hidden="true" />{t("Без регистрации")}</span></div>
      </section>

      <section id="features" className="features section-wrap" aria-labelledby="features-title">
        <div className="section-heading"><div><p className="eyebrow">{t("ОТ ВЫБОРА ДО ЗАГРУЗКИ")}</p><h2 id="features-title">{t("Меньше переключений.")}<br />{t("Больше кино и игр.")}</h2></div><p className="section-description">{t("Каталог, поиск раздач и торрент-клиент работают вместе. Всё нужное — под рукой.")}</p></div>
        <div className="steps-grid">
          <article className="feature-step"><div className="step-top"><span>{t("01 / НАЙДИ")}</span><Search size={23} aria-hidden="true" /></div><h3>{t("Что посмотреть? Во что играть?")}</h3><p>{t("Находи фильмы, сериалы и игры по названию. Для кино — подборки, оценки, описания и фильтры по жанру, году, стране и рейтингу.")}</p><div className="feature-tags"><span>{t("Фильмы")}</span><span>{t("Сериалы")}</span><span>{t("Игры")}</span></div></article>
          <article className="feature-step"><div className="step-top"><span>{t("02 / ВЫБЕРИ")}</span><Settings2 size={23} aria-hidden="true" /></div><h3>{t("Твоя раздача.")}</h3><p>{t("Сравни качество, размер и число сидов. Отфильтруй озвучку, субтитры и кодек, чтобы загрузить подходящий вариант.")}</p><div className="feature-tags"><span>HD</span><span>Full HD</span><span>4K</span></div></article>
          <article className="feature-step"><div className="step-top"><span>{t("03 / ЗАГРУЗИ")}</span><ArrowDownToLine size={23} aria-hidden="true" /></div><h3>{t("Дальше — очередь.")}</h3><p>{t("Скачивай через magnet и .torrent прямо в Качалке. Следи за скоростью и прогрессом, ставь на паузу и продолжай позже.")}</p><div className="feature-tags"><span>Magnet</span><span>.torrent</span><span>{t("Пауза / продолжение")}</span></div></article>
        </div>
      </section>

      <section className="comfort section-wrap" aria-labelledby="comfort-title">
        <div className="comfort-heading"><p className="eyebrow">{t("ПРОДУМАНО ДЛЯ КАЖДОГО ДНЯ")}</p><h2 id="comfort-title">{t("Как удобно тебе.")}</h2><p>{t("Сохрани интересное. Настрой своё. Вернись к тому, на чём остановился.")}</p></div>
        <div className="comfort-grid">
          <article className="comfort-item"><SavedHeartIcon /><h3>{t("Не потеряй находку")}</h3><p>{t("Добавляй фильмы и сериалы в «Сохранённое» — всё, что хочется посмотреть, останется в одном списке.")}</p></article>
          <article className="comfort-item"><Moon size={23} aria-hidden="true" /><h3>{t("Светлая или тёмная")}</h3><p>{t("Выбирай тему в настройках. Приложение запомнит её вместе с папкой загрузок и лимитами скорости.")}</p></article>
          <article className="comfort-item"><ShieldCheck size={23} aria-hidden="true" /><h3>{t("Продолжай с того же места")}</h3><p>{t("Очередь сохраняется между запусками. Активные задачи можно продолжать автоматически, а ручные паузы остаются на паузе.")}</p></article>
        </div>
        <div className="lan-feature">
          <div className="lan-copy"><p className="eyebrow"><Network size={16} aria-hidden="true" />{t("СВЯЗАННЫЕ УСТРОЙСТВА")}</p><h3>{t("Нашёл на ноутбуке.")}<br />{t("Скачал на другом ПК.")}</h3><p>{t("Свяжи две Качалки в одной локальной сети и отправляй раздачу на другой компьютер. Он загрузит файлы в свою папку. Сопряжение подтверждается на обоих устройствах.")}</p></div>
          <div className="device-diagram" aria-label={t("Раздача передаётся с ноутбука на домашний компьютер в одной локальной сети")}><div className="device"><div className="device-screen"><Laptop size={42} strokeWidth={1.2} aria-hidden="true" /><span className="device-indicator" /></div><span>{t("Ноутбук")}</span></div><div className="device-connection"><span className="connection-line" /><ArrowRight size={20} aria-hidden="true" /><small>{t("Одна сеть")}</small></div><div className="device"><div className="device-screen destination"><Monitor size={42} strokeWidth={1.2} aria-hidden="true" /></div><span>{t("Домашний ПК")}</span></div></div>
        </div>
      </section>

      <section id="download" className="download-section section-wrap" aria-labelledby="download-title">
        <div className="download-panel"><img className="download-app-icon" src="/brand/logo.png" alt="" width="88" height="88" /><p className="eyebrow">{t("Качалка для Windows и macOS")}</p><h2 id="download-title">{t("Пора выбрать кино или игру.")}</h2><p>{t("Установи приложение, выбери папку для загрузок —")}<br className="desktop-break" /> {t("и выбирай, как провести свой вечер.")}</p><PlatformDownload release={release} /><div className="download-options"><span>{t("Версия")} {release.version} · {mac ? "macOS 14+" : "Windows x64"}</span><a href={mac ? "/download?format=mac-arm64-zip" : "/download?format=zip"}>{mac ? "ZIP · Apple Silicon" : t("Скачать ZIP")}<ArrowUpRight size={14} aria-hidden="true" /></a>{mac && <a href="/download?format=mac-x64-zip">ZIP · Intel</a>}<a href={release.releaseUrl} target="_blank" rel="noreferrer">{t("Что нового")}<ArrowUpRight size={14} aria-hidden="true" /></a></div></div>
      </section>

      <section id="installation" className="installation section-wrap" aria-labelledby="installation-title">
        <p className="eyebrow">{t("НЕСКОЛЬКО ПРОСТЫХ ШАГОВ")}</p><h2 id="installation-title">{t("Как установить.")}</h2>
        {mac ? <ol className="install-steps">
          <li><span className="install-number">1</span><div><h3>{t("Скачай DMG для своего Mac")}</h3><p>{t("Выбери Apple Silicon или Intel. Процессор указан в меню Apple → «Об этом Mac».")}</p></div></li>
          <li><span className="install-number">2</span><div><h3>{t("Перенеси в Applications")}</h3><p>{t("Открой DMG и перетащи Ka4alka в папку «Программы» (Applications).")}</p></div></li>
          <li><span className="install-number">3</span><div><h3>{t("Запусти Качалку")}</h3><p>{t("Выбери папку для загрузок. Требуется macOS 14 или новее; игровой каталог содержит игры для Windows.")}</p></div></li>
        </ol> : <ol className="install-steps">
          <li><span className="install-number">1</span><div><h3>{t("Скачай установщик")}</h3><p>{t("Нажми «Скачать для Windows». Файл загрузится из официального GitHub Качалки.")}</p></div></li>
          <li><span className="install-number">2</span><div><h3>{t("Открой файл")}</h3><p>{t("В папке «Загрузки» дважды нажми на Kachalka-Setup-")}{release.version}.exe.</p></div></li>
          <li><span className="install-number">3</span><div><h3>{t("Если появится SmartScreen")}</h3><p>{t("В окне «Система Windows защитила ваш компьютер» нажми")} <strong>{t("«Подробнее»")}</strong>{t(", затем")} <strong>{t("«Выполнить в любом случае»")}</strong>{t(". Продолжай только если файл скачан из официального релиза Качалки и ты доверяешь приложению.")}</p><div className="install-clicks"><span>{t("Подробнее")}</span><ArrowRight size={16} aria-hidden="true" /><span>{t("Выполнить в любом случае")}</span></div></div></li>
          <li><span className="install-number">4</span><div><h3>{t("Заверши установку")}</h3><p>{t("Следуй шагам установщика. Запусти Качалку и выбери папку для загрузок — готово.")}</p></div></li>
        </ol>}
        {mac ? <p className="install-note">{t("Приложение пока без нотариализации Apple. При первом запуске может потребоваться разрешение в «Системные настройки → Конфиденциальность и безопасность». Используй только официальный релиз.")}</p> : <p className="install-note">{t("Если кнопки «Выполнить в любом случае» нет, не меняй защиту Windows. На рабочем компьютере обратись к администратору.")} <a href="https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation" target="_blank" rel="noreferrer">{t("О SmartScreen — Microsoft")}<ArrowUpRight size={12} aria-hidden="true" /></a></p>}
      </section>

      <section id="questions" className="faq section-wrap" aria-labelledby="faq-title"><div className="faq-heading"><p className="eyebrow">{t("ПЕРЕД ПЕРВЫМ ЗАПУСКОМ")}</p><h2 id="faq-title">{t("Пара вопросов.")}</h2><p>{t("Будем рады вашим идеям и обратной связи.")}<br /><a href="https://t.me/+6sT2ocaKvLxlNmRi" target="_blank" rel="noopener noreferrer">{t("Присоединяйтесь к нашей группе в Telegram")} <ArrowUpRight size={15} aria-hidden="true" /></a></p></div><div className="faq-list">
        <details><summary>{t("Какая система нужна?")}<Plus size={20} aria-hidden="true" /></summary><p>{t("Windows 10/11 x64 или macOS 14+ на Apple Silicon и Intel. .NET включён в сборки. Каталог игр содержит версии для Windows.")}</p></details>
        <details><summary>{t("Как установить Качалку?")}<Plus size={20} aria-hidden="true" /></summary><p>{t(mac ? "Открой DMG и перетащи Ka4alka в папку «Программы» (Applications)." : "Нажми «Скачать для Windows», открой установщик и следуй его шагам. При первом запуске выбери папку для загрузок. Если предпочитаешь ZIP, распакуй архив целиком и запусти Kachalka.exe, оставив остальные файлы рядом.")}</p></details>
        <details><summary>{t("Нужен отдельный торрент-клиент?")}<Plus size={20} aria-hidden="true" /></summary><p>{t("Нет. Качалка сама работает с magnet-ссылками и .torrent-файлами: добавляет задачи, скачивает и позволяет управлять очередью внутри приложения.")}</p></details>
        <details><summary>{t("Как получать новые версии?")}<Plus size={20} aria-hidden="true" /></summary><p>{t("Приложение проверяет обновления с GitHub. Управлять проверкой и установкой можно в настройках. Очередь и пользовательские настройки сохраняются при обновлении.")}</p></details>
      </div></section>
    </main>
    <footer className="site-footer section-wrap"><a className="brand" href="#"><img src="/brand/logo.png" width="32" height="32" alt="" /><span>{t("Качалка")}</span></a><p>{t("Твой вечер. Играй, смотри, качай.")}</p><a className="footer-github" href="https://github.com/peregond/ka4alka" target="_blank" rel="noreferrer"><Code2 size={17} aria-hidden="true" />{t("Исходный код")}<ArrowUpRight size={14} aria-hidden="true" /></a></footer>
  </>;
}

