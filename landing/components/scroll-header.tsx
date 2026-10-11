"use client";

import { useEffect, useState } from "react";
import { ArrowDownToLine } from "lucide-react";

export function ScrollHeader() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const update = () => setVisible(window.scrollY > 8);
    update();
    window.addEventListener("scroll", update, { passive: true });
    return () => window.removeEventListener("scroll", update);
  }, []);

  return <header className="site-header" data-visible={visible} inert={!visible}>
    <div className="nav-inner">
      <a className="brand" href="#" aria-label="Качалка — на главную"><img src="/brand/logo.png" width="40" height="40" alt="" /><span>Качалка<span className="brand-platform">для Windows</span></span></a>
      <nav className="desktop-nav" aria-label="Основная навигация"><a href="#features">Возможности</a><a href="#interface">Интерфейс</a><a href="#installation">Установка</a></nav>
      <a className="button button-small" href="/download"><ArrowDownToLine size={16} aria-hidden="true" />Скачать<span className="nav-windows"> для Windows</span></a>
    </div>
  </header>;
}
