# DIFFICULTY_SCALING.md

Plan for fixing the inverted difficulty curve (2026-09-12).

## Status: P1–P5 implemented (commits `difficulty P4` … `difficulty P5`)

**Round 2 planned (2026-10-16), nothing implemented:** see "Round 2" at the end of this file — the
knobs beyond hp/damage/fire rate (stand-off range, ammo speed, small-ship speed, composition,
tracking, pellets, concurrency, mines, reward side), with the numbers and the rules they must obey.
P6 (type unlock) and P7 (surges) from below are still unimplemented and are folded into that plan.

Deviations from the proposed values, and why:

- **P1 cull distance = 5500 px** (plan said ~4000): P4 widens the initial spawn band to 2400–5000 px, so the cull must exceed it or initial ships die on spawn.
- **P4**: kept 6 initial ships and took the plan's primary option (widen band) at 2400–5000 instead of 2400–6000, to keep the cull distance sane; mines 15 → 9 (mid-range); ship `InitialDelay` 5 → 9 s (mid-range).
- **P3**: plan gave no values — chose `ReferenceSpeed = 100 px/s`, `MaxFactor = 3`. The interval is *divided* by the factor (the stated effect — constant density per unit distance — requires faster spawning at higher speed).
- **P5**: "capped multipliers" had no value — capped via `MaxTier = 10` (stats stop growing after 10 minutes). Damage scales on `Turret.Weapon.Damage` (base 1), the only enemy damage that actually hits the player; the dead `EnemyShip.Damage` field was removed.
- **P6/P7**: not implemented (out of scope for this work).

Verification: unit tests per item (`EnemyShipCullTest`, `EnemySpeedScalingTest`, `SpawnIntervalScalingTest`, `EnemyTierStatsTest`) plus existing `WorldRngTest` (determinism) and `PerformanceBenchmark`. The headless "enemies within 1000 px" diagnostic log from the verification section was not added — no Xvfb in this environment to observe it; add if a manual threat-curve check is wanted.

## Problem statement

The only hard part of the game is the start: initial spawns come from all sides in great numbers while the player is weak. Later, a fast-moving player outruns everything and only a few enemies bother them at a time.

## Root causes (verified in code)

**Early spike:**
- `GameInitializer` places 6 enemy ships at 1600–3200 px from **all directions** plus 15 mines just outside the screen from all directions — an omnidirectional burst against a player with no upgrades.
- First ship contact lands ~25–50 s in, with most of the initial pack arriving within a few minutes while mine respawns (from t=10 s) keep adding pressure.

**Late-game collapse:**
- Player speed is unbounded: thrust applies directly as acceleration with no linear drag (`PhysicsSystem` damps only angular velocity), boost ×2.5, and `ForwardAcceleration` upgrades compound ×1.1 each. Sustained speeds of hundreds of px/s are trivial.
- Enemy top speeds are static forever: 65 / 90 / 50 px/s (Default / Interceptor / HeavyCannon). Once the player's sustained speed exceeds ~90, no enemy can catch up from behind.
- Spawns appear in a ±45° cone **ahead** of the player; fast players run past them, enemies brake to their cap and become permanent tailers that never threaten again.
- There is **no culling or despawn anywhere**: stale tailers persist forever and count against `MaxEnemyShips = 100`. At 100 live ships, `EnemyShipSpawnSystem` stops spawning entirely — the world literally empties out.
- Spawn interval plateaus after 3 minutes (2–4 s); enemy HP/damage/fire rate never scale while player DPS and HP compound via upgrades → difficulty ratio trends to zero.

## Design goals

- Threat level non-decreasing over time, at any player speed.
- Survivable start: pressure ramps up instead of bursting from all sides.
- All scaling deterministic (derived from `ElapsedTime` + current player velocity; no new RNG sources).
- Small commits, pure functions where possible, unit-testable without graphics.

## Proposals (prioritized)

### P1 — Cull stale enemies (structural prerequisite)

Remove enemy ships beyond a cull distance (~4000 px, a few screens) from the player.

- Where: inside `EnemyShipSystem`'s existing per-ship loop (it already has ship + player positions), or a small dedicated system if that keeps it shorter.
- Effect: frees the 100-ship cap for fresh threats; world stays populated around the player instead of a km-long tail; fewer entities to iterate (helps the 10k-object goal).
- Size: ~20 lines + test (beyond distance → removed, within → kept).

