// Lists which trackers Knaben's public index returns for typical catalog titles
// and whether those rows carry an info hash (usable as a magnet without the tracker site).
const titles = ["Интерстеллар", "Interstellar", "Мангуст", "Южный парк", "South Park", "Брат 2", "Слово пацана", "Дюна", "Оппенгеймер", "Объект преступления"];
const totals = new Map();
for (const query of titles) {
  const response = await fetch("https://api.knaben.org/v1", { method: "POST", headers: { "Content-Type": "application/json", "Accept": "application/json", "User-Agent": "KachalkaIndex/0.2" },
    body: JSON.stringify({ query, search_field: "title", search_type: "100%", order_by: "seeders", order_direction: "desc", size: 150, hide_unsafe: true, hide_xxx: true }) });
  const data = await response.json();
  const counts = new Map();
  for (const row of data.hits ?? []) {
    const key = row.tracker ?? "?";
    const hash = /^[a-f0-9]{40}$/i.test(row.hash ?? "") || /urn:btih:[a-f0-9]{40}/i.test(row.magnetUrl ?? "");
    const item = counts.get(key) ?? { rows: 0, hashes: 0, links: 0, sample: row.title };
    item.rows++; if (hash) item.hashes++; if (row.link) item.links++;
    counts.set(key, item);
    const total = totals.get(key) ?? { rows: 0, hashes: 0 }; total.rows++; if (hash) total.hashes++; totals.set(key, total);
  }
  console.log(`\n## ${query}: ${response.status}, ${data.hits?.length ?? 0} rows`);
  for (const [tracker, item] of [...counts].sort((a, b) => b[1].rows - a[1].rows)) console.log(`${tracker}: ${item.rows} rows, ${item.hashes} with hash, ${item.links} with link · ${item.sample}`);
  await new Promise(resolve => setTimeout(resolve, 2200));
}
console.log("\n## Totals");
for (const [tracker, item] of [...totals].sort((a, b) => b[1].rows - a[1].rows)) console.log(`${tracker}: ${item.rows} rows, ${item.hashes} with hash`);
