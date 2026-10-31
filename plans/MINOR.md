# MINOR.md — small shipped features (combined record)

Status: record (entries verified 2026-10-31). Features too small for their own plan file.

## Camera follows mouse (shipped, verified 2026-10-31)


Camera drifts based on the mouse's screen offset from window center (`CameraSystem`):
- Mouse inside a dead zone (5% of half-viewport) → camera stays centered on player ship
- Drift ramps linearly to `MaxDriftFraction` (0.5) of the half-viewport at the window edge, so at full deflection the player sits a quarter-screen from the edge — always visible
- Corner offsets are normalized before mapping, so diagonal drift never exceeds edge drift
- The camera tracks the player directly (per-tick motion is already smooth); only the mouse-driven drift eases in at `DriftFollowSpeed` (1.5/s), so fast mouse moves don't yank the screen — the eased offset persists on the `Camera.Drift` component
- Mouse screen position flows into the simulation via `WorldView.MouseScreenPosition` (same seam as `ViewportSize`, set per tick in GameSession); aiming math is unchanged because it converts mouse→world through the camera target, so there is no feedback loop


## Shadow weapon test ships (shipped, verified 2026-10-31)


Shadow carries a real weapon in normal play; the load-test spray moved to a diagnostics-only Shadow copy, joining one copy per new weapon.

