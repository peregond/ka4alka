import { getLatestRelease } from "@/lib/app-release";

export async function GET(request: Request) {
  const format = new URL(request.url).searchParams.get("format") ?? "installer";
  if (format === "mac") {
    const release = await getLatestRelease();
    if (!release.mac) return Response.redirect(release.releaseUrl, 302);
    return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Ka4alka for Mac</title><style>body{font:18px system-ui;background:#101919;color:white;max-width:620px;margin:12vh auto;padding:24px}a{display:block;color:#79e0bf;padding:20px;border:1px solid #496659;border-radius:12px;margin:16px 0}p{line-height:1.6}</style><h1>Ka4alka for macOS</h1><p>Choose your Mac processor. Find it in Apple menu → About This Mac.</p><a href="${release.mac.arm64}">Apple Silicon · M-series</a><a href="${release.mac.x64}">Intel</a><a href="/">← Ka4alka</a></html>`, { headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" } });
  }
  if (!["installer", "zip", "mac-arm64", "mac-x64", "mac-arm64-zip", "mac-x64-zip"].includes(format)) return Response.json({ error: "Неизвестный формат загрузки." }, { status: 400 });
  const release = await getLatestRelease();
  const targets: Record<string, string | undefined> = { installer: release.installerUrl, zip: release.zipUrl, "mac-arm64": release.mac?.arm64, "mac-x64": release.mac?.x64, "mac-arm64-zip": release.mac?.arm64Zip, "mac-x64-zip": release.mac?.x64Zip };
  const target = targets[format];
  if (!target) return Response.json({ error: "This platform has no published package yet." }, { status: 404 });
  return new Response(null, { status: 302, headers: { Location: target, "Cache-Control": "no-store" } });
}
