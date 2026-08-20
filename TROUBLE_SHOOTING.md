# TROUBLE SHOOTING

Known problems hit while screenshot-testing the game headlessly (Xvfb + xdotool) and how to avoid them.
Each entry: **Symptom** → **Cause** → **Fix / Prevention**.

## 1. Stale `screenshot.png` copies (cp race)

- **Symptom**: Copied screenshots contained content from an *earlier* frame/shot; two "different" shots were byte-identical (`md5sum` equal); led to false conclusions like "scrolling did nothing".
- **Cause**: `TakeScreenshot("screenshot.png")` runs inside the game's input phase and its log lines are block-buffered when stdout is redirected to a file, so both the file content and `app.log` can lag behind what actually happened. A short `sleep 1; cp screenshot.png` can grab stale data.
- **Fix / Prevention**:
  - The raylib-cs port also writes an auto-incremented unique file per shot: `screenshot000.png`, `screenshot001.png`, ... Read those instead of copying `screenshot.png`.
  - Verify with `md5sum` before trusting a screenshot; identical hashes = same frame.
  - Don't trust `tail app.log` for "did the last event happen" — check filesystem artifacts (numbered files) instead.

## 2. Leftover game processes / windows from earlier test runs

- **Symptom**: Screenshots showed impossible composites (e.g. gameplay world + ship-select cards in one frame); inputs had no effect; `xdotool` acted on the wrong window.
- **Cause**: A previous run's game process was never killed (its kill used `2>/dev/null`, hiding failure). `xdotool search --name SpaceVors | head -1` then targeted a stale window, and both processes wrote to the same `screenshot*.png` files.
- **Fix / Prevention**:
  - Before launching: `pgrep -a Game` must show nothing (or kill leftovers first).
  - After launching: assert exactly one window — `xdotool search --name SpaceVors | wc -l` == 1.
  - Never suppress stderr on `kill`; check it actually died with a follow-up `pgrep`.

## 3. Backgrounded app hangs the bash tool until timeout

- **Symptom**: A command that launched the game in the background (`... &`) never returned; the shell tool reported "terminated after exceeding timeout" even though all steps had completed (final `echo DONE` printed).
- **Cause**: The tool waits for the whole process group; a plain `&` keeps the game attached to the session.
- **Fix / Prevention**: Fully detach: `setsid <cmd> > log 2>&1 < /dev/null &`. The command then returns immediately and the app survives independently (kill it explicitly by PID when done).

## 4. `pkill -f` matched our own shell

- **Symptom**: A compound command containing `pkill -f "net10.0/Game"` produced no output and timed out — it killed its own shell, because the shell's command line contained the pattern string.
- **Fix / Prevention**: Kill by PID instead: `pgrep -a Game` (pattern that does not appear in your own command), then `kill <pid>`. Or run pkill as a standalone simple command whose text doesn't contain the target pattern.

## 5. raylib-cs API differs from stock raylib

- **Symptom**: Code using `SetKeyRepeatDelay`, `SetKeyRepeatInterval`, or `GetMouseWheelDelta` fails to compile against Raylib-cs 8.0.0.
- **Cause**: The port omits those functions; wheel input is exposed as `Raylib.GetMouseWheelMove()` (single), and it returns small notch values (~±1 per xdotool Button4/5 event), not pixel deltas.
- **Fix / Prevention**:
  - Verify the API surface before use: a tiny reflection program printing `typeof(Raylib).GetMethods(...)` (see `/tmp/opencode/apicheck` pattern) or grep the DLL.
  - Implement hold-to-repeat locally (accumulate `GetFrameTime()` while key down; step after initial delay, then at repeat interval).
  - Scale wheel deltas with a named constant (e.g. `WheelScrollPixelsPerUnit = 120f`) — raw values are invisible.

## 6. Fast synthetic mouse clicks get missed

- **Symptom**: `xdotool click 1` did not register as a selection in the game, while keys and wheel worked fine.
- **Cause**: The press+release can land between two input polls (frame ~8 ms at 120 fps); per-frame `IsMouseButtonPressed` then never sees the transition.
- **Fix / Prevention**: Send explicit slow clicks: `xdotool mousemove X Y mousedown 1; sleep 0.3; xdotool mouseup 1`.

## General workflow that works

```bash
# 1. Ensure clean state
pgrep -a Game || true                      # must be empty, else kill by PID

# 2. Launch detached (Xvfb :99 already running at target resolution)
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 setsid ./src/Game/bin/Debug/net10.0/Game \
    > /tmp/opencode/app.log 2>&1 < /dev/null &

# 3. Verify single window, then drive with xdotool (slow clicks!)
WID=$(DISPLAY=:99 xdotool search --name SpaceVors | head -1)   # count must be 1

# 4. Screenshot via F12, read the numbered file (screenshot0NN.png), verify md5sum

# 5. Kill by PID when done; remove screenshot*.png artifacts from repo root
```