### P2 — Enemy speed scales with player speed (core fix)

Replace the static `ship.Speed` cap with an effective cap that references current player speed, e.g. `max(base, 0.75 × playerSpeed)` with a hard ceiling (~2× base).

- Where: the velocity clamp in `EnemyShipSystem` and its drift-cancel threshold (`speed > ship.Speed`).
- Effect: fast movement no longer guarantees safety; pressure persists at any speed. Slow play is unchanged because of the `max(base, …)`.
- Tradeoff: "run away" stops being a full escape strategy — that is exactly what we want to fix. Keep the factor modest so it reads as pursuit, not rubber-banding.
- Size: ~10 lines + test (pure function of base speed and player speed).

### P3 — Spawn rate scales with player speed

Scale the spawn interval by `clamp(playerSpeed / ReferenceSpeed, 1, MaxFactor)` in `EnemyShipSpawnSystem` — a fast player covers more ground per second, so threats must arrive faster to keep density constant.

- Effect: threat density per unit distance traveled stays roughly constant instead of thinning out at speed.
- Size: ~10 lines + test (interval within expected range for given elapsed time and player speed).

### P4 — Early-game tuning (constants only, independent quick win)

- Initial mines 15 → 8–10: omnidirectional mine pressure is the biggest early unfairness.
- Stagger initial ship arrivals: widen the distance band (e.g. 2400–6000 px) so ships arrive one at a time over ~90 s instead of as a pack; or reduce count to 3–4 and let the spawner fill in.
- Delay first respawns: mine `InitialDelay` 10 → 20 s, ship `InitialDelay` 5 → 8–10 s — a real grace period before pressure compounds.
- Effect: the start becomes survivable; difficulty comes from player weakness and mistakes, not an unavoidable burst.

### P5 — Time-based stat tiers (counters compounding upgrades)

Derive `tier = floor(elapsed / TierDuration)` (e.g. 60 s) and apply capped multipliers **at spawn time** in `EnemyShipFactory.CreateComponents`: HP ×(1 + 0.25·tier), damage +1 every 2 tiers, fire rate ×(1 + 0.1·tier).

- Spawn-time only: no mid-run stat mutation, existing entities and tests unaffected.
- Effect: rising enemy toughness meets the player's compounding DPS; difficulty ratio stops trending to zero.
- Size: ~30 lines + test (elapsed → expected HP/damage/fire rate at tier boundaries).

### P6 — Enemy type unlock schedule (pacing)

`PickRandomType` currently rolls 33/33/33 from t=0. Gate types by elapsed time, e.g. Interceptor after ~30 s, HeavyCannon after ~60 s.

- Effect: new enemy types read as visible escalation; early fights stay simple and drop the tanky heavies from the opening minutes.
- Size: ~10 lines + test (elapsed → allowed type set).

### P7 — Optional later: waves or surges

If linear scaling feels flat after P1–P6 are measured: discrete threat tiers with a short lull between them, or periodic one-directional "surge" windows telegraphed by the HUD. Bigger design; defer until the above is in place and observed.

## What NOT to do

- Don't add player drag / top speed to fix this — it changes core feel and punishes progression; scale enemies up instead of nerfing the player.
- Don't scale enemy speed purely on elapsed time — a fast player still outruns it; it must reference player speed (P2) or cull + respawn (P1).
- No new RNG sources: keep determinism for `WorldRngTest` and reproducible headless runs.

## Suggested order of work

1. P4 first if we want an immediate feel fix (constants only, one commit).
2. P1 (prerequisite for the cap to mean anything again).
3. P2 + P3 together conceptually (both address the speed mismatch), separate commits.
4. P5, then P6 — independent of each other.
5. Re-measure; consider P7 only if needed.

## Verification plan

- Unit tests per item above: cull distance, effective-speed function, scaled interval, tier stats, type unlock set. All pure and deterministic.
- Watch existing tests: `EnemyShipChaseTest`, `OffScreenSpawnTest`, `WorldRngTest` (determinism), `PerformanceBenchmark` (culling should help).
- Headless run per the TROUBLE_SHOOTING.md recipe with `SPACEVORS_DIAGNOSTIC=1`; add a diagnostic log of "enemies within 1000 px" every N seconds to confirm the threat curve stays flat/rising past the 3-minute plateau instead of collapsing.

---

