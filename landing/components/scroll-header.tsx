"use client";

import { useEffect, useState } from "react";
import { ArrowDownToLine } from "lucide-react";
import { LanguageSwitcher, useSiteLanguage } from "@/components/site-language";

export function ScrollHeader() {
  const [visible, setVisible] = useState(false);
  const { t } = useSiteLanguage();

  useEffect(() => {
    const update = () => setVisible(window.scrollY > 8);
    update();
    window.addEventListener("scroll", update, { passive: true });
    return () => window.removeEventListener("scroll", update);
  }, []);

  return <header className="site-header" data-visible={visible}>
    <div className="nav-inner section-wrap">
      <a className="brand" href="#" aria-label={t("Качалка — на главную")}><img src="/brand/logo.png" width="36" height="36" alt="" /><span>{t("Качалка")}</span></a>
      <nav className="desktop-nav" inert={!visible} aria-label={t("Основная навигация")}><a href="#features">{t("Возможности")}</a><a href="#interface">{t("Интерфейс")}</a><a href="#installation">{t("Установка")}</a></nav>
      <div className="header-actions">
        <a className="button button-small header-download" href="#download" inert={!visible}><ArrowDownToLine size={16} aria-hidden="true" />{t("Скачать")}</a>
        <LanguageSwitcher />
      </div>
    </div>
  </header>;
}

