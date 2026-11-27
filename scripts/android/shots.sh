#!/usr/bin/env bash
# Runs inside the Android TV emulator: walks through the app with remote keys and saves screenshots.
set -u  # screenshots, eighth pass
out="${1:-shots}"; apk="$2"; mkdir -p "$out"
adb install -r "$apk" >/dev/null
adb shell settings put global window_animation_scale 0; adb shell settings put global transition_animation_scale 0; adb shell settings put global animator_duration_scale 0
shot() { sleep "${2:-2}"; adb exec-out screencap -p > "$out/$1.png"; echo "shot $1 $(stat -c %s "$out/$1.png")"; }
key() { for k in "$@"; do adb shell input keyevent "$k"; sleep 0.8; done; }
go() { adb shell am start -n cc.ka4alka.tv/.MainActivity --es screen "$1" >/dev/null; sleep "${2:-6}"; }
adb shell am start -n cc.ka4alka.tv/.MainActivity >/dev/null
shot 01-loading 3
sleep 40
shot 02-home 3
key KEYCODE_DPAD_CENTER
sleep 30
shot 05-detail 3
sleep 15
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
shot 06-detail-releases
key KEYCODE_BACK
sleep 3
key KEYCODE_DPAD_DOWN
shot 04-home-shelves 2
key KEYCODE_DPAD_UP KEYCODE_DPAD_LEFT
shot 03-rail 2
go movies 8; shot 07-movies 3
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
shot 08-movies-scrolled 2
go series 8; shot 09-series 3
go search 6; shot 10-search 2
adb shell input text "matrica"; sleep 2; shot 11-search-typed 3
go saved 6; shot 12-saved 2
go downloads 6; shot 13-downloads 2
go settings 6; shot 14-settings 2
adb logcat -d -s AndroidRuntime:E 2>/dev/null | tail -30 > "$out/crash.txt"
