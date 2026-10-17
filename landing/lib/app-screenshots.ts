import type { SiteLanguage } from "./site-language";

// Capture the verified published binary; English is also used on the Spanish site.
export const appScreenshotsVersion = "0.43.4";
export function appScreenshot(language: SiteLanguage, file: string): string {
  return `/app/${appScreenshotsVersion}/${language === "ru" ? "ru" : "en"}/${file}`;
}
