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
