"""Embed a verified daily text catalog in the next native release.

Download manifest.json from the catalog-data release, resolve its archive with
catalog_snapshot.py --asset-name, then run this helper with --manifest, --archive,
--output-dir web-index/app/data and --collections-output scripts/catalog-collections.json.
All integrity, schema and completeness checks finish before resources are replaced.
"""
import argparse
import datetime
import importlib.util
import json
import os
import tempfile
from pathlib import Path

from catalog_collections import attach_collections
from catalog_snapshot import MAX_SNAPSHOT_BYTES, restore_snapshot

_spec = importlib.util.spec_from_file_location('refresh_library_bundle', Path(__file__).with_name('refresh-library.py'))
_refresh = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_refresh)
MAX_STARTER_BYTES = 1024 * 1024


def _encode(value):
    # Match the publisher's compact encoding: formatting must not make an
    # accepted snapshot exceed the same resource size limit during bundling.
    payload = (json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n').encode('utf-8')
    if len(payload) > MAX_SNAPSHOT_BYTES:
        raise ValueError('Bundled catalog resource exceeds the supported limit')
    return payload


def _starter_rows(rows):
    """Keep the first cards small enough for the synchronous native warmup."""
    selected = [row for section in ['movies', 'series']
                for row in [item for item in rows if item['section'] == section][:40]]
    optional_limits = dict(poster=2000, originalTitle=300, imdb=20, kinopoisk=20, imdbId=14)
    starter = []
    for row in selected:
        card = {field: row[field] for field in ['id', 'section', 'title', 'year', 'pageUrl']}
        for field, maximum in optional_limits.items():
            value = row.get(field)
            if field in ['imdb', 'kinopoisk'] and type(value) in [int, float]:
                value = str(value)
            # Omit oversized optional values instead of truncating poster URLs.
            # Control characters can expand sixfold during JSON encoding.
            if isinstance(value, str) and 0 < len(value) <= maximum and all(ord(character) >= 32 for character in value):
                card[field] = value
        starter.append(card)
    return starter


def _replace_resources(resources):
    """Stage all validated resources before any atomic file replacement."""
    staged = []
    try:
        for target, payload in resources:
            target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=target.parent, prefix=target.name + '.', suffix='.tmp', delete=False) as handle:
                temporary = Path(handle.name)
                staged.append((temporary, target))
                handle.write(payload)
                handle.flush()
                os.fsync(handle.fileno())
        for temporary, target in staged:
            temporary.replace(target)
    finally:
        for temporary, _ in staged:
            temporary.unlink(missing_ok=True)


def bundle_catalog(manifest_path, archive_path, output_dir, collections_output):
    """Keep rich item metadata and normalized collection definitions in resources."""
    with tempfile.TemporaryDirectory(prefix='kachalka-catalog-bundle-') as directory:
        restored = Path(directory) / 'catalog.json'
        manifest = restore_snapshot(manifest_path, archive_path, restored)
        feed = json.loads(restored.read_text(encoding='utf-8'))
        feed['items'] = _refresh.load_previous(restored)
        # Enforce the same 15,000-item, 32 MiB and 2,000-film/300-series limits as
        # the daily publisher, including unique identities and trusted page URLs.
        _refresh.snapshot_bytes(feed)
        definitions = feed.get('collections', [])
        if not isinstance(definitions, list) or any(not isinstance(pack, dict) for pack in definitions):
            raise ValueError('Invalid bundled collections')
        configuration = Path(directory) / 'collections.json'
        configuration.write_text(json.dumps(definitions, ensure_ascii=False), encoding='utf-8')
        try:
            attach_collections(feed, configuration, datetime.datetime.now(datetime.timezone.utc))
        except (AttributeError, TypeError) as error:
            raise ValueError('Invalid bundled collection fields') from error
        rows = feed['items']
        starter_payload = _encode(_starter_rows(rows))
        if len(starter_payload) >= MAX_STARTER_BYTES:
            raise ValueError('Bundled starter exceeds the native warmup limit')
        data = Path(output_dir)
        resources = [(data / 'seed.json', _encode(rows)),
                     (data / 'starter.json', starter_payload),
                     (Path(collections_output), _encode(feed['collections']))]
        if len({target.resolve() for target, _ in resources}) != len(resources):
            raise ValueError('Bundled catalog output paths must be distinct')
        _replace_resources(resources)
    return dict(revision=manifest['revision'], generatedAtUtc=manifest['generatedAtUtc'],
                items=len(rows), movies=sum(row['section'] == 'movies' for row in rows),
                series=sum(row['section'] == 'series' for row in rows),
                collections=len(feed['collections']))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--archive', required=True)
    parser.add_argument('--output-dir', required=True)
    parser.add_argument('--collections-output', required=True)
    args = parser.parse_args()
    try:
        result = bundle_catalog(args.manifest, args.archive, args.output_dir, args.collections_output)
    except (OSError, ValueError) as error:
        parser.error(str(error))
    print(json.dumps(result, ensure_ascii=False, separators=(',', ':')))


if __name__ == '__main__':
    main()
