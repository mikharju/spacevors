# GRAPHICS_REVIEW.md

Review of all graphics-related code (2026-08-18).

Scope: `src/Game/Renderer.cs`, `Lighting.cs`, `LitSprite.cs`, `LitSpriteMatcher.cs`, `ImageLoader.cs`, `ThrusterFlameRenderer.cs`, plus graphics-relevant parts of `SpaceVorsApp.cs` and `GameInitializer.cs`.

Overall the system is clean (off-screen culling via `IsOffScreen`/`HalfDiagonal`, defensive `Id != 0` texture checks, testable `LitSpriteMatcher`, shader fallback design). Findings below.

## Status (checked 2026-08-21)

Fixed: 20 · Open: 7 (incl. 4 post-review findings below)

Remaining work:
- Upgrade menu renderer still polls mouse for hover state (`UpgradeMenuRenderer.cs:29-30`)
- ARCHITECTURE.md:124 interpolation claim still neither implemented nor corrected
- Nit: per-entity second Position lookup in draw loops (deferred — measure first)
- Post-review findings: turn flames estimated from render-time deltas + static `PrevAngles` state; side/back flame vs light normalization mismatch; duplicated explosion growth formula; stale `Acceleration` during upgrade pause (see "Post-review findings")

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
  **Open** — `UpgradeMenuRenderer.Draw` still polls `Raylib.GetMouseX()/GetMouseY()` for hover (UpgradeMenuRenderer.cs:27-28).

- **[doc-drift] no render interpolation despite ARCHITECTURE.md:124.**
  "Rendering interpolates if necessary" is not implemented — the renderer reads post-tick component state. Fine at a locked 120 Hz, but on high-refresh displays some frames show duplicate state and the documented design doesn't match code.
  Fix: implement prev/current interpolation or correct ARCHITECTURE.md (prefer fixing the doc if interpolation isn't wanted).
  **Open** — ARCHITECTURE.md:124 still says "Rendering interpolates if necessary."; no interpolation in code.

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

- **[consistency] `ThrusterFlameRenderer.cs:80` vs `LightGatherer.cs:52-53` — side/back thrust normalized by different max forces.**
  The flame renderer divides all player flames (fwd, back, side) by `Thrust * Boost`; the light gatherer uses `max(Thrust*Boost, BackThrust)` for main and `SideThrust` for side. On every current ship retro/side flames are pinned at minimum visible intensity (e.g. Scout 80/1000 = 0.08 → clamped to 0.2), and a retroburn shows a flame but no point light (0.08 < `MinThrustLightRatio` 0.1).
  Fix: normalize by per-direction max force (forward `Thrust*Boost`, backward `BackThrust`, side `SideThrust`) in both places.

- **[maintainability] `WorldRenderer.cs:116` + `LightGatherer.cs:32` — explosion growth formula duplicated.**
  Both compute `Radius * (1 + (1 - lifeRatio))`. New instance of the "domain data encoded twice" pattern flagged for ammo colors; change one side and light radius desyncs from the visible fireball.
  Fix: single source (helper or method on `Explosion`).

### Nit

- **F11/F12 handling duplicated in both screens.** Keep it in one place.
  **Fixed** — handled once at the top of the outer loop in `SpaceVorsApp.Main` (SpaceVorsApp.cs:35-39); removed from `ShipSelectScreen.Update` and the gameplay loop.
- **Stale `Acceleration` during upgrade pause** — input/sim stop but the last-added component remains, so flames + point lights keep rendering on a frozen ship behind the menu overlay (visible through the 215-alpha dim). Decide: intentional frozen look, or clear acceleration when entering pause.

Not issues (verified): `PrevAngles` is safe from entity-ID reuse (`EntityManager._nextId` never reuses IDs), and the clockwise-winding comment at `ThrusterFlameRenderer.cs:184` matches raylib's backface culling behavior.

## Suggested order of work (remaining, updated 2026-08-21)

Done: all original items, plus ImageLoader robustness + `AppContext.BaseDirectory` paths, `ship-test-1.png` deletion, `Lighting.Init` shader unload, `DrawMines` single check (double-circle fallback kept), and the nit batch (GAME OVER `MeasureText`, `GetUpgradeCardRect` guard, EnemyShipRenderer cx/cy reuse, diagnostics env var cached at startup, F11/F12 deduped to one place).

Remaining, smallest first:

1. Explosion growth formula: single source shared by `WorldRenderer.DrawExplosions` and `LightGatherer.CollectExplosionLights`
2. Stale `Acceleration` during upgrade pause: decide frozen look vs clear-on-pause, then implement
3. ThrusterFlameRenderer cleanup: read `AngularVelocity` for turn flames (delete `PrevAngles`/`PruneStaleKeys`/render-time dt); normalize side/back flame intensity by per-direction max force to match LightGatherer
4. Upgrade menu input: pass hovered-card index (or mouse pos) into `UpgradeMenuRenderer.Draw` from SpaceVorsApp — the app already reads the mouse for click hit-testing, so hover state can be computed there too
5. Correct ARCHITECTURE.md:124 ("Rendering interpolates if necessary") to match actual fixed-timestep rendering — prefer fixing the doc over implementing interpolation
6. Per-entity `Position` second lookup in draw loops (multi-component queries) — last; measure first per AGENTS.md, only do it if profiling shows it matters for the 10k-object goal
