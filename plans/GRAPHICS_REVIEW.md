# GRAPHICS_REVIEW.md

Review of all graphics-related code (2026-08-18).

Scope: `src/Game/Renderer.cs`, `Lighting.cs`, `LitSprite.cs`, `LitSpriteMatcher.cs`, `ImageLoader.cs`, `ThrusterFlameRenderer.cs`, plus graphics-relevant parts of `SpaceVorsApp.cs` and `GameInitializer.cs`.

Overall the system is clean (off-screen culling via `IsOffScreen`/`HalfDiagonal`, defensive `Id != 0` texture checks, testable `LitSpriteMatcher`, shader fallback design). Findings below.

## Status (checked 2026-08-21)

Fixed: 26 · Open: 1

Remaining work:
- Nit: per-entity second Position lookup in draw loops (deferred — measure first; see "Suggested order of work")

## Findings

### Major

- **[correctness] `Renderer.cs:108` — far starfield layer is invisible.**
  `DrawStarfield` casts `(int)size`; layer 0 sizes are `[0.5, 1)` (GameInitializer.cs:148), so all 150 far stars draw with radius 0 and never appear. The whole parallax depth cue of the background is missing.
  Fix: clamp to a minimum visible size (`Math.Max(1f, size)`) or drop the cast if `DrawCircle` takes float.
  **Fixed** — `BackgroundRenderer.cs:29` clamps with `(int)Math.Max(1f, size)`.

- **[perf/design] `SpaceVorsApp.cs:74-78, 244-248, 327-331` — FPS cap does not actually cap at 120.**
  The three duplicated `Thread.Sleep` blocks only pad *render* time (sim + input run before the timer starts) and truncate to whole ms, so real frame rate wanders ~120–133 fps. Also contradicts the `MaxFps = 120` constant's intent.
  Fix: call `Raylib.SetTargetFps(MaxFps)` once after `InitWindow` and delete all three sleep blocks (net code deletion).
  **Fixed** — `SpaceVorsApp.cs:19` calls `SetTargetFPS(MaxFps)` after `InitWindow`; no `Thread.Sleep` remains in src.

- **[dead] `Renderer.cs:177-218` — second `DrawAsteroids` loop is unreachable.**
  It draws asteroids without a `Rotation` component, but `AsteroidFactory.AddAsteroidComponents` always adds one (AsteroidFactory.cs:15). ~40 lines of duplicated texture/fallback draw logic that can never run.
  Fix: delete the second loop and the `HasComponent<Rotation>` skip in it; keep only the rotating path.
  **Fixed** — single `<Asteroid, Rotation>` query loop in `WorldRenderer.DrawAsteroids` (WorldRenderer.cs:32-62), with an `Id != 0` texture fallback.

- **[dead] `Renderer.cs:717-741, 849-944` — engine/loadout card UI has no callers.**
  `DrawEngineCards`, `DrawLoadoutCards`, `GetEngineCardRect`, `GetLoadoutCardRect` are never invoked; the current flow only uses ship cards (`DrawShipCards`). ~100 lines. They also duplicate layout math in two places (rect getters vs draw methods), a desync hazard if ever revived.
  Fix: delete all four; re-add from git history if the engine/loadout selection screens come back.
  **Fixed** — all four deleted; no references remain anywhere in src.

### Minor

- **[design] `Renderer.cs:240-251` — ammo color inferred from magic radius thresholds.**
  `DrawAmmo` maps `Radius >= 5f` → green, `>= 3.7f` → blue, silently mirroring `GetAmmoRadius` (TurretFiringSystem.cs:368: 5.0 / 3.75 / 2.5). A new weapon with an intermediate radius gets the wrong color with no error; the renderer encodes domain data.
  Fix: carry a color or kind on the `Ammo` component (set at spawn) and draw from it.
  **Fixed** — `Ammo` carries `AmmoColor Color` (CombatComponents.cs:17), set at spawn from `WeaponType.Color` via `GetAmmoColor` (TurretFiringSystem.cs:354-398); renderer switches on the enum (WorldRenderer.cs:95-101).

- **[maintainability] `Renderer.cs:620, 660` — effect lifetimes hardcoded in two places.**
  `DrawGreenSparks` divides by `0.6f` and `DrawDebugMarkers` by `0.5f`, duplicating spawn values (PickupMagnetSystem.cs:146, TurretFiringSystem.cs:31). `Spark` already has an `InitialLifetime` field; `GreenSpark`/`DebugMarker` don't. Change one side and `lifeRatio` can exceed 1 → alpha > 255 wraps to transparent.
  Fix: add `InitialLifetime` to both components (as `Spark` has) and use it in the renderer.
  **Fixed** — `GreenSpark`/`DebugMarker` have `InitialLifetime` (EffectComponents.cs:11,17), `EffectSystem` preserves it on lifetime updates, renderer divides by it (WorldRenderer.cs:294, :313).

