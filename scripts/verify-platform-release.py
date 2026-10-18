"""Reject partial releases: verify the exact source revision and completed platform jobs."""
import json, os, subprocess, sys
from pathlib import Path

def gh(*args):
    return json.loads(subprocess.check_output(['gh', *args], text=True))

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
run = gh('api', f'repos/{repo}/actions/runs/{int(proof["runId"])}')
if run['path'] != '.github/workflows/release.yml' or run['head_sha'] != proof['commit']:
    raise ValueError('Validation evidence does not match the release workflow source')
tag = gh('api', f'repos/{repo}/git/ref/tags/v{version}')
if tag['object']['type'] != 'commit' or tag['object']['sha'] != proof['commit']:
    raise ValueError('Release tag does not point to the verified source revision')
jobs = gh('api', f'repos/{repo}/actions/runs/{int(proof["runId"])}/jobs?per_page=100')['jobs']
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
