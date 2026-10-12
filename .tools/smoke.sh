#!/bin/bash
# End-to-end check of the screenshot toolchain: display, window, capture,
# determinism, keyboard, mouse. Run it after starting the two background jobs:
#   .tools/xvfb.sh    (background job)
#   .tools/game.sh    (background job)
# Exits non-zero on the first failed check.
. "$(dirname "$0")/env.sh"
shots="$_TOOLS/shots"
log="$_TOOLS/run/app.log"
fail=0
check() { if eval "$2"; then echo "PASS  $1"; else echo "FAIL  $1"; fail=1; fi; }

xdpyinfo >/dev/null 2>&1 || { echo "no display: run .tools/xvfb.sh as a background job"; exit 1; }
[ -n "$(xdotool search --class SpaceVors 2>/dev/null | head -1)" ] ||
    { echo "no game window: run .tools/game.sh as a background job"; exit 1; }
mkdir -p "$shots"

echo "== capture =="
bash "$_TOOLS/shot.sh" smoke-select >/dev/null && echo "PASS  ship select captured" || fail=1

echo "== determinism (two frames of the same static screen) =="
sleep 1
bash "$_TOOLS/shot.sh" smoke-select2 >/dev/null
bash "$_TOOLS/diff.sh" "$shots/smoke-select.png" "$shots/smoke-select2.png" --expect-same || fail=1

echo "== keyboard (number key starts a run) =="
bash "$_TOOLS/input.sh" key 1
sleep 2
bash "$_TOOLS/shot.sh" smoke-ingame >/dev/null
bash "$_TOOLS/diff.sh" "$shots/smoke-select.png" "$shots/smoke-ingame.png" --expect-different || fail=1

echo "== mouse (click is logged by the target picker) =="
count_clicks() { local n; n="$(grep -ac "\[target\]" "$log" 2>/dev/null | tail -1)"; echo "${n:-0}"; }
before="$(count_clicks)"
bash "$_TOOLS/input.sh" click 900 500
sleep 1.5
after="$(count_clicks)"
check "click reached the game" '[ "$after" -gt "$before" ]'

echo
[ "$fail" = 0 ] && echo "all checks passed" || echo "some checks failed"
exit "$fail"
