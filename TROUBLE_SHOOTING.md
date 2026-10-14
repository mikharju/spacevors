# TROUBLE SHOOTING

Known LLM-workflow problems hit while working on this project headlessly (screenshot testing with Xvfb + xdotool, sandboxed build/test) and how to avoid them. One-off project code bugs are not recorded here — the fix lives in the code itself.
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

## 12. Fast synthetic key taps get lost (same race as mouse clicks)

- **Symptom**: `xdotool key F12` / `key l` intermittently produced no effect at all — e.g. 0 of 3 screenshot presses registered during gameplay, while the same press worked on another screen; a single tap sometimes also appears to "double-fire" (see entry 1: each shot writes both a numbered file and an updated `screenshot.png`).
- **Cause**: Same as entry 6 — a quick press+release can land inside one input poll batch, so the per-frame edge-triggered `IsKeyPressed` never sees the transition. It is timing luck, not screen-dependent (once F11/F12 handling runs in both game loops).
- **Fix / Prevention**: For keys that must register reliably, hold them: `xdotool keydown F12; sleep 0.15; xdotool keyup F12` (~13 frames at 120 fps guarantees the edge is polled while down). Plain `xdotool key X` is fine for navigation keys where a miss just means retrying.

 ## 13. Re-reading an overwritten image file can show stale content
 
 - **Symptom**: Cropped a new screenshot into the same output filename as an earlier crop, read it back, and saw the *previous* image (e.g. "GAME OVER" text that was not in the source frame); later crops of the same source file showed different content, contradicting the first read.
 - **Cause**: The Read tool appears to cache by file path; overwriting a file and re-reading it can return the cached older bytes even though `md5sum` on disk shows new content.
 - **Fix / Prevention**: Give every crop/derivative image a unique filename (include the source screenshot number, e.g. `tight_008.png`). Never overwrite-and-reread; if in doubt, verify with `md5sum` and re-read under a fresh name.
 
## 15. Escape is raylib's default exit key — it closes the window

- **Symptom**: Assigned Escape to close an in-game overlay (stats screen); pressing it during a screenshot test made the whole game quit ("Window closed successfully" in log, process gone) instead of closing the overlay.
- **Cause**: The app never calls `Raylib.SetExitKey(0)`, so raylib's default exit key (Escape) is active and checked via `WindowShouldClose()`. Any in-game use of Escape competes with it — whichever runs first wins, and the window-close check runs at loop top.
- **Fix / Prevention**: Don't bind Escape to UI actions unless you also call `Raylib.SetExitKey(0)` at init (which then removes Esc-to-quit entirely). The stats screen uses Tab for both open and close.

## 17. Transient `dotnet build` CLR crash with exit code 0

- **Symptom**: Build output ends in "Fatal error. / Internal CLR error. (0x80131506)" but the command exits 0, so a chained `&& dotnet test --no-build` runs against *stale* binaries and can pass while testing old code.
- **Cause**: Transient toolchain crash (seen once on this machine); a plain re-run builds fine. The exit code does not reflect the failure when output is piped through `tail`.
- **Fix / Prevention**: Treat any build whose output lacks "Build succeeded" as failed — re-run until it prints, then re-run tests even if an earlier `--no-build` pass already went green (same discipline as entry 7).

## 18. Mouse-driven camera drift shifts click world coordinates

- **Symptom**: A left-click computed as "window center + (targetWorld − playerWorld)" landed ~57px away from the intended target — outside the pick zone — while keys and mousemove worked fine. Reproduced across several attempts; the miss distance changed with where the mouse was parked.
- **Cause**: `CameraSystem` eases a drift offset toward the mouse's screen position (dead zone 5%, max half of the half-viewport, ease speed 1.5/s). The click→world conversion uses the *current* camera target (`player + drift`), so an off-center mouse moves the whole world under the cursor. A quick `mousemove; mousedown` gives the drift only ~0.3s to settle (≈36% of its final value) — enough to miss a 30px zone.
- **Fix / Prevention**: For deterministic clicks, park the mouse at the *drift fixed point* — the position where settled drift maps the click exactly onto the target: solve `M = center + offset − Drift(n(M))` (one-dimensional per axis; converges in 2–3 hand iterations), then wait ≥2.5s after `mousemove` before `mousedown`. Verify with the game's own log: GameSession logs every click as `[target] ... click=(x,y) player=(x,y)` under SPACEVORS_DIAGNOSTIC=1, so a miss is measurable instead of guessed.

## 19. Game over screen silently ignores all input — and test enemies kill the player fast

