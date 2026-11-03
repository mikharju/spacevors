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
- **Fix / Prevention**: Send explicit slow clicks: `xdotool mousemove X Y mousedown 1; sleep 0.3; xdotool mouseup 1`. And take the coordinates from the rendered frame, not from layout constants: the stats screen shrinks its content to fit, so a hand-computed button position can be off by tens of pixels and the click lands harmlessly on nothing.

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
- **Addendum**: `.tools/scenario.sh` publishes in a child process that does not see an exported `HOME` alone — publish then dies with "The user's home directory could not be determined." and `scenario.sh` reports only "publish failed; see .tools/run/publish.log". Also export `DOTNET_CLI_HOME=/tmp/dotnet-home` when driving `scenario.sh` in the sandbox.
- **Addendum (2026-11-02)**: when /tmp is a per-command overlay, put the sentinel dir in the workspace instead and restore offline from the repo's own cache — `DOTNET_CLI_HOME` alone is enough, `HOME` may stay read-only, and no network is needed:
  `mkdir -p .tools/run/dotnet-home && DOTNET_CLI_HOME=$PWD/.tools/run/dotnet-home NUGET_PACKAGES=$PWD/.nuget-packages DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test src/Tests/Tests.csproj`.
  `.tools/run/` is gitignored, so the sentinel files stay out of the tree.
- **Addendum (2026-11-02), two traps while A/B-ing a change against HEAD.** (1) A `git worktree add` copy has no
  `.tools/pylibs` (it is gitignored), so every scenario run from the worktree needs
  `PYTHONPATH=/absolute/path/to/main/workspace/.tools/pylibs`, or the analysis step dies with
  `ModuleNotFoundError: No module named 'numpy'` after the run already succeeded. (2) `--out DIR` writes one
  `summary.json` per directory, so looping several scenarios into the same `--out` silently keeps only the last
  one — give each scenario its own dir (`.tools/run/ab2-mine-1-missile-384`). Both failures look like the run
  was fine: the script exits 0, so check the files, not the exit code.

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

## 43. `dotnet build` fails in the agent sandbox on a read-only home

- **Symptom**: `dotnet build src/Domain/Domain.csproj` died with `System.IO.IOException: Read-only file system : '/home/agent/.dotnet'`, or with `The user's home directory could not be determined. Set the 'DOTNET_CLI_HOME' environment variable`.
- **Cause**: the agent shell has no writable home directory, and each command runs in a fresh sandbox, so anything dotnet wants to cache there (or in `$HOME`) fails on the first MSBuild/nuget write.
- **Fix / Prevention**: point both variables at a scratch dir on every dotnet command: `HOME=/tmp/dh DOTNET_CLI_HOME=/tmp/dh DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build …`. `/tmp` is writable but does not survive between commands (entry 26), so the first-use setup runs again per command — a couple of seconds, not worth caching. The same applies to `.tools/scenario.sh`, which shells out to dotnet.

## 44. `ComponentQuery<…>` is not a LINQ source, so `Select`/`Where` fail in a new test

- **Symptom**: A freshly written test failed to build with `'ComponentQuery<Ammo>' does not contain a definition for 'Select'`, while `.Count()` and `.ToList()` on the very same query compiled fine.
- **Cause**: `ComponentQuery<T…>` is a `readonly struct` with a pattern-based `GetEnumerator` and hand-written `ToList`/`Count`/`Any`/`TryFirst`. It does not implement `IEnumerable<T>`, so LINQ extension methods never bind on it.
- **Fix / Prevention**: in a test, `em.GetEntitiesWithComponents<T…>().ToList()` first and then use LINQ on the list, or `foreach` directly. The tuple element names follow the arity: one component yields `(Entity, Value1)`, two `(Entity, Value1, Value2)`.

## 45. A stat field's name is not its behaviour

- **Symptom**: A balance rule written as "guns with big per-shot damage are manual-target-only, so overkill is the player's choice" was simply false: `AutoTarget: false` on `WeaponType` only selects `FindTargetWithoutPrediction` in `TurretFiringSystem` — the gun still acquires targets on its own, it just does not lead them.
- **Cause**: the rule was written from the field's name instead of from its read sites. Names like `AutoTarget`, `PointDefenceTurret` and `BeamSpacing` each imply a mechanic; only some of them exist.
- **Fix / Prevention**: before stating a rule, a comment or a plan line about a stat field, grep the field and read every site that reads it. Where the name lies, fix the name in the same change (`PointDefenceTurret` → `InterceptorTurret`) rather than carrying the lie forward.

## 46. `scenario.sh --sim` cannot see anything that lives in `GameSession`

- **Symptom**: I added a once-a-second `[THREAT]` line in `GameSession`, ran `scenario.sh ammo-spray --sim`, grepped the log for it and got zero hits — as if the feature did nothing.
- **Cause**: `--sim` runs `ScenarioBench`, which drives `WorldFactory` + `ScenarioWorld` + `SimulationRunner.RunTick` with **no `GameSession` at all**. Everything in the window path — HUD lines, the threat readout, key handling, `[FRAME]`/`[RENDER]`/`[SIM]` rows — is absent from that artifact. The `[PERF]` rows in a `--sim` log come from the bench, so their presence proves nothing about the game loop.
- **Fix / Prevention**: to verify window-path work, run the game and capture its stdout: `.tools/game.sh > .tools/run/app.log 2>&1` as a background job (plain `game.sh` writes only to its own stdout, which no file keeps), then `.tools/input.sh key 1` — the digit keys start a run directly from ship select — wait, then grep. A 14 px HUD line needs a native-scale crop, since a 1920×1024 shot is downscaled when read: `PYTHONPATH=.tools/pylibs python3 -c "from PIL import Image; Image.open(shot).crop((0, h-70, 980, h)).save(crop)"`. And `.tools/xvfb.sh` exiting 1 with "server already running" is not a failure — display :99 is up and the game connects.

## 47. A scan over a shorter component list is not a fallback for entities missing a component

- **Symptom**: Auto-aim was reported as "consistently missing while the hull moves". `EvaluateMovingTarget` solves the lead correctly and its result was verifiably right, yet a moving Fighter landed 0 of 71 rounds. `FindTargetWithPrediction` clearly led its targets — there was a second scan right below it.
- **Cause**: the second scan iterated `GetEntitiesWithComponents<EnemyShip, Position>` and read as "ships that have no `Velocity`", a last-resort pass. Multi-component queries mean **at least** these components (`EntityManager` iterates the smallest storage and `TryGetSlot`s the rest), so it re-saw every ship the lead pass had already considered and re-selected it with the lead discarded — and because it ran last, its aim overrode the correct one whenever the lead was rejected. Dead-looking code that was in fact the busiest path in the function.
- **Fix / Prevention**: when a scan looks like a fallback for a *missing* component, check the query semantics before trusting it, and check whether anything in the codebase ever omits that component (`grep RemoveComponent<Velocity>`: nothing does). A fallback that cannot fire is a bug generator, not a safety net: prefer deleting it. Prove the behaviour with a measurement on the real system (a shot's world path against the target's, not the aim vector) — reading the lead math alone said "correct", and it was.