- **[clarity] `Renderer.cs:524-573` — `DrawMines` checks texture presence three different ways.**
  `mineTex.HasValue && Id != 0` for extent, re-fetch from `ImageLoader.MineTexture` inside the branch, then a trailing `if (!HasValue || Id == 0)` that draws an extra core circle over the fallback disc. Verify the double-circle fallback is intentional; either way collapse to one check and reuse the local.
   **Fixed** — single `hasTexture` check hoisted out of the loop and reused for extent + draw (WorldRenderer.cs:190-238). Double-circle no-texture fallback kept intentionally as a readable placeholder; health-based alpha computed once per mine.

- **[robustness] `ImageLoader.cs:28 vs 61` — inconsistent failure behavior.**
  Missing ship directories degrade gracefully (`Directory.Exists`), but a missing `asteroids/` or `mines/` directory throws from `GetFiles`. Failed asteroid texture loads (Id==0) are also unhandled, unlike ships/mines — the extent math divides by `tex.Width` → 0.
  Fix: same `Directory.Exists` guard for all dirs; treat Id==0 asteroid textures like the other fallbacks.
   **Fixed** — all sprite dirs including `assets/mines` are guarded by `Directory.Exists` (ImageLoader.cs:31, :74), and Id==0 asteroid textures fall back to a rectangle (WorldRenderer.cs:44-60).

- **[architecture] `Renderer.cs:7` — adapter in the Domain namespace.**
  `Renderer` declares `namespace Spacevors.Domain.Systems` but is a Raylib adapter living in `src/Game/`. Muddies the domain/adapter boundary ARCHITECTURE.md enforces ("Domain never references infrastructure").
  Fix: move to `Spacevors.Game`, matching `ThrusterFlameRenderer`.
  **Fixed** — `Renderer.cs:5` declares `namespace Spacevors.Game`.

