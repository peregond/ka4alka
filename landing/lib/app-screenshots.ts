import type { SiteLanguage } from "./site-language";
import type { AppPlatform } from "./site-platform";
export const appScreenshotsVersion = "0.44.27";
export const macScreenshotsVersion = "0.44.27";
export const screenshotFiles = {
  windows: ["featured-dark.webp", "catalog-dark.webp", "series-dark.webp", "games-dark.webp", "detail-dark.webp", "downloads-dark.webp", "settings-lite-dark.webp", "settings-lan-dark.webp", "catalog-light.webp"],
  macos: ["featured-dark.webp", "catalog-dark.webp", "series-dark.webp", "games-dark.webp", "detail-dark.webp", "downloads-dark.webp", "settings-dark.webp", "catalog-light.webp"],
};
export function screenshotVersion(platform: AppPlatform): string { return platform === "macos" ? macScreenshotsVersion : appScreenshotsVersion; }
export function appScreenshot(language: SiteLanguage, file: string, platform: AppPlatform = "windows"): string {
  const root = platform === "macos" ? `macos/${macScreenshotsVersion}` : appScreenshotsVersion;
  return `/app/${root}/${language === "ru" ? "ru" : "en"}/${file}`;
}
