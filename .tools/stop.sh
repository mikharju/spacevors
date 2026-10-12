#!/bin/bash
# Stop the game by closing its window: each command runs in its own PID
# namespace, so pkill/pgrep cannot see a process started by another command.
# WM_DELETE_WINDOW makes GLFW leave its loop, so assets unload cleanly.
# With --all the virtual display is closed too (it holds no window of its own).
. "$(dirname "$0")/env.sh"
found=0
for w in $(xdotool search --class SpaceVors 2>/dev/null); do xdotool windowclose "$w" && found=1; done
[ "$found" = 1 ] && echo "game closing" || echo "no game window"
if [ "${1:-}" = "--all" ]; then
    for w in $(xdotool search --class Xvfb 2>/dev/null); do xdotool windowclose "$w"; done
    pkill -f "Xvfb[ ]*:99" 2>/dev/null   # works when run from the display's own command
    rm -rf "$_TOOLS/run"
fi
