# DIAGNOSTICS.md — debug helpers

Reference, usage only — no implementation details or history (plan files have those).

All helpers are active only with `SPACEVORS_DIAGNOSTIC=1`. It also turns on the cyan
collider overlay — over hulls and rocks in flight, and over the hull previews on the ship-select
cards — the manoeuvre brackets on enemy hulls (green diamond: holding a spread lane, yellow diamond:
a crippled hull hiding behind a healthier one), the [FRAME]/[FPS]/[SIM]/[APPLY]/[RENDER]/[SYSTEM]/[UPGRADE]/[MOUNT]/[target]
logs, the spike log file (see below), and a fixed test asteroid at (0,-300). Without it the select
cards show the ship art alone.

## Threat readout

Once a second, with diagnostics on, the game logs a `[THREAT]` line and shows it in the overlay:

    [THREAT] t=312s tier=5/5 ships=61 ring=44 rounds=38 missiles=6/12 in=14.2/s out=51.0/s hp=88/120 lvl=9 mines=14 rocks=22 sectors=3

`sectors=` is how many of the eight bearing sectors around the player hold a live ship — the number to
watch when judging a formation change rather than guessing from feel: a swarm trailing you reads 1–2, a
fleet holding flanks should climb toward 4–5 (plans/ENEMY_BEHAVIORS.md follow-up). `missiles=` is the
enemy's guided rounds in the air against what the ladder allows, the number the missile knobs are tuned
against.

## Manœuvre log

Diagnostics also writes `behaviours/behaviours-<utc>Z-p<pid>.log` beside the binary, timing what each enemy
manœuvre actually delivered — the armament ladder only says what share of spawns is *dealt* one, which is not
what the player meets:

    [BEHAVIOUR] log: …/behaviours-20261112T172614Z-p9.log sample=0.25s window=10s totals every=60s keepDays=1
    [BEHAVIOUR] t=20s win=10s Chase +1 hulls 61.0s alive 5.2s on-screen; Spread +0 hulls 10.1s alive 10.1s on-screen; CoverTuck +1 hulls 8.5s alive 8.5s on-screen;
    [BEHAVIOUR-TOTAL] t=60s Chase hulls=18 alive=512.3s onscreen=210.4s peak=6; Spread hulls=2 alive=41.0s onscreen=30.2s peak=2; Rammer hulls=0 alive=0.0s onscreen=0.0s peak=0; …
    [BEHAVIOUR-END] t=612s …

- One row per 10 s of play, **deltas since the previous row**: `+N hulls` new arrivals, `alive` hull-seconds
  anywhere in the world, `on-screen` hull-seconds inside the viewport box — a flanker still crossing the
  field is alive but not yet something you met. A row that says `none` is a lull.
- `peak=` is how many were alive at once. Every behaviour is listed in the totals, including ones that never
  happened, so a zero is a measurement rather than an omission.
- A `[BEHAVIOUR-TOTAL]` line repeats the running totals once a minute, because a run that is killed rather
  than quit never reaches the end; `[BEHAVIOUR-END]` is the same line at a clean exit and prints to stdout as
  well. A new run from the same process (game over → ship select) restarts the tally.
- A row whose lanes suddenly double is a **flanker wave** (`Spread +12 hulls …` where a normal window gives
  `+2`): every other hull of such a wave arrives holding a lane (plans/ENEMY_BEHAVIORS.md). Cross-check the
  `sectors=` of the `[THREAT]` rows around the same time — a flank wave should push it up.
- A hull counts once however long it lives. Entity ids are recycled without a generation, so `hulls` is a
  floor: a hull replaced by the same behaviour within one 0.25 s sample counts once.

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
- Q: spawn a Carmine carrying the next enemy manoeuvre — spread, strafe, tuck, ram, cut-off, burst, one per
  press (plans/ENEMY_BEHAVIORS.md). Press `K` on it a few times to cripple it, which is what starts a tuck.
  Its bracket says which manoeuvre it holds: green for a spread lane, yellow once a tuck hull is crippled
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
- `SPACEVORS_BEHAVIOUR_DIR=<dir>` — same, for the manœuvre log (`behaviours/` beside the binary by default)
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
