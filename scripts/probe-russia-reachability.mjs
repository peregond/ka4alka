// Measures, from probes inside Russia, whether the hosts used by Качалка answer.
// Globalping runs HTTP/DNS requests from volunteer probes on Russian networks;
// OONI aggregates recent web connectivity tests from Russian users. The runner's
// own request is printed as a baseline from outside Russia.
import { readFileSync, writeFileSync, appendFileSync } from "node:fs";

const API = "https://api.globalping.io/v1/measurements";
const PROBES = Number(process.env.PROBES ?? 8);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

const targets = [
  { name: "Онлайн-индекс: карточка", host: "ka4alka-online-new.peregon.chatgpt.site", path: "/api/media", query: "id=movies:obekt-prestupleniya", keepBody: true },
  { name: "Cloudflare: 15 КБ", host: "speed.cloudflare.com", path: "/__down", query: "bytes=15000" },
  { name: "Cloudflare: 300 КБ", host: "speed.cloudflare.com", path: "/__down", query: "bytes=300000" },
  { name: "Cloudflare Workers (workers.dev)", host: "workers.cloudflare.com", path: "/" },
  { name: "Онлайн-индекс: каталог (>16 КБ)", host: "ka4alka-online-new.peregon.chatgpt.site", path: "/api/catalog", query: "section=movies&page=1" },
  { name: "Онлайн-индекс: раздачи (>16 КБ)", host: "ka4alka-online-new.peregon.chatgpt.site", path: "/api/releases", query: "id=movies:obekt-prestupleniya" },
  { name: "Онлайн-индекс: torrent через бэкенд", host: "ka4alka-online-new.peregon.chatgpt.site", path: "/api/torrent", query: "url=" + encodeURIComponent("https://nnmclub.to/forum/download.php?id=1") },
  { name: "Railway: карточка", host: "web-production-d7aa7.up.railway.app", path: "/api/media", query: "id=movies:obekt-prestupleniya" },
  { name: "Railway: раздачи", host: "web-production-d7aa7.up.railway.app", path: "/api/releases", query: "id=movies:obekt-prestupleniya" },
  { name: "Railway: torrent через бэкенд", host: "web-production-d7aa7.up.railway.app", path: "/api/torrent", query: "url=" + encodeURIComponent("https://nnmclub.to/forum/download.php?id=1") },
  { name: "Cloudflare trace (страна адреса)", host: "www.cloudflare.com", path: "/cdn-cgi/trace" },
  { name: "RuTor", host: "rutor.info", path: "/" },
  { name: "NNM-Club", host: "nnmclub.to", path: "/forum/index.php" },
  { name: "MegaPeer", host: "megapeer.vip", path: "/" },
  { name: "BigFanGroup", host: "bigfangroup.org", path: "/" },
  { name: "Nyaa", host: "nyaa.si", path: "/" },
  { name: "EZTV", host: "eztvx.to", path: "/" },
  { name: "Knaben API", host: "api.knaben.org", path: "/v1" },
  { name: "The Pirate Bay API", host: "apibay.org", path: "/q.php", query: "q=dune&cat=201" },
  { name: "YTS", host: "yts.gg", path: "/" },
  { name: "Internet Archive", host: "archive.org", path: "/" },
  { name: "Zona", host: "w6.zona.plus", path: "/movies" },
  { name: "Общий каталог на GitHub", host: "raw.githubusercontent.com", path: "/peregond/ka4alka/main/README.md" },
  { name: "Обновления GitHub", host: "github.com", path: "/peregond/ka4alka/releases" },
  { name: "HTTPS-трекер foreverpirates", host: "tracker.foreverpirates.co", path: "/announce" },
  { name: "HTTPS-трекер ftorrent", host: "open.ftorrent.com", path: "/announce" }
];
// Deployed relays (distribution/relays.json) are what Russian users reach instead of the site.
try {
  const { relays = [] } = JSON.parse(readFileSync(new URL("../distribution/relays.json", import.meta.url), "utf8"));
  for (const relay of relays.slice(0, 3)) {
    const url = new URL(relay);
    targets.splice(1, 0, { name: `Посредник ${url.hostname}: поиск`, host: url.hostname, path: url.pathname + "api/search", query: "id=movies:interstellar&title=%D0%98%D0%BD%D1%82%D0%B5%D1%80%D1%81%D1%82%D0%B5%D0%BB%D0%BB%D0%B0%D1%80&original=Interstellar&year=2014" });
  }
} catch { /* No relay is published yet. */ }
const dnsTargets = ["api.knaben.org", "apibay.org", "rutor.info", "nnmclub.to", "megapeer.vip", "nyaa.si", "ka4alka-online-new.peregon.chatgpt.site", "web-production-d7aa7.up.railway.app", "tracker.opentrackr.org"];
const ooniDomains = ["rutor.info", "nnmclub.to", "megapeer.vip", "bigfangroup.org", "nyaa.si", "eztvx.to", "api.knaben.org", "knaben.org", "apibay.org", "yts.gg", "archive.org", "w6.zona.plus", "chatgpt.site", "up.railway.app", "raw.githubusercontent.com", "github.com"];

