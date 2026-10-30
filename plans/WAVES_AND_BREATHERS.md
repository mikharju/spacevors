# WAVES_AND_BREATHERS.md

Plan: discrete enemy waves + scheduled breather windows for loot/health collection, and how to
*show* the pause instead of printing "Wave 4". 2026-10-19.

**Status (2026-10-30): W1–W3 implemented — waves, lulls and the 3× loot lifetime. Deviations, per
user direction:**
- **No enemy behavior change during lulls** — W4 (fleeing survivors) is dropped; enemies behave
  identically in waves and lulls. "Waves and pauses only."
- **Loot lasts 3× (30 s → 90 s)** on the `XpPickup`/`HealthOrb` component defaults instead of
  pausing decay during lulls — simpler, and the lulls only need to hold loot, not freeze clocks.
- Implemented exactly as designed: `WaveStats` + pure `WaveSchedule` (20 s opening lull; 45 s wave
  + 9 s quiet gap + 45 s wave + 15 s full lull pair; quota 6 + 2/wave capped at 40; 0.6–1.5 s
  in-wave interval kept divided by `SpeedFactor`), ships gated by `InWave`, mines by `MinesPaused`.
  The old interval-ramp constants and both `InitialDelay`s were deleted.
- The ship's `AmmoColor` enum gained `DarkGreen` for AcidBubbleSpray (health orbs brightened to
  mint `150,255,190`, acid bubbles darkened to `35,120,50`) — color-identity fix bundled here
  because lulls make orbs collectible and they read as the spray.
- **Not implemented**: the render half (edge telegraph, tint lerp, camera breath, pressure bar) and
  the magnet bonus / last-of-wave health-orb reward. Live `[THREAT]` verification (2026-10-30) shows
  the sawtooth: waves open at t=20/74/134 with their quotas (observed peak 11/12/10), ships only fall across
  the 119–134 full lull, and mine respawns stay flat across it while continuing in the 65–74 quiet gap.

Supersedes P7 ("optional later: waves or surges") in plans/DIFFICULTY_SCALING.md.

## Current state (read off the code)

- `EnemyShipSpawnSystem`: endless trickle of single ships. Interval ramps 5–10 s → 2–4 s over
  `RampDuration = 180 s`, divided by `SpeedFactor(playerSpeed)`. 9 s grace. Cap `MaxEnemyShips = 100`.
  Spawns in a cone ahead of the player's velocity.
- `MineRespawnSystem`: same pattern, 4–8 s → 10–20 s, cap 23, 20 s grace.
- Cull at `Chase.CullDistance = 5500` px; pursuit speed follows the player.
- Loot: XP orb per kill + 5 % health orb (`LootStats`); orbs live 30 s; magnet inside `PickupRadius`.
- The upgrade menu is already a full sim pause — level-ups are accidental breathers.
- `ThreatMeter` already samples "ships / ships-in-ring / incoming dps" once a second: it can verify
  the wave/lull curve without new instrumentation.
- No wave concept exists anywhere. No audio adapter exists yet.

## Design goal

Rhythm: pressure wave → lull → bigger wave → shorter lull. Lulls are where the player collects the
orbs the last wave scattered. "Some breathers with less pressure, some with none."

## Core decision: clock-driven waves (recommended) vs clear-driven

### Option A — clock-driven (recommended)

A pure static `WaveSchedule` (in `Stats/`), a function of `elapsedTime` only — same style as
`EnemyShipStats.TierFor`. No entity state, no RNG draw for timing → stays deterministic for
`WorldRngTest` and headless scenarios.

Shape (constants in a new `WaveStats`):

- Fixed cycle for simplicity: `Cycle = WaveDuration + LullDuration`, e.g. 60 s = 45 s wave + 15 s lull.
- `waveIndex = (elapsed + LullDuration) / Cycle` — an opening grace period *is* wave 0's lull, so the
  current 9 s `InitialDelay` becomes the first (longer) lull.
