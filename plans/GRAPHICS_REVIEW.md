# GRAPHICS_REVIEW.md

Review of all graphics-related code (2026-08-18).

Scope: `src/Game/Renderer.cs`, `Lighting.cs`, `LitSprite.cs`, `LitSpriteMatcher.cs`, `ImageLoader.cs`, `ThrusterFlameRenderer.cs`, plus graphics-relevant parts of `SpaceVorsApp.cs` and `GameInitializer.cs`.

Overall the system is clean (off-screen culling via `IsOffScreen`/`HalfDiagonal`, defensive `Id != 0` texture checks, testable `LitSpriteMatcher`, shader fallback design). Findings below.

## Findings

### Major

- **[correctness] `Renderer.cs:108` — far starfield layer is invisible.**
  `DrawStarfield` casts `(int)size`; layer 0 sizes are `[0.5, 1)` (GameInitializer.cs:148), so all 150 far stars draw with radius 0 and never appear. The whole parallax depth cue of the background is missing.
  Fix: clamp to a minimum visible size (`Math.Max(1f, size)`) or drop the cast if `DrawCircle` takes float.

- **[perf/design] `SpaceVorsApp.cs:74-78, 244-248, 327-331` — FPS cap does not actually cap at 120.**
  The three duplicated `Thread.Sleep` blocks only pad *render* time (sim + input run before the timer starts) and truncate to whole ms, so real frame rate wanders ~120–133 fps. Also contradicts the `MaxFps = 120` constant's intent.
  Fix: call `Raylib.SetTargetFps(MaxFps)` once after `InitWindow` and delete all three sleep blocks (net code deletion).

- **[dead] `Renderer.cs:177-218` — second `DrawAsteroids` loop is unreachable.**
  It draws asteroids without a `Rotation` component, but `AsteroidFactory.AddAsteroidComponents` always adds one (AsteroidFactory.cs:15). ~40 lines of duplicated texture/fallback draw logic that can never run.
  Fix: delete the second loop and the `HasComponent<Rotation>` skip in it; keep only the rotating path.

- **[dead] `Renderer.cs:717-741, 849-944` — engine/loadout card UI has no callers.**
  `DrawEngineCards`, `DrawLoadoutCards`, `GetEngineCardRect`, `GetLoadoutCardRect` are never invoked; the current flow only uses ship cards (`DrawShipCards`). ~100 lines. They also duplicate layout math in two places (rect getters vs draw methods), a desync hazard if ever revived.
  Fix: delete all four; re-add from git history if the engine/loadout selection screens come back.

### Minor

- **[design] `Renderer.cs:240-251` — ammo color inferred from magic radius thresholds.**
  `DrawAmmo` maps `Radius >= 5f` → green, `>= 3.7f` → blue, silently mirroring `GetAmmoRadius` (TurretFiringSystem.cs:368: 5.0 / 3.75 / 2.5). A new weapon with an intermediate radius gets the wrong color with no error; the renderer encodes domain data.
  Fix: carry a color or kind on the `Ammo` component (set at spawn) and draw from it.

- **[maintainability] `Renderer.cs:620, 660` — effect lifetimes hardcoded in two places.**
  `DrawGreenSparks` divides by `0.6f` and `DrawDebugMarkers` by `0.5f`, duplicating spawn values (PickupMagnetSystem.cs:146, TurretFiringSystem.cs:31). `Spark` already has an `InitialLifetime` field; `GreenSpark`/`DebugMarker` don't. Change one side and `lifeRatio` can exceed 1 → alpha > 255 wraps to transparent.
  Fix: add `InitialLifetime` to both components (as `Spark` has) and use it in the renderer.

- **[clarity] `Renderer.cs:524-573` — `DrawMines` checks texture presence three different ways.**
  `mineTex.HasValue && Id != 0` for extent, re-fetch from `ImageLoader.MineTexture` inside the branch, then a trailing `if (!HasValue || Id == 0)` that draws an extra core circle over the fallback disc. Verify the double-circle fallback is intentional; either way collapse to one check and reuse the local.

- **[robustness] `ImageLoader.cs:28 vs 61` — inconsistent failure behavior.**
  Missing ship directories degrade gracefully (`Directory.Exists`), but a missing `asteroids/` or `mines/` directory throws from `GetFiles`. Failed asteroid texture loads (Id==0) are also unhandled, unlike ships/mines — the extent math divides by `tex.Width` → 0.
  Fix: same `Directory.Exists` guard for all dirs; treat Id==0 asteroid textures like the other fallbacks.