# Round 2 — what scales beyond hp, damage and fire rate

**Status: plan only (2026-10-16). No code changed.** Asked for: longer attack range, a tiny bit of
projectile speed, small enemies a bit faster, and other knobs worth having.

## R2.1 What already scales, and what is frozen (read off the code today)

| knob | where | tier 0 | tier 10 | note |
|---|---|---|---|---|
| hull hp | `EnemyShipStats.HpMultiplier = 1 + 0.25·t` | Interceptor 7 · Default 15 · HeavyCannon 30 | 25 · 53 · 105 | applied at spawn only |
| ammo damage | `DamageAdd = t/2` | 1 | 6 | shared gun, all types |
| fire rate | `FireRateMultiplier = 1 + 0.1·t` | 1.5 / 0.6 / 0.8 per s | 3.0 / 1.2 / 1.6 | |
| spawn cadence | `SpawningStats.EnemyShips` | 5–10 s | 2–4 s after a 180 s ramp | cap 100 live ships |
| spawn rate vs speed | `ReferenceSpeed 100`, `MaxSpeedFactor 3` | ÷1 | ÷3 at ≥300 px/s | density per distance |
| pursuit speed | `Chase`: `min(max(base, 0.75·playerSpeed), 2·base)` | base 65/90/50 | same, but the player term dominates | cull at 5500 px |
| mines | `MineStats`, `SpawningStats.Mines` | hp 2, contact 3, respawn 4–8 s | hp 2, contact 3, respawn **10–20 s**, cap 23 | the only content that gets *weaker* |
| firing range | `EnemyShipType.FiringRange` | 700 all types | 700 | frozen |
| ammo speed | `TurretAmmoSpeed` | 200 / 200 / 160 | 200 / 200 / 160 | frozen |
| arc / scatter / pellets | `EnemyShipStats` | π/8, 0.05, 1 | same | frozen |
| tier ceiling | `MaxTier = 10` | — | growth stops at 10 min | deliberate |

Terminology trap: the fragile fast hull is **Interceptor** (7 hp, 90 px/s) but its radius is 45 —
bigger than **Default** (15 hp, 20 px, 65 px/s). "Small enemies faster" should name the fragile one
by role, not size, or the knob lands on the wrong row.

## R2.2 The player side these knobs are priced against (from the weapon balance pass)

- Hulls: radii 46/58/58/84, hp 8/10/10/20 (+2 per `Hp` card); starter raw DPS 17 (Scout) / 22.5
  (Fighter) / 24 (Shadow) / 45 (Heavy).
- Reach ladder: aura 600 → close 900 → mid 1350 → long 1600–1700, and a clicked target is engaged at
  3× base range.
- Today's engagement ring: `FiringRange + player.Radius` = **746 px (Scout) … 784 px (Heavy)**.
- TTK at a starter Scout's 17 DPS: Interceptor 0.4 / 0.9 / 1.5 s, Default 0.9 / 2.0 / 3.1 s,
  HeavyCannon 1.8 / 4.0 / 6.2 s at tiers 0 / 5 / 10.
- Incoming: 6 damage per hit at tier 10 against a base-8 Scout = two hits.

## R2.3 Range: the ring is really a crowd dial

`EnemyShipSystem` stops a hull once `dist ≤ FiringRange` and it shoots from there, so enemies form a
ring at that radius. Range therefore does two things at once:

- **Crowd**: the ring's circumference grows with r — 700 → 950 px is +36% of rim, i.e. ~36% more
  ships holding and firing at you at the same time, for free.
- **Cover**: a rock's angular cover shrinks with distance — a 40 px rock spans ~6.6° at 700 px and
  ~4.8° at 950 px. Growing enemy range quietly cancels part of the rock cover the player just got.

Rules for it:

- **D1 — never out-reach the player's mid band.** `max(FiringRange) + heaviest radius (84) < 1350`,
  so mid and long guns still trade favourably. That puts the hard ceiling at ~1000 px, and 1150+
  would make the whole 900-close band a dead pick (the weapon pass priced those guns against a 700 ring).
- **D2 — make range a type property before a tier multiplier.** A per-type spread reads as
  escalation ("the long-range line showed up") and keeps one row honest instead of moving everyone.

Proposal (t = 0…10):

