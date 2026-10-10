// Announces real RuTracker hashes (from Knaben, which answers in Russia) to the
// trackers RuTracker publishes for magnet links, then asks the same from probes
// on Russian networks. Also resolves the DHT bootstrap router from Russia.
const API = "https://api.globalping.io/v1/measurements";
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const hosts = ["bt.t-ru.org", "bt2.t-ru.org", "bt3.t-ru.org", "bt4.t-ru.org"];
const rows = [];
for (const query of ["Терминатор", "Интерстеллар"]) {
  const response = await fetch("https://api.knaben.org/v1", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ query, search_field: "title", search_type: "100%", order_by: "seeders", order_direction: "desc", size: 50, hide_unsafe: true, hide_xxx: true }) });
  for (const row of (await response.json()).hits ?? []) if (/rutracker/i.test(row.tracker ?? "") && /^[a-f0-9]{40}$/i.test(row.hash ?? "")) rows.push(row);
  await sleep(2200);
}
const picks = rows.sort((a, b) => (b.seeders ?? 0) - (a.seeders ?? 0)).slice(0, 3);
const encode = hex => hex.match(/../g).map(byte => "%" + byte.toUpperCase()).join("");
const query = hash => `magnet&info_hash=${encode(hash)}&peer_id=-KC0408-${Math.random().toString(36).slice(2, 14).padEnd(12, "0")}&port=6881&uploaded=0&downloaded=0&left=1&compact=1&numwant=50&event=started`;
function describe(bytes) {
  const text = new TextDecoder("latin1").decode(bytes);
  const failure = /14:failure reason(\d+):/.exec(text);
  if (failure) return `failure: ${text.substr(failure.index + failure[0].length, Number(failure[1]))}`;
  const peers = /5:peers(\d+):/.exec(text);
  const interval = /8:intervali(\d+)e/.exec(text);
  return peers ? `${Number(peers[1]) / 6} peers, interval ${interval?.[1] ?? "?"}` : `unparsed: ${text.slice(0, 80)}`;
}
console.log("## Announce from the GitHub runner");
for (const row of picks) {
  console.log(`${row.title.slice(0, 80)} · seeders ${row.seeders} · ${row.hash}`);
  for (const host of hosts) {
    try {
      const response = await fetch(`http://${host}/ann?${query(row.hash)}`, { signal: AbortSignal.timeout(15000), headers: { "User-Agent": "Kachalka/0.40" } });
      console.log(`  ${host}: ${response.status} ${describe(new Uint8Array(await response.arrayBuffer()))}`);
    } catch (error) { console.log(`  ${host}: ${error.cause?.code ?? error.message}`); }
  }
}
async function measure(body) {
  const response = await fetch(API, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!response.ok) throw new Error(`Globalping ${response.status}: ${(await response.text()).slice(0, 200)}`);
  const { id } = await response.json();
  for (let attempt = 0; attempt < 90; attempt++) { const data = await (await fetch(`${API}/${id}`)).json(); if (data.status !== "in-progress") return data.results ?? []; await sleep(1000); }
  return [];
}
console.log("\n## Announce from Russian networks (Globalping)");
if (picks[0]) for (const host of hosts) {
  for (const { probe, result } of await measure({ type: "http", target: host, locations: [{ country: "RU", limit: 6 }], measurementOptions: { protocol: "HTTP", port: 80, request: { method: "GET", path: "/ann", query: query(picks[0].hash) } } })) {
    const body = result.rawBody ?? "";
    console.log(`  ${host} | ${probe.city} · ${probe.network} (AS${probe.asn}) | ${result.status} ${result.statusCode ?? ""} | ${result.status === "finished" ? describe(new TextEncoder().encode(body)) : (result.rawOutput ?? "").replace(/\s+/g, " ").slice(0, 100)}`);
  }
}
console.log("\n## DNS from Russian networks");
for (const host of ["router.bittorrent.com", "bt2.t-ru.org", "tracker.opentrackr.org"]) {
  for (const { probe, result } of await measure({ type: "dns", target: host, locations: [{ country: "RU", limit: 4 }], measurementOptions: { query: { type: "A" } } }))
    console.log(`  ${host} | ${probe.city} · ${probe.network} | ${result.status} | ${(result.answers ?? []).map(a => a.value).join(", ")}`);
}