async function request(body) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const response = await fetch(API, { method: "POST", headers: { "Content-Type": "application/json", "Accept": "application/json" }, body: JSON.stringify(body) });
    if (response.status === 429) { await sleep(15_000 * (attempt + 1)); continue; }
    const text = await response.text();
    if (!response.ok) throw new Error(`Globalping ${response.status}: ${text.slice(0, 300)}`);
    return JSON.parse(text).id;
  }
  throw new Error("Globalping rate limit");
}
async function result(id) {
  for (let attempt = 0; attempt < 90; attempt++) {
    const data = await (await fetch(`${API}/${id}`, { headers: { "Accept": "application/json" } })).json();
    if (data.status !== "in-progress") return data;
    await sleep(1000);
  }
  throw new Error(`Measurement ${id} did not finish`);
}
const where = probe => `${probe.city ?? "?"} · ${probe.network ?? "?"} (AS${probe.asn ?? "?"})`;
const blockPage = body => /(?:заблокирован|blocked|rkn\.gov|eais\.rkn|zapret|роскомнадзор|warning\.rt\.ru|blocklist)/i.test(body ?? "");

function httpSummary(target, data) {
  return (data.results ?? []).map(({ probe, result: r }) => ({
    target: target.name, host: target.host, probe: where(probe), asn: probe.asn,
    status: r.status, code: r.statusCode ?? null, totalMs: r.timings?.total ?? null,
    firstByteMs: r.timings?.firstByte ?? null, downloadMs: r.timings?.download ?? null,
    truncated: r.truncated ?? null, bodyBytes: r.rawBody?.length ?? 0,
    blockPage: blockPage(r.rawBody) || blockPage(r.rawHeaders),
    error: r.status === "finished" ? null : (r.rawOutput ?? "").replace(/\s+/g, " ").slice(0, 160),
    ...(target.keepBody ? { headers: (r.rawHeaders ?? "").slice(0, 1500), body: (r.rawBody ?? "").slice(0, 6000) } : {})
  }));
}

async function baseline(target) {
  const url = `https://${target.host}${target.path}${target.query ? "?" + target.query : ""}`;
  const started = Date.now();
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(20_000), headers: { "User-Agent": "Mozilla/5.0 Kachalka reachability probe" } });
    const body = await response.arrayBuffer();
    return { target: target.name, status: response.status, bytes: body.byteLength, ms: Date.now() - started };
  } catch (error) {
    return { target: target.name, status: "error", error: String(error.cause?.code ?? error.message), ms: Date.now() - started };
  }
}

async function ooni(domain) {
  const until = new Date(); const since = new Date(until.getTime() - 30 * 86400_000);
  const day = value => value.toISOString().slice(0, 10);
  const url = `https://api.ooni.io/api/v1/aggregation?probe_cc=RU&test_name=web_connectivity&domain=${encodeURIComponent(domain)}&since=${day(since)}&until=${day(until)}`;
  try {
    const data = await (await fetch(url, { signal: AbortSignal.timeout(30_000) })).json();
    return { domain, ...data.result };
  } catch (error) { return { domain, error: String(error.message) }; }
}

