# DAMAGE_GRAPHICS.md

Plan for the PLAN.md section "Graphical damage indicators" (2026-09-15).

## Status: done

Implemented in three commits: `7f050d4` spark rename (separate concern), `2e68a43` emission system, `787ecd0` smoke rendering + K diagnostic key; this update is the final docs commit. The PLAN.md section is now the canonical done summary.

Deviations from this plan:
- Stage 1 also moved the bracket-freshness window into `AutoTargetMark.FreshWindow` (shared by `TargetingRenderer` and `DamageEffectSystem`) instead of a renderer-local constant.
- Headless screenshot verification was not possible in the build environment (no Xvfb/xdotool, no root to install) — visual tuning of rates/color/puff size is pending; starting values from this plan are what shipped.

## Requirements (verbatim from PLAN.md)

- Damaged ships may emit smoke puffs or sparks
- Less than 2/3 hp left, few smoke puffs
- Less than 1/3 hp left, more smoke puffs and some sparks
- Smoke puffs are left behind when ship moves, but follow moving ships at slower speed than ship speed
- Smoke and sparks fade away after 1 second
- Smoke and sparks get budget after which they are culled
- Damage graphics priority is player ship > manual target > auto targets > other visible ships
- Ships far away and not visible in game screen will not emit smoke or sparks even if damaged

## Scope decisions (confirm before implementing)

- **Emitters = player + enemy ships only** (`Health` + `Player`/`EnemyShip`). Mines are excluded: PLAN.md says "ships", mines are small (7.5–15 px), short-lived, and already get hit sparks from `CollisionSystem`. Adding mine smoke later is a one-line query change.
- **"Follow at slower speed" = velocity inheritance at spawn**: each puff spawns with `Velocity = FollowFactor × shipVelocity + jitter` (`FollowFactor ≈ 0.5`) and then coasts (no per-tick source tracking). A moving ship leaves a growing trail behind it; a stationary ship puffs in place. Simpler than a leash that re-reads the source's velocity every tick, no stale-source handling needed.
- **Sparks reuse the existing `DamageSpark` component** (lifetime 1 s) — zero new spark rendering or lifetime code; only smoke needs a new component and draw pass.

## Current state (verified in code)

- Effect entities are plain components + `Position`/`Velocity`: `Explosion`, `DamageSpark`, `HealSpark`, `DebugMarker` (`EffectComponents.cs`). `EffectSystem` (Resolution phase) decrements each lifetime via CommandBuffer and destroys at 0 — one loop per component type, same pattern four times.
- Hit/death sparks are spawned by `CollisionSystem` / `ShipDeathExplosionSystem` with random velocity + lifetime through `view.Rng`; drawn as fading orange circles in `WorldRenderer.DrawSparks`.
- `Health(int Current, int Max)` exists on player, enemy ships, and mines — hp ratio is computable everywhere.
- Targeting state: `PrimaryTarget(Entity)` on the player (manual lock), `AutoTargetMark(float LastTargetedAt)` on targets, refreshed every tick by `TurretFiringSystem` (Action phase); renderer treats marks as fresh for 1 s (`AutoTargetMark.FreshWindow`, moved from a renderer-local constant during implementation).
- Camera: `Camera(Vector2 Target, Vector2 Drift)`; rendering uses `Camera.Target` as screen center. Domain systems can read it via `view.GetEntitiesWithComponents<Camera>()`; viewport size flows in per tick via `WorldView.ViewportSize` (set in `GameSession.StepSimulation`).
- Phase order: Movement → Action → Resolution → Intent, CommandBuffer applied after each phase — an **Intent-phase system sees this tick's** collision damage (Resolution) and target marks (Action).
- Enemy ships beyond 5500 px are culled by `EnemyShipSystem`; the on-screen check below covers everything nearer.
- Diagnostics: `SPACEVORS_DIAGNOSTIC=1` already adds L/M/N/H/T test keys in `GameSession`. The N-key test enemy has 100 000 hp — too slow to damage into the smoke tiers with weapons, so a small diagnostic hook is needed for screenshot verification (Stage 2).

