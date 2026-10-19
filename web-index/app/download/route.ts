import { getLatestRelease } from "@/lib/app-release";

export async function GET(request: Request) {
  const format = new URL(request.url).searchParams.get("format") ?? "installer";
  if (!["installer", "zip", "mac-arm64", "mac-x64", "mac-arm64-zip", "mac-x64-zip"].includes(format)) return Response.json({ error: "Неизвестный формат загрузки." }, { status: 400 });
  const release = await getLatestRelease();
  const targets: Record<string, string | undefined> = { installer: release.installerUrl, zip: release.zipUrl, "mac-arm64": release.mac?.arm64, "mac-x64": release.mac?.x64, "mac-arm64-zip": release.mac?.arm64Zip, "mac-x64-zip": release.mac?.x64Zip };
  const target = targets[format];
  if (!target) return Response.json({ error: "This platform has no published package yet." }, { status: 404 });
  return new Response(null, { status: 302, headers: { Location: target, "Cache-Control": "no-store" } });
}
