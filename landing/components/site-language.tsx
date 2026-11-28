"use client";

import "./site-language.css";

import { Check } from "lucide-react";
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
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
  const details = useRef<HTMLDetailsElement>(null);
  const trigger = useRef<HTMLElement>(null);
  const choices = [
    { value: "auto", label: t("Автоматически") },
    { value: "ru", label: "Русский", lang: "ru" },
    { value: "en", label: "English", lang: "en" },
    { value: "es", label: "Español", lang: "es" },
  ] as const;

  useEffect(() => {
    function dismiss(event: PointerEvent) {
      const menu = details.current;
      if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) menu.open = false;
    }

    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, []);

  function closeMenu() {
    if (details.current) details.current.open = false;
    trigger.current?.focus();
  }

  return <details
    ref={details}
    className="site-language-switcher"
    onKeyDown={event => {
      if (event.key === "Escape" && event.currentTarget.open) {
        event.preventDefault();
        closeMenu();
      }
    }}
    onBlur={event => {
      if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget)) {
        event.currentTarget.open = false;
      }
    }}
  >
    <summary ref={trigger} aria-label={t("Язык")}>
      <span aria-hidden="true">🌐</span>
    </summary>
    <div className="site-language-options" role="group" aria-label={t("Язык")}>
      {choices.map(choice => <button
        key={choice.value}
        type="button"
        aria-pressed={preference === choice.value}
        onClick={() => {
          setPreference(choice.value);
          closeMenu();
        }}
      >
        <span lang={"lang" in choice ? choice.lang : undefined}>{choice.label}</span>
        {preference === choice.value && <Check size={16} aria-hidden="true" />}
      </button>)}
    </div>
  </details>;
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
