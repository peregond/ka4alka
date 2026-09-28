export const runtime = "edge";

export async function GET(request: Request) {
  const raw = new URL(request.url).searchParams.get("src");
  if (!raw) return new Response(null, { status: 400 });

  let source: URL;
  try { source = new URL(raw); } catch { return new Response(null, { status: 400 }); }
  if (source.protocol !== "https:" || !/^img[1-4]\.zonapic\.com$/.test(source.hostname) ||
      !/^\/images\/film_\d+\/\d+\/\d+\.jpe?g$/.test(source.pathname) || source.search) {
    return new Response(null, { status: 400 });
  }

  try {
    const upstream = await fetch(source.href, { signal: AbortSignal.timeout(8000) });
    if (!upstream.ok) {
      return new Response(null, { status: 404 });
    }
    const length = Number(upstream.headers.get("content-length") ?? 0);
    if (length > 2_000_000) return new Response(null, { status: 413 });
    const body = await upstream.arrayBuffer();
    if (body.byteLength > 2_000_000) return new Response(null, { status: 413 });
    const signature = new Uint8Array(body, 0, Math.min(3, body.byteLength));
    if (signature[0] !== 0xff || signature[1] !== 0xd8 || signature[2] !== 0xff) return new Response(null, { status: 404 });
    return new Response(body, { headers: { "Content-Type": "image/jpeg", "Cache-Control": "public, max-age=86400, s-maxage=604800" } });
  } catch {
    return new Response(null, { status: 502 });
  }
}