## 48. A solution computed in one system and consumed in another is only valid for the inputs both use

- **Symptom**: After the discarded-lead fix, aim was right and shots still missed by tens of pixels — and a test that asserted an exact lead direction passed one commit and could fail the next for no visible reason.
- **Cause**: the intercept is solved at target-search time from the **nominal** `AmmoSpeed`, while the round is spawned at fire time with a rolled `AmmoSpeed × (1 ± 15%)` and 20 px further along the barrel. The direction was therefore solved for a round that never exists; the error scales with `crossing speed × flight-time error`, so slow ammo and fast targets made it large while the existing test — written against that same nominal solution — only matched when the roll happened to land near zero.
- **Fix / Prevention**: any number a later stage re-rolls must be part of the earlier stage's inputs, or the later stage must re-solve with it (here: `InterceptTime` is one helper shared by the search and the shot, and the shot re-solves per round at the speed and from the muzzle it really has). And when a test hard-codes a value derived from a rolled quantity, assert the invariant instead (`the round's line runs onto the target`), not the number.

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
- **Fix / Prevention**: Even with the correct GL mode the `rlgl` block drew nothing visible on screen, so batch shapes through raylib's own batched 2D path instead (`DrawTexturePro` with a generated disc — `Game/ShapeSprites.cs`), which is the path sprites already use successfully. Generalise: when a binding layer re-declares an enum, do not assume its values match the native constants. And read a suspicious win with suspicion: a pass that gets cheaper than the geometry it must rasterize can justify is telling you the geometry is not being drawn. Verify a render change with `SPACEVORS_SCENARIO_SHOTS` frames plus a pixel diff against a known-good frame of the same seeded scenario — timings alone cannot see this class of bug.

## 35. Never assert a machine budget inside a timing test

- **Symptom**: A perf test written against the game's real frame budget (1000/120 ms) would pass on one machine and fail on another, and the old `PerformanceBenchmark` carried three phases with `mustMeetBudget: false` because they "currently exceed budget" - a gate that is switched off teaches nothing.
- **Cause**: Absolute milliseconds are not portable. The same deterministic world measured 0.36ms inside the xunit host and 0.82ms out-of-process in `ScenarioBench`; `impact-storm` swung 0.85 → 1.94ms run to run on the same machine because xunit runs test classes in parallel and the cost is GC/scheduler dominated. A CI box, a dev desktop and a software-rendered sandbox differ by more than the budget itself.
- **Fix / Prevention**: `Tests/ScenarioPerformanceTest.cs` asserts **characterization ceilings** - at least 2.5x the worst median observed *in the test host*, rounded up - and prints every median so drift is visible long before a ceiling trips. Keep the real budget in the scenario spec, where the `.tools/scenario.sh` harness reports against it on the machine that has a GPU. When writing any timing test: median of measured ticks with warm-up discarded, ceilings derived from the host the assertion runs in, and prove the gate is not vacuous by lowering one ceiling once.

## 36. Search the existing logs before adding instrumentation

- **Symptom**: I proposed adding per-system simulation timing (`[SIM-SYSTEM]` brackets, a new timings class, aggregator changes) as the next measurement step. It would have been about 100 lines of new code.
- **Cause**: I reasoned from the aggregator's parsed fields rather than from the raw log. `DiagnosticLogger.LogSystem` and the `onSystemComplete` hook already existed, wired up whenever diagnostics are on, and every scenario run already contained 3,600 `[SYSTEM]` lines per iteration. Only the parser was missing, and it took ~40 lines.
- **Fix / Prevention**: Before writing a measurement, grep for the seam and look at a raw log file with the numbers you want. A missing *report* is not a missing *measurement*. The same lesson appeared earlier the same day: `[RENDER-PASS]` costs looked fine while the drawn shapes were invisible, because nobody had looked at the frames that were also already being written.

## 37. Scenario names are prefixes of other scenario names

- **Symptom**: A loop re-aggregating the five baselines overwrote `ammo-spray` with `ammo-spray-miss` data, and nothing complained: the numbers merely looked plausible.
- **Cause**: Run dirs are `.tools/run/scenario-<name>-<utc>/`, so `ls -1dt .tools/run/scenario-ammo-spray-*` matches the `ammo-spray-miss` dirs too, and they were newer. Scenario names are not disjoint under glob prefixes.
- **Fix / Prevention**: Anchor the pattern on what follows the name - `scenario-$name-[0-9]*` - or select the dir another way. When scripting over scenarios, print which run dir was picked before using it: an ambiguous glob fails silently, and in perf work a wrong number is worse than a missing one.

## 38. A characterization ceiling measured on one host fails on another

- **Symptom**: The first CI run of the new scenario perf test failed: `ammo-spray: median tick 5.28ms exceeds the 5.00ms characterization ceiling` - 5.6% over - while the same suite was green locally, twice, at ~1.7ms for that scenario.
- **Cause**: The ceilings were derived from the worst median seen on the machine that wrote them, and two host differences were missing from that picture. A CI runner's single-core throughput is roughly 3x slower, and xunit runs test classes in parallel, so on a small runner the timed ticks compete with every other class.
- **Fix / Prevention**: Derive ceilings from the **worst host that runs the suite**, and keep that host list in the comment next to them (it names CI explicitly now). `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `Tests/AssemblySettings.cs` removes the scheduling penalty for about a second of suite time. Failure messages print `Environment.ProcessorCount` so a breach shows its host. Do not "fix" this by asserting the game's real 8.33ms budget - that number belongs to a machine with a GPU and is already gated by `.tools/scenario.sh`. Split the assertion by what each number can mean: median tick ms gets **one ceiling set per host** (desktop and CI, selected by `SPACEVORS_PERF_HOST=ci` in the workflow) because milliseconds belong to the machine, while allocated KB per tick gets **one budget for all hosts** - `GC.GetTotalAllocatedBytes` read 289.9KB in the test host and the real-GPU harness measured 296KB per frame for the same scenario - and it catches the ECS regression class, allocating inside a system, that milliseconds only reveal indirectly. Prove each seam once: set a CI ceiling absurdly low and confirm it fails **only** when the CI marker is set.

## 39. A hot-loop cost measured over a short window can be the JIT, not the code

