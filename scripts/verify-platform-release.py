"""Reject partial releases: verify the exact source revision and completed platform jobs."""
import json, os, subprocess, sys, time
from pathlib import Path

def gh(*args):
    result = subprocess.run(['gh', *args], capture_output=True, text=True, encoding='utf-8', check=True)
    if not result.stdout or not result.stdout.strip():
        raise ValueError('gh ' + ' '.join(args) + ' returned no output: ' + (result.stderr or '').strip())
    return json.loads(result.stdout)

def fresh_api(path):
    # A rerun keeps the run ID. Avoid a cached job list from its failed attempt
    # when checking that the latest Windows/Mac publication gates really passed.
    separator = '&' if '?' in path else '?'
    return gh('api', path + separator + 'validation=' + str(time.time_ns()))

version, directory = sys.argv[1:]
if tuple(map(int, version.split('.'))) < (0, 44, 0):
    raise SystemExit(0)
root = Path(directory)
required = ['Kachalka-macos-arm64.zip','Kachalka-macos-x64.zip','Kachalka-macos-arm64.dmg','Kachalka-macos-x64.dmg','macos-latest.json','macos-latest.sig','macos-components-arm64.json','macos-components-x64.json','platform-validation.json']
for file in required:
    if not (root/file).is_file():
        raise ValueError(f'Missing required Mac release asset: {file}')
repo = os.environ['GITHUB_REPOSITORY']
proof = json.loads((root/'platform-validation.json').read_text())
if proof['repository'] != repo or proof['version'] != version:
    raise ValueError('Validation evidence belongs to a different release')
run = fresh_api(f'repos/{repo}/actions/runs/{int(proof["runId"])}')
if run['path'] != '.github/workflows/release.yml' or run['head_sha'] != proof['commit']:
    raise ValueError('Validation evidence does not match the release workflow source')
release = gh('release', 'view', f'v{version}', '--json', 'isDraft,targetCommitish')
if release['targetCommitish'] != proof['commit']:
    raise ValueError('Release does not target the verified source revision')
try:
    tag = fresh_api(f'repos/{repo}/git/ref/tags/v{version}')
except subprocess.CalledProcessError:
    # GitHub creates a new tag only when a draft is published. The draft must
    # already target the full checked commit; a mutable branch is insufficient.
    if not release['isDraft']:
        raise
else:
    target = tag['object']
    if target['type'] == 'tag':
        target = fresh_api(f'repos/{repo}/git/tags/{target["sha"]}')['object']
    if target['type'] != 'commit' or target['sha'] != proof['commit']:
        raise ValueError('Release tag does not point to the verified source revision')
jobs = fresh_api(f'repos/{repo}/actions/runs/{int(proof["runId"])}/jobs?per_page=100')['jobs']
# "publish" itself is still running when this gate is called.
for name in ['release','mac-assets','macos / macos (macos-15, arm64)','macos / macos (macos-15-intel, x64)','portable / shared-engine (ubuntu-latest)','portable / shared-engine (windows-latest)']:
    found = [job for job in jobs if job['name'] == name]
    if len(found) != 1 or found[0]['conclusion'] != 'success':
        raise ValueError(f'Required platform validation has not passed: {name}')
windows = json.loads((root/'latest.json').read_text())
mac = json.loads((root/'macos-latest.json').read_text())
if windows.get('version', windows.get('Version')) != version or mac['version'] != version:
    raise ValueError('Windows and Mac versions do not match')
print(f'Verified one source revision for Windows, Apple Silicon and Intel: {proof["commit"]}')
