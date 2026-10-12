#!/bin/bash
# Start the virtual display. Run as a background job; it must outlive the command.
. "$(dirname "$0")/env.sh"
exec Xvfb :99 -listen tcp -ac -screen 0 "${SCREEN:-1920x1024x24}" -xkbdir "$XKBDIR"