- **Symptom**: `Collision: detection` looked like the bottleneck: 1.10ms mean, and one-frame spikes at tick 2 (23ms) and tick 9 (13.6ms) in `ammo-spray`. Nothing in the world explained it - no spawn, no split, no GC (`gc0=gc1=0`), and `ammo-spray-miss` spiked at a **different** tick with zero particles in the world. Only ~50 entities are ever in the broadphase, so candidate counts could not produce a 30x jump.
- **Cause**: The spikes were tiered-JIT promotions, and the "steady" 1.10ms was tier-0 code. The scenario window is 120 ticks (~1s of sim), far shorter than the time it takes the JIT to promote the collision loop, so the whole measured window runs unoptimized. Re-running with `DOTNET_TieredCompilation=0` removed the spikes and cut detection to 0.36ms mean; tick median 1.83 -> 1.02ms, p99 12.56 -> 2.50ms, max 22.99 -> 3.03ms.
- **Fix / Prevention**: When a cost is dominated by one system and no *world state* change correlates with its spikes, flip the runtime from the environment before touching code: `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`, `DOTNET_GCHeapHardLimit` are zero-diff experiments. A spike at an arbitrary tick in a short window, absent in a longer one, is the signature. Say which tier the code was running at when quoting a per-system cost, and do not let a 76%-of-sim number justify a parallelization plan until it survives tiering off.

## 40. Starting a display for a smoke test hands the user a dead DISPLAY

- **Symptom**: Two `APP=… .tools/game.sh` runs by the user segfaulted (core dumped). The log looked like a GPU/asset problem: `GPU is not ready to load data, trying to load before InitWindow()?` on every texture, then `[LIGHTING] shader failed to compile; falling back to flat sprites`.
- **Cause**: `.tools/game.sh` sources `.tools/env.sh`, which hard-exports `DISPLAY=localhost:99` and `LIBGL_ALWAYS_SOFTWARE=1` (lines 23-24) for the sandbox. The display on :99 was an Xvfb **I** started for a smoke test and then killed, so the game got `X11: Failed to open display localhost:99` → `Failed to initialize GLFW` → `Failed to initialize platform`, and every later GL warning is cascade. `scenario.sh` handles this properly (it warns when neither DISPLAY nor WAYLAND_DISPLAY is set and prints the display it used); `game.sh` does not.
- **Fix / Prevention**: A display started as a background job is part of the experiment: stop it only when no user run depends on it, and say so in the reply. When handing the user a command that launches a GUI, state which display it needs and check `DISPLAY`/`WAYLAND_DISPLAY` in the log they paste - `Renderer: llvmpipe` versus `Renderer: AMD Radeon …` is one grep and tells you whether a measurement is even on the target hardware. Do not read raylib's asset warnings as an ordering bug: `InitWindow` is called before `ImageLoader.LoadAssets()`; the warning means the window never came up. And a segfault after `Failed to initialize platform` is a missing guard (`Raylib.IsWindowReady()`), not a random crash. Corollary for my own measurements: `scenario.sh` only checks that `DISPLAY` is *set*, not that it answers, so a run against a dead display crashes fast and its wall time still looks like a quick success. Before believing any window-host number, count rows: `grep -c '^\[PERF\] frame=' iter-1.log` - zero rows means the game never drew, and `grep -m1 Renderer:` says whose hardware it was on.

## 41. A build property I did not verify on the artifact can invert a conclusion

- **Symptom**: I measured ReadyToRun and reported "R2R changes nothing: frame 1 80.8ms vs 80.1ms, steady median identical", recommended dropping it, and the user then felt no stutter at all with the R2R build. My measurement and their experience disagreed.
- **Cause**: The `PublishReadyToRun` property was in `Game.csproj` only. The benchmark artifacts I measured came from `ScenarioBench.csproj`, which did not have it, so two of the three rows in my table were **not ReadyToRun builds at all** - the only real difference between them was the tiering flag I overrode on the command line. Nothing in the publish output at default verbosity says whether crossgen ran.
- **Fix / Prevention**: Verify the property on the artifact, not on the flag: assembly size is immediate (published `Domain.dll` 186KB before, 739KB after), and `obj/<config>/<tfm>/<rid>/R2R/` lists exactly the assemblies crossgen processed. Any build-level knob that lives in one csproj is invisible to every other project - when comparing build configurations across projects, either put the property in both or pass it on every command line. Print the artifact path in measurement tools (`scenario.sh` now prints `artifact: ... (ReadyToRun)`) so a number always carries the binary that produced it. Correct the record out loud when the user's experience contradicts a measurement: here their eyes were right and the measurement was measuring the wrong thing.

## 42. A cost that lands after the frame row is written is invisible to per-frame timing

- **Symptom**: The user described a scenario run as steady, then "frozen for 20-40 frame intervals" about halfway, then frozen again at the end until the window closed. My per-frame analysis said the opposite: 0 of 232 frames over the 8.33ms budget, worst middle frame 5.10ms. I told them screenshots were not the cause.
- **Cause**: The in-program screenshots (default `SPACEVORS_SCENARIO_SHOTS=start,mid,end`) run *after* the frame's `[PERF]` row is written and after `EndDrawing`, so their cost sits between rows: `dt` of the shot frame (3.03ms at frame 60) and of its successor (about +1.3ms) stay small while the window is actually stalled. Reproduced: with shots, wall 12978ms and sum(dt) 8425ms; without, wall 11830ms and sum(dt) 8435ms - identical work, ~1.1s of extra wall for 12 encodes (~0.1s each, 0.2s in an earlier A/B). Frames 1/60/120 of 120 is exactly "halfway" and "at the end".
- **Fix / Prevention**: `dt` measures the frame, not the loop's whole life. To find unaccounted time, compare **wall time against the sum of dt** for the same run, and A/B the flag that adds work outside the frame (`--shots` vs `--no-shots`). Also check how much of the work is thrown away: that run wrote 12 PNGs and 3 survived, because every iteration overwrites the same filenames. And when the user reports a feel that contradicts my numbers, the numbers are measuring the wrong window until proven otherwise - it has now happened twice (ReadyToRun, this one).
- **Implemented**: screenshots now happen only in the final run (3 encodes instead of 12, and the stall is in the one run whose PNGs survive), and the harness prints `iterations: N+1 runs = N measured + 1 discarded cold run` so the extra window is not read as an off-by-one. `scenario.py` got one `measured_blocks` rule shared by the frame, render-pass and system statistics: frame 1 of a run, a shot frame, and its successor are out of **all** of them. Only the pass/system tables changed - on a real-GPU run the pass means moved -1% (sparks) to -28% (asteroids, whose cold first frame is now excluded), and `perShapeNs` by -0.2% to -3%, i.e. retaking a baseline also tightens that gate slightly.

