// OONI web_connectivity from Russian users downloads whole pages. A 16 KB cut-off
// of Cloudflare traffic would show as anomalies or failures on unblocked
// Cloudflare-hosted sites, while sites on other networks stay OK.
const until = new Date(), since = new Date(until.getTime() - 30 * 86400_000);
const day = value => value.toISOString().slice(0, 10);
const groups = {
  "Cloudflare": ["www.cloudflare.com", "gitlab.com", "www.npmjs.com", "medium.com", "www.patreon.com", "discord.com", "www.change.org", "proton.me", "www.reddit.com"],
  "Other networks": ["github.com", "raw.githubusercontent.com", "archive.org", "www.wikipedia.org", "www.bbc.com", "www.google.com"]
};
for (const [group, domains] of Object.entries(groups)) {
  console.log(`\n## ${group}`);
  for (const domain of domains) {
    const url = `https://api.ooni.io/api/v1/aggregation?probe_cc=RU&test_name=web_connectivity&domain=${domain}&since=${day(since)}&until=${day(until)}`;
    try {
      const { result } = await (await fetch(url, { signal: AbortSignal.timeout(30_000) })).json();
      const share = result.measurement_count ? Math.round(100 * result.ok_count / result.measurement_count) : "—";
      console.log(`${domain}: ${result.measurement_count} measurements, ${result.ok_count} OK (${share}%), ${result.anomaly_count} anomalies, ${result.confirmed_count} confirmed, ${result.failure_count} failures`);
    } catch (error) { console.log(`${domain}: ${error.message}`); }
  }
}
// Recent individual anomalies on Cloudflare sites: what failed and after how many bytes.
for (const domain of ["gitlab.com", "www.npmjs.com", "medium.com"]) {
  const list = await (await fetch(`https://api.ooni.io/api/v1/measurements?probe_cc=RU&test_name=web_connectivity&domain=${domain}&since=${day(since)}&limit=8&order_by=measurement_start_time&order=desc`)).json();
  console.log(`\n## Latest ${domain}`);
  for (const row of list.results ?? []) {
    let detail = "";
    try {
      const raw = await (await fetch(`https://api.ooni.io/api/v1/raw_measurement?measurement_uid=${row.measurement_uid}`)).json();
      const keys = raw.test_keys ?? {};
      const request = (keys.requests ?? [])[0];
      detail = `blocking=${keys.blocking} http_failure=${keys.http_experiment_failure ?? ""} body=${request?.response?.body?.length ?? request?.response?.body_length ?? "?"} control_body=${keys.control?.http_request?.body_length ?? "?"}`;
    } catch (error) { detail = error.message; }
    console.log(`${row.measurement_start_time} AS${row.probe_asn} anomaly=${row.anomaly} failure=${row.failure} ${detail}`);
  }
}
