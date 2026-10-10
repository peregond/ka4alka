// Runs against the standalone relay started locally by wrangler: real searches,
// then real torrent files of the trackers that Russia blocks, through the relay.
const base = process.env.RELAY ?? "http://127.0.0.1:8798";
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
for (let attempt = 0; attempt < 90; attempt++) { try { if ((await fetch(base + "/")).ok) break; } catch { /* starting */ } await sleep(1000); }
const cards = [
  { id: "movies:interstellar", title: "Интерстеллар", original: "Interstellar", year: 2014 },
  { id: "movies:dyuna-chast-vtoraya", title: "Дюна: Часть вторая", original: "Dune: Part Two", year: 2024 },
  { id: "movies:oppengeymer", title: "Оппенгеймер", original: "Oppenheimer", year: 2023 },
  { id: "series:yuzhnyy-park", title: "Южный парк", original: "South Park", year: 1997 },
  { id: "series:slovo-patsana-krov-na-asfalte", title: "Слово пацана. Кровь на асфальте", original: "", year: 2023 }
];
const relayed = ["NNM-Club", "MegaPeer", "BigFanGroup", "Nyaa"];
const picks = new Map();
for (const card of cards) {
  const query = new URLSearchParams({ id: card.id, title: card.title, original: card.original, year: String(card.year) });
  const started = Date.now();
  const response = await fetch(`${base}/api/search?${query}`);
  const data = await response.json();
  const counts = {};
  for (const item of data.items ?? []) counts[item.source] = (counts[item.source] ?? 0) + 1;
  console.log(`search ${card.title}: ${response.status} in ${Date.now() - started} ms, ${data.items?.length ?? 0} rows`, JSON.stringify(counts));
  for (const item of data.items ?? []) {
    if (!relayed.includes(item.source) || !item.torrentUrl) continue;
    const list = picks.get(item.source) ?? [];
    if (list.length < 3) list.push(item);
    picks.set(item.source, list);
  }
}
let ok = 0, failed = 0;
for (const [source, items] of picks) for (const item of items) {
  const started = Date.now();
  const response = await fetch(`${base}/api/torrent?url=${encodeURIComponent(item.torrentUrl)}`);
  const body = new Uint8Array(await response.arrayBuffer());
  const head = new TextDecoder("latin1").decode(body.subarray(0, Math.min(body.length, 200000)));
  const torrent = response.ok && body[0] === 0x64 && head.includes("4:infod");
  if (torrent) ok++; else failed++;
  console.log(`${torrent ? "OK  " : "FAIL"} ${source}: ${response.status} ${response.headers.get("content-type")} ${body.length} bytes in ${Date.now() - started} ms · ${item.torrentUrl} · ${torrent ? "" : new TextDecoder().decode(body.subarray(0, 200))}`);
}
console.log(`relayed torrent files: ${ok} valid, ${failed} failed, sources with rows: ${[...picks.keys()].join(", ") || "none"}`);
if (ok === 0) process.exit(1);
