#!/usr/bin/env bash
# Runs inside the Android TV emulator: walks through the app with remote keys and saves screenshots.
set -u
out="${1:-shots}"; apk="$2"; mkdir -p "$out"
adb install -r "$apk" >/dev/null
adb shell settings put global window_animation_scale 0; adb shell settings put global transition_animation_scale 0; adb shell settings put global animator_duration_scale 0
adb shell appops set cc.ka4alka.tv REQUEST_INSTALL_PACKAGES allow >/dev/null 2>&1 || true
shot() { sleep "${2:-2}"; adb exec-out screencap -p > "$out/$1.png"; echo "shot $1 $(stat -c %s "$out/$1.png")"; }
key() { for k in "$@"; do adb shell input keyevent "$k"; sleep 0.7; done; }
adb shell am start -n cc.ka4alka.tv/.MainActivity >/dev/null
shot 01-loading 4
sleep 35
shot 02-home 3
key KEYCODE_DPAD_DOWN
shot 03-home-focus
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
shot 04-home-shelves
key KEYCODE_DPAD_UP KEYCODE_DPAD_UP
key KEYCODE_DPAD_CENTER
sleep 25
shot 05-detail 3
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
shot 06-detail-releases
key KEYCODE_BACK KEYCODE_BACK
key KEYCODE_DPAD_LEFT
shot 07-rail 2
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 08-movies 5
key KEYCODE_DPAD_LEFT KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 09-series 5
key KEYCODE_DPAD_LEFT KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 10-search 3
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
adb shell input text "matrica"
shot 11-search-typed 3
key KEYCODE_BACK KEYCODE_DPAD_LEFT KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 12-saved 3
key KEYCODE_DPAD_LEFT KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 13-downloads 3
key KEYCODE_DPAD_LEFT KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
shot 14-settings 3
adb logcat -d -s AndroidRuntime:E 2>/dev/null | tail -30 > "$out/crash.txt"
