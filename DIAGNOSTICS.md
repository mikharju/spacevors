# DIAGNOSTICS.md — debug helpers

Reference, usage only — no implementation details or history (plan files have those).

All helpers are active only with `SPACEVORS_DIAGNOSTIC=1`. It also turns on the cyan
collider overlay, the [FRAME]/[FPS]/[SIM]/[APPLY]/[RENDER]/[SYSTEM]/[UPGRADE]/[MOUNT]/[target]
logs, and a fixed test asteroid at (0,-300).

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
- `SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun"` — scripted upgrade choices, one
  entry per level-up; entry is a weapon name or `Stat:WeaponName`; falls back to the random
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