## 49. A key hold longer than a frame is a key *repeat*, not one key press

`.tools/input.sh` holds a key for 0.4 s on purpose (llvmpipe renders at ~4 fps and raylib only sees input
that is still down when the next frame is polled). On a menu that steps once per press — the ship-select
list — X11 auto-repeat fires *inside* that hold, so one `key Down` moves several cards, and pressing Down
often enough clamps on the last card. Every run then starts on the same wrong ship, and the screenshot
proves nothing about the feature under test.

Recipe that works: `HOLD=0.05 bash .tools/input.sh key Down` (a few frames at 60 fps, under the repeat
delay), then **verify the highlight in a screenshot before pressing Enter** — a list that clamps at its
end makes a mis-count invisible. Crop before looking: `PYTHONPATH=.tools/pylibs python3` with PIL (no
ImageMagick in this container), because a downscaled 1920x1024 frame hides a 4 px sprite entirely.

## 50. An unattended run stops at the first level-up, not at death

A long headless play session (a visual check of late-game content) freezes with the log still rendering
frames and the `[THREAT]` line stuck at the same `t=`: `LevelUpSystem` spawns a `PendingChoice`, and the
app pauses while the upgrade menu is open. Nothing is broken — the run is waiting for a card. Send
`key 1` on a loop (or watch for the menu) and the clock starts again.

## 51. A headless run's game clock runs slower than the wall clock

Waiting real minutes for a time-based feature (the armament ladder opens at 3 minutes of *game* time) does
not work under llvmpipe: `UpdateRunning` clamps the accumulated frame time per step, so a frame that took
250 ms only advances the simulation by the clamp. Observed: ~200 s of wall time bought ~100 s of
`[THREAT] t=`, i.e. roughly half speed at ~4-8 fps. Budget wall time at 2x the game clock, or spawn the
content directly — the `SPACEVORS_DIAGNOSTIC=1` bench keys exist for exactly that (`U` spawns a rack-armed
hull, `H` makes the hull unkillable for a long session).

## 52. An instant key tap is dropped when the frame rate collapses

`.tools/shot.sh` pressed F12 with `xdotool key` (press+release in one go) and then waited. In a heavy
scene — 15 ships, torpedoes crossing the map — llvmpipe drops this game to ~1 fps, and raylib only sees a
key that is still down when the next frame is polled, so every retry tapped between two polls: three
attempts, no screenshot, and the misleading "is the game drawing?" while the log showed a full render pass
row every frame. `shot.sh` now **holds** F12 for `SHOT_HOLD` (default 1.5 s) like `input.sh` holds keys.
Same rule for anything else driven by hand: `xdotool keydown K; sleep 2; xdotool keyup K`.

## 53. The edit tool only accepts a file the read tool has seen

`edit` answers `requires reading "…" first — read the file, then retry` even when the same lines were
already inspected with `sed`, `grep` or `cat`, and `file changed since it was read — re-read the file,
then retry` after a script rewrote it. The gate tracks the read/edit tools only. Read the region with the
read tool immediately before editing, and re-read after any scripted change to the same file.

## 54. A scripted bulk edit lands inside a multi-line call, and a fixture component moves unrelated tests

