# DIFFICULTY_SCALING.md

Plan for fixing the inverted difficulty curve (2026-09-12). No code changes yet.

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
