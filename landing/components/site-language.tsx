"use client";

import "./site-language.css";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { languageStorageKey, readPreference, resolveLanguage, translate, type LanguagePreference, type SiteLanguage } from "@/lib/site-language";

const LanguageContext = createContext({ language: "ru" as SiteLanguage, preference: "auto" as LanguagePreference, setPreference: (_value: LanguagePreference) => {} });

export function SiteLanguageProvider({ children }: { children: ReactNode }) {
  // Identical server/client initial state keeps hydration deterministic.
  const [preference, setStoredPreference] = useState<LanguagePreference>("auto");
  const [languages, setLanguages] = useState<readonly string[] | null>(null);
  useEffect(() => {
    try { setStoredPreference(readPreference(localStorage.getItem(languageStorageKey))); } catch {}
    const update = () => setLanguages(navigator.languages?.length ? [...navigator.languages] : [navigator.language]);
    const sync = (event: StorageEvent) => { if (event.key === languageStorageKey || event.key === null) setStoredPreference(readPreference(event.newValue)); };
    update();
    window.addEventListener("languagechange", update);
    window.addEventListener("storage", sync);
    return () => { window.removeEventListener("languagechange", update); window.removeEventListener("storage", sync); };
  }, []);
  const language = languages ? resolveLanguage(preference, languages) : "ru";
  useEffect(() => { document.documentElement.lang = language; }, [language]);
  function setPreference(value: LanguagePreference) {
    setStoredPreference(value);
    try { if (value === "auto") localStorage.removeItem(languageStorageKey); else localStorage.setItem(languageStorageKey, value); } catch {}
  }
  return <LanguageContext.Provider value={{ language, preference, setPreference }}>{children}</LanguageContext.Provider>;
}

export function useSiteLanguage() {
  const context = useContext(LanguageContext);
  return { ...context, t: (key: string, values?: Record<string, string | number>) => translate(context.language, key, values) };
}

export function LanguageSwitcher() {
  const { preference, setPreference, t } = useSiteLanguage();
  return <label className="site-language-switcher"><span aria-hidden="true">🌐</span><select aria-label={t("Язык")} value={preference} onChange={event => setPreference(readPreference(event.target.value))}>
    <option value="auto">{t("Автоматически")}</option><option value="ru">Русский</option><option value="en">English</option><option value="es">Español</option>
  </select></label>;
}

export function LocalizedMetadata({ title, description }: { title: string; description?: string }) {
  const { language } = useSiteLanguage();
  useEffect(() => {
    document.title = translate(language, title);
    const tag = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    if (tag && description) tag.content = translate(language, description);
  }, [language, title, description]);
  return null;
}
