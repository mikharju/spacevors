# DIAGNOSTICS.md — debug helpers

Reference, usage only — no implementation details or history (plan files have those).

All helpers are active only with `SPACEVORS_DIAGNOSTIC=1`. It also turns on the cyan
collider overlay — over hulls and rocks in flight, and over the hull previews on the ship-select
cards — the [FRAME]/[FPS]/[SIM]/[APPLY]/[RENDER]/[SYSTEM]/[UPGRADE]/[MOUNT]/[target]
logs, the spike log file (see below), and a fixed test asteroid at (0,-300). Without it the select
cards show the ship art alone.

## Spike log

The `[FRAME]`/`[SYSTEM]` flood is ~100 lines per frame (a 4-minute session is ~70 MB of stdout), so
a freeze you felt is hard to find afterwards. With diagnostics on, the game also writes **one line
per stalled frame** to a file, so a whole session leaves a handful of rows:

    [SPIKE] log: src/Game/bin/Debug/net10.0/spikes/spikes-20261108T135033Z-p15.log factor=4 floorMs=25 windowFrames=256 keepDays=1
    [SPIKE] frame=2407 dt=65.7 sim=0.9 render=64.7 other=0.0 median=5.3 t=20.1 ents=53 ships=7 asteroids=35 missiles=0 ammo=1 \
            worst=world.enemyShips:61.2ms subworst=enemyShips.litDraws:61.0ms litBlock=id=34 draws=1 gap=-1 px=0.01M begin=0.0 draw=0.0 flush=60.9ms \
            newLit=34,35 gc0=0 gc1=0 gc2=0 allocKb=16
    [SPIKE-END] frames=3000 spikes=1

- A frame is a spike when `dt` reaches **4× the median of the last 256 frames** *and* 25 ms. The
  first 64 frames are never judged, so the startup hitch is not reported.
- `worst=` names the slowest render pass of that frame — `world.enemyShips` — and `subworst=` the
  slowest part **inside** it. The lit sprite blocks and the collider overlay are timed separately
  (`asteroids.litDraws`, `asteroids.overlay`, `enemyShips.litDraws`, `enemyShips.overlay`), and also
  appear as their own `[RENDER-PASS]` rows every frame while the enclosing pass keeps its old total.
  `other=` is what neither sim nor render accounts for.
- `litBlock=` is the most expensive **lit sprite block** of the frame — one variant drawn for one
  faction, the unit the shader is switched for. `id=` its base texture, `draws=` how many sprites it
  batched, `px=` how much of the screen it painted, `gap=` how many frames since that variant was last
  drawn (`-1` = never, `3600` ≈ 30 s at 120 fps), then where its milliseconds went: `begin=` shader
  switch + uniforms + texture bind, `draw=` queueing the sprites, `flush=` the batch flush `EndDraw`
  triggers — the GPU submission, and where a driver wait shows up. Reading a freeze: large `px` with
  many `draws` is fill rate; time in `begin` is the bind; time in `flush` with a large `gap` is a
  texture coming back cold; time in `flush` with `gap=-1` and `newLit` filled is a first-ever draw.
- `newLit=` lists the texture ids drawn **for the first time ever** on that frame, `-` otherwise. On a
  machine with a real GPU a first draw is cheap, so an empty `newLit` on a freeze frame rules the
  first-use story out — under software rendering (llvmpipe) it is exactly what shows up.
- `gc2=0` rules out a garbage-collection pause; a count that jumped versus the previous row (`ships`,
  `asteroids`, `missiles`, …) marks the frame where new content first appeared.
- Files go **inside the project, next to the running binary** — `spikes/` beside `Game.dll`, which for
  a dev run is `src/Game/bin/Debug/net10.0/spikes/` (gitignored with `bin/`). Override with
  `SPACEVORS_SPIKE_DIR`; a read-only install (an AppImage) cannot write beside its binary, so there it
  needs the override or it warns and runs without the log. When a run opens its log it first deletes
  `spikes-*.log` older than 1 day from that directory. Nothing is written without diagnostics.
- The `[SPIKE] log:` line prints the exact path at startup, and `[SPIKE-END]` says how many spikes
  the session found. Unflushed rows survive a crash: the file flushes on every line.

Keys (in-game):

