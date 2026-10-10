// Same Globalping request to each copy of the online index from several countries:
// a block that only Russian probes receive is a country rule of the hosting. The
// runner also checks that every copy serves the API the application uses.
const API = "https://api.globalping.io/v1/measurements";
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sites = (process.env.SITES ?? "ka4alka-online-new.peregon.chatgpt.site,web-production-d7aa7.up.railway.app").split(",");
const countries = ["RU", "BY", "KZ", "RS", "DE", "US"];
const paths = ["/api/media?id=movies:obekt-prestupleniya", "/api/catalog?section=movies&page=1", "/api/releases?id=series:dyuna-prorochestvo",
  "/api/torrent?url=" + encodeURIComponent("https://evil.example/x"), "/api/backdrop?id=movies:mangust-2026"];
for (const site of sites) {
  console.log(`\n## ${site}: API from the GitHub runner`);
  for (const path of paths) {
    try {
      const response = await fetch(`https://${site}${path}`, { signal: AbortSignal.timeout(30000) });
      const text = await response.text();
      console.log(`${response.status} ${text.length}B ${path} · ${text.slice(0, 120).replace(/\s+/g, " ")}`);
    } catch (error) { console.log(`error ${path} · ${error.cause?.code ?? error.message}`); }
  }
  console.log(`\n## ${site}: /api/media by country (Globalping)`);
  for (const country of countries) {
    const response = await fetch(API, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({
      type: "http", target: site, locations: [{ country, limit: country === "RU" ? 8 : 3 }],
      measurementOptions: { protocol: "HTTPS", request: { method: "GET", path: "/api/media", query: "id=movies:obekt-prestupleniya" } } }) });
    const { id } = await response.json();
    let data;
    for (let attempt = 0; attempt < 60; attempt++) { data = await (await fetch(`${API}/${id}`)).json(); if (data.status !== "in-progress") break; await sleep(1000); }
    for (const { probe, result } of data.results ?? []) {
      const title = /<title>([^<]*)<\/title>/i.exec(result.rawBody ?? "")?.[1] ?? (result.rawBody ?? result.rawOutput ?? "").slice(0, 60).replace(/\s+/g, " ");
      console.log(`${country} | ${probe.city} · ${probe.network} (AS${probe.asn}) | ${result.status} | ${result.statusCode ?? ""} | ${result.timings?.total ?? ""} ms | ${title}`);
    }
  }
}
