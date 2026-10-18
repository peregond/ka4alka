import type { SiteLanguage } from "./site-language";
import type { AppPlatform } from "./site-platform";
export const appScreenshotsVersion = "0.43.4";
export const macScreenshotsVersion = "0.44.1";
export const screenshotFiles = {
  windows: ["catalog-dark.webp", "series-dark.webp", "games-dark.webp", "detail-dark.webp", "downloads-dark.webp", "catalog-light.webp"],
  macos: ["catalog-dark.webp", "series-dark.webp", "games-dark.webp", "settings-dark.webp", "downloads-dark.webp", "settings-light.webp"],
};
export function screenshotVersion(platform: AppPlatform): string { return platform === "macos" ? macScreenshotsVersion : appScreenshotsVersion; }
export function appScreenshot(language: SiteLanguage, file: string, platform: AppPlatform = "windows"): string {
  const root = platform === "macos" ? `macos/${macScreenshotsVersion}` : appScreenshotsVersion;
  return `/app/${root}/${language === "ru" ? "ru" : "en"}/${file}`;
}
