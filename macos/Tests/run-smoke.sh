#!/usr/bin/env bash
# Runs the packaged native application in one of its two test modes and verifies what it wrote.
#
#   run-smoke.sh render <app-executable> <output-dir> [label]   page x size x theme x language matrix
#   run-smoke.sh close  <app-executable> <output-dir> [label]   closing the window saves state and ends the engine
#
# KACHALKA_DATA must point at a scratch profile. The script never stops at the first problem:
# it reports every one it finds and exits non-zero at the end.
set -u

mode="${1:?mode (render or close) is required}"
app="${2:?path to the Ka4alka executable is required}"
output="${3:?output directory is required}"
label="${4:-macOS}"
here="$(cd "$(dirname "$0")" && pwd)"
: "${KACHALKA_DATA:?KACHALKA_DATA must point at a scratch profile}"
failed=0

# The engine is a child of the application. Once the application is gone it must be gone too.
engine_gone() {
  local attempt
  for ((attempt = 0; attempt < 20; attempt++)); do
    pgrep -f Kachalka.Engine >/dev/null 2>&1 || return 0
    sleep 1
  done
  pgrep -fl Kachalka.Engine || true
  echo "::error title=Engine outlived the application::the engine child process is still running after the application exited"
  return 1
}

# Waits up to $1 seconds for process $2. Returns 0 when it ended on its own.
wait_for() {
  local limit="$1" process="$2" attempt
  for ((attempt = 0; attempt < limit; attempt++)); do
    kill -0 "$process" 2>/dev/null || return 0
    sleep 1
  done
  return 1
}

stop_stuck() {
  local process="$1"
  sample "$process" 3 -file "$output/native-sample.txt" || true
  kill "$process" 2>/dev/null || true
  sleep 2
  kill -9 "$process" 2>/dev/null || true
}

run_render() {
  mkdir -p "$KACHALKA_DATA/downloads" "$output"
  python3 - <<'PY'
import json, os
from pathlib import Path
root = Path(os.environ['KACHALKA_DATA'])
(root / 'settings.json').write_text(json.dumps(dict(
    Folder=str(root / 'downloads'), FolderConfigured=True, AutoResumeDownloads=False,
    CheckForUpdates=False, AutoUpdate=False, UiLanguage='ru')))
PY
  "$app" --smoke-test "$output" > "$output/app.log" 2>&1 &
  local process=$!
  if ! wait_for "${SMOKE_TIMEOUT:-900}" "$process"; then
    stop_stuck "$process"
    echo "::error title=Native smoke test timed out::last stage: $(cat "$output/stage.txt" 2>/dev/null || echo unknown)"
    tail -n 60 "$output/app.log" || true
    failed=1
    return
  fi
  local status=0
  wait "$process" || status=$?
  if [ "$status" -ne 0 ]; then
    echo "::error title=Native smoke test::the application exited with status $status"
    tail -n 60 "$output/app.log" || true
    failed=1
  fi
  if [ -f "$output/error.txt" ]; then
    echo "::group::error.txt"
    cat "$output/error.txt"
    echo "::endgroup::"
    failed=1
  fi
  if [ ! -f "$output/checks.json" ]; then
    echo "::error title=Native smoke test::checks.json was not written"
    failed=1
    return
  fi
  python3 "$here/verify_smoke.py" "$output" \
    --required "$here/required-checks.json" --allowlist "$here/layout-allowlist.json" --label "$label" || failed=1
}

run_close() {
  mkdir -p "$output"
  local stamp
  stamp="$(mktemp)"
  sleep 1
  "$app" --close-smoke-test "$output" > "$output/app.log" 2>&1 &
  local process=$!
  if ! wait_for 60 "$process"; then
    stop_stuck "$process"
    echo "::error title=Native window close timed out::the application did not quit after its window was closed"
    tail -n 60 "$output/app.log" || true
    failed=1
    return
  fi
  local status=0
  wait "$process" || status=$?
  if [ "$status" -ne 0 ]; then
    echo "::error title=Native window close::the application exited with status $status"
    failed=1
  fi
  if [ -f "$output/error.txt" ]; then cat "$output/error.txt"; failed=1; fi
  if [ ! -f "$output/checks.json" ]; then
    echo "::error title=Native window close::the termination marker was not written"
    failed=1
  else
    cat "$output/checks.json"
    python3 - "$output/checks.json" <<'PY' || failed=1
import json, sys
data = json.load(open(sys.argv[1]))
problems = []
if data.get('windowClosedBeforeTerminate') is not True:
    problems.append('the window did not report that it closed before the application terminated')
if data.get('terminated') is not True:
    problems.append('the application did not terminate through the normal quit path')
if problems:
    for problem in problems:
        print('::error title=Native window close::' + problem)
    sys.exit(1)
PY
  fi
  local changed
  changed="$(find "$KACHALKA_DATA" -type f -newer "$stamp" -not -path '*/downloads/*' 2>/dev/null | head -n 20)"
  if [ -z "$changed" ]; then
    echo "::error title=Native window close::closing the window did not save any state under the profile"
    ls -lT "$KACHALKA_DATA" || true
    failed=1
  else
    echo "State written while closing:"
    echo "$changed"
  fi
  rm -f "$stamp"
}

case "$mode" in
  render) run_render ;;
  close) run_close ;;
  *) echo "unknown mode: $mode" >&2; exit 2 ;;
esac
engine_gone || failed=1
exit "$failed"
