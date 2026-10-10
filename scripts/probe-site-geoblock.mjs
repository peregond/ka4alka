// Same Globalping request to the online index from several countries: a block
// that only Russian probes receive is a country rule of the hosting, not a bot rule.
const API = "https://api.globalping.io/v1/measurements";
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const countries = ["RU", "BY", "KZ", "RS", "DE", "US"];
for (const country of countries) {
  const response = await fetch(API, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({
    type: "http", target: "ka4alka-online-new.peregon.chatgpt.site", locations: [{ country, limit: 4 }],
    measurementOptions: { protocol: "HTTPS", request: { method: "GET", path: "/api/media", query: "id=movies:obekt-prestupleniya" } } }) });
  const { id } = await response.json();
  let data;
  for (let attempt = 0; attempt < 60; attempt++) { data = await (await fetch(`${API}/${id}`)).json(); if (data.status !== "in-progress") break; await sleep(1000); }
  for (const { probe, result } of data.results ?? []) {
    const title = /<title>([^<]*)<\/title>/i.exec(result.rawBody ?? "")?.[1] ?? (result.rawBody ?? "").slice(0, 60).replace(/\s+/g, " ");
    console.log(`${country} | ${probe.city} · ${probe.network} (AS${probe.asn}) | ${result.status} | ${result.statusCode ?? ""} | ${title}`);
  }
}