- **[architecture] `Renderer.cs:7` — adapter in the Domain namespace.**
  `Renderer` declares `namespace Spacevors.Domain.Systems` but is a Raylib adapter living in `src/Game/`. Muddies the domain/adapter boundary ARCHITECTURE.md enforces ("Domain never references infrastructure").
  Fix: move to `Spacevors.Game`, matching `ThrusterFlameRenderer`.

- **[architecture] `Renderer.cs` (1052 lines) — one file, four responsibilities.**
  Scene rendering, HUD (health bar), three menu screens (upgrade/engine/loadout/ship cards), text wrapping, and label copy all live in one static class. Breaches "keep files reasonably small" / one responsibility per type.
  Fix: split along existing seams — scene draws, HUD, card menus (layout + draw together so hit-test rects can't desync).

- **[architecture] `Renderer.cs:688-689` — input read inside the renderer.**
  `DrawUpgradeCards` calls `GetMouseX/Y` for hover state. Input belongs in the app/input layer; rendering should receive state, not poll devices.
  Fix: pass hovered-card index (or mouse pos) into `DrawUpgradeCards`.

- **[doc-drift] no render interpolation despite ARCHITECTURE.md:124.**
  "Rendering interpolates if necessary" is not implemented — the renderer reads post-tick component state. Fine at a locked 120 Hz, but on high-refresh displays some frames show duplicate state and the documented design doesn't match code.
  Fix: implement prev/current interpolation or correct ARCHITECTURE.md (prefer fixing the doc if interpolation isn't wanted).

- **[robustness] `ImageLoader.cs:28` — relative asset paths depend on CWD.**
  `"assets/..."` resolves against the working directory; running from anywhere else silently loses all textures and every fallback kicks in with no warning.
  Fix: resolve against `AppContext.BaseDirectory` (or exe dir) once at startup.

- **[design] enemy lit-sprite pipeline is half-built.**
  `EnemyShipLitSprites` is loaded/unloaded (ImageLoader.cs:23,50,124-125) but `DrawEnemyShips` only ever uses flat textures, and no enemy `-texture/-normals/-depth` assets exist. Also `ship-test-1.png` loads into VRAM but is never drawn (lookup keys are ship names).
  Fix: either wire lit sprites into `DrawEnemyShips` (feature) or delete the dormant branch; remove/rename the test asset.

### Nit

- **`Renderer.cs:27`** — "GAME OVER" centered via magic `-80` offset instead of `MeasureText`, unlike every other text call.
- **`Renderer.cs:704-715`** — `GetUpgradeCardRect` divides by `optionCount` unguarded; callers happen to be safe, the public API isn't.
- **`SpaceVorsApp.cs:125, 324`** — diagnostics env var re-read every frame in two places; `DiagnosticLogger` already caches it (`_enabled`). Cache once at startup too.
- **`Renderer.cs:397-398`** — `DrawEnemyShips` recomputes `cx/cy` already computed as `screenCx/screenCy` at :353-354.
- **`Lighting.cs:80-84`** — shader handle leaked if compilation fails (returns without unload). One-time, trivial; unload before returning.
- **[dead] `GameInitializer.cs:185` + `SpaceVorsApp.cs:86`** — `turretEntities` is built and returned but never read. Drop it from the tuple.
- **[dead] `GameplayComponents.cs:103-104`** — `ShipType.NoseLength`/`WingSpread` set for all four ships, never read (legacy of procedural ship drawing). Remove fields.
- **Per-entity second lookup in draw loops** — each `Draw*` method calls `GetComponent<Position>(entity)` per entity after a single-component query; multi-component queries (`<Asteroid, Position>`) would halve lookups. Relevant to the 10k-object goal but measure first (AGENTS.md: optimize only after measuring).

## Suggested order of work

1. Star-layer visibility fix (one line)
2. `SetTargetFps` + delete sleep blocks (net deletion)
3. Dead code: asteroid second loop, engine/loadout cards, `turretEntities`, ship fields
4. Ammo color data-driven; effect `InitialLifetime` fields
5. Renderer split + namespace move (largest change, do last)