- **[architecture] `Renderer.cs` (1052 lines) — one file, four responsibilities.**
  Scene rendering, HUD (health bar), three menu screens (upgrade/engine/loadout/ship cards), text wrapping, and label copy all live in one static class. Breaches "keep files reasonably small" / one responsibility per type.
  Fix: split along existing seams — scene draws, HUD, card menus (layout + draw together so hit-test rects can't desync).
  **Fixed** — `Renderer.cs` is now a 69-line orchestrator; drawing split into `BackgroundRenderer`, `WorldRenderer`, `EnemyShipRenderer`, `ShipSpriteRenderer`, `HudRenderer`, `UpgradeMenuRenderer` (+ `RenderHelpers`). Split documented in ARCHITECTURE.md.

- **[architecture] `Renderer.cs:688-689` — input read inside the renderer.**
  `DrawUpgradeCards` calls `GetMouseX/Y` for hover state. Input belongs in the app/input layer; rendering should receive state, not poll devices.
   Fix: pass hovered-card index (or mouse pos) into `DrawUpgradeCards`.
   **Fixed** — hover computed in the pause branch of `SpaceVorsApp.Main` (mouse + `GetUpgradeCardRect` loop → `hoveredIndex`, reused for click selection) and passed through `Renderer.RenderUpgradePause` into `UpgradeMenuRenderer.Draw(windowWidth, windowHeight, options, playerLevel, hoveredIndex)`; mouse polling removed from the renderer.

- **[doc-drift] no render interpolation despite ARCHITECTURE.md:124.**
  "Rendering interpolates if necessary" is not implemented — the renderer reads post-tick component state. Fine at a locked 120 Hz, but on high-refresh displays some frames show duplicate state and the documented design doesn't match code.
   Fix: implement prev/current interpolation or correct ARCHITECTURE.md (prefer fixing the doc if interpolation isn't wanted).
   **Fixed** — ARCHITECTURE.md now states rendering draws the latest simulated state with no interpolation between ticks, matching the fixed-timestep code.

- **[robustness] `ImageLoader.cs:28` — relative asset paths depend on CWD.**
  `"assets/..."` resolves against the working directory; running from anywhere else silently loses all textures and every fallback kicks in with no warning.
  Fix: resolve against `AppContext.BaseDirectory` (or exe dir) once at startup.
   **Fixed** — all asset paths resolve through `AssetPath` against `AppContext.BaseDirectory` (ImageLoader.cs:48), and Game.csproj copies `assets/` to the output directory (`PreserveNewest`) so the built binary finds them; CWD no longer matters for texture loading.

- **[design] enemy lit-sprite pipeline is half-built.**
  `EnemyShipLitSprites` is loaded/unloaded (ImageLoader.cs:23,50,124-125) but `DrawEnemyShips` only ever uses flat textures, and no enemy `-texture/-normals/-depth` assets exist. Also `ship-test-1.png` loads into VRAM but is never drawn (lookup keys are ship names).
  Fix: either wire lit sprites into `DrawEnemyShips` (feature) or delete the dormant branch; remove/rename the test asset.
   **Fixed** — lit pipeline wired into `EnemyShipRenderer.Draw` via `Lighting.TryDraw` (EnemyShipRenderer.cs:30-68), all three enemy types have `-texture/-normal(s)/-depth` assets in `assets/enemy-ships/`, and the unused `assets/player-ships/ship-test-1.png` was deleted.

### Nit

- **`Renderer.cs:27`** — "GAME OVER" centered via magic `-80` offset instead of `MeasureText`, unlike every other text call.
   **Fixed** — centered via `MeasureText` like every other text call (HudRenderer.cs:30-35).

- **`Renderer.cs:704-715`** — `GetUpgradeCardRect` divides by `optionCount` unguarded; callers happen to be safe, the public API isn't.
   **Fixed** — returns an empty rect for `optionCount <= 0` before any division (UpgradeMenuRenderer.cs:45).

- **`SpaceVorsApp.cs:125, 324`** — diagnostics env var re-read every frame in two places; `DiagnosticLogger` already caches it (`_enabled`). Cache once at startup too.
   **Fixed** — read once into a local in `SpaceVorsApp.Main` (SpaceVorsApp.cs:24); both per-frame reads removed. (`GameInitializer.cs:31` still reads it, but only once per game start.)

- **`Renderer.cs:397-398`** — `DrawEnemyShips` recomputes `cx/cy` already computed as `screenCx/screenCy` at :353-354.
   **Fixed** — turret rect and diagnostics circle reuse `screenCx/screenCy` (EnemyShipRenderer.cs:75-84).

- **`Lighting.cs:80-84`** — shader handle leaked if compilation fails (returns without unload). One-time, trivial; unload before returning.
   **Fixed** — `UnloadShader` called before returning on an invalid shader (Lighting.cs:140-145).

- **[dead] `GameInitializer.cs:185` + `SpaceVorsApp.cs:86`** — `turretEntities` is built and returned but never read. Drop it from the tuple.
  **Fixed** — `Initialize` returns `(em, playerEntity, cameraEntity, stars, clutter)` (GameInitializer.cs:13).

- **[dead] `GameplayComponents.cs:103-104`** — `ShipType.NoseLength`/`WingSpread` set for all four ships, never read (legacy of procedural ship drawing). Remove fields.
  **Fixed** — fields removed from the `ShipType` record (GameplayComponents.cs:129-140).

- **Per-entity second lookup in draw loops** — each `Draw*` method calls `GetComponent<Position>(entity)` per entity after a single-component query; multi-component queries (`<Asteroid, Position>`) would halve lookups. Relevant to the 10k-object goal but measure first (AGENTS.md: optimize only after measuring).
  **Open (deferred)** — asteroid/turret loops now use multi-component queries for their own components, but `Position` is still fetched separately in every `Draw*` loop; left as-is pending measurement.

## Post-review findings (added 2026-08-21)

Issues introduced by graphics work after the original review (lit ships/asteroids, point lights, turn flames, ship selection screen). Scope additions: `ShipSelectScreen.cs`, `LightGatherer.cs`.

### Minor

- **[design] `ThrusterFlameRenderer.cs:22-24, 115-153` — turn flames estimated from render-time angle deltas.**
  Static `PrevAngles` dict + `Raylib.GetTime()` dt means the renderer holds per-entity state across frames and flame intensity depends on render frame timing (varies with refresh rate / dropped frames), not simulation. The domain already maintains an authoritative `AngularVelocity` for both player and enemy ships.
   Fix: read `AngularVelocity` in `DrawTurnFlame`; delete `PrevAngles`, `PruneStaleKeys`, and the dt tracking.
   **Fixed** — `DrawTurnFlame` reads `AngularVelocity` (intensity = clamp(|angVel| / max turn rate), side from its sign); `PrevAngles`, `StaleKeys`, render-time dt, `Reset()`, `PruneStaleKeys`, and `NormalizeAngle` all deleted.

- **[consistency] `ThrusterFlameRenderer.cs:80` vs `LightGatherer.cs:52-53` — side/back thrust normalized by different max forces.**
   The flame renderer divides all player flames (fwd, back, side) by `Thrust * Boost`; the light gatherer uses `max(Thrust*Boost, BackThrust)` for main and `SideThrust` for side. On every current ship retro/side flames are pinned at minimum visible intensity (e.g. Scout 80/1000 = 0.08 → clamped to 0.2), and a retroburn shows a flame but no point light (0.08 < `MinThrustLightRatio` 0.1).
   Fix: normalize by per-direction max force (forward `Thrust*Boost`, backward `BackThrust`, side `SideThrust`) in both places.
   **Fixed** — all player thrust directions are normalized by one common reference, `Player.MaxThrustForce` = max of the three thruster powers, used identically by `ThrusterFlameRenderer.DrawPlayerThrustFlames` and `LightGatherer.CollectThrustLights`. Note: a first pass normalized each direction by its *own* max force, which made full retro/side thrust render at maximum flame size — wrong, since weak RCS thrusters must look smaller than the main engine unless upgraded past it. The common reference keeps flames and lights consistent and scales correctly as upgrades raise side/back power above main.

- **[maintainability] `WorldRenderer.cs:116` + `LightGatherer.cs:32` — explosion growth formula duplicated.**
   Both compute `Radius * (1 + (1 - lifeRatio))`. New instance of the "domain data encoded twice" pattern flagged for ammo colors; change one side and light radius desyncs from the visible fireball.
   Fix: single source (helper or method on `Explosion`).
   **Fixed** — `Explosion.CurrentRadius` property (EffectComponents.cs) is the single source; both `WorldRenderer.DrawExplosions` and `LightGatherer.CollectExplosionLights` use it.

### Nit

- **F11/F12 handling duplicated in both screens.** Keep it in one place.
   **Fixed** — single `HandleGlobalKeys()` helper (SpaceVorsApp.cs) called from the top of *both* loops: the outer loop covers the ship-select screen, the inner loop covers gameplay/pause. Note: an earlier pass moved the checks to the outer loop only, which silently disabled F11/F12 during gameplay (the outer loop doesn't iterate while the inner loop runs); verified by screenshot test in all three states.
- **Stale `Acceleration` during upgrade pause** — input/sim stop but the last-added component remains, so flames + point lights keep rendering on a frozen ship behind the menu overlay (visible through the 215-alpha dim). Decide: intentional frozen look, or clear acceleration when entering pause.
   **Fixed** — decided: clear-on-pause. On entering upgrade pause `SpaceVorsApp.Main` removes `Acceleration` and `AngularVelocity` from the player entity (`wasPaused` edge), so flames/lights stop behind the menu; input re-adds them on the first frame after resume. Verified by screenshot (no flame/light artifacts behind the overlay while thrusting at pause).

Not issues (verified): `PrevAngles` is safe from entity-ID reuse (`EntityManager._nextId` never reuses IDs), and the clockwise-winding comment at `ThrusterFlameRenderer.cs:184` matches raylib's backface culling behavior.

## External review findings (checked 2026-08-21)

### Major

- **[correctness] normal/depth map dimensions do not match their base texture.**
  Four sets mismatch: small-1 (base 574×524, maps 524×478), interceptor (945×926 vs 926×907), fighter (851×937 vs 930×1024), heavy (922×913 vs 1024×1014). The review claimed this misregisters lighting because the shader samples maps with the base texture's UVs.
  Investigation: all four pairs have matching aspect ratios (within 0.1%) and visually identical framing — they are different resolutions of the same crop. Both maps and base are sampled in normalized [0,1] UV space, so registration is correct; only map resolution differs. Rejecting these sets (the review's primary recommendation) would disable working lighting on 4 of 13 lit sprites — a regression.
   Fix: warn at load time instead, so genuinely differently-framed maps are diagnosable rather than silently misregistering. Regenerating the four mismatched maps at base dimensions is the remaining asset-side cleanup (warnings will then stop firing).
   **Fixed** — `ImageLoader.LoadLitSet` logs via `DiagnosticLogger.LogWarning` on a size mismatch; no rejection/fallback.

## Suggested order of work (remaining, updated 2026-08-21)

Done: all original items, plus ImageLoader robustness + `AppContext.BaseDirectory` paths, `ship-test-1.png` deletion, `Lighting.Init` shader unload, `DrawMines` single check (double-circle fallback kept), the nit batch (GAME OVER `MeasureText`, `GetUpgradeCardRect` guard, EnemyShipRenderer cx/cy reuse, diagnostics env var cached at startup, F11/F12 deduped to one place — later corrected to both loops via `HandleGlobalKeys()`), and all five post-review items:
1. Explosion growth formula → single source `Explosion.CurrentRadius`
2. Stale `Acceleration` during upgrade pause → clear-on-pause (remove `Acceleration` + `AngularVelocity`)
3. ThrusterFlameRenderer cleanup → turn flames from `AngularVelocity`, static state deleted, all thrust directions normalized by common `Player.MaxThrustForce` reference in renderer and LightGatherer
4. Upgrade menu input → `hoveredIndex` computed in SpaceVorsApp and passed into the renderer (mouse polling removed)
5. ARCHITECTURE.md:124 corrected to match fixed-timestep rendering

Remaining, smallest first:

1. Per-entity `Position` second lookup in draw loops (multi-component queries) — last; measure first per AGENTS.md, only do it if profiling shows it matters for the 10k-object goal
