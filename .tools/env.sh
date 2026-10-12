# Source this in every command that touches the virtual display:  . .tools/env.sh
# The sandbox has no root, a read-only /usr, and a per-command /tmp, so the
# whole X11 stack lives in .tools/root and /tmp is repopulated on every source.
# Works from bash (sourced) and from sh (sourced by the launcher scripts).
_self="${BASH_SOURCE:-$0}"
case "$_self" in
  */env.sh) _TOOLS="$(cd "$(dirname "$_self")" && pwd)" ;;
  *)        _TOOLS="$(cd "$(dirname "$0")" && pwd)" ;;
esac
_ROOT="$_TOOLS/root"
export TOOLS_ROOT="$_TOOLS"
export PATH="$_ROOT/usr/bin:$PATH"
export LD_LIBRARY_PATH="$_ROOT/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:}$LD_LIBRARY_PATH"
export LIBGL_DRIVERS_PATH="$_ROOT/usr/lib/x86_64-linux-gnu/dri"
export XKBDIR="$_ROOT/usr/share/X11/xkb"

# Xvfb is patched to call /tmp/xkb/xkbcomp and write keymaps to /tmp/xkmo.
mkdir -p /tmp/xkmo
ln -sfn "$_ROOT/usr/bin" /tmp/xkb

# /tmp is a per-command overlay, so the X11 unix socket of a display started in
# one command is invisible to the next. TCP is the only transport that survives.
export DISPLAY=localhost:99
export LIBGL_ALWAYS_SOFTWARE=1   # llvmpipe: no GPU in the sandbox
export HOME=/tmp/dotnet-home     # real HOME is read-only; dotnet writes here
mkdir -p "$HOME"
