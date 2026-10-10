// Checks a deployed relay (relay-worker/) the way a user in Russia reaches it:
// a real search and a real NNM-Club/MegaPeer/BigFanGroup torrent file, first from
// this runner, then from Globalping probes on Russian networks.
// Usage: RELAY=https://ka4alka-relay.example.workers.dev/ node scripts/probe-relay-russia.mjs
import { appendFileSync } from "node:fs";

const relay = new URL(process.env.RELAY ?? process.argv[2] ?? "");
if (!relay.pathname.endsWith("/")) relay.pathname += "/";
const API = "https://api.globalping.io/v1/measurements";
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const search = new URLSearchParams({ id: "movies:interstellar", title: "Интерстеллар", original: "Interstellar", year: "2014" });
const lines = [`# Посредник ${relay.origin}`, ""];
const say = (...rows) => { for (const row of rows) { console.log(row); lines.push(row); } };

const local = await fetch(new URL(`api/search?${search}`, relay));
const found = local.ok ? (await local.json()).items ?? [] : [];
const torrentUrl = found.find(item => ["NNM-Club", "MegaPeer", "BigFanGroup"].includes(item.source) && item.torrentUrl)?.torrentUrl;
say(`С раннера GitHub: поиск ${local.status}, ${found.length} раздач; для проверки взят ${torrentUrl ?? "—"}`);
if (torrentUrl) {
  const response = await fetch(new URL(`api/torrent?url=${encodeURIComponent(torrentUrl)}`, relay));
  const body = new Uint8Array(await response.arrayBuffer());
  say(`С раннера GitHub: torrent ${response.status}, ${body.length} байт, ${body[0] === 0x64 ? "bencode" : "не torrent"}`);
}

async function measure(path, query) {
  const response = await fetch(API, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({
    type: "http", target: relay.hostname, locations: [{ country: "RU", limit: 8 }],
    measurementOptions: { protocol: "HTTPS", request: { method: "GET", path, ...(query ? { query } : {}) } } }) });
  if (!response.ok) throw new Error(`Globalping ${response.status}: ${(await response.text()).slice(0, 200)}`);
  const { id } = await response.json();
  for (let attempt = 0; attempt < 90; attempt++) {
    const data = await (await fetch(`${API}/${id}`)).json();
    if (data.status !== "in-progress") return data.results ?? [];
    await sleep(1000);
  }
  return [];
}
const checks = [["Поиск раздач", relay.pathname + "api/search", search.toString()], ...(torrentUrl ? [["Torrent-файл", relay.pathname + "api/torrent", `url=${encodeURIComponent(torrentUrl)}`]] : [])];
let searchOk = 0, searchTotal = 0;
say("", "| Проверка | Зонд в России | Код | мс | Ошибка |", "|---|---|---|---|---|");
for (const [name, path, query] of checks) {
  for (const { probe, result } of await measure(path, query)) {
    const ok = result.status === "finished" && result.statusCode >= 200 && result.statusCode < 300;
    if (name === "Поиск раздач") { searchTotal++; if (ok) searchOk++; }
    say(`| ${name} | ${probe.city} · ${probe.network} (AS${probe.asn}) | ${result.statusCode ?? result.status} | ${result.timings?.total ?? ""} | ${ok ? "" : (result.rawOutput ?? "").replace(/\s+/g, " ").slice(0, 120)} |`);
  }
}
say("", `Поиск через посредника ответил на ${searchOk} из ${searchTotal} российских зондов.`);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, lines.join("\n") + "\n");
if (!local.ok || searchTotal === 0 || searchOk * 2 < searchTotal) process.exit(1);
