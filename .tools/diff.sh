#!/bin/bash
# Pixel-compare two shots; adds the local numpy+pillow install to PYTHONPATH.
. "$(dirname "$0")/env.sh"
export PYTHONPATH="$_TOOLS/pylibs${PYTHONPATH:+:}$PYTHONPATH"
exec python3 "$_TOOLS/diff.py" "$@"