Adding `Rotation` to every hand-built hull fixture (the collision pass started reading a hull's heading)
was scripted over 21 sites: it inserted the line inside a multi-line `AddComponent(…)` — a brace-counting
error nothing caught until compile — and in three fixtures it changed what the chase and turret systems
saw, flipping 5 tests that had nothing to do with collision. Compile straight after any bulk edit, scope
the pattern to single-line calls, and when a bulk edit moves a behaviour test, `git checkout` that fixture
rather than adapting a test that was never exercising the new path.

## 55. A perf gate that cannot reach the new code prices nothing

The shape change measured as free on `hull-stream` and `hull-crowding` — and it was, because both worlds
placed swarmers, whose shape is a disc, so the capsule path never ran once. A budget that passes proves
the scenario still runs, not that the scenario touches the code being measured. Before accepting "no cost",
force the new path off with a one-line patch (restore with `git checkout`), measure the same worlds both
ways, and only report the difference. Then make the world reach the new path: the stream now fires at an
interceptor and the crowding rings are three different hulls.

## 56. A filled shape drawn in the world pass can vanish with no error at all

Two traps when drawing custom geometry over the world (`ShipSpriteRenderer.DrawCapsule` hit both):

- **`Raylib.DrawPoly` in this binding is the regular-polygon form** — `DrawPoly(Vector2 center, int sides,
  float radius, float rotationDeg, Color)`. There is no point-list overload, so an arbitrary silhouette is
  fanned by hand with `DrawTriangle`.
- **The world pass culls counter-clockwise triangles.** A correctly computed fan in the wrong winding draws
  *nothing*: no warning, no artefact, the shape is simply absent, which reads as "the overlay is not being
  drawn for this ship" while every call site is fine. Winding is the first thing to try when custom geometry
  is invisible; `DrawFallbackShip`'s vertex order encodes the same rule.

Check the overlay by counting its pixels instead of looking for it: the collider cyan is unmistakable
(`g > 90 and b > 90 and r < 80` over the dark field), so a crop-region count says in one number whether the
shape drew at all.

 ## 57. A crop saved to `/tmp` is unreadable from the next command

- **Symptom**: cropped a ship preview to `/tmp/crop-scout.png` in one command, then the read tool answered
  `cannot read "/tmp/crop-scout.png": not found` — as if the crop had failed, though PIL had written it.
- **Cause**: `/tmp` is private to each command's sandbox (entry 26), and the read tool runs in a different one
  from the `python3` that wrote the file. Same reason the unix socket in `/tmp` cannot reach Xvfb (entry 43).
- **Fix / Prevention**: write anything meant to be looked at later into the workspace — `.tools/run/` is
  gitignored and already holds the scenario output, so `Image.crop(...).save('.tools/run/crop-x.png')` and read
  that path. It also keeps the crop next to the shot it came from (`.tools/shots/`), which is gitignored too.

## 58. Two systems rewriting one component in the same phase: the per-phase flush makes the stale write win

- **Symptom**: unit tests that ran one system at a time passed, but in the live game a hitscan beam drew at
  full range *through* its target and its impact glow re-spawned every tick — while
  `CollisionSystem` verifiably wrote the shortened length.
- **Cause**: `SimulationRunner` flushes the command buffer once per phase, and `CollisionSystem` and
  `EffectSystem` are in the same phase (Resolution). Both queued `AddComponentCommand<LaserBeam>` for the
  beam: the collision one shortened `Length` and cleared `DamagePending`, the effect one — derived from the
  pre-resolution component copy it had read — wrote the beam back with the *old* length and `DamagePending`
  restored. Commands apply in order, so the stale copy landed last: full-length drawing, damage applied
  every tick of the flash, a fresh impact per tick.
- **Fix / Prevention**: when two systems in one phase can write the same component, the later one must skip
  entities the earlier one just resolved (`EffectSystem` skips `beam.DamagePending` beams). Pin the semantics
  with a test that runs **both systems against one view and applies the commands once** — the shape of a
  phase — not each system in isolation (`LaserTest.CollisionAndEffectInOnePhaseFlush_…`).

## 59. `DrawTexturePro` places the sprite's *origin* at the dest rect's position — DrawCircle forgot

- **Symptom**: a beam's impact glow rendered ~its own radius up-left of the beam tip, and a debug cross
  drawn at the very `cx, cy` the circle pass computed landed clearly outside the circle it annotated.
- **Cause**: the renderer-wide convention (ARCHITECTURE.md, settled by the DrawMissile centre fix
  62d3917) is that the dest rect's **position is the point the sprite is centred/rotated about** —
  `RenderHelpers.CenterOrigin` supplies the half-size origin. `ShapeSprites.DrawCircle` built its rect as
  `centre - size/2` with a half-size origin instead, sliding every disc it draws (explosions, sparks, smoke,
  ammo dots, beam impacts) up-left by its own radius; `DrawStreak` was off by half its thickness. The
  convention was documented and had already bitten the missile once.
- **Fix / Prevention**: for any new `DrawTexturePro` call, pass the centre as the dest rect's position. When
  a positioned effect looks off, settle it numerically before theorising: mark the computed centre with a
  `DrawPixel` cross behind the effect (`WorldRenderer.BeamImpactDebug`, on when `SPACEVORS_BEAM_SHOT` is
  set) and measure both against the same screenshot. Also note `Raylib.TakeScreenshot` treats its path as
  relative to the process cwd and glues cwd in front of absolute-looking paths (`FILEIO: [.../run/.../run/x.png]
  Failed to open file`) — pass a bare file name and run the game from the intended directory, as game.sh does.

## 60. New art that is never drawn, and a lit set dropped by one misspelt map file

- **Symptom**: six new asteroid sprites were in `assets/asteroids/{small,large}`, shapes were authored for
  them, tests passed — and nothing in play changed. Separately, one variant rendered as a fallback rectangle
  in an earlier session.
- **Cause**: three lists have to agree and none of them fails loudly: the art directory,
  `Asteroid.SmallVariantCount`/`LargeVariantCount` (which decide which variant the domain ever rolls), and the
  shape table's entry per variant (`Stats/AsteroidShapes.cs`). With the counts at 3, variants 4-7 were never
  picked, so both the new art and its shape were dead data. And `LoadSpriteSets` needs
  `X-texture/-normals/-depth` to share a prefix: `large-aseroid-6-depth.png` (one letter missing) left that
  set without a depth map, so the whole variant vanished from the loaded list — which also *shifted the index*
  every later variant maps to. Both cases log at most one `[WARNING] ... no complete lit set` line in a load
  log hundreds of lines long.
- **Fix / Prevention**: when adding art, check the count and the shape table in the same change, and read the
  loader's log: `grep -c "TEXTURE: \[ID" <game log>` counts the textures that actually loaded, and
  `grep "skipped map file\|no complete lit set"` must be empty for a new set. `AsteroidShapesTest` now pins the
  coupling — the highest variant each count can roll must have a non-circle entry — so raising a count without
  authoring the shape fails a test instead of shipping a disc.

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

`job_kill` on a game job leaves its X window **mapped**: `xdotool search --class SpaceVors` then finds
two windows and `input.sh` focuses the stale one, so keys land nowhere and captures never fire. Run
`stop.sh` (or wait for the window to close) before starting the next game job, and check
`xdotool search --class SpaceVors | wc -l` == 1 when input seems ignored.

For moments no hand can time with F12, start the game with `SPACEVORS_BEAM_SHOT=<file.png>` (first
frame rendering a beam impact, with position logs and debug cross-hairs) or
`SPACEVORS_EXPLOSION_SHOT=<file.png>` (first frame with an Explosion grown past 100 px). One shot per
process, path relative to the run directory. Both need live gameplay: a GAME OVER screen stops the
world render passes, so press R (after the ship-select screen appears — early keys land on nothing)
and let it play.

Notes: run the game with `dotnet Game.dll`, not the apphost — the built `Game`
binary asks for a linuxbrew `ld.so` that this container does not have. Whether a
frame can be *looked at* depends on the active model: an image-capable one reads
`.tools/shots/*.png` directly, but 1920x1024 is downscaled, so crop first when
small text or 1-px detail matters. Keep the assertions numeric (`diff.sh`), so a
test never depends on the model's eyes.

## 61. A scenario's slow window run can be the HUD reacting to the scenario's data, not the feature measured

- **Symptom**: `missile-384` window runs at 183 ms median while its `--sim` median was 1.8 ms, so sim was fine and render was pathological — but nothing about missiles was being changed.
- **Cause**: `HudRenderer.DrawHealthBar` drew one rounded pip per hp point (six primitives each); the scenario set the player's hp to 40,000, so the HUD emitted ~240,000 draw primitives per frame. Any scenario that inflates a "small integer" stat can trip a per-unit loop written for play-scale values.
- **Fix / Prevention**: With `SPACEVORS_DIAGNOSTIC=1` every window run carries a `renderPasses` worst-first table — read it before touching the feature under test; one pass at 96% of the frame names the culprit outright. Keep HUD/per-unit renderers bounded (fixed draw counts) when the stat can be inflated by tests or upgrades.

## 62. `scenario.sh --iters 0` measures nothing and then reports "no [PERF] frames found"

- **Symptom**: a windowed scenario run prints `iteration 1: 120 frames` and the run dir holds a full
  `iter-1.log` (all 120 `[PERF]` rows) plus shots — yet the script ends with "no [PERF] frames found,
  no summary.json produced; baseline left untouched", reading like the run itself failed.
- **Cause**: `--iters N` means N *measured* runs **plus one discarded cold run**, so `--iters 0`
  leaves zero measured runs; the aggregator parses an empty set. The log is fine — only the summary is missing.
- **Fix / Prevention**: use `--iters 1` as the minimum for any run you intend to read. If a run dir
  has an `iter-1.log` without `summary.json`, the numbers are still there: median the `[PERF]` rows
  (`dt=`/`render=`/`sim=`) and the `[RENDER-PASS]` lines from the log directly.

## 63. `new HealthOrb()` gives zeros, not the declared defaults, when every parameter is optional

- **Symptom**: a hand-built test world's health orb disappeared on the very first tick. The system was
  not wrong: it read `Lifetime = 0`, decided the orb had expired, and destroyed it. The same component
  built with any argument at all behaved with its declared 90 s lifetime.
- **Cause**: the pickup components (`HealthOrb`, `XpPickup`, `RerollToken`, `SalvageField`, `EcmPulse`)
  are record structs whose primary-constructor parameters **all** have defaults. `new HealthOrb()` binds
  to the implicit parameterless *struct* constructor, which zero-initialises every field. The declared
  defaults belong to the primary constructor, and only apply when it is actually called — one named
  argument is enough: `new HealthOrb(Radius: 8f)` does get `Lifetime = 90f`. A struct has no "constructor
  with defaults" to fall back to, so the bug is invisible until a test entity dies for no visible reason.
- **Fix / Prevention**: in test worlds, name the values the test reasons about
  (`new HealthOrb(Lifetime: 90f, Radius: 8f)`). Production code is unaffected because it always passes a
  radius. When an entity in a test vanishes, dump `commands.Commands` before the `Apply`: the
  `DestroyEntityCommand` names the component whose fields came out as zeros.

## 64. An A/B script that greps away build errors measures the previous build's dll

- **Symptom**: an A/B of a lit-shader change reported a tidy "-8.5% cpu, pixels differ by 1/255 in
  0.02% of the frame" — with a suspiciously *small* spread on the new side and a control (`flat-200`,
  code the change cannot touch) that moved by 11% in the same run. The change had never compiled:
  `CS0214: Pointers and fixed size buffers may only be used in an unsafe context`.