| type | FiringRange | effect |
|---|---|---|
| Interceptor | 700 → **550** (−15/t) | it must come to you: a rammer, and impact damage needs ≥160 px/s closing speed |
| Default | 700 → **800** (+10/t) | the body of the fleet drifts outward a little |
| HeavyCannon | 700 → **950** (+25/t) | the firing line; slow 160 px/s ammo, so it stays dodgeable at distance |

Ring at tier 10: 550+46 = 596 px (Interceptor on Scout) … 950+84 = **1034 px** (HeavyCannon on
Heavy) — inside D1.

One coupling to note: `FiringRange` is **both** the gun's range (the turret is built with
`Range = FiringRange`) and the distance at which the chase system brakes the hull, so shortening the
Interceptor's makes it come to 550 px and poke from there — it still parks, it does not collide. A
real rammer needs that stand-off decoupled from gun range (R2.6 item 10), which is why the row above
is a pressure knob and not yet a role change.

## R2.4 Ammo speed: keep it a rounding error

At 200 px/s a round takes ~4.8 s to cross half a 1920 px screen; at 230 px/s ~4.2 s. Reaction time is
the genre's readability budget, and enemy turrets already solve the intercept (they lead), so extra
speed buys accuracy you do not need.

- **D3 — enemy ammo stays ≤ 250 px/s and always slower than the player's slowest round (400 px/s).**
- Proposal: **+1%/tier** (200 → 220, 160 → 176). Or buy the same "sharper" feel with
  `WeaponScatter` 0.05 → 0.08, which raises hit probability without shortening reaction time.
  **Pick one, not both** — they stack invisibly.

## R2.5 Small-ship speed, and the cap that already dominates it

Pursuit is `min(max(base, 0.75·playerSpeed), 2·base)`. A late-game player sustains 150–350 px/s, so
the 0.75 term (113–263) already beats every base speed: raising base speed changes how fast they
close when you are slow or cornered, and where the 2× ceiling sits — it does **not** change the chase
at speed.

- Proposal: **Interceptor 90 → 117 (+5%/tier)**, **Default 65 → 84 (+3%/tier)**, HeavyCannon untouched.
- The knob that actually ends "kite forever" is `Chase.PlayerSpeedFactor` 0.75 → 0.80. Bigger feel
  change, decide it by playtest with the threat log (R2.8), not by tuning.
- **D4 — any speed knob is an impact-damage knob**: `ImpactDamage` switches on at 160 px/s closing
  speed and is quadratic, and `MaxImpactDamage = 3` per hit. Re-check the ram numbers whenever base
  speed moves; keep `MaxSpeedMultiplier ≤ 2.5` so pursuit never reads as rubber-banding.

## R2.6 The rest of the knob set, ranked

1. **Composition by tier (P6, still unimplemented).** Type unlock (Default from 0, Interceptor ~45 s,
   HeavyCannon ~90 s) then weight drift (HeavyCannon share up late). Cheapest big win: no new
   mechanics, and it makes every other knob legible.
2. **The ring, via D2** — simultaneous shooters, no per-shot lethality change.
3. **Pellets on one type**: HeavyCannon 1 → 2 pellets at t ≥ 5 and 3 at t ≥ 8, scatter 0.05 → 0.10 —
   a suppressive fan whose per-hit damage never changes. Budget: +~106 rounds/s at full concurrency,
   ~+320 live rounds. Entity-cheap, perception-expensive, so cap it to one type.
4. **Concurrency and cadence**: `MaxEnemyShips` 100 → 120–140, interval floor 2–4 → 1.5–3 s. The
   straightforward "busier world", and the one the 10k-object goal is measured against (ship = AI +
   grid insert + render + turret).
5. **Tracking**: `TurnRate` 1.0 → 1.0 + 0.03·t (1.30 at tier 10). Punishes strafing without touching
   speed or damage; mirrors the mount-slew rule the weapon pass uses for the player.
6. **Mines — currently the only thing that decays**: respawn slows 4–8 s → 10–20 s, cap fixed at 23,
   hp 2 forever. Raise `MaxMines` (23 → 30) or flatten the late interval rather than mine hp: mines
   are the XP clock (1–2 xp each, level *n* wants 10·n) *and* a heal-orb source, so their density
   sets both progression pace and survivability. Do not scale their hp — clearing them becomes a chore.
7. **Reward-side difficulty (the lever nobody pulls)**: `LootStats.HealthOrbChance` 0.05 → 0.03 late,
   or a slower `Hp` card cadence. This raises pressure with zero sim cost and no enemy stat creep, and
   it is the honest answer to "damage is already 6 against 8 hp".