- **Symptom**: After spawning a diagnostic enemy ship (N key), every subsequent xdotool click/key produced no log line at all; earlier clicks in the same run had worked.
- **Cause**: The spawned enemy's turret fires at the player from within its 700px range (~4.5 dmg/s vs 8 HP) → game over within seconds. On the game-over screen `ReadPlayerInput` does not run, so all input is silently dropped — indistinguishable from a focus problem.
- **Fix / Prevention**: Before debugging "input stopped working", take an F12 screenshot and check for GAME OVER (and the HP bar). For visual tests that outlast a few seconds: press H first (player health → 10000, diagnostic-only) so test enemies cannot end the run.

## 20. Player position is not stable during headless tests — kickback and collisions move it (no linear drag)

- **Symptom**: Two player-position samples taken seconds apart differed by hundreds of px with no keys pressed; any click computed from a stale player position missed.
- **Cause**: Physics has angular damping but *no* linear drag, so anything that changes velocity persists: turret kickback (`TurretFiringSystem` adds recoil to the player on every shot), collision impulses, explosion knockback. With auto-targeting weapons firing continuously, the ship drifts at 50–200 px/s even with zero input.
- **Fix / Prevention**: Press T (diagnostic-only) to toggle a position pin: it zeroes velocity and re-writes Position/Acceleration every tick before the simulation step, so no force can move the player. Sample positions from the `[target]`/`[diag]` log lines rather than assuming the ship stays where it spawned.

## 21. LoadTestWeapon sprays thousands of bullets in a ring — don't use Shadow for weapon-behavior tests

- **Symptom**: A visual test with the ship carrying `LoadTestWeapon` (Scout, key 1 at the time; moved to Shadow, key 4) showed a dense isotropic ring of ~2500 yellow bullets around the player instead of shots converging on targets; any conclusion about targeting/aiming from that frame is meaningless.
- **Cause**: The load-test ship's loadout carries `LoadTestWeapon` (`GameplayComponents.cs`, "Intentionally kept while development is ongoing so manual load testing is easy"). It fires continuously into a wide arc, producing the ring and dominating the ammo entity count.
- **Fix / Prevention**: For tests of normal weapon behavior (auto-targeting, lead prediction, click targeting), select Scout, Fighter or Heavy (keys 1/2/3). Reserve Shadow for deliberate load/perf checks.

## 22. Upgrade-menu pause silently swallows all input — and kills/asteroid XP trigger it mid-test

