import { getLatestRelease } from "@/lib/app-release";

export async function GET(request: Request) {
  const format = new URL(request.url).searchParams.get("format") ?? "installer";
  if (format !== "installer" && format !== "zip") return Response.json({ error: "Неизвестный формат загрузки." }, { status: 400 });
  const release = await getLatestRelease();
  return new Response(null, { status: 302, headers: { Location: format === "zip" ? release.zipUrl : release.installerUrl, "Cache-Control": "no-store" } });
}
