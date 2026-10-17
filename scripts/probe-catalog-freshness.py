"""Diagnostic: what does the catalog source return today, and how much of it is new compared with the published library?
Prints GitHub annotations; changes nothing and publishes nothing."""
import gzip, importlib.util, json, sys, urllib.request
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
spec = importlib.util.spec_from_file_location('refresh_library', root / 'refresh-library.py')
refresh = importlib.util.module_from_spec(spec); spec.loader.exec_module(refresh)

def note(title, text):
    print(f"::notice title={title}::{text.replace('%', '%25').replace(chr(13), '').replace(chr(10), '%0A')[:6000]}", flush=True)

base = 'https://github.com/peregond/ka4alka/releases/download/catalog-data/'
manifest = json.load(urllib.request.urlopen(base + 'manifest.json', timeout=60))
previous = json.loads(gzip.decompress(urllib.request.urlopen(manifest['downloadUrl'], timeout=120).read()))['items']
known = {row['id'] for row in previous}
note('published', f"{len(previous)} items, generated {manifest['generatedAtUtc']}; years of the newest 5: {sorted((r['year'] for r in previous), reverse=True)[:5]}")

try:
    rows = refresh.collect()
except Exception as error:
    note('collect failed', f'{type(error).__name__}: {error}')
    raise
fresh = [row for row in rows if row['id'] not in known]
note('source', f"{len(rows)} cards collected; movies {sum(r['section']=='movies' for r in rows)}, series {sum(r['section']=='series' for r in rows)}; not in the published library: {len(fresh)}")
years = Counter(r['year'] for r in rows)
note('years in source', ', '.join(f'{y}: {n}' for y, n in sorted(years.items(), reverse=True)[:8]))
note('first cards of the source (movies)', ' | '.join(f"{r['year']} {r['title']}" for r in rows if r['section'] == 'movies')[:1200] if False else ' | '.join(f"{r['year']} {r['title']}" for r in [x for x in rows if x['section']=='movies'][:25]))
note('first cards of the source (series)', ' | '.join(f"{r['year']} {r['title']}" for r in [x for x in rows if x['section']=='series'][:15]))
note('new in source', ' | '.join(f"{r['year']} {r['id']}" for r in fresh[:40]) or 'none')
