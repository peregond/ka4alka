import { ArrowDown, ArrowDownToLine, ArrowRight, ArrowUpRight, Check, Code2, Laptop, Monitor, Moon, Network, Plus, Search, Settings2, ShieldCheck } from "lucide-react";
import { ScrollHeader } from "@/components/scroll-header";
import { ProductPreview } from "@/components/product-preview";
import { getLatestRelease } from "@/lib/app-release";

export const dynamic = "force-dynamic";

function SavedHeartIcon() {
  return <svg width="23" height="23" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 21 C9 18 3 14 3 8 C3 2 10 2 12 7 C14 2 21 2 21 8 C21 14 15 18 12 21 Z" /></svg>;
}

function TelegramIcon() {
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M21.4 3.3 2.9 10.5c-1.3.5-1.3 1.2-.2 1.5l4.7 1.5 1.8 5.4c.2.6.1.8.7.8.4 0 .6-.2.8-.4l2.3-2.2 4.8 3.5c.9.5 1.6.2 1.8-.8l3.1-14.6c.3-1.3-.5-1.9-1.3-1.4ZM8.2 13.1l10.5-6.6c.5-.3.9-.1.6.2l-8.5 7.7-.3 3.2-2.3-4.5Z" /></svg>;
}

function WindowsIcon({ size = 21 }: { size?: number }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M2 3.7 10.7 2.5v8.7H2zm10.3-1.5L22 1v10.2h-9.7zM2 12.8h8.7v8.7L2 20.3zm10.3 0H22V23l-9.7-1.2z" /></svg>;
}

