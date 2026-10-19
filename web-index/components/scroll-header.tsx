"use client";

import { useEffect, useState } from "react";
import { useSitePlatform } from "@/components/site-platform";
import { ArrowDownToLine } from "lucide-react";
import { LanguageSwitcher, useSiteLanguage } from "@/components/site-language";

export function ScrollHeader() {
  const [visible, setVisible] = useState(false);
  const { t } = useSiteLanguage();
  const { platform } = useSitePlatform();
  const mac = platform === "macos";

  useEffect(() => {
    const update = () => setVisible(window.scrollY > 8);
    update();
    window.addEventListener("scroll", update, { passive: true });
    return () => window.removeEventListener("scroll", update);
  }, []);

  return <header className="site-header" data-visible={visible} inert={!visible}>
    <div className="nav-inner">
      <a className="brand" href="#" aria-label={t("Качалка — на главную")}><img src="/brand/logo.png" width="40" height="40" alt="" /><span>{t("Качалка")}<span className="brand-platform">{mac ? "macOS" : "Windows"}</span></span></a>
      <nav className="desktop-nav" aria-label={t("Основная навигация")}><a href="#features">{t("Возможности")}</a><a href="#interface">{t("Интерфейс")}</a><a href="#installation">{t("Установка")}</a></nav>
      <div className="header-actions"><LanguageSwitcher /><a className="button button-small" href={mac ? "#download" : "/download?format=installer"}><ArrowDownToLine size={16} aria-hidden="true" />{t("Скачать")}<span className="nav-windows"> {mac ? "macOS" : "Windows"}</span></a></div>
    </div>
  </header>;
}

