"use client";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { detectPlatform, macArchitecture, type AppPlatform, type MacArchitecture } from "@/lib/site-platform";
import { useSiteLanguage } from "@/components/site-language";
const PlatformContext = createContext({ platform: "windows" as AppPlatform, detected: null as AppPlatform | null, ready: false, architecture: null as MacArchitecture, setPlatform: (_value: AppPlatform) => {} });
export function SitePlatformProvider({ children }: { children: ReactNode }) {
  const [detected, setDetected] = useState<AppPlatform | null>(null);
  const [selected, setSelected] = useState<AppPlatform | null>(null);
  const [ready, setReady] = useState(false);
  const [architecture, setArchitecture] = useState<MacArchitecture>(null);
  useEffect(() => {
    const os = detectPlatform(navigator);
    setDetected(os); setReady(true);
    // Safari's MacIntel platform value is also used on Apple Silicon. Only an
    // explicit architecture client hint can select a package automatically.
    const hints = (navigator as Navigator & { userAgentData?: { getHighEntropyValues?: (keys: string[]) => Promise<{ architecture?: string }> } }).userAgentData;
    let cancelled = false;
    if (os === "macos" && hints?.getHighEntropyValues) {
      hints.getHighEntropyValues(["architecture"]).then(value => { if (!cancelled) setArchitecture(macArchitecture(value.architecture)); }).catch(() => {});
    }
    return () => { cancelled = true; };
  }, []);
  return <PlatformContext.Provider value={{ platform: selected ?? detected ?? "windows", detected, ready, architecture: detected === "macos" ? architecture : null, setPlatform: setSelected }}>{children}</PlatformContext.Provider>;
}
export function useSitePlatform() { return useContext(PlatformContext); }
export function PlatformSwitcher() {
  const { platform, setPlatform, ready, detected } = useSitePlatform();
  const { t } = useSiteLanguage();
  return <div className="platform-switcher" role="group" aria-label={t("Версия приложения")}>
    <span>{t("Посмотреть интерфейс:")}</span>
    {(["windows", "macos"] as const).map(os => <button type="button" key={os} aria-pressed={ready && platform === os} onClick={() => setPlatform(os)}>{os === "windows" ? "Windows" : "macOS"}{ready && detected === os && <small>{t("Ваша система")}</small>}</button>)}
  </div>;
}
