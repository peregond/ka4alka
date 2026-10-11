"""Curated text-only collections reference existing catalog IDs and have optional display dates."""
import datetime
import json
import re
from pathlib import Path


def attach_collections(feed, config_path, now):
    path = Path(config_path)
    definitions = json.loads(path.read_text(encoding='utf-8')) if path.exists() else []
    if not isinstance(definitions, list) or len(definitions) > 100:
        raise ValueError('Invalid collections configuration')
    known = {row['id'] for row in feed['items']}
    packs, seen = [], set()
    for definition in definitions:
        key, title = definition.get('id', ''), definition.get('title', '')
        if not re.fullmatch(r'[a-z0-9]+(?:-[a-z0-9]+)*', key) or len(key) > 80 or key in seen:
            raise ValueError('Invalid or duplicate collection ID')
        if not isinstance(title, str) or not title.strip() or len(title) > 200:
            raise ValueError('Invalid collection title')
        seen.add(key)
        pack = dict(id=key, title=title.strip())
        dates = {}
        for field in ['startsAtUtc', 'endsAtUtc']:
            if definition.get(field):
                date = datetime.datetime.fromisoformat(definition[field].replace('Z', '+00:00'))
                if date.utcoffset() != datetime.timedelta(0):
                    raise ValueError('Collection dates must be UTC')
                pack[field], dates[field] = date.isoformat().replace('+00:00', 'Z'), date
        if dates.get('startsAtUtc') and dates.get('endsAtUtc') and dates['startsAtUtc'] >= dates['endsAtUtc']:
            raise ValueError('Collection date interval is empty')
        members = definition.get('mediaIds', [])
        if not isinstance(members, list) or len(members) > 1000 or any(not isinstance(x, str) for x in members):
            raise ValueError('Invalid collection membership')
        pack['mediaIds'] = list(dict.fromkeys(x for x in members if x in known))
        if pack['mediaIds']:
            packs.append(pack)
    feed['collections'] = packs
    return feed