const report = { generatedUtc: new Date().toISOString(), http: [], dns: [], ooni: [], baseline: [] };
// The first measurement chooses Russian probes; the rest reuse exactly the same probes.
let probesId = null;
for (const target of targets) {
  const body = {
    type: "http", target: target.host, inProgressUpdates: false,
    locations: probesId ? probesId : [{ country: "RU", limit: PROBES }],
    measurementOptions: { protocol: "HTTPS", request: { method: "GET", path: target.path, ...(target.query ? { query: target.query } : {}) } }
  };
  try {
    const id = await request(body);
    if (!probesId) probesId = id;
    report.http.push(...httpSummary(target, await result(id)));
  } catch (error) { report.http.push({ target: target.name, host: target.host, status: "measurement-error", error: String(error.message) }); }
  report.baseline.push(await baseline(target));
}
for (const host of dnsTargets) {
  try {
    const id = await request({ type: "dns", target: host, locations: probesId ?? [{ country: "RU", limit: PROBES }], measurementOptions: { query: { type: "A" } } });
    const data = await result(id);
    for (const { probe, result: r } of data.results ?? []) report.dns.push({ host, probe: where(probe), status: r.status, answers: (r.answers ?? []).map(a => a.value).join(", "), resolver: r.resolver ?? null });
  } catch (error) { report.dns.push({ host, status: "measurement-error", error: String(error.message) }); }
}
for (const domain of ooniDomains) report.ooni.push(await ooni(domain));

writeFileSync("russia-reachability.json", JSON.stringify(report, null, 2));
for (const row of report.http.filter(row => row.body !== undefined).slice(0, 2)) console.log(`--- ${row.target} · ${row.probe} · ${row.code}\n${row.headers}\n${row.body}\n---`);
const lines = ["# Доступность из России", "", `Сформировано: ${report.generatedUtc}`, "", "## HTTP с российских зондов Globalping", "", "| Цель | Зонд | Итог | Код | мс | Байт тела | Страница блокировки | Ошибка |", "|---|---|---|---|---|---|---|---|"];
for (const row of report.http) lines.push(`| ${row.target} | ${row.probe ?? ""} | ${row.status} | ${row.code ?? ""} | ${row.totalMs ?? ""} | ${row.bodyBytes ?? ""}${row.truncated ? "+" : ""} | ${row.blockPage ? "да" : ""} | ${row.error ?? ""} |`);
lines.push("", "## Сводка по целям", "", "| Цель | Зондов | Ответили 2xx/3xx | Страница блокировки | Ошибка/тайм-аут |", "|---|---|---|---|---|");
for (const target of targets) {
  const rows = report.http.filter(row => row.target === target.name);
  const ok = rows.filter(row => row.status === "finished" && row.code >= 200 && row.code < 400 && !row.blockPage).length;
  lines.push(`| ${target.name} | ${rows.length} | ${ok} | ${rows.filter(row => row.blockPage).length} | ${rows.filter(row => row.status !== "finished").length} |`);
}
lines.push("", "## DNS с тех же зондов", "", "| Домен | Зонд | Итог | Ответ |", "|---|---|---|---|");
for (const row of report.dns) lines.push(`| ${row.host} | ${row.probe ?? ""} | ${row.status} | ${row.answers ?? row.error ?? ""} |`);
lines.push("", "## OONI web_connectivity, Россия, 30 дней", "", "| Домен | Измерений | OK | Аномалий | Подтверждённых блокировок | Сбоев |", "|---|---|---|---|---|---|");
for (const row of report.ooni) lines.push(`| ${row.domain} | ${row.measurement_count ?? row.error ?? ""} | ${row.ok_count ?? ""} | ${row.anomaly_count ?? ""} | ${row.confirmed_count ?? ""} | ${row.failure_count ?? ""} |`);
lines.push("", "## Тот же запрос с раннера GitHub (вне России)", "", "| Цель | Код | Байт | мс |", "|---|---|---|---|");
for (const row of report.baseline) lines.push(`| ${row.target} | ${row.status}${row.error ? " " + row.error : ""} | ${row.bytes ?? ""} | ${row.ms} |`);
const markdown = lines.join("\n") + "\n";
writeFileSync("russia-reachability.md", markdown);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
console.log(markdown);