- **Cause**: the A/B helper ran `dotnet build … | grep -E "error|Build FAILED"` and continued whatever
  happened. On a build failure the dll on disk is still the **other side's** binary, so both sides
  measure the same code and the "difference" is host drift. The 1/255 pixel "difference" was not the
  change either: the probe screenshot is not bit-exact across runs on llvmpipe (~0.02 % of pixels
  differ by one step, `mean=0 max=1`).
- **Fix / Prevention**: a build failure must fail that side of the run — `.tools/ab-lit.sh` returns
  non-zero from its `build()` and always pops the stash, so a failed A side cannot strand the change
  in the stash *or* get measured. Two tells that you measured the wrong binary: a control that moves
  as much as the subject, and a "changed" pixel diff at 1/255 rather than the exact 0 you expect from
  a change that is only a repacking of the same texels.

## 65. Wall-clock render timings on this host are noise; per-frame CPU time is not

- **Symptom**: the byte-identical `flat-200` bench scenario measured 4.8, then 8.5, then 12.5 ms in
  one afternoon, and one run had `lit-200-L0` **cheaper** than `flat-200` in the same process.
- **Cause**: the sandbox has no GPU — llvmpipe rasterises on the host's CPU, the host is shared, and
  wall time also charges the process for being descheduled. Ratios (lit vs flat) drift by 2x.
