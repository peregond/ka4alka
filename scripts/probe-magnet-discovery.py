"""Bounded public discovery probe; no video download and no peer addresses in logs."""
import concurrent.futures
import json
import re
import urllib.parse
import urllib.request


def read(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'Kachalka/0.28.1'})
    with urllib.request.urlopen(req, timeout=12) as response:
        raw = response.read(2_000_001)
        if len(raw) > 2_000_000:
            raise ValueError('Oversized response')
        return response.status, raw


if __name__ == '__main__':
    rows = []
    try:
        _, raw = read('https://apibay.org/q.php?q=The%20Love%20Hypothesis&cat=201,202,207')
        rows = [r for r in json.loads(raw) if re.fullmatch('[A-Fa-f0-9]{40}', r.get('info_hash', '')) and r.get('id') != '0']
        print(json.dumps({'source': 'The Pirate Bay', 'rows': [{k: r.get(k) for k in ('id', 'name', 'info_hash', 'seeders', 'size')} for r in rows[:10]]}), flush=True)
    except Exception as error:
        print(json.dumps({'source': 'The Pirate Bay', 'error': str(error)}), flush=True)
    exact = next((r for r in rows if r.get('name') == 'The Love Hypothesis (2026) [1080p] [BluRay] [5.1]'), None)
    info_hash = bytes.fromhex(exact['info_hash']) if exact else bytes(20)
    query = 'info_hash=' + urllib.parse.quote_from_bytes(info_hash, safe='') + '&peer_id=-KA0281-012345678901&port=51413&uploaded=0&downloaded=0&left=1&compact=1&numwant=50&event=started'

    def probe(url):
        try:
            status, raw = read(url + '?' + query)
            v4 = re.search(rb'5:peers(\d+):', raw)
            v6 = re.search(rb'6:peers6(\d+):', raw)
            failure = re.search(rb'14:failure reason\d+:([^e]+)', raw)
            return {'tracker': url, 'status': status, 'bencoded': raw.startswith(b'd'), 'bytes': len(raw), 'peers_v4': int(v4[1]) // 6 if v4 else None, 'peers_v6': int(v6[1]) // 18 if v6 else None, 'failure': failure[1].decode(errors='replace')[:120] if failure else None, 'exact_film_hash': bool(exact)}
        except Exception as error:
            return {'tracker': url, 'error': str(error)}

    urls = ['https://tracker.gbitt.info/announce', 'https://tracker.tamersunion.org/announce', 'https://tr.bangumi.moe/announce', 'https://tracker.foreverpirates.co/announce', 'https://pybittrack.retiolus.net/announce', 'https://open.ftorrent.com/announce', 'https://tr.nyacat.pw/announce']
    with concurrent.futures.ThreadPoolExecutor(max_workers=7) as pool:
        for result in pool.map(probe, urls):
            print(json.dumps(result), flush=True)
