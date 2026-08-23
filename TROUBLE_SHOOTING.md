# TROUBLE SHOOTING

Known problems hit while screenshot-testing the game headlessly (Xvfb + xdotool) and how to avoid them.
Each entry: **Symptom** → **Cause** → **Fix / Prevention**.

## 1. Stale `screenshot.png` copies (cp race)

- **Symptom**: Copied screenshots contained content from an *earlier* frame/shot; two "different" shots were byte-identical (`md5sum` equal); led to false conclusions like "scrolling did nothing".
- **Cause**: `TakeScreenshot("screenshot.png")` runs inside the game's input phase and its log lines are block-buffered when stdout is redirected to a file, so both the file content and `app.log` can lag behind what actually happened. A short `sleep 1; cp screenshot.png` can grab stale data.
- **Fix / Prevention**:
  - Each F12 shot in a game run produces two files: an auto-incremented unique copy `screenshot0NN.png` (per-process counter, starts at 000) and an updated `screenshot.png` holding the latest frame. Either is safe to read; verify with `md5sum` before trusting it (identical hashes = same frame).
  - The exact mechanism lives in the stripped native raylib lib and was not reproducible from tight-loop test programs — don't chase it, just handle both file patterns.
  - Clean up both patterns when done: `rm screenshot*.png`.
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

## 7. Stale binary after a failed rebuild (silent)

- **Symptom**: A test program "behaved" completely differently from its source (exited early, missing output); changes appeared to have no effect.
- **Cause**: `dotnet build` had failed (compile error), but the launch step still ran — executing the *previous* binary silently. Piping build output through `tail -1` hid the failure.
- **Fix / Prevention**: Always check for "Build succeeded" (or a non-zero exit code) before launching; don't truncate build output when iterating on scratch programs.

## 8. xdotool window search only finds live windows

- **Symptom**: `xdotool search --name <title>` returned nothing, so `windowfocus`/input targeting failed with "Invalid window".
- **Cause**: The target process had already exited (short-lived test program); its X window is gone. Chasing the missing window wasted time.
- **Fix / Prevention**: Check `pgrep -x <procname>` FIRST; only search for windows of processes confirmed alive.

## 9. No window focus needed for xdotool input

- **Symptom/Note**: `xdotool windowactivate` fails on Xvfb ("windowmanager claims not to support _NET_ACTIVE_WINDOW") — but plain `xdotool key ...` still works anyway.
- **Cause**: GLFW grabs X input focus when it creates the window, even without a window manager. The failed activate is harmless; don't add workarounds for it.

## 10. Stuck keydowns degrade all keyboard input (persists across processes)

- **Symptom**: `xdotool key` events stop being processed by the game (F12/R/ESC/digits do nothing) while mousemove/click still work and X reports correct focus; ship-select highlight drifts down on its own.
- **Cause**: An unpaired `xdotool keydown X` (e.g. an interrupted hold sequence) leaves the key DOWN at the X server level — this state persists across client processes. Xvfb auto-repeats held keys, flooding the event queue; other clients' synthetic keypresses get dropped while the stuck key's `IsKeyDown` stays true forever.
- **Fix / Prevention**:
  - Prefer atomic `xdotool key X`; when holding is needed, always pair `keydown`/`keyup` (even on error paths).
   - If input mysteriously dies or UI drifts: release everything — `DISPLAY=:99 xdotool keyup w a s d Up Down Left Right Return Escape F12`. Note xdotool keysym names are case-sensitive X names (`Up`, `Return`, not `up`/`enter`; shift is `shift`, NOT `LeftShift` — the bad name is only reported as "Ignoring it", so the key silently never goes down).
  - Nuclear option: restart Xvfb to reset keyboard state.

## 11. Resolving assets against `AppContext.BaseDirectory` silently loses all textures

- **Symptom**: After switching asset loading from CWD-relative paths to `AppContext.BaseDirectory`, the game ran but every sprite fell back (no ship previews on the select screen, rectangle asteroids) with no error logged.
- **Cause**: `BaseDirectory` is the build output dir (`src/Game/bin/Debug/net10.0/`), which did not contain an `assets/` folder — nothing in the csproj copied it there. CWD-relative paths only "worked" when launching from the repo root, masking the dependency.
- **Fix / Prevention**: Game.csproj has `<None Include="..\..\assets\**\*" CopyToOutputDirectory="PreserveNewest" LinkBase="assets" />`. When adding new asset folders, they are picked up automatically; verify with `find src/Game/bin/Debug/net10.0/assets -type f` after a build. Launching the binary from an unrelated CWD (e.g. `/tmp`) is the test that proves resolution is correct.

## 12. Fast synthetic key taps get lost (same race as mouse clicks)

