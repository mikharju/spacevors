# Screenshot testing tools

Headless visual testing for SpaceVors: a virtual X display, the real game
running on software GL, keyboard/mouse injection, and pixel comparison.
Everything lives under `.tools/` and needs no root.

## Why it looks like this

Four sandbox facts drive the design (verified, see TROUBLE_SHOOTING.md #25):

- **No root, read-only `/usr`.** Xvfb, xdotool, mesa and their deps are
  unpacked from `.deb` files into `.tools/root` and used via `LD_LIBRARY_PATH`.
  Xvfb is binary-patched (`patch-xvfb.py`) because it hardcodes
  `/usr/bin/xkbcomp` and `/var/lib/xkb/`, neither of which exists here.
- **`/tmp` is recreated for every command.** Nothing can be shared through it,
  so the display is reached over TCP (`DISPLAY=localhost:99`), not a unix socket.
- **Processes die when the command that started them ends.** The display and the
  game must be started as harness background jobs, then driven from later commands.
- **Each command has its own PID namespace.** `pgrep`/`pkill` cannot see them, so
  `stop.sh` closes the window instead (WM_DELETE → GLFW leaves its loop).

## Setup

```bash
bash .tools/setup.sh     # debs + unpack + Xvfb patch + numpy/pillow; network, ~350 MB
```

## Use

Start the two long-lived processes as **background jobs** (they must outlive the
command that starts them):

```bash
.tools/xvfb.sh           # background job: virtual display :99, 1920x1024x24
.tools/game.sh           # background job: the game, logs to .tools/run/app.log
```

Then, from any ordinary command:

```bash
.tools/smoke.sh                        # end-to-end check of everything below
.tools/shot.sh main-menu               # F12 -> .tools/shots/main-menu.png
.tools/input.sh key 1                  # hold a key one frame (1 = pick ship)
.tools/input.sh click 960 512          # move + hold left button one frame
.tools/diff.sh a.png b.png --expect-different
.tools/scenario.sh ammo-spray --sim    # perf scenario; logs + index.tsv under .tools/run/
bash .tools/stop.sh                    # close the game window
```

`. .tools/env.sh` is what the scripts source; source it yourself to use
`xdotool`, `xwininfo` or `xdpyinfo` directly.

## Limits

- **~4 fps.** llvmpipe renders this game's lighting at 1920x1024 in about
  250 ms/frame. Input must be held longer than a frame, which is why
  `input.sh` exists: a plain `xdotool key`/`click` can land entirely between
  two polls and be lost without any error.
- **Looking at a frame depends on the active model.** An image-capable model can
  read `.tools/shots/<name>.png` directly; the harness downscales 1920x1024, so
  crop the region of interest first when small text or 1-px detail matters.
- **Deterministic.** Two captures of the same static screen are pixel-identical,
  so `--expect-same` is a real regression gate. Prefer it over describing a
  frame: `diff.sh` and `PYTHONPATH=.tools/pylibs python3 -c '...'` work with any
  model and do not depend on judgement.
- `stop.sh` makes raylib log a `BadDrawable` X error on the way out; harmless.
- `xdotool search --name` does not match on this build; use `--class SpaceVors`.