## Design decisions

1. **New component** in `EffectComponents.cs`:
   ```csharp
   public readonly record struct SmokePuff(float Lifetime, float InitialLifetime = 1f, float Radius = 6f);
   ```
   `Radius` is set at spawn from the source ship's radius (puffs scale with hull size). `EffectSystem` gains a sixth loop following the existing pattern exactly.

2. **New domain system** `DamageEffectSystem` (`src/Domain/Combat/`, registered in `SimulationRunner.IntentSystems`). Per tick:
   1. Read camera target + `WorldView.ViewportSize`; an emitter is *visible* if its position is within half-viewport + `EmitMargin` (≈100 px) of the camera target — ships just past the edge still emit so smoke doesn't pop at screen borders, far/off-screen ships never do.
   2. Collect candidates: player and enemy ships with `Health`, no `Dead`, hp ratio < `LightHpThreshold` (2/3), visible.
   3. Tier each candidate: **0** = player · **1** = live manual `PrimaryTarget` · **2** = fresh `AutoTargetMark` (≤ 1 s, same window as the brackets) · **3** = other. Sort by `(tier, distance to camera target)` — deterministic tie-break, and it is what makes the budget allocation priority-correct.
   4. Emit in sorted order while per-tick budget remains: light tier rolls `SmokeRateLight × dt` (≈2 puffs/s); heavy tier (< 1/3) rolls `SmokeRateHeavy × dt` (≈6 puffs/s) **and** `SparkRateHeavy × dt` (≈3 sparks/s). One `view.Rng` call per roll, consumed in sorted order.
   - Smoke spawn: position = ship pos + random offset inside hull radius; velocity = `FollowFactor × shipVelocity` + jitter (±20 px/s); lifetime 1 s.
   - Spark spawn: same position rule; velocity = random direction × 60–140 px/s (+ small share of ship velocity); existing `DamageSpark(1f, 1f)`.

3. **Budget = per-tick spawn cap** (`MaxSmokePerTick`, `MaxSparksPerTick` ≈ 1 each), allocated in priority order; candidates past the budget simply don't emit that tick ("culled" before birth). Because every particle lives exactly 1 s, steady-state live count is bounded by cap × 120 ticks (≈ ≤125 puffs + ≤125 sparks worst case) — **no separate culling pass needed**. Normal combat demand (~14 puffs/s for player + a few damaged enemies) never approaches the cap; it only binds in extreme swarms. If screenshots ever show too many particles, lower the rates/cap — no new mechanism required.

4. **Rendering**: `WorldRenderer.DrawSmokePuffs` (new private method), called after `DrawGreenSparks`, before `ThrusterFlameRenderer` — smoke trails behind ships and flames. One grey circle per puff: radius grows ~2× over life, alpha ≈ 80 × lifeRatio, color ≈ `(120, 120, 130)`. Off-screen cull via existing `RenderHelpers.IsOffScreen`. Sparks need no renderer change (existing `DrawSparks` pass).

5. **Diagnostic hook** (`SPACEVORS_DIAGNOSTIC=1` only): new key **K** halves the current hp of the player's live primary target (nearest enemy ship if nothing is locked) — two presses take a test enemy from full → light tier → heavy tier, making both tiers screenshot-verifiable in seconds instead of minutes. Same pattern as existing L/M/N/H/T keys; no effect on normal play or determinism (diagnostic writes are app-layer, like the others).

## Stages (one commit each)

### Stage 1 — Emission system + SmokePuff (Domain)