- **Symptom**: `xdotool key F12` / `key l` intermittently produced no effect at all — e.g. 0 of 3 screenshot presses registered during gameplay, while the same press worked on another screen; a single tap sometimes also appears to "double-fire" (see entry 1: each shot writes both a numbered file and an updated `screenshot.png`).
- **Cause**: Same as entry 6 — a quick press+release can land inside one input poll batch, so the per-frame edge-triggered `IsKeyPressed` never sees the transition. It is timing luck, not screen-dependent (once F11/F12 handling runs in both game loops).
- **Fix / Prevention**: For keys that must register reliably, hold them: `xdotool keydown F12; sleep 0.15; xdotool keyup F12` (~13 frames at 120 fps guarantees the edge is polled while down). Plain `xdotool key X` is fine for navigation keys where a miss just means retrying.

 ## 13. Re-reading an overwritten image file can show stale content
 
 - **Symptom**: Cropped a new screenshot into the same output filename as an earlier crop, read it back, and saw the *previous* image (e.g. "GAME OVER" text that was not in the source frame); later crops of the same source file showed different content, contradicting the first read.
 - **Cause**: The Read tool appears to cache by file path; overwriting a file and re-reading it can return the cached older bytes even though `md5sum` on disk shows new content.
 - **Fix / Prevention**: Give every crop/derivative image a unique filename (include the source screenshot number, e.g. `tight_008.png`). Never overwrite-and-reread; if in doubt, verify with `md5sum` and re-read under a fresh name.
 
 ## 14. Shader uniforms are global at batch-flush time — per-sprite uniform sets leak inside one shader-mode block
 
 - **Symptom**: Tried to batch many lit sprites under a single `BeginShaderMode`/`EndShaderMode`, setting normal/depth map textures and the rotation-angle uniform per sprite before each `DrawTexturePro`. Rendered output was wrong: earlier sprites drew with *later* sprites' maps/angle (probe pixel diff maxChannelDelta=187), even though every call sequence looked correct.
 - **Cause**: In this raylib build (6.0 via Raylib-cs 8.0.0), `SetShaderValue*` uploads to GL state immediately, but vertex data is batched and only drawn at flush time — with whatever uniform/sampler state exists *then*. So all sprites in one block get the last-set values. A texture change forces a mid-block flush (which is why different base textures partially masked it), but scalar uniforms leak unconditionally. Upstream raylib 6.0 source does not even ship its GL layer, so this could only be established empirically via `RenderBench probe`.
 - **Fix / Prevention**: Inside one shader-mode block, every draw must share identical uniform state — group draws by sprite variant (same maps) and pass per-instance data with the vertices instead. That is why `Lighting` packs the rotation angle into the vertex color's RG channels (`EncodeAngle`) rather than using an `angleRad` uniform. Verify any future batching change with `RenderBench probe`, which pixel-compares against a frozen legacy-shader oracle.
 
## 15. Escape is raylib's default exit key — it closes the window

- **Symptom**: Assigned Escape to close an in-game overlay (stats screen); pressing it during a screenshot test made the whole game quit ("Window closed successfully" in log, process gone) instead of closing the overlay.
- **Cause**: The app never calls `Raylib.SetExitKey(0)`, so raylib's default exit key (Escape) is active and checked via `WindowShouldClose()`. Any in-game use of Escape competes with it — whichever runs first wins, and the window-close check runs at loop top.
- **Fix / Prevention**: Don't bind Escape to UI actions unless you also call `Raylib.SetExitKey(0)` at init (which then removes Esc-to-quit entirely). The stats screen uses Tab for both open and close.

## 16. Ship-level upgrade options must use an empty weapon name

- **Symptom**: Stats screen showed `x0` for pickup radius after upgrading it (the stat value itself changed correctly). Found while screenshot-verifying the upgrade-count column.
- **Cause**: `LevelUpSystem` created the PickupRadius option with `weaponNames[0]`, so `ApplyUpgrade` stored the count under `(PickupRadius, "MachineGun")` while the stats screen looks up ship-level stats with an empty weapon name. The label rendering masked it (pickup radius labels ignore the weapon name).
- **Fix / Prevention**: Ship-level upgrade options (Hp, thrusts, turn speed, pickup radius) must use `""` as weapon name — it is the count-tracking key. When adding new upgrade options, verify the stats screen count column increments (scripted run with SPACEVORS_DIAG_UPGRADES + Tab screenshot).

## 17. Transient `dotnet build` CLR crash with exit code 0

- **Symptom**: Build output ends in "Fatal error. / Internal CLR error. (0x80131506)" but the command exits 0, so a chained `&& dotnet test --no-build` runs against *stale* binaries and can pass while testing old code.
- **Cause**: Transient toolchain crash (seen once on this machine); a plain re-run builds fine. The exit code does not reflect the failure when output is piped through `tail`.
- **Fix / Prevention**: Treat any build whose output lacks "Build succeeded" as failed — re-run until it prints, then re-run tests even if an earlier `--no-build` pass already went green (same discipline as entry 7).

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
