import { imageUrl } from "@/lib/tmdb-backdrop";

export const runtime = "edge";
// Fixed TMDB image host and size; this route cannot proxy arbitrary destinations.
export async function GET(request: Request) {
  const path = new URL(request.url).searchParams.get("file");
  const url = imageUrl(path);
  if (!url) return new Response(null, { status: 400 });
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(6000), redirect: "manual" });
    if (!response.ok) return new Response(null, { status: 502 });
    if (Number(response.headers.get("content-length") ?? 0) > 2 * 1024 * 1024) return new Response(null, { status: 413 });
    const reader = response.body?.getReader(); if (!reader) return new Response(null, { status: 502 });
    const chunks: Uint8Array[] = []; let size = 0;
    try {
      for (;;) { const next = await reader.read(); if (next.done) break; size += next.value.length; if (size > 2 * 1024 * 1024) return new Response(null, { status: 413 }); chunks.push(next.value); }
    } finally { await reader.cancel(); }
    const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    if (bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff) return new Response(null, { status: 502 });
    return new Response(bytes, { headers: { "Content-Type": "image/jpeg", "Cache-Control": "public, max-age=86400, s-maxage=604800", "X-Content-Type-Options": "nosniff" } });
  } catch { return new Response(null, { status: 502 }); }
}
