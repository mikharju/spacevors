#!/usr/bin/env python3
"""Rewrite the two absolute paths Xvfb hardcodes into short /tmp paths.

The sandbox has a read-only /usr and /var, so Xvfb cannot find xkbcomp at
/usr/bin/xkbcomp nor write compiled keymaps to /var/lib/xkb/. Both strings are
replaced in place (same or shorter length) and env.sh creates the /tmp
counterparts. Run once after extracting the .deb files.
"""

import sys
from pathlib import Path

TARGET = Path(__file__).resolve().parent / "root/usr/bin/Xvfb"
PATCHES = [
    (b"/usr/bin\x00", b"/tmp/xkb\x00"),        # xkbcomp lookup dir
    (b"/var/lib/xkb/\x00", b"/tmp/xkmo/\x00"),  # compiled keymap output dir
]

data = bytearray(open(TARGET, "rb").read())
for old, new in PATCHES:
    if len(new) > len(old):
        sys.exit(f"replacement {new!r} longer than {old!r}")
    hits = data.count(old)
    if hits == 0:
        if data.count(new) and new in data:
            print(f"already patched: {old!r}")
            continue
        sys.exit(f"pattern not found: {old!r}")
    data = data.replace(old, new + b"\x00" * (len(old) - len(new)))  # pad: keep offsets intact
    print(f"patched {hits}x {old!r} -> {new!r}")
open(TARGET, "wb").write(data)
