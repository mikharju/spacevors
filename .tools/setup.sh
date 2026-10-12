#!/bin/bash
# Rebuild the screenshot toolchain from Ubuntu .deb files. No root needed:
# packages are downloaded with apt-get download and unpacked into .tools/root.
# Safe to re-run; already-downloaded and already-extracted pieces are skipped.
# Needs network access to archive.ubuntu.com / security.ubuntu.com.
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$TOOLS/root"
PKGS="xvfb xdotool x11-utils x11-xkb-utils xfonts-base libgl1-mesa-dri libglx-mesa0 libegl-mesa0 libegl1"
MIRRORS="http://security.ubuntu.com/ubuntu/dists/resolute-updates/main http://archive.ubuntu.com/ubuntu/dists/resolute/main"

mkdir -p "$TOOLS/debs" "$TOOLS/.extracted" "$ROOT"
cd "$TOOLS/debs" || exit 1

# apt cannot install as a user but can plan an install, which resolves deps.
apt-get install -s --no-install-recommends $PKGS 2>/dev/null |
    awk '/^Inst /{print $2}' | sort -u > "$TOOLS/pkgs.txt" || exit 1

# apt-get download fails with 404 when the local package lists are older than
# the archive; then the current filename is read straight from the Packages index.
fetch() {
    apt-get download "$1" 2>/dev/null && return 0
    for m in $MIRRORS; do
        f=$(curl -sf "$m/binary-amd64/Packages.gz" | gunzip |
            awk -v p="$1" '$1=="Package:"{hit=$2==p} $1=="Filename:"&&hit{print $2; exit}')
        if [ -n "$f" ]; then curl -sfO "${m%/dists/*}/$f" && return 0; fi
    done
    echo "MISS $1" >&2
    return 1
}

while read -r p; do
    ls "${p%%:*}"_*.deb >/dev/null 2>&1 || fetch "$p" || exit 1
done < "$TOOLS/pkgs.txt"

# Stamps only count while the tree they produced is still there.
if [ ! -x "$ROOT/usr/bin/Xvfb" ]; then rm -f "$TOOLS/.extracted"/*; fi

for d in "$TOOLS"/debs/*.deb; do
    stamp="$TOOLS/.extracted/$(basename "$d")"
    [ -f "$stamp" ] && continue
    dpkg-deb -x "$d" "$ROOT" && touch "$stamp" || { echo "extract failed: $d" >&2; exit 1; }
done

# numpy + pillow for diff.py, kept out of the (read-only) system site-packages.
if ! PYTHONPATH="$TOOLS/pylibs" python3 -c "import numpy, PIL" 2>/dev/null; then
    python3 -m pip install --quiet --break-system-packages --target "$TOOLS/pylibs" pillow numpy || exit 1
fi

python3 "$TOOLS/patch-xvfb.py" || exit 1
[ -x "$ROOT/usr/bin/Xvfb" ] && [ -x "$ROOT/usr/bin/xdotool" ] ||
    { echo "Xvfb/xdotool missing after setup" >&2; exit 1; }
echo "toolchain ready in $ROOT"