export default async function Home() {
  const release = await getLatestRelease();
  const size = Math.round(release.installerSize / 1_000_000);
  return <>
    <a className="skip-link" href="#main">Перейти к содержимому</a>
    <ScrollHeader />
    <main id="main">
      <section className="hero section-wrap" aria-labelledby="hero-title">
        <a className="release-badge" href={release.releaseUrl} target="_blank" rel="noreferrer"><span className="status-dot" />Версия {release.version}<span className="badge-divider" />Уже на GitHub<ArrowUpRight size={13} aria-hidden="true" /></a>
        <h1 id="hero-title">Твой вечер.<br /><span>Твоё кино. Качалка.</span></h1>
        <p className="hero-description">Фильмы, сериалы и загрузки — в одном приложении для Windows.<br className="desktop-break" /> Найди, что посмотреть, выбери раздачу и добавь в очередь.</p>
        <div className="hero-actions"><a className="button button-download" href="/download"><WindowsIcon />Скачать для Windows<ArrowDownToLine size={21} aria-hidden="true" /></a><a className="button button-telegram" href="https://t.me/ka4alkacc" target="_blank" rel="noopener noreferrer"><TelegramIcon />Мы в Telegram<ArrowUpRight size={16} aria-hidden="true" /></a></div>
        <div className="hero-feature-link"><a className="button-secondary" href="#features">Что умеет Качалка<ArrowDown size={17} aria-hidden="true" /></a></div>
        <p className="download-meta">Бесплатно и без рекламы<span />Windows 10 / 11 · x64<span />Установщик · {size} МБ</p>
        <ProductPreview />
        <div className="product-facts"><span><Check size={17} aria-hidden="true" />Бесплатно и без рекламы</span><span><Code2 size={16} aria-hidden="true" />Открытый исходный код</span><span><Check size={17} aria-hidden="true" />Без регистрации</span></div>
      </section>

      <section id="features" className="features section-wrap" aria-labelledby="features-title">
        <div className="section-heading"><div><p className="eyebrow">ОТ ВЫБОРА ДО ЗАГРУЗКИ</p><h2 id="features-title">Меньше переключений.<br />Больше хорошего кино.</h2></div><p className="section-description">Каталог, поиск раздач и торрент-клиент работают вместе. Всё нужное — под рукой.</p></div>
        <div className="steps-grid">
          <article className="feature-step"><div className="step-top"><span>01 / НАЙДИ</span><Search size={23} aria-hidden="true" /></div><h3>Что посмотреть?</h3><p>Фильмы и сериалы, подборки, оценки и описания. Ищи по названию или выбирай по жанру, году, стране и рейтингу.</p><div className="feature-tags"><span>Фильмы</span><span>Сериалы</span><span>Подборки</span></div></article>
          <article className="feature-step"><div className="step-top"><span>02 / ВЫБЕРИ</span><Settings2 size={23} aria-hidden="true" /></div><h3>Твоя раздача.</h3><p>Сравни качество, размер и число сидов. Отфильтруй озвучку, субтитры и кодек, чтобы загрузить подходящий вариант.</p><div className="feature-tags"><span>HD</span><span>Full HD</span><span>4K</span></div></article>
          <article className="feature-step"><div className="step-top"><span>03 / ЗАГРУЗИ</span><ArrowDownToLine size={23} aria-hidden="true" /></div><h3>Дальше — очередь.</h3><p>Скачивай через magnet и .torrent прямо в Качалке. Следи за скоростью и прогрессом, ставь на паузу и продолжай позже.</p><div className="feature-tags"><span>Magnet</span><span>.torrent</span><span>Пауза / продолжение</span></div></article>
        </div>
      </section>

      <section className="comfort section-wrap" aria-labelledby="comfort-title">
        <div className="comfort-heading"><p className="eyebrow">ПРОДУМАНО ДЛЯ КАЖДОГО ДНЯ</p><h2 id="comfort-title">Как удобно тебе.</h2><p>Сохрани интересное. Настрой своё. Вернись к тому, на чём остановился.</p></div>
        <div className="comfort-grid">
          <article className="comfort-item"><SavedHeartIcon /><h3>Не потеряй находку</h3><p>Добавляй фильмы и сериалы в «Сохранённое» — всё, что хочется посмотреть, останется в одном списке.</p></article>
          <article className="comfort-item"><Moon size={23} aria-hidden="true" /><h3>Светлая или тёмная</h3><p>Выбирай тему в настройках. Приложение запомнит её вместе с папкой загрузок и лимитами скорости.</p></article>
          <article className="comfort-item"><ShieldCheck size={23} aria-hidden="true" /><h3>Продолжай с того же места</h3><p>Очередь сохраняется между запусками. Активные задачи можно продолжать автоматически, а ручные паузы остаются на паузе.</p></article>
        </div>
        <div className="lan-feature">
          <div className="lan-copy"><p className="eyebrow"><Network size={16} aria-hidden="true" />СВЯЗАННЫЕ УСТРОЙСТВА</p><h3>Нашёл на ноутбуке.<br />Скачал на другом ПК.</h3><p>Свяжи две Качалки в одной локальной сети и отправляй раздачу на другой компьютер. Он загрузит файлы в свою папку. Сопряжение подтверждается на обоих устройствах.</p></div>
          <div className="device-diagram" aria-label="Раздача передаётся с ноутбука на домашний компьютер в одной локальной сети"><div className="device"><div className="device-screen"><Laptop size={42} strokeWidth={1.2} aria-hidden="true" /><span className="device-indicator" /></div><span>Ноутбук</span></div><div className="device-connection"><span className="connection-line" /><ArrowRight size={20} aria-hidden="true" /><small>Одна сеть</small></div><div className="device"><div className="device-screen destination"><Monitor size={42} strokeWidth={1.2} aria-hidden="true" /></div><span>Домашний ПК</span></div></div>
        </div>
      </section>

      <section id="download" className="download-section section-wrap" aria-labelledby="download-title">
        <div className="download-panel"><img className="download-app-icon" src="/brand/logo.png" alt="" width="88" height="88" /><p className="eyebrow">КАЧАЛКА ДЛЯ WINDOWS</p><h2 id="download-title">Пора выбрать кино.</h2><p>Установи приложение, выбери папку для загрузок —<br className="desktop-break" /> и начинай собирать свой вечер.</p><a className="button button-download" href="/download"><WindowsIcon />Скачать для Windows<ArrowDownToLine size={21} aria-hidden="true" /></a><div className="download-options"><span>Версия {release.version} · Windows x64</span><a href="/download?format=zip">Скачать ZIP<ArrowUpRight size={14} aria-hidden="true" /></a><a href={release.releaseUrl} target="_blank" rel="noreferrer">Что нового<ArrowUpRight size={14} aria-hidden="true" /></a></div></div>
      </section>

      <section id="installation" className="installation section-wrap" aria-labelledby="installation-title">
        <p className="eyebrow">НЕСКОЛЬКО ПРОСТЫХ ШАГОВ</p><h2 id="installation-title">Как установить.</h2>
        <ol className="install-steps">
          <li><span className="install-number">1</span><div><h3>Скачай установщик</h3><p>Нажми «Скачать для Windows». Файл загрузится из официального GitHub Качалки.</p></div></li>
          <li><span className="install-number">2</span><div><h3>Открой файл</h3><p>В папке «Загрузки» дважды нажми на Kachalka-Setup-{release.version}.exe.</p></div></li>
          <li><span className="install-number">3</span><div><h3>Если появится SmartScreen</h3><p>В окне «Система Windows защитила ваш компьютер» нажми <strong>«Подробнее»</strong>, затем <strong>«Выполнить в любом случае»</strong>. Продолжай только если файл скачан из официального релиза Качалки и ты доверяешь приложению.</p><div className="install-clicks"><span>Подробнее</span><ArrowRight size={16} aria-hidden="true" /><span>Выполнить в любом случае</span></div></div></li>
          <li><span className="install-number">4</span><div><h3>Заверши установку</h3><p>Следуй шагам установщика. Запусти Качалку и выбери папку для загрузок — готово.</p></div></li>
        </ol>
        <p className="install-note">Если кнопки «Выполнить в любом случае» нет, не меняй защиту Windows. На рабочем компьютере обратись к администратору. <a href="https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation" target="_blank" rel="noreferrer">О SmartScreen — Microsoft<ArrowUpRight size={12} aria-hidden="true" /></a></p>
      </section>

      <section id="questions" className="faq section-wrap" aria-labelledby="faq-title"><div className="faq-heading"><p className="eyebrow">ПЕРЕД ПЕРВЫМ ЗАПУСКОМ</p><h2 id="faq-title">Пара вопросов.</h2><p>Будем рады вашим идеям и обратной связи.<br /><a href="https://t.me/+6sT2ocaKvLxlNmRi" target="_blank" rel="noopener noreferrer">Присоединяйтесь к нашей группе в Telegram <ArrowUpRight size={15} aria-hidden="true" /></a></p></div><div className="faq-list">
        <details><summary>Какая Windows нужна?<Plus size={20} aria-hidden="true" /></summary><p>Windows 10 или Windows 11, 64-разрядная версия (x64). В сборку включён .NET — устанавливать его отдельно не нужно.</p></details>
        <details><summary>Как установить Качалку?<Plus size={20} aria-hidden="true" /></summary><p>Нажми «Скачать для Windows», открой установщик и следуй его шагам. При первом запуске выбери папку для загрузок. Если предпочитаешь ZIP, распакуй архив целиком и запусти Kachalka.exe, оставив остальные файлы рядом.</p></details>
        <details><summary>Нужен отдельный торрент-клиент?<Plus size={20} aria-hidden="true" /></summary><p>Нет. Качалка сама работает с magnet-ссылками и .torrent-файлами: добавляет задачи, скачивает и позволяет управлять очередью внутри приложения.</p></details>
        <details><summary>Как получать новые версии?<Plus size={20} aria-hidden="true" /></summary><p>Приложение проверяет обновления с GitHub. Управлять проверкой и установкой можно в настройках. Очередь и пользовательские настройки сохраняются при обновлении.</p></details>
      </div></section>
    </main>
    <footer className="site-footer section-wrap"><a className="brand" href="#"><img src="/brand/logo.png" width="32" height="32" alt="" /><span>Качалка</span></a><p>Твой вечер. Твоё кино.</p><a className="footer-github" href="https://github.com/peregond/ka4alka" target="_blank" rel="noreferrer"><Code2 size={17} aria-hidden="true" />Исходный код<ArrowUpRight size={14} aria-hidden="true" /></a></footer>
  </>;
}