- **Fix / Prevention**: compare **CPU time per frame** (`Process.TotalProcessorTime`, which counts
  llvmpipe's worker threads — RenderBench prints it next to wall time) with the rasteriser pinned:
  `LP_NUM_THREADS=4`. The same build then reproduces within 0.1 %, and paired A/B (`.tools/ab-lit.sh`)
  resolves single-digit-percent changes. It is still software rasterisation: the number prices CPU
  fragment work, not GPU bandwidth, so treat it as direction and take the final verdict on the
  real-GPU window host with `.tools/scenario.sh`.

## 66. `Raylib_cs` `Image.Data` is a `void*`, so touching pixels needs unsafe

- **Symptom**: `Marshal.Copy(normals.Data, buffer, 0, count)` fails to compile with `CS1503: cannot
  convert from 'byte[]' to 'int'` plus `CS0214: Pointers and fixed size buffers may only be used in
  an unsafe context` — the overload looks right and is not.
- **Cause**: the binding exposes raylib's raw pixel pointer, not an `IntPtr`, so no `Marshal` overload
  accepts it and the project has no unsafe context.
- **Fix / Prevention**: the one project that edits image pixels (`Game`, `ImageLoader`) keeps
  `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` with a comment saying why, and indexes the pointer
  directly inside `Width * Height * 4`. Keep the unsafe region to a single loop over two buffers the
  caller owns; everything above it stays safe code.

## 67. Mutating a raylib `Image` inside a helper leaves the caller holding freed memory

- **Symptom**: RenderBench died right after loading the first lit set with `free(): invalid size`, or
  with `double free or corruption (!prev)`. Which message appeared — and whether anything appeared at
  all — changed when *unrelated* code was edited: a heap-layout lottery, not a logic bug. The crash
  lands at some later `free()`, far from the code that caused it.
- **Cause**: `Image` is a value-type handle over a raylib-allocated buffer. A helper that takes it **by
  value** and calls a mutating raylib function (`ImageAlphaPremultiply`, `ImageFormat`, `ImageResize`)
  updates only its copy: raylib can allocate a replacement buffer and free the original, so the caller
  keeps a stale `Data` pointer and its later `Raylib.UnloadImage` frees bytes raylib already freed. The
  same class of bug is a wrong stride: PNG maps decode to `R8G8B8`, **three** bytes a texel, so a packer
  that writes RGBA into that buffer runs a quarter past the end of it.
- **Fix / Prevention**: run every raylib image mutator on the caller's **own** variable (`ref`), and keep
  helpers that take an `Image` by value read-only. When a helper must produce changed pixels, let it build
  and return a **new** image it owns (`ImageLoader.PackSurfaceMap` allocates with `GenImageColor` rather
  than writing into the normal map) and derive the stride from `image.Format`. Do not guess what a PNG
  decoded to: run once and read raylib's own `INFO: IMAGE: ... (WxH | R8G8B8 | ...)` line.

## 68. A lit scenario that times the same as `flat` means the lit path never ran

- **Symptom**: a shader edit reported a **-74%** cost on `lit-200-L0/L4/L16` — the lit scenarios came
  back at exactly the `flat-200` number — while the cross-build screenshot diff moved 9 % of pixels.
  The bench exited 0 and `probe: PASS` still appeared (from the *other* side of the A/B).
- **Cause**: the lighting shader failed to compile, so `Lighting.IsReady` was false and every lit draw
  fell back to a flat `DrawTexturePro`. The fallback is silent by design (an unlit game still runs),
  the one `LIGHTING: shader failed to compile` line went to stderr, and the A/B helper had `2>/dev/null`
  on the bench call. The edit itself was a scripted replace anchored on `void main()`, which appears in
  **both** the vertex and fragment shader: the new helper landed in the vertex stage, where
  `gl_FragCoord` does not exist.
- **Fix / Prevention**: RenderBench now refuses to time a `lit-*` scenario when the shader is not live
  (it prints `ERROR <name>: lit shader unavailable` and exits non-zero), and `.tools/ab-lit.sh` keeps
  the bench's stderr in `.tools/run/ab-lit-rep.txt`. Two tells to read before believing a render win:
  a lit scenario costing what the flat scenario costs, and a "speedup" that arrives with a pixel diff
  that was expected to be zero. Scripted edits into GLSL must anchor on a line unique to one shader
  stage, or assert the expected number of occurrences before writing.

## 69. An A/B that runs one side after the other measures the host, not the change

- **Symptom**: a paired A/B of a small shader change reported the subject at +36 % **and** the untouched
  control (`flat-200`, a path the change cannot reach) at +41 %. Taking the minimum over four reps did
  not help, because all four reps of a side run in the same minute.
- **Cause**: the two sides were measured back to back — four reps of B, then four of A — so any load the
  host picked up between the two blocks lands entirely on one side. Minimum-of-N only removes
  *within-block* spikes; it cannot remove a shift in the baseline. (Related: entry 65, wall time vs CPU
  time.)
- **Fix / Prevention**: build both sides up front, snapshot the binaries that differ, and **alternate the
  reps** — A, B, A, B — so interference is charged to both. `.tools/ab-lit.sh` does this and prints the
  md5 of the dll each rep runs, which also catches the wrong-binary trap of entry 64. Judge the run by
  the control first: if it moved, the subject's delta means nothing; on a quiet host the control now
  repeats within ~1 %.
## 70. An intentional colour change makes the A/B `probe: FAIL` — that is the check working, not a bug

- **Symptom**: `.tools/ab-lit.sh` on a lighting-rig change printed `A probe: PASS` on every rep and
  `B probe: FAIL (compare /tmp/probe_legacy.png vs /tmp/probe_current.png)` on every rep, which reads as
  "the working tree's grouped path disagrees with the per-sprite path".
- **Cause**: the probe pixel-compares the current shader against `LegacyFragmentShader`, a **frozen oracle
  of the old rig** kept to prove that batching and the transparent early-out change no pixel (RenderBench
  says this at its own line 226). Change the rig on purpose and the two sides disagree by construction, so
  the probe fails on the changed side and only on the changed side.
- **Fix / Prevention**: for a palette or rig change run the A/B with `AB_PIXELS=different` and read the
  final pixel step (`mean/max/changed`) as the report; treat the probe line as informational. For a change
  that must not move pixels, keep the default `AB_PIXELS=same` — then a probe FAIL really is the batching
  bug of entry 14. Judge the cost by the `flat-200` control first (entry 69): if the control moved, the
  subject's delta means nothing.

## 71. An upgrade-offer change cannot be priced by the perf gate, and `app.log` is not the game's log

- **Symptom**: Changing `UpgradeOffer`/`LevelUpSystem` and running `.tools/scenario.sh … --sim` moves
  nothing, and `grep UPGRADE .tools/run/app.log` prints zero rows even in a run whose HUD clearly reads
  `LV 3` — so either the code never ran or the logging is broken.
- **Cause**: Two separate harness facts. (1) A gated scenario runs 120-357 ticks; the player never picks
  up enough XP in that window to leave level 1, so with `SPACEVORS_DIAGNOSTIC=1` the sim logs carry
  `[SYSTEM]` rows and **zero** `[UPGRADE]` rows (checked on `hull-stream`, `ammo-spray`, `missile-384`).
  The offer path is genuinely not executed, so its cost is not measurable there — it is 0% by inspection.
  (2) `.tools/README.md` says the game logs to `.tools/run/app.log`, but `game.sh` is an `exec dotnet
  Game.dll` with no `tee`, so a fresh run's rows go to the **background job's stdout** (and stdout is
  block-buffered off a TTY, so the job can show nothing for minutes). The `app.log` that sits there is a
  stale file from some earlier run, and grepping it silently answers about that old process.
- **Fix / Prevention**: To price an offer/upgrade change, do not reach for the gate — read the code path
  (`grep -c UPGRADE` in a diagnostics sim log proves whether it runs) and report the medians as unchanged
  with that reason. To *see* what the game deals, drive a window run: `.tools/xvfb.sh` + `.tools/game.sh`
  as background jobs, `.tools/input.sh key 1` to pick a hull, `key l` to force a level-up, then
  `.tools/shot.sh <name>` and read the PNG — the card screen is the evidence, and it does not depend on
  buffered stdout. Capture the game's rows yourself (`.tools/game.sh 2>&1 | tee my.log`) when you really
  need them, and check the file's mtime before believing any log: a 40-minute-old `app.log` will happily
  confirm that your change "did nothing".

## 72. A hand-built firing-test fixture fires for a hitscan gun but silently not for a projectile gun

- **Symptom**: A new test copied the LaserTest fixture — player, turret, clicked asteroid, one
  `TurretFiringSystem.Update` — and asserted a spawned `Ammo`. Zero rounds, no error. The same
  fixture asserted a `LaserBeam` fine.
- **Cause**: A clicked target is solved two ways. Hitscan takes `EvaluateStaticTarget` (position
  only); a travelling round takes `EvaluateMovingTarget`, which **requires a `Velocity` component**
  on the target (`TryGetComponent<Velocity>` → no selection). The passing beam fixture had no
  `Velocity` on its asteroid, so copying it built a fixture that could never shoot a gun.
- **Fix / Prevention**: Hand-built firing fixtures give the target `Velocity(Vector2.Zero)` even
  when nothing moves. `ArmorCombatTest` and `TurretFiringTest` both do; copy those, not LaserTest,
  when the assertion is about `Ammo`.

## 73. A balance-budget test that models raw DPS goes on lying the day a mitigation stat ships

- **Symptom**: `EnemyDifficultyTest.ToughestTierHullDiesInsideTheTtkBudget` stayed green after armor
  shipped — while the real starter-kit TTK against the new armored hull had nearly doubled, because
  the test multiplied `FireRate × PelletCount × Damage` and armor subtracts per hit.
- **Cause**: The budget test models damage as the table number, not as the number that reaches hp.
  Any per-hit mitigation (armor, shields) silently re-prices the stat the test computes.
- **Fix / Prevention**: When a mitigation stat ships, route every budget/balance test's damage
  through the same rule combat uses (`ArmorRules.EffectiveDamage`) and re-set the budget honestly —
  the armor-tax budget widened 7 s → 12 s in the same commit as the armor, not in a later "fix the
  red test" patch.

## 74. An A/B worktree of HEAD runs the scenario but cannot summarize it: no numpy there

- **Symptom**: `git worktree add .ab-shield HEAD` for an honest interleaved A/B, then
  `.ab-shield/.tools/scenario.sh ammo-spray --sim` printed the iterations and died with
  `ModuleNotFoundError: No module named 'numpy'`; no `summary.json`, so a `grep budget:` A/B loop
  silently compared *nothing* on one side and reported a clean delta that was never measured.
- **Cause**: `.tools/pylibs` (numpy/pillow, unpacked by `setup.sh`) is gitignored, so it exists only in
  the live checkout. The worktree runs the sim fine — only `scenario.py`'s summary step needs python.
- **Fix / Prevention**: The raw `iter-N.log` rows are complete, so read those:
  `grep '^\[PERF\]' iter-*.log | sed -E 's/.* sim=([0-9.]+).*/\1/' | sort -n` and take the middle
  element (skip `iter-1`, the discarded cold run). Or `ln -s $PWD/.tools/pylibs .ab-shield/.tools/pylibs`.
  Always print a marker per side in an A/B loop — an empty side looks exactly like a fast side.

## 75. A small faint graphic cannot be judged from a screenshot, and identical shot hashes mean the world stopped

- **Symptom**: asked to confirm a ~40 px shield arc, six consecutive `shot.sh` captures came back with
  the **same md5**, and the frames I could look at showed no ring at all — read as "the feature draws
  nothing".
- **Cause**: two unrelated things. (1) 1920×1024 is downscaled before the model sees it, so a thin
  alpha-90 arc is gone; and a *correctly invisible* state (a full shield draws nothing by design) looks
  identical to a broken one. (2) Once the run hits GAME OVER the world render passes stop, so every
  later capture is the same frozen frame — and `input.sh key r` does not restart it here, so keys after
  that point are dropped without error (see #27).
- **Fix / Prevention**: assert numerically on the captured frame — decode the PNG (pillow from
  `.tools/pylibs`, or stdlib `zlib` + `struct` when python has no imaging library) and count pixels near
  the palette colour plus their bounding box: 0 near-white pixels at the hull when the pool is whole,
  ~150 forming the arc once it is spent. Crop the few hundred pixels around the entity and read *that*
  if eyes are needed. Pair the two states (whole vs spent) so "nothing drawn" and "nothing to draw"
  cannot be confused, cross-check the `[RENDER-PASS]` row for the pass, and if two shots hash the same,
  stop and relaunch the game job rather than capturing more frames.

## 76. `FirstOrDefault` on a nullable *struct* never returns null, so every `is { }` guard passes

- **Symptom**: `SPACEVORS_DIAG_UPGRADES="Hp:,FireRate:MachineGun,RailGun"` dealt two cards labelled
  `new weapon Hp:` and `new weapon FireRate:MachineGun` — the scripted bench looked broken, and its
  parser looked like the culprit. The parser was fine; its lookup was lying.
- **Cause**: `WeaponType` is a `readonly record struct`, and `WeaponType.FromName` was
  `All.FirstOrDefault(w => w.Name == name)` with return type `WeaponType?`. On a miss LINQ returns
  `default(WeaponType)`, which the compiler *lifts* into `WeaponType?` as a value with `HasValue == true`.
  So `FromName("Hp") is not null`, `is { } gun` and `?.Stats.Homing == true` all succeeded for a name that
  names nothing, and `ParseEntry` therefore took its "this entry is a gun name" branch for every entry.
  Any guard written against null was dead code, which is why the bug hid until a bench fed it unknown names.
- **Fix / Prevention**: a lookup over a struct collection returns the struct or nothing — write it as a loop
  that returns `null` on a miss (or return a non-nullable `WeaponType` plus a separate `TryFromName`), and
  say why in the comment, because `All.FirstOrDefault(...)` reads as the correct idiom. When a guard "never
  fires", check what the expression it guards actually returns for a miss before blaming the code above it:
  for `T?` where `T` is a struct, `is null` is only true if someone wrote the null themselves. Same shape of
  trap in tests: `FromName(x)!.IsSupport` does not compile for a nullable struct — use `is { } gun && gun.…`.

## 77. Diagnostic keys do nothing until the ship-select screen is cleared, so the shots look frozen

**Symptom.** `.tools/game.sh` is up and the window is drawn, `.tools/input.sh key l` is sent (the force-level-up
diagnostic), and two consecutive `.tools/shot.sh` runs return **the same hash**. Nothing in `app.log` or the
captured stdout, no exception, the game is alive and rendering.

**Cause.** The window boots into **ship select** — the hull list with "Arrows/WASD or scroll to browse, CLICK
or ENTER to select". Diagnostic keys are handled in `GameSession.HandleDiagnosticKeys`, which only runs in the
playing step, so `L`, `G`, `Y`, `X` are all swallowed harmlessly there. The identical hash is not the world
stopping (entry 75); it is the *menu* sitting still.

**Fix.** Send `key Return` (or click a hull card) first, wait a frame or two for the run to start, then send the
diagnostic key. Same trap one step later: after taking a bundle whose half is a new gun the game is on the
**turret-mount** screen, where number keys pick mounts and `Esc` leaves — a `key l` sent there is consumed by
that screen and no level-up is dealt. Read the captured stdout (`[UPGRADE]` / `[MOUNT]` rows) to tell which
step the game is on, not just whether it is alive.

## 78. xUnit's `Assert` overloads swallow a `because` message into the wrong signature

**Symptom.** A test that reads fine — `Assert.Equal(hand.Length, pairs, "the same pair twice")`, or
`Assert.Contains(bundles, bundle => IsDamageCard(bundle.Powerful), "no damage card")` — fails to compile with a
message about an unrelated signature: `Argument 3: cannot convert from 'string' to 'Func<int,int,bool>'`, or
`cannot convert lambda to IAsyncEnumerable<T>`. The three-argument call *does* exist, just not with a string.

**Cause.** `Assert.Equal` has comparer and tolerance overloads in the third slot, and `Assert.Contains` has a
comparer there. Neither takes a `because` string, so the compiler binds the message to whatever overload it can
and reports the mismatch against that instead of saying "no such overload".

**Fix.** Put the message on an assertion that takes one: `Assert.True(pairs == hand.Length, $"…: {Names(hand)}")`,
`Assert.True(bundles.Any(bundle => IsDamageCard(bundle.Powerful)), "…")`. Keep the interpolated detail — the
message is what makes a sweep failure debuggable in one read — and note that the collection asserts that *do*
take a `because` (`Assert.True`, `Assert.False`, `Assert.DoesNotContain`, `Assert.InRange`) are the ones to aim
the message at.