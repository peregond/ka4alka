"use client";
import { ArrowDownToLine, Monitor, Apple } from "lucide-react";
import type { AppRelease } from "@/lib/app-release";
import { useSiteLanguage } from "@/components/site-language";
import { useSitePlatform } from "@/components/site-platform";
export function PlatformDownload({ release }: { release: AppRelease }) {
  const { platform, architecture, setPlatform } = useSitePlatform();
  const { t } = useSiteLanguage();
  const mac = platform === "macos";
  return <div className="platform-download" data-platform={platform}>
    {mac ? release.mac ? architecture ? <a className="button button-download" href={`/download?format=mac-${architecture}`}><Apple size={21} />{t("Скачать для macOS")}<ArrowDownToLine size={21} /></a> : <details className="mac-package-picker"><summary className="button button-download"><Apple size={21} />{t("Скачать для macOS")}<ArrowDownToLine size={21} /></summary><div className="mac-package-options"><p>{t("Выбери процессор Mac")}</p><a href="/download?format=mac-arm64">Apple Silicon · M-series</a><a href="/download?format=mac-x64">Intel</a><small>{t("Процессор указан в меню Apple → «Об этом Mac».")}</small></div></details> : <a className="button button-download" href={release.releaseUrl}>{t("Все версии для macOS")}</a> : <a className="button button-download" href="/download?format=installer"><Monitor size={21} />{t("Скачать для Windows")}<ArrowDownToLine size={21} /></a>}
    <a className="button button-alternative" href={mac ? "/download?format=installer" : "/download?format=mac"} onClick={mac ? undefined : event => { event.preventDefault(); setPlatform("macos"); }}>{mac ? <Monitor size={18} /> : <Apple size={18} />}{t(mac ? "Скачать для Windows" : "Версия для macOS")}</a>
  </div>;
}
export function DownloadMeta({ release }: { release: AppRelease }) {
  const { platform, architecture } = useSitePlatform(); const { t } = useSiteLanguage();
  return <p className="download-meta">{t("Бесплатно и без рекламы")}<span />{platform === "macos" ? <>macOS 14+ · {architecture === "arm64" ? "Apple Silicon" : architecture === "x64" ? "Intel" : "Apple Silicon / Intel"}<span />DMG</> : <>Windows 10 / 11 · x64<span />{t("Установщик ·")} {Math.round(release.installerSize / 1_000_000)} {t("МБ")}</>}</p>;
}
