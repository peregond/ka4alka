export type AppPlatform = "windows" | "macos";
export type MacArchitecture = "arm64" | "x64" | null;
export type PlatformNavigator = { userAgent?: string; platform?: string; maxTouchPoints?: number; userAgentData?: { platform?: string } };
export function detectPlatform(value: PlatformNavigator): AppPlatform | null {
  const ua = value.userAgent ?? "";
  if (/Android|iPhone|iPad|iPod|CrOS/i.test(ua) || (/Mac/i.test(value.platform ?? "") && (value.maxTouchPoints ?? 0) > 1)) return null;
  const hint = value.userAgentData?.platform ?? "";
  if (/^Windows$/i.test(hint)) return "windows";
  if (/^macOS$/i.test(hint)) return "macos";
  if (/Windows NT|Win32|Win64/i.test(ua + " " + (value.platform ?? ""))) return "windows";
  if (/Macintosh|MacIntel|MacPPC/i.test(ua + " " + (value.platform ?? ""))) return "macos";
  return null;
}
export function macArchitecture(value: unknown): MacArchitecture {
  return value === "arm" || value === "arm64" ? "arm64" : value === "x86" || value === "x86_64" ? "x64" : null;
}
