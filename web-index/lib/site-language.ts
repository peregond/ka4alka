import ru from "./locales/ru.json";
import en from "./locales/en.json";
import es from "./locales/es.json";

export type SiteLanguage = "ru" | "en" | "es";
export type LanguagePreference = SiteLanguage | "auto";
export const languageStorageKey = "ka4alka.site-language";
export const dictionaries = { ru, en, es };

export function normalizeLanguage(value: unknown): SiteLanguage | null {
  if (typeof value !== "string") return null;
  const base = value.trim().toLowerCase().split(/[-_]/)[0];
  return base === "ru" || base === "en" || base === "es" ? base : null;
}

export function readPreference(value: unknown): LanguagePreference {
  return value === "ru" || value === "en" || value === "es" ? value : "auto";
}

export function resolveLanguage(preference: LanguagePreference, languages: readonly string[]): SiteLanguage {
  if (preference !== "auto") return preference;
  for (const value of languages) {
    const language = normalizeLanguage(value);
    if (language) return language;
  }
  return "en";
}

export function translate(language: SiteLanguage, key: string, values: Record<string, string | number> = {}): string {
  const entries: Record<string, string> = dictionaries[language];
  return (entries[key] ?? key).replace(/\{(\w+)\}/g, (placeholder, name: string) => values[name] === undefined ? placeholder : String(values[name]));
}

export function catalogCount(language: SiteLanguage, count: number, section: "movies" | "series"): string {
  if (language === "ru") {
    const form = new Intl.PluralRules("ru").select(count);
    const forms = section === "movies" ? { one: "фильм", few: "фильма", other: "фильмов" } : { one: "сериал", few: "сериала", other: "сериалов" };
    return `${count} ${form === "one" ? forms.one : form === "few" ? forms.few : forms.other}`;
  }
  const noun = language === "es" ? (section === "movies" ? (count === 1 ? "película" : "películas") : (count === 1 ? "serie" : "series")) : (section === "movies" ? (count === 1 ? "movie" : "movies") : (count === 1 ? "TV show" : "TV shows"));
  return `${new Intl.NumberFormat(language).format(count)} ${noun}`;
}

export function downloadSize(language: SiteLanguage, bytes: number | null): string {
  if (!bytes || bytes <= 0) return translate(language, "Размер не указан");
  const gb = bytes >= 1073741824;
  const value = gb ? bytes / 1073741824 : Math.round(bytes / 1048576);
  const unit = language === "ru" ? (gb ? "ГБ" : "МБ") : (gb ? "GB" : "MB");
  return `${new Intl.NumberFormat(language, { minimumFractionDigits: gb ? 1 : 0, maximumFractionDigits: gb ? 1 : 0 }).format(value)} ${unit}`;
}