- L: force level-up
- G: grant 2 salvage (reroll) charges
- E: drop an ECM capsule just outside arm's reach (jams every ship, mine and enemy round when touched)
- F: drop a salvage-field capsule just outside arm's reach (sweeps the field when touched)
- M: spawn a test explosion at the fixed asteroid
- N: spawn a test enemy ship with huge health up-right of the player (click-targeting tests)
- H: set player health to 10000
- T: toggle a position pin freezing the player (stable visual tests)
- K: halve hp of the locked target or nearest enemy (reach damage smoke tiers quickly)
- J: halve player hp
- U: spawn a rack-armed test hull (enemy missiles)
- B: guided-round bench (8 headings x 2 plume sizes, drawn in screen space)
- V: plume anchor overlay on every guided round
- Y: fill the hull's shield pool, giving it one if it has none (the card is gated at level 10)
- X: spend the shield pool to nothing and fire the shatter the way a real break would — this is how the
  ring is seen at all, since a full pool deliberately draws nothing but its occasional glint
  (plans/SHIELDS.md §7)

Environment variables:

- `SPACEVORS_DIAGNOSTIC=1` — enables everything above
- `SPACEVORS_SPIKE_DIR=<dir>` — send the spike-log files somewhere other than `spikes/` beside the
  binary — e.g. a `.tools/run/...` dir, to keep a run's spikes with its logs. Only read with diagnostics on
- `SPACEVORS_DIAG_UPGRADES="RailGun,Range:MachineGun,Hp:,FireRate:MachineGun"` — scripted upgrade choices,
  one entry per **card granted**: a level grants the two cards of one bundle (plans/UPGRADE_BUNDLE_PAIRS.md),
  so entries are consumed **in pairs, strong card then weak card** — an odd leftover entry is not a bundle and
  falls back to the dealt offer; entry is a weapon name or `Stat:WeaponName`. A hull card needs its empty weapon name — `Hp:`,
  `Armor:`, `Shield:` — because a bare word is read as a new weapon's name. Falls back to the random
  pool when exhausted. Armor cards work the same way: `Armor:` for the hull card (empty weapon
  name) and `ArmorPierce:RailGun` for a gun — both are only dealt from level 5, so script the
  early levels around them. The shield card works the same way (`Shield:`, empty weapon name) and is
  dealt only from level 10
- `SPACEVORS_DIAG_REROLLS=<n>` — start the run holding n reroll charges, so the card screen's reroll is
  testable without farming a ship kill (a capsule only drops from a dying ship, 6 % of them)
- `SPACEVORS_DIAG_SHIELD=<points>` — start the run with a hull shield pool of that size, whole and
  unshocked (the card is gated at level 10, so a run otherwise never has one). A whole pool draws nothing
  but its ~5 s glint by design; press `X` to spend it and see the ring, the shatter and the refill front.
  Note the ring is a **draw-only** radius — 1.15× the hull's bounding circle — so `SPACEVORS_DIAGNOSTIC=1`'s
  cyan overlay draws the true hurtbox *inside* it; nothing about collision or the broad phase moves
  (plans/SHIELDS.md §7)
- `SPACEVORS_SCENARIO_SHOTS=start,mid,end` (or frame numbers) — the game writes
  `scenario-<name>-frameNNN.png` into its working directory for those scenario frames; the harness
  collects them into the run dir. raylib's own F12 needs window focus, which a headless display
  does not give
- `SPACEVORS_BEAM_SHOT=<path>` — one PNG of the first frame that renders an on-screen `BeamImpact`,
  plus `[BEAM-SHOT]` lines with each beam's endpoints and impact position
- `SPACEVORS_EXPLOSION_SHOT=<path>` — one PNG of the first fully on-screen explosion grown past
  100 px, where an under-sized source texture steps at the edge
- `SPACEVORS_PERF_HOST=ci` — selects the CI ceiling set in `Tests/ScenarioPerformanceTest.cs`
  (the CI runner is ~3x slower per core, so the budgets differ per host)
- `SPACEVORS_CEILING=<budgetMs>` with `SPACEVORS_CEILING_START` / `_MAX` — the window half of
  `.tools/ceiling.sh`: doubles a pinned mixed-object world until the median frame misses that budget,
  then bisects, one `GameSession` per probe inside a single window. `[CEIL]` per probe, `CEILING` per
  bracket, nothing gated, exit 0. plans/CEILING_TEST.md

Perf scenario env vars (`SPACEVORS_SCENARIO`, `_CAP`, `_TICKS`, `_HOLD`): plans/PERF_SCENARIOS.md.
Headless build/run/screenshot recipe: TROUBLE_SHOOTING.md, "General workflow that works".