- `EffectComponents.cs`: add `SmokePuff`.
- `EffectSystem.cs`: add the smoke-puff lifetime loop (copy of the spark loop pattern; diagnostic log line "Effects: smoke puff").
- New `src/Domain/Combat/DamageEffectSystem.cs` per Design #2, with a small public static `OrderCandidates(...)` helper so ordering is unit-testable without RNG. All rates/thresholds/margins as named constants (no magic numbers).
- `SimulationRunner.cs`: register in `IntentSystems`.
- Tests — new `DamageEffectTest.cs` (world-builder helpers exist in `TargetingTest.cs` / `TurretTargetMarkTest.cs`):
  - full-hp ship → no puffs/sparks over N ticks;
  - light-damaged visible ship → smoke spawns, **no** sparks; heavy-damaged → both spawn (run enough ticks that the probability is certain);
  - off-screen damaged ship (far from camera target) → nothing; dead ship → nothing;
  - `OrderCandidates`: player first, then manual target, then fresh auto-mark, then others by distance; stale (>1 s) marks sort as "other";
  - budget: with more heavy candidates than the per-tick cap allows, total spawns in one tick ≤ cap (assert via command count);
  - puff lifetime: a spawned `SmokePuff` is destroyed by `EffectSystem` after >1 s.
- Commit: `damage smoke and spark emission`

### Stage 2 — Smoke rendering + diagnostic key (Game)

- `WorldRenderer.cs`: `DrawSmokePuffs` pass per Design #4, inserted in draw order.
- `GameSession.cs`: K-key hp-halving under `_diagnostics` (Design #5), logged via `DiagnosticLogger.LogEvent`.
- Headless verification per TROUBLE_SHOOTING.md "General workflow that works": Fighter (key 2 — Scout's LoadTestWeapon ring pollutes the scene, troubleshooting #21); H + T for a stable invincible pinned player; N to spawn test enemies.
  - K once → light tier: sparse grey puffs trailing behind moving ships, gone ~1 s after emission stops; no sparks.
  - K twice → heavy tier: denser smoke **plus** orange sparks at the hull.
  - Move the ship (release T briefly) → trail stretches behind it; stop → puffs linger in place and fade.
  - Left-click a damaged enemy (manual target) vs an auto-targeted one vs untargeted ones under swarm load: priority ships keep emitting when the budget binds.
  - Camera drift / far ships: damaged enemies beyond the viewport emit nothing (check with N-spawned enemies at distance + `[diag]` logs).
- Commit: `smoke rendering + damage diagnostics`

### Stage 3 — Performance check, tuning, docs

- Perf: run under LoadTestWeapon + max enemies (Scout), read `[FRAME]` per-system timings; expect negligible (≤124 Health entities scanned, small sort, ≤2 RNG calls/candidate). Also confirm `PerformanceBenchmark` scenarios stay within the 8.3 ms budget and `WorldRngTest` passes unchanged.
- Tune via screenshots: rates, follow factor, puff size/growth/alpha/color; verify smoke reads against both dark space background and bright hulls/explosions.
- Docs: rewrite the PLAN.md section as a done summary (style of "Mouse clicks to set primary target — done"); ARCHITECTURE.md component list gains `SmokePuff`, project layout notes the new system + renderer pass; note the K key in PLAN.md diagnostics.
- Commit: `damage graphics tuning + docs`

## Determinism & invariants

- All randomness goes through `view.Rng`; candidates are **sorted before** any RNG consumption, so emission order is a pure function of state — immune to storage swap-pop reordering. No new RNG sources; `WorldRngTest` must pass unchanged.
- Entity ids are never reused: a stale `PrimaryTarget`/`AutoTargetMark` can only point at a dead entity — the `Dead` check covers it (same invariant as `TargetingRenderer`).
- No yield returns, no new direct-write paths; emission is pure CommandBuffer usage (default write pattern).
- Pause/game-over: simulation stops or player carries `Dead` → no new emissions either way; existing puffs fade out naturally.

## Open tuning items (decide via screenshots in Stage 3)

- Exact rates (2/6 puffs/s, 3 sparks/s), follow factor (0.5), and per-tick caps — starting values above are guesses.
- Smoke color: single grey vs slightly darker/heavier tint for the <1/3 tier.
- Puff size scaling with ship radius (`clamp(radius × 0.35, 4, 14)`) — Heavy (84 px) puffs may need a lower cap to avoid blobs.