- Shadow = Graviton Burst (Balanced engines, 2 slots); `WeaponLoadout.MountedAsUpgrade(weapon)` builds a one-turret loadout from the weapon's own level-up mount
- `ShipType.DiagnosticShips`: Shadow hull, engines and graphics carrying Laser Lance, Prism Lance, Ripple Cutter, Cyclotron, Graviton Burst, Flak Curtain and LoadTestWeapon — one weapon each — appended to the select list only under `SPACEVORS_DIAGNOSTIC=1`
- 4 new tests (Shadow's weapon, clone roster and hull identity, distinct names, every clone fires in a started world)


## Render cost: shape batching (shipped, verified 2026-10-31)


- The baselines settled where the frame goes: ~**1.1 µs per drawn shape** (sparks, bullets, explosions — measured six times across three scenarios), i.e. the cost is per GL draw call, not per entity or pixel. `world.sparks` is 58% of `ammo-spray` render and 78% of `impact-storm`; sim is 6-11%.
- `Game/ShapeSprites.cs` draws those shapes as tinted quads of one generated disc via `DrawTexturePro`, staying in raylib's batched 2D path. An `Rlgl` vertex block (first attempt) submitted geometry that never appeared on screen, so the check for any render change is now the automatic start/mid/end frames plus a pixel diff against a known-good frame: the batched frame differs from the immediate one in 0.8% of pixels (streak caps and edge filtering).
- Sandbox pass effect: `world.sparks` 18.6 → 1.53 ms, `world.ammo` 13.4 → 0.60 ms, `world.explosions` 5.0 → 0.24 ms (llvmpipe). Re-take the real-GPU baselines to get the meaningful numbers.
- Scenario shots: `SPACEVORS_SCENARIO_SHOTS=start,mid,end` writes PNGs of those frames into the run dir, so a render change can be verified with one command instead of pressing F12 into an unfocused window.


## Rocks are cover, and the small ones break (shipped, verified 2026-10-31)


- **Guns ignore rocks unless one is clicked.** Auto-targeting now sees only what shoots back — enemy ships and mines. A rock becomes a target through a click, and then it is treated like any other primary target: 3× base range capped by what the ammo can physically reach, lead-predicted, reticled, and it keeps priority over closer enemies. `TurretFiringSystem` lost its asteroid last-resort target scan (and with it the generic `CheckTargets` helper); `PrimaryTargetPicker` gained an asteroid pass, where the click zone is the rock's own radius — no forgiveness multiplier, because the hull is already ship-sized and a wider zone would steal clicks meant for the space beside it. A destroyed rock is noticed by the same rule that drops a killed ship: its components are gone, so `PrimaryTarget` is cleared.
- **Small rocks are destructible, big ones are scenery.** `Asteroid.SmallHealth = 50`, given by `AsteroidFactory` to the 35–50 px rocks only. Having `Health` *is* the difference: a hit on a rock without it is just an obstacle hit, and no bookkeeping follows. Gunfire — bullets and beams — chips a small rock.
- **A breaking rock leaves smoke, not a fireball.** While it survives, each hit flashes the usual impact effect; the hit that ends it flashes nothing (`DamageAsteroid` reports whether the rock is still there, and the flash is skipped), and the resolution pass replaces it with `AsteroidFactory.SpawnBreakupSmoke`: 16 grey `SmokePuff`s scattered over the rock's own disc, inheriting 0.4 of its drift and jittering apart, 2.2 s lifetime, drawn by the existing smoke pass which grows them to about twice their radius as they fade. No `Explosion`, no staged `ShipDeathExplosion`, no loot, no XP.
- **Rocks are still broken by gunfire only.** Impact damage keeps skipping them, so the field cannot grind itself away as ships and rocks collide. Rocks are still never auto-bracketed: `AutoTargetMark` stays ships-and-mines, and a clicked rock shows the manual reticle instead.
- Tests: 6 in `AsteroidHealthTest` (damage tracked, break-up cloud, no fireball on the killing hit, puffs dissipate, big rock immune, ramming harmless, factory arms only small rocks) plus 4 targeting tests (a rock is ignored until clicked, a clicked rock is engaged beyond base range, a smashed rock loses priority, the picker clicks a rock and prefers a ship clicked over the rock behind it). `TurretFiringTest`, `TurretSlewTest`, `TurretTargetMarkTest`, `LaserTest`, `ScatterConeTest` and `ShipRosterTest` now click the rock they were firing at.
- Measured: `ammo-spray` sim median **1.29 ms** / p99 2.08 against 1.33 / 2.37, `impact-storm` **2.09 ms** / 4.35 against 2.14 / 4.96 — this costs ~0%, keep. The added work is one component lookup per rock hit; the per-tick health bookkeeping only runs for small rocks, and the killing hit now spawns fewer effects than the hits before it.
- Open knob: clearing a rock pays nothing — no XP, no loot — so it is pure DPS spent on cover. Whether rocks should drop something, or whether the reward is simply the shot lane you opened, is a playtest call.


## Auto-aim keeps its lead, and aims at the speed the round actually has (shipped, verified 2026-10-31)


- **The symptom, measured**: a Fighter moving at 150 px/s or more hit *nothing*. In a 5 s fight against four closing ships with the starter loadout, 0 of 71 rounds crossed a hull; parked, 16 did.
- **Answer to "does auto-aim account for the hull's movement?"** Yes, and correctly. `EvaluateMovingTarget` solves the intercept in the hull's frame (`relVel = target − hull`, `aimDir = (toPredicted − hull·t) / (ammoSpeed·t)`) and `FireAtTarget` adds the hull's velocity to the round, so shot and solution agree. Measured: a Cyclotron's shots pass 2-6 px from a stationary or closing target at every hull speed from 0 to 300 px/s, and so does any other gun's lead that its arc allows.
- **The bug**: `FindTargetWithPrediction` carried a second ship scan over `<EnemyShip, Position>`. A multi-component query means *at least* these components, not *exactly* these, so that scan re-saw every ship the lead pass had already looked at — and re-selected it with the lead discarded. It fired whenever the solution was rejected for any reason (barrel direction outside the mount's arc, the round unable to catch a fleeing target, intercept outside range), putting the shot exactly `hull velocity × flight time` off: 313 px at 200 px/s across a 700 px gap, 416 px at 300. That is the "slow shots like shotgun consistently miss while moving" report. `FindTargetWithoutPrediction` had the same duplicate scan — pure re-work for the guns that never lead.
- **The fix — two of them.** (1) Both duplicate scans are gone (−38 lines): a mount whose lead falls outside its arc now holds fire instead of spending the volley. (2) `TargetSelection` carries a `Led` flag and `FireAtTarget` re-solves the intercept **per round, at the speed that round actually leaves at and from the muzzle it leaves from**. The ±15% speed roll used to be drawn *after* the solution, so the round flew at a speed the aim was never solved for — up to ~4° of tilt on a slow led round (≈50 px at 700 px) — and the 20 px muzzle offset added `hullSpeed × 20 / ammoSpeed` on top (10 px for a shotgun at 200 px/s). One shared `InterceptTime` helper now feeds both the target search and the shot, replacing two copies of the same quadratic.
- **Measured, same fight (hits per 5 s)**: parked 16 → 15 → **18**; 150 px/s 0 → 16 → **18**; 250 px/s 0 → 17 → **19**; 400 px/s 0 → 17 → **20**. Rounds thrown drop from 70 to ~20 because the shotgun's mounts go quiet — see the open knob. Enemy turrets obey the same rule, so incoming fire is now as accurate as it was aimed: difficulty-visible, worth a playtest against the `[THREAT]` readout. Sim cost unchanged: `ammo-spray` sim median **1.30 ms** against 1.28/1.29, `impact-storm` **2.12** / 4.52 against 2.07–2.14 / 4.08–4.35 — the per-round solve runs only for rounds actually fired, and the scenarios seed their volleys instead.
- **Tests** (`AimLeadTest`, 6): a scatter-free 400 px/s gun in the Fighter's 45° left mount, a ship 700 px dead ahead, hull strafing at 0/100/200/300 px/s — no shot may pass more than 150 px from it (the discarded-lead shot was 328 px off at 200 and 431 px at 300), and where the lead fits inside the arc (100 px/s, 14.5°) the shot has to pass within **5 px** of a 20 px hull. Plus the arc's real meaning: a ship crossing 45° off the nose — outside the ±22.5° arc, and it does not reach the arc's edge for 0.6 s — **is** engaged, because the arc gates the firing solution and not the target's bearing. `TestEnemyAmmoInheritsShipVelocity` no longer hard-codes a lead direction (that was the nominal-speed solution, which the rolled speed invalidates); it now asserts the round's world line runs onto the player within 1 px whatever speed it rolled.
- **Open knob — the lead has to fit inside the arc, because the barrel is what carries the round.** The largest cross-line drift a mount can lead is `ammoSpeed × sin(halfArc)`: for a 400 px/s round (`Shotgun`, `AcidBubbleSpray`) in a 45° arc that is 153 px/s, against a hull with 400 px/s² of thrust and no drag. `MachineGun` 230, `InterceptorTurret` 459, `Cyclotron` 1608. The Fighter's side mounts are the only 45° small mounts in the game — Scout, Shadow and Heavy give small mounts 120°, which lifts a shotgun to 346 px/s. Options for a playtest call: widen the Fighter's side arcs to match the other hulls, lift the slow guns' ammo speed (the DPS ladder prices delivery, so faster rounds buy less damage), or accept that a mount goes quiet when it cannot point at the solution.



## Every volley keeps one round on the aim line (shipped, verified 2026-10-31)


- A fan of `N` pellets used offsets `(i - (N-1)/2) × Scatter`, so an even volley straddled the aim line:
  `InterceptorTurret` (2) and any Shotgun/ScatterCannon/Swarmlet grown past 3 to an even count put every
  round a little off and missed a small target at everything but touching range.
- `ScatterOffset` now fans **out from a centre round**: steps `0, -1, +1, -2, +2 …`, so round 0 always
  flies down the aim (or intercept) direction and the rest alternate to each side. Odd counts keep the
  exact same set of angles; even counts gain the centre round and widen their outer edge from
  `(N-1)/2` to `N/2` steps — a four-pellet Shotgun now reaches ±0.24 rad instead of ±0.18.
- `BeamSpacing` (PrismLance, grown to an even volley) uses the same steps, so one beam stays exactly on
  the aim line instead of straddling it at ±35 px.
- `ScatterConeTest` gained the even-count case: every volley has a round on the aim line, all rounds
  distinct, and a 4-pellet fan measured at `-0.4, -0.2, 0, +0.2`.
- Cost (`--sim --iters 3`, host=sim): `ammo-spray` median 1.35 vs 1.33, p99 2.26 vs 2.24;
  `impact-storm` 2.23/4.80 vs 2.18/4.62, entity counts unchanged. Noise — keep.
