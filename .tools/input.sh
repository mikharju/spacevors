#!/bin/bash
# Drive the game window. Every action focuses the window first (there is no
# window manager, so focus is not automatic) and holds buttons/keys down for
# longer than one frame: llvmpipe renders this game at ~4 fps, and raylib only
# sees input that is still down when the next frame is polled.
#   .tools/input.sh key <keysym>      e.g. key 1, key F12, key h
#   .tools/input.sh click <x> <y>     left click at window coordinates
#   .tools/input.sh move <x> <y>      move the pointer without clicking
#   .tools/input.sh hold <keysym> <seconds>
. "$(dirname "$0")/env.sh"
HOLD="${HOLD:-0.4}"
wid="$(xdotool search --class SpaceVors | head -1)"
[ -n "$wid" ] || { echo "no SpaceVors window running (start .tools/game.sh)" >&2; exit 1; }
xdotool windowfocus --sync "$wid"

case "${1:-}" in
  key)
    xdotool keydown "${2:?key name}" && sleep "$HOLD" && xdotool keyup "$2" ;;
  hold)
    xdotool keydown "${2:?key name}" && sleep "${3:?seconds}" && xdotool keyup "$2" ;;
  click)
    xdotool mousemove --sync "${2:?x}" "${3:?y}"
    xdotool mousedown 1 && sleep "$HOLD" && xdotool mouseup 1 ;;
  move)
    xdotool mousemove --sync "${2:?x}" "${3:?y}" ;;
  *)
    sed -n '2,8p' "$0" >&2; exit 2 ;;
esac