8. **Elites** (PLAN "Future"): a share of ships at t ≥ 5 with +hp and +radius plus a visible ring —
   interesting difficulty instead of numeric difficulty. Needs one component and a visual tell.
9. **Spawn geometry**: tighten the band (2400–5000 → 1800–4500) at high tier — but never inside
   2× the ring, so nothing spawns able to shoot you the frame it appears — plus flanking/rear spawns
   and the old P7 one-directional surge, telegraphed.
10. **Behaviour**: orbit/strafe at the ring instead of parking (a moving firing line), kamikaze
    Interceptors, and a **weapons-hot delay** on spawn (~0.75 s cooldown) — that last one is the
    fairness purchase that makes the longer ring readable.
11. **Difficulty selection / assist**: one multiplier on the tier gains (0.75 / 1 / 1.3). Cheap, but it
    multiplies every tuning table by three; only worth it if the single curve is contested.

## R2.7 What not to scale

- **Per-hit damage.** Already 6 vs a base-8 Scout at tier 10. Any range/count growth must be paid for
  from this budget staying flat: **D5 — one hit takes ≤ 1/3 of a typical late hull (≈16 hp)**.
- Fire rate past ~3.5/s per ship, scatter past ~0.15 (fake accuracy), ammo speed past D3.
- XP per kill (it compounds the player) and enemy hp past the cap without re-reading the TTK table:
  **D6 — a tier-10 HeavyCannon must die in ≤ 6 s under a starter Scout's sustained fire** (today 6.2).
- No mutation of already-spawned ships, no new RNG sources (determinism), no per-frame allocation in
  the spawn loop.

## R2.8 Measure first: a `[THREAT]` diagnostic line

Add it before tuning anything, 1 Hz under `SPACEVORS_DIAGNOSTIC=1`: ships alive, ships inside the
ring, live enemy rounds, rounds/s spawned, **incoming dps estimate** (Σ firing-capable ships × fire
rate × damage), player hp, mine count, elapsed + tier. Then print the curve for 0/3/6/10/15 min per
hull and tune to a target: incoming dps inside a survivable band, a kill required every ~2 s to keep
the rim clear, and no single hit over D5. Every knob in R2.6 becomes readable in one column.

## R2.9 Verification

- New `EnemyDifficultyTest` asserting the rules as data, in the `WeaponBalanceTest` style: the D1 ring
  ceiling, the D3 ammo-speed ceiling, the D6 TTK ceiling, the enemy round budget
  (`Σ types at tier 10 (fire rate × pellets) × MaxEnemyShips` under a stated cap), and the type-unlock
  set by elapsed time.
- Extend `EnemyTierStatsTest` for the new per-type tier functions; keep `WorldRngTest` green
  (determinism) and `EnemyShipChaseTest` / `OffScreenSpawnTest` for the speed and spawn-band changes.
- Perf: `.tools/scenario.sh impact-storm --sim --iters 3` after each of concurrency, pellets and the
  ring; add a `ring-fire` scenario (tier-10 fleet at the grown ring, 3-pellet heavies) if the pellet
  knob lands. Compare median/p99 with `.tools/run/index.tsv` and put the verdict in the reply.

## R2.10 Commit order

1. `[THREAT]` log (measurement only, no balance change).
2. Type unlock + weight drift (P6) — the pacing baseline everything else is read against.
3. Range: per-type `FiringRange` with tier growth, rule asserted (D1).
4. Speed: small-ship tier growth; decide `PlayerSpeedFactor` from the log.
5. Ammo speed **or** scatter — one, not both.
6. Turn rate, then pellets, then concurrency — each measured against `impact-storm`.
7. Reward side (orb chance) once the enemy curve is known.
8. Elites / surges only if the above reads flat in play.

## R2.11 Open questions

- Is the late-game answer meant to be **a busier world** (count, pellets — costs entities) or
  **deadlier individuals** (range, tracking — free, but harsher on low-HP hulls)?
- Keep `MaxTier = 10` (stats freeze at 10 minutes and only composition grows), or run tiers to ~20 with
  the TTK rule re-checked?
- Do you want the reward-side knob in the same pass, or is difficulty only about enemies?
- Should the Interceptor stop shooting entirely and become a pure rammer once its range drops to 550?