- Wave size = quota drawn from per wave: `Size = min(StartSize + Growth·waveIndex, MaxSize)`
  (e.g. 6 + 2·i, cap 40). Within a wave the spawn interval is short (~0.6–1.5 s) so arrivals read
  as a wave, not a trickle. Wave duration itself is the hard stop: leftover quota despawns unused
  (or carries — decide at implementation; simplest: unused quota is lost, cap is what bounds the field).
- The existing interval-ramp interpolation (`StartMinInterval`/`MinInterval`/`RampDuration`) can be
  **deleted** — wave index replaces it. Net code change is close to neutral.

### Option B — clear-driven ("next wave when field is empty")

Next wave starts when live ships ≤ threshold, timer fallback. Problems:

- Bad for exactly what we want: a struggling player sits in an unearned empty field with nothing to
  do; a strong player clears instantly and skips the breather they just earned.
- Stateful (needs a scheduler entity) → harder to keep deterministic and to unit-test as a pure function.

### Option C — hybrid (later, only if A feels dead)

Clock stays master; if the whole wave quota dies before the wave window ends, the next wave starts
early (or the lull shortens). One bool of state ("wave cleared at t") — add only after playing A.

## Lull tiers — "less pressure" vs "no pressure"

Alternate the two lull types so half the breathers are soft, half are clean:

- **Quiet gap** (every odd wave): short (~8–10 s). Ship spawning paused; mine respawn keeps running
  at the *late* (slow) interval; survivors behave normally. Less pressure.
- **Full lull** (every even wave, and always after waves 0 and 1): longer (~15–20 s). No ship spawns,
  no mine respawns, and **survivor ships break off and fly away** (see W4). No pressure.

`WaveSchedule.PhaseFor(elapsed)` returns `{ WaveIndex, Phase: Wave | QuietGap | FullLull }`; both
spawn systems and (later) the renderer read the same pure function. No messaging, no events.

## Loot during lulls (W3)

Problem the design creates: kills late in a wave drop orbs that expire (30 s) during a long lull —
the player goes to collect and finds nothing.

Cheapest fixes, in order of preference:

1. **Pause orb decay during lulls.** Orbs' lifetime ticks only while `Phase == Wave`. One branch in
   the existing orb lifetime bookkeeping; makes every orb from a wave recoverable in its lull.
2. Magnet bonus during lulls: `PickupRadius × 1.5` while not in a wave. Both a real breather help and
   (if the ring is drawn) a visual "collect now" cue. Cheap: one multiply in `PickupMagnetSystem` /
   `Player.PickupRadius` use site.
3. Wave-clear health reward: the last ship of a wave's quota always drops a health orb (override the
   5 % roll). Deterministic, reads as "the lull resupplies you". (R2 reward-side idea from
   plans/DIFFICULTY_SCALING.md, now with a pacing slot for it.)

## Survivor behavior during a full lull (W4)

Instead of despawning leftovers, damaged/far ships **flee**: in `EnemyShipSystem`'s existing chase
branch, when `Phase == FullLull` steer away from the player at the ship's top speed; the existing
cull at 5500 px then removes them honestly. ~10 lines + a pure steer-target function + test.

Why this matters twice over: it empties the arena without a despawn pop, **and the retreating fleet
is itself the announcement that the pause has arrived** — the primary answer to "show, don't tell".

## Showing the pause without big text

Ranked by (impact ÷ cost), all grounded in systems that exist:

1. **The fleet leaves / arrives.** Fleeing ships during a full lull (W4) and edge-side assembly at
   wave start: spawn the wave's first ships farther out than usual so they're visible at the view
   edge assembling before closing. You see the wave gathering; no words needed.
2. **Screen-edge threat direction.** Waves arrive from a chosen bearing (the forward-cone spawn
   already picks one). Draw a faint glow/chevron on the screen edge at that bearing during the last
   few seconds of the lull, fading in. Tells *where*, which is actionable, instead of *what number*.
   (`HudRenderer` edge drawing already exists — the threat readout draws on the bottom edge.)
3. **Light language.** `Lighting` has point lights + a shader; add an ambient tint that lerps: cool
   and dim during lulls, warm/red during waves, with the crossfade timed to the phase. Subconscious,
   cheap (one uniform), and doubles as the wave-start announcement.
