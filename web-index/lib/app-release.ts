export type AppRelease = { version: string; installerUrl: string; zipUrl: string; releaseUrl: string; installerSize: number };

const fallback: AppRelease = {
  version: "0.43.4",
  installerUrl: "https://github.com/peregond/ka4alka/releases/download/v0.43.4/Kachalka-Setup-0.43.4.exe",
  zipUrl: "https://github.com/peregond/ka4alka/releases/download/v0.43.4/Kachalka-win-x64.zip",
  releaseUrl: "https://github.com/peregond/ka4alka/releases/tag/v0.43.4",
  installerSize: 72898428,
};
let cached = fallback;
let expires = 0;
let pending: Promise<AppRelease> | null = null;

export function releaseAssetUrl(value: unknown, tag: string): string | null {
  if (typeof value !== "string") return null;
  try {
    const url = new URL(value);
    return url.origin === "https://github.com" && !url.username && !url.password && !url.search && !url.hash &&
      url.pathname.startsWith(`/peregond/ka4alka/releases/download/${tag}/`) ? url.href : null;
  } catch { return null; }
}

export async function getLatestRelease(): Promise<AppRelease> {
  if (expires > Date.now()) return cached;
  if (pending) return pending;
  pending = (async () => {
    try {
      const response = await fetch("https://api.github.com/repos/peregond/ka4alka/releases/latest", {
        headers: { Accept: "application/vnd.github+json", "User-Agent": "Kachalka-Website" },
        signal: AbortSignal.timeout(4000), redirect: "manual",
      });
      if (!response.ok || Number(response.headers.get("content-length") ?? 0) > 1_000_000) throw new Error("Release unavailable");
      const text = await response.text();
      if (text.length > 1_000_000) throw new Error("Release too large");
      const release = JSON.parse(text);
      const tag = release.tag_name;
      if (typeof tag !== "string" || !/^v\d+\.\d+\.\d+$/.test(tag) || release.draft || release.prerelease || !Array.isArray(release.assets)) throw new Error("Invalid release");
      const installer = release.assets.find((asset: { name?: string }) => asset.name === `Kachalka-Setup-${tag.slice(1)}.exe`);
      const zip = release.assets.find((asset: { name?: string }) => asset.name === "Kachalka-win-x64.zip");
      const installerUrl = releaseAssetUrl(installer?.browser_download_url, tag);
      const zipUrl = releaseAssetUrl(zip?.browser_download_url, tag);
      if (!installerUrl || !zipUrl || !Number.isSafeInteger(installer.size) || installer.size <= 0) throw new Error("Missing Windows assets");
      cached = { version: tag.slice(1), installerUrl, zipUrl, installerSize: installer.size, releaseUrl: `https://github.com/peregond/ka4alka/releases/tag/${tag}` };
      expires = Date.now() + 30 * 60_000;
    } catch { expires = Date.now() + 5 * 60_000; }
    return cached;
  })().finally(() => { pending = null; });
  return pending;
}
