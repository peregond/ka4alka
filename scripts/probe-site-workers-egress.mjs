// The site is itself a Cloudflare Worker. Its live answers show whether the
// trackers answer requests that leave from Cloudflare Workers, like the relay's.
const site = "https://ka4alka-online-new.peregon.chatgpt.site";
for (const id of ["series:dyuna-prorochestvo", "movies:bratya-po-oruzhiyu-nasledie", "movies:mangust-2026", "movies:obekt-prestupleniya"]) {
  const response = await fetch(`${site}/api/releases?id=${encodeURIComponent(id)}`);
  const data = await response.json();
  const counts = {}, newest = {};
  for (const item of data.items ?? []) { counts[item.source] = (counts[item.source] ?? 0) + 1; newest[item.source] = Math.max(newest[item.source] ?? 0, item.indexedAt ?? 0); }
  console.log(`${id}: ${response.status}, updated=${data.updated}, ${data.items?.length ?? 0} rows`);
  for (const [source, count] of Object.entries(counts)) console.log(`  ${source}: ${count}, newest ${Math.round((Date.now() - newest[source]) / 3600_000)} h ago`);
}