4. **Camera breath.** `CameraSystem` eases the zoom out ~5–8 % across a lull and back at wave start.
   Wide quiet field while collecting loot; closing in as pressure returns. Reads as breathing room.
5. **Silent pressure meter.** Replace the numbers idea with a 3–5 px bar on the bottom edge (the
   threat readout's current slot): fills/eases toward `ShipsInRing·incoming dps` — the ThreatMeter
   already computes it per second. It rises *before* you feel it (rising = wave) and drains during
   lulls. Purely diegetic-ish instrumentation, no text.
6. **Loot as the lull's soundtrack of objects.** Paused orb decay + magnet boost leaves a visible
   field of surviving orbs during the lull — the arena littered with collectables *is* the signal
   that time is safe.
7. **Audio (future).** No audio adapter exists. When one lands: quiet room tone in lulls, low swell
   3 s before a wave. Deferred, not part of this plan.

Anti-goals: no floating "Wave N" text, no countdown numbers. If any HUD number is ever wanted, the
pressure bar (5) carries it non-verbally.

## Determinism and testing

- `WaveSchedule` is pure `elapsed → phase/index/quota`; unit-test boundaries (phase at t=0, lull
  edges, size growth, cap).
- No new RNG sources: wave index and quota are clock-derived; spawn draws keep using `em.Rng` in the
  same call pattern (interval values change, so existing seeded-run traces change — expected, note
  it in the commit).
- Watch: `WorldRngTest`, `OffScreenSpawnTest`, `EnemyShipChaseTest`, `PerformanceBenchmark`.
- Measure per AGENTS.md: `.tools/scenario.sh <scenario> --sim --iters 3` before/after; the
  `ThreatMeter` row series now has an obvious sawtooth (wave peak → lull floor) — assert the lull
  actually empties the ring (e.g. `ShipsInRing` ≤ threshold during full lulls in the sim log).

## Tuning first-pass numbers (all in one `WaveStats` class, no magic numbers elsewhere)

- `Cycle = 60 s`: `WaveDuration = 45 s`, `FullLull = 15 s` (even waves + opening), `QuietGap = 9 s`
  (odd waves) — i.e. cycle alternates 45+15 / 45+9; two cycle lengths, still pure arithmetic.
- First lull (the grace period): 20 s, replacing ships `InitialDelay = 9` and mines `InitialDelay = 20`.
- `WaveStartSize = 6`, `WaveGrowth = 2`, `WaveMaxSize = 40` (wave 17+).
- Within-wave interval: `0.6–1.5 s`, still divided by `SpeedFactor(playerSpeed)` (keep P3).
- Stat tiers (`TierFor`, armament ladder): keep time-based; wave index only decides *when things
  appear*, not their stats. Switching composition to wave-index is a possible later tweak, not here.

## Suggested order of work (small commits)

1. `WaveStats` + pure `WaveSchedule` + unit tests. No behavior change.
2. `EnemyShipSpawnSystem` → wave-quota spawning; delete the interval-ramp interpolation.
3. `MineRespawnSystem` → gated by phase (off in full lulls, slow-only in quiet gaps).
4. Orb decay pause during lulls (+ optional magnet boost) (+ optional last-of-wave health orb).
5. Flee steering during full lulls + test.
6. Render: edge telegraph, ambient tint lerp, camera breath, pressure bar (separate small commits;
   each render change measured per AGENTS.md).
7. Playtest + scenario measure; tune `WaveStats` numbers only (the schedule shape stays).

## Tradeoffs

- Clock-driven = predictable rhythm the player learns; costs: lull happens even mid-fight-with-1-HP
  (that is the genre contract; Option C later if it bothers).
- Fleeing survivors cost a little pursuit opportunity (chase-then-kill players lose those kills) —
  the same rule that empties the field also protects the lull. Accept.
- Fixed cycle is the simplest pure schedule; variable cycle (longer waves late) is easy arithmetic in
  the same class if playtest wants it.
