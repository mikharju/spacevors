#!/bin/bash
# Capture one frame from the running game into .tools/shots/<name>.png.
# raylib writes screenshot.png plus a screenshotNNN.png copy, so the newest
# match wins. F12 is retried because a key sent while the window is unfocused
# is dropped by GLFW without any error.
# Usage: .tools/shot.sh <name>
. "$(dirname "$0")/env.sh"
name="${1:?usage: shot.sh <name>}"
run="$_TOOLS/run"

# This xdotool build matches WM_CLASS but not WM_NAME, so search by class.
wid="$(xdotool search --class SpaceVors | head -1)"
[ -n "$wid" ] || { echo "no SpaceVors window running (start .tools/game.sh)" >&2; exit 1; }

# The game writes screenshots into its own working directory, which is .tools/run for game.sh but
# .tools/run/scenario-<name>-<utc> for scenario.sh, so search the whole tree.
clear_shots() { find "$run" -name 'screenshot*.png' -delete 2>/dev/null; }
newest_shot() { find "$run" -name 'screenshot*.png' -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -1 | cut -d' ' -f2-; }

clear_shots
for _ in 1 2 3; do
    xdotool windowfocus --sync "$wid"
    xdotool key --clearmodifiers F12
    for _ in $(seq 20); do
        shot="$(newest_shot)"
        [ -n "$shot" ] && break
        sleep 0.2
    done
    [ -n "$shot" ] && break
done
[ -n "${shot:-}" ] || { echo "no screenshot written (is the game drawing?)" >&2; exit 1; }

mkdir -p "$_TOOLS/shots"
mv "$shot" "$_TOOLS/shots/$name.png"
clear_shots
md5sum "$_TOOLS/shots/$name.png"