- **Symptom**: Keys (N/H/T) and clicks that worked seconds earlier suddenly produced no log lines at all; nothing looked broken, focus was correct.
- **Cause**: Destroying enemies or asteroids drops XP; the magnet pulls it in, a level-up pauses the game on the upgrade menu, and `ReadPlayerInput` does not run while paused — every key/click is silently dropped until a card is chosen (same trap as entry 19's game-over screen).
- **Fix / Prevention**: When input mysteriously dies mid-run, grep for `[UPGRADE] level=` in app.log or take an F12 shot and look for the bottom card row. Resume by pressing 1–5 (or clicking a card) before sending further test input.

## 23. `dotnet` crashes on first run in sandboxed environments (read-only HOME, non-persistent /tmp)

- **Symptom**: Any `dotnet` command dies with `System.IO.IOException: Read-only file system : '/home/agent/.dotnet'` during first-time-use configuration; or "The user's home directory could not be determined."
- **Cause**: The sandbox makes the default HOME read-only, and /tmp is a per-command overlay — writes there do not persist between commands. dotnet needs a writable HOME for its first-run sentinel files.
- **Fix / Prevention**: Do it all in one command: `mkdir -p /tmp/dotnet-home && export HOME=/tmp/dotnet-home NUGET_PACKAGES=/tmp/nuget-packages DOTNET_CLI_TELEMETRY_OPTOUT=1 && dotnet test src/Tests/Tests.csproj`. NuGet packages (xunit, Test.Sdk, Raylib-cs) are not preinstalled and re-download from nuget.org on each invocation (network required); bin/obj outputs inside the workspace do persist.

## 24. `new Color(...)` is ambiguous when mixing byte and int arguments

- **Symptom**: `Color c = new((byte)r, (byte)g, (byte)b, 255);` fails with CS0121 "The call is ambiguous between 'Color.Color(byte, byte, byte, byte)' and 'Color.Color(int, int, int, int)'".
- **Cause**: Raylib-cs defines both overloads. An all-int list resolves to the int overload (identity beats constant conversion), but a mixed list keeps both candidates viable — `255` is a constant that fits in byte, so neither overload is strictly better.
- **Fix / Prevention**: Make all four arguments the same type: either all ints (`new(255, 80, 80, 230)`) or a typed constant for the odd one out (`private const byte OpaqueAlpha = 255; new(r, g, b, OpaqueAlpha)`).

## 25. No display in the agent sandbox — screenshots need the rootless toolchain in `.tools/`

- **Symptom**: The "General workflow that works" recipe fails at step 2: no `Xvfb`, no `xdotool`, no `/tmp/.X11-unix`, no wayland socket, `DISPLAY` empty — `Game` and `RenderBench` both need a window and cannot run, so F12 screenshots are impossible.
- **Cause**: The agent sandbox runs headless without X/Wayland and without root to install Xvfb. This varies per environment — check before promising screenshot verification.
- **Fix / Prevention**: A rootless toolchain for exactly this sandbox lives in `.tools/` (see `.tools/README.md` and "General workflow that works" below). If `.tools/root` is missing, rebuild it with `bash .tools/setup.sh`, which unpacks .debs without root. Only when even that is impossible (no network) fall back to offline shader validation: download the `glslang-wasm-validator` npm tarball with curl (npm itself fails: unwritable HOME; do download + untar + run in ONE command since /tmp is per-command), extract the shader strings from `Lighting.cs` with awk, rewrite `#version 330` → `#version 310 es` + `precision highp float;` and `{{MaxPointLights}}` → `16`, then `node package/dist/cli.mjs --vertex v.vert --fragment f.frag --json`. That checks compile+link only; say so when reporting.

## 26. Every agent command is a fresh sandbox: /tmp, PIDs and processes do not carry over

- **Symptom**: A display or game started with `setsid ... &` in one command is gone in the next (its `/tmp/.X11-unix/X99` socket with it); `pgrep`/`pkill` report nothing while the game is provably still running; `dotnet` dies with "Read-only file system : '/home/agent/.dotnet'".
- **Cause**: Each command runs in a new mount + PID namespace over the same workspace. `/tmp` and `/dev/shm` are recreated, `HOME` is read-only, a process is killed when the command that started it ends, and processes owned by other commands are not visible to `pgrep`. Only the workspace directory persists.
- **Fix / Prevention**: Long-lived things (Xvfb, the game) must be started as harness **background jobs**, which do survive between commands, and must talk over TCP (`DISPLAY=localhost:99`) because a unix socket in `/tmp` cannot be reached from another command. Stop them with `xdotool windowclose`, never `pkill`. Point `HOME` at `/tmp/dotnet-home` for anything dotnet writes. `.tools/env.sh` does all of this — source it instead of re-deriving it.

## 27. Input sent to the game is silently dropped when it lands between two frames

- **Symptom**: `xdotool key 1` / `xdotool click 1` produced nothing at all — no log line, no state change — while the same action worked when done by hand, and window focus was correct.
- **Cause**: Under llvmpipe the game renders at ~4 fps (250 ms/frame). raylib samples input once per frame, so a press+release pair that completes between two polls is in neither one. Nothing is queued and nothing is reported.
- **Fix / Prevention**: Hold keys and buttons longer than a frame; `.tools/input.sh key|click` does `keydown; sleep 0.4; keyup`. Do not use bare `xdotool key`/`click` against this game.

## 28. `PerformanceBenchmark` frame-budget failures are timing noise, not proof your change regressed perf

- **Symptom**: A full `dotnet test` run fails with "5k ammo + 100 ships: average frame time 12.55ms exceeds budget 8.33ms" right after adding tests, while the same test passes on the untouched tree and passes again on re-run.
- **Cause**: That scenario measures 7.9-8.1 ms against an 8.33 ms budget, so it fails whenever the machine is busy (parallel test collections, cold JIT on the first run after a build). The reported number is not reproducible run to run.
- **Fix / Prevention**: Before blaming a change for a benchmark failure, re-run the benchmark alone (`dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~PerformanceBenchmark"`) three times with the change and three times with it stashed, and compare the same scenario line. Only a stable difference across runs counts.

## 29. `.tools/env.sh` cannot be sourced under `set -u`

- **Symptom**: A new `.tools/*.sh` script died at once with `env.sh: line 13: LD_LIBRARY_PATH: unbound variable`.
- **Cause**: env.sh appends to variables that are legitimately unset in a fresh shell (`${LD_LIBRARY_PATH:+…}`); `set -u` makes that fatal. The other tool scripts deliberately do not use `set -u`.
- **Fix / Prevention**: Do not put `set -u` in a script that sources `.tools/env.sh`. Related bash trap: an expanded word is never an assignment prefix, so a conditional env var must be `export`ed before the command instead of written inline as `${var:+NAME=$var} cmd`.

## 30. `Raylib.GetFrameTime()` describes the previous frame, so per-frame cost splits go negative

- **Symptom**: A frame breakdown printed `other=-703ms` — the sum of the measured stages was far larger than the frame interval they were supposed to fit inside.
- **Cause**: `GetFrameTime()` returns the time since the *last* call, read at the top of the update. The sim and render measured after it belong to a different interval, so subtracting them from it compares two different frames.
- **Fix / Prevention**: When splitting a frame into stages, time the whole frame with your own `Stopwatch` around the stage calls and derive `other = frame - stages`. Use `GetFrameTime()` only for physics accumulation.

## 31. A tool script that sources `.tools/env.sh` breaks the same script on a real desktop

- **Symptom**: `.tools/scenario.sh <name>` printed `Segmentation fault (core dumped)` six times, opened no window, and then reported "0 frames" while still claiming `baseline updated`.
- **Cause**: `.tools/env.sh` is the *sandbox* setup — it forces `DISPLAY=localhost:99`, `LIBGL_ALWAYS_SOFTWARE=1`, and Mesa from `.tools/root`. Sourced on a real desktop it points raylib at a display that does not exist, and raylib's failed `InitWindow` segfaults instead of reporting. The script then kept looping, and the baseline step ran on a run that produced nothing.
- **Fix / Prevention**: Only the sandbox launcher scripts (`game.sh`, `shot.sh`, `xvfb.sh`) source env.sh; anything meant to run on both machines takes the environment as given and says so in its usage (` . .tools/env.sh && .tools/...` for the sandbox). Guard the two failure modes explicitly: check `DISPLAY`/`WAYLAND_DISPLAY` before launching a window, and treat a non-zero exit or a missing `summary.json` as fatal — never let a bookkeeping step report success after the measurement failed.

## Maybe remove

Borderline entries: they document project code/library issues rather than LLM-workflow problems. Keep only if the knowledge is still needed for future feature work; otherwise delete.

### 11. Resolving assets against `AppContext.BaseDirectory` silently loses all textures

- **Symptom**: After switching asset loading from CWD-relative paths to `AppContext.BaseDirectory`, the game ran but every sprite fell back (no ship previews on the select screen, rectangle asteroids) with no error logged.
- **Cause**: `BaseDirectory` is the build output dir (`src/Game/bin/Debug/net10.0/`), which did not contain an `assets/` folder — nothing in the csproj copied it there. CWD-relative paths only "worked" when launching from the repo root, masking the dependency.
- **Fix / Prevention**: Game.csproj has `<None Include="..\..\assets\**\*" CopyToOutputDirectory="PreserveNewest" LinkBase="assets" />`. When adding new asset folders, they are picked up automatically; verify with `find src/Game/bin/Debug/net10.0/assets -type f` after a build. Launching the binary from an unrelated CWD (e.g. `/tmp`) is the test that proves resolution is correct.

### 14. Shader uniforms are global at batch-flush time — per-sprite uniform sets leak inside one shader-mode block

- **Symptom**: Tried to batch many lit sprites under a single `BeginShaderMode`/`EndShaderMode`, setting normal/depth map textures and the rotation-angle uniform per sprite before each `DrawTexturePro`. Rendered output was wrong: earlier sprites drew with *later* sprites' maps/angle (probe pixel diff maxChannelDelta=187), even though every call sequence looked correct.
- **Cause**: In this raylib build (6.0 via Raylib-cs 8.0.0), `SetShaderValue*` uploads to GL state immediately, but vertex data is batched and only drawn at flush time — with whatever uniform/sampler state exists *then*. So all sprites in one block get the last-set values. A texture change forces a mid-block flush (which is why different base textures partially masked it), but scalar uniforms leak unconditionally. Upstream raylib 6.0 source does not even ship its GL layer, so this could only be established empirically via `RenderBench probe`.
- **Fix / Prevention**: Inside one shader-mode block, every draw must share identical uniform state — group draws by sprite variant (same maps) and pass per-instance data with the vertices instead. That is why `Lighting` packs the rotation angle into the vertex color's RG channels (`EncodeAngle`) rather than using an `angleRad` uniform. Verify any future batching change with `RenderBench probe`, which pixel-compares against a frozen legacy-shader oracle.

## 32. A helper's name is not its contract — `RandomInDisc` sampled a square

- **Symptom**: In the `ammo-spray` scenario, impact particles appeared across the whole asteroid and in its corners, where nothing could be colliding. Reported by a human watching the window, not by any measurement: entity counts, per-frame timing and the pass split were all unaffected, because the particles were the right *number*, just in the wrong *place*.
- **Cause**: The seeder used `RandomInDisc(radius, rng)`, which was `(rand*2-1)*radius` on each axis — a square of ±radius, so its corners sit at 1.41 radius, outside the rock. Nothing in the trace revealed it: the count was flat and the cost plausible. A geometric helper written to be plausible rather than checked.
- **Fix / Prevention**: Place seeded particles at the entry points solved from the seeded bullets' own rays, so the impact area is derived from the stream rather than assumed from the target's bounding circle. Guard the property directly (`SeededParticles_SitOnTheRockTheStreamHits`: every seeded particle must touch an asteroid). Two lessons: sample a shape by the shape, and when a rendering/scenario output looks wrong, *look at it* — cost traces cannot see placement. Also watch tolerance units: adding a pixel slack to a squared distance is 1000× too tight.

## 33. raylib shape draws are one GL call each, and in-game screenshots land in the process cwd

- **Symptom**: (a) `world.sparks`/`world.ammo` dominated the render total with cost proportional to entity count and nothing else. (b) Three attempts to screenshot a scenario window with `.tools/shot.sh` all reported "no screenshot written", while screenshots were in fact being produced.
- **Cause**: (a) `Raylib.DrawCircle`/`DrawLineEx` flush raylib's 2D render batch and issue their own draw call per shape, ~1.1 µs each on a real GPU. Nothing in the entity counts or system traces shows this — only per-pass render timing does. (b) `TakeScreenshot` writes to the *process* cwd: `.tools/run/` for `game.sh`, but `.tools/run/scenario-<name>-<utc>/` for `scenario.sh`, which `shot.sh` did not search. Separately, `xdotool key --window <id>` does not reach raylib at all (GLFW ignores sent events); the key must go to the focused window via XTEST.
- **Fix / Prevention**: Batch shapes into one `Rlgl.Begin(DrawMode.Triangles)` … `Rlgl.End()` block (`Game/ShapeBatch.cs`) with per-vertex colour. `shot.sh` now searches the whole `.tools/run` tree for the newest screenshot. When a screenshot tool reports nothing, check where the *program's* cwd is before assuming the capture failed.

## 34. `Raylib_cs.DrawMode` values are not the GL constants they name

- **Symptom**: After switching shape draws to a batched `rlgl` block, particles became almost invisible — a faint glow where thousands of sparks should be. The pass timings *improved*, which initially read as a win.
- **Cause**: `Rlgl.Begin(DrawMode.Triangles)` compiles and runs, but `DrawMode` is declared `Lines, Triangles, Quads` with no explicit values, so `Triangles` is 1 — which in GL is `GL_LINES` (GL_TRIANGLES is 0x0004). The fans were submitted and rasterized as wireframe lines. Cheap, wrong and easy to mistake for a speed-up.
- **Fix / Prevention**: Pass the raw GL mode (`Rlgl.Begin(0x0004)`) and name the constant. Generalise: when a binding layer re-declares an enum, do not assume its values match the native constants — check, or pass the native value. And read a suspicious win with suspicion: a pass that gets cheaper than the geometry it must rasterize can justify is telling you the geometry is not being drawn.

## General workflow that works

Headless build + run + screenshot. Details in `.tools/README.md`.

```bash
# 0. once per sandbox: unpack Xvfb/xdotool/mesa into .tools/root (no root needed)
bash .tools/setup.sh

# 1. start the two long-lived processes as harness BACKGROUND JOBS
#    (a plain `cmd &` dies with the command that started it — see entry 26)
.tools/xvfb.sh                       # virtual display :99, 1920x1024x24
.tools/game.sh                       # the game; log at .tools/run/app.log

# 2. from any ordinary command: drive it and capture frames
bash .tools/smoke.sh                 # display + window + capture + input, all PASS
bash .tools/input.sh key 1           # hold >= one frame, llvmpipe runs at ~4 fps
bash .tools/input.sh click 960 512
bash .tools/shot.sh main-menu        # -> .tools/shots/main-menu.png
bash .tools/diff.sh a.png b.png --expect-same

# 3. stop: close the window (pkill cannot see processes of other commands)
bash .tools/stop.sh
```

Notes: run the game with `dotnet Game.dll`, not the apphost — the built `Game`
binary asks for a linuxbrew `ld.so` that this container does not have. Whether a
frame can be *looked at* depends on the active model: an image-capable one reads
`.tools/shots/*.png` directly, but 1920x1024 is downscaled, so crop first when
small text or 1-px detail matters. Keep the assertions numeric (`diff.sh`), so a
test never depends on the model's eyes.
