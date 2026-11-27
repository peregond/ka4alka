"use client";

import { Apple, ArrowDownToLine, ArrowUpRight, ChevronDown, Monitor, Tv } from "lucide-react";
import type { AppRelease } from "@/lib/app-release";
import { useSiteLanguage } from "@/components/site-language";
import { useSitePlatform } from "@/components/site-platform";

export function PlatformDownload({ release }: { release: AppRelease }) {
  const { detected, architecture } = useSitePlatform();
  const { t } = useSiteLanguage();

  const macLabel = <>
    <span className="download-tile-icon"><Apple size={22} aria-hidden="true" /></span>
    <span className="download-tile-label">
      <strong>macOS</strong>
      <small>{release.mac ? "macOS 14+ · DMG" : t("Все версии для macOS")}</small>
    </span>
  </>;

  return <div className="download-tiles" role="group" aria-label={t("Скачать Качалку")}>
    <a className="download-tile" data-current={detected === "windows"} href="/download?format=installer" aria-label={t("Скачать для Windows")}>
      <span className="download-tile-icon"><Monitor size={22} aria-hidden="true" /></span>
      <span className="download-tile-label"><strong>Windows</strong><small>Windows 10 / 11 · x64</small></span>
      <ArrowDownToLine size={18} aria-hidden="true" />
    </a>
    {release.mac && !architecture ? <details className="mac-package-picker">
      <summary className="download-tile" data-current={detected === "macos"} aria-label={t("Скачать для macOS")}>
        {macLabel}
        <ChevronDown size={18} aria-hidden="true" />
      </summary>
      <div className="mac-package-options">
        <p>{t("Выбери процессор Mac")}</p>
        <a href="/download?format=mac-arm64">Apple Silicon · M-series</a>
        <a href="/download?format=mac-x64">Intel</a>
        <small>{t("Процессор указан в меню Apple → «Об этом Mac».")}</small>
      </div>
    </details> : <a className="download-tile" data-current={detected === "macos"}
      href={release.mac ? `/download?format=mac-${architecture}` : release.releaseUrl} aria-label={t("Скачать для macOS")}>
      {macLabel}
      {release.mac ? <ArrowDownToLine size={18} aria-hidden="true" /> : <ArrowUpRight size={18} aria-hidden="true" />}
    </a>}
    <a className="download-tile download-tile-android" href="/download?format=android-tv" aria-label={t("Скачать APK для Android TV")}>
      <span className="download-tile-icon"><Tv size={22} aria-hidden="true" /></span>
      <span className="download-tile-label"><strong>Android TV</strong><small>{t("Скачать APK")}</small></span>
      <ArrowDownToLine size={18} aria-hidden="true" />
    </a>
  </div>;
}
