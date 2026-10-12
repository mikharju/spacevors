#!/bin/bash
# Start SpaceVors on the virtual display. Run as a background job so it
# outlives the command that starts it.
. "$(dirname "$0")/env.sh"
APP="${APP:-$_TOOLS/../src/Game/bin/Debug/net10.0}"
mkdir -p "$_TOOLS/run" && cd "$_TOOLS/run" || exit 1
rm -f screenshot.png
exec env SPACEVORS_DIAGNOSTIC="${SPACEVORS_DIAGNOSTIC:-1}" dotnet "$APP/Game.dll"
