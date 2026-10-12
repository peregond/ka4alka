import { env } from "cloudflare:workers";
export async function GET() {
  try {
    if (!env.DB) throw new Error("Database unavailable");
    await env.DB.prepare("SELECT 1 AS ready").first();
    return Response.json({ status: "ok" }, { headers: { "Cache-Control": "no-store" } });
  } catch { return Response.json({ status: "unavailable" }, { status: 503 }); }
}
