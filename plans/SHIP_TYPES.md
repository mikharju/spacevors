# Ship types

Status: shipped (verified 2026-10-31)

Ship types have:
- One engine layout
- Set of initial turrets
- Max hp
- Weapon slots
- Graphics definition
- Model name
- Description

At game start, player must select one ship to fly during the game.


# Implementation phases:

## Phase 1 Ship record

Starting ships:
- Scout: balanced engines, 8 hp, side shotguns, green thin triangle
- Fighter: Pursuit engines, 10 hp, machinegun, medium blue triangle
- Heavy: new slow engines, 20 hp, side shotguns and machinegun, large fat red triangle

## Phase 2 Weapon specific upgrades

Add weapon slots to the ships: heavy gets 3, scout 1, fighter 2

Weapons get their own upgrade stats
- auto targeting range for weapons which do autotargeting 15 %
- range (how long shots live) 15 %

New weapon rail gun that shoots a fast powerful shot forward
- no auto targeting
- long cooldown 1,5 sec
- more kick back then other weapons
- slightly bright blue bullet (1,5 x size of machine gun bullet)

New weapon twin chain gun
- two side turrets firing straight forward only
- low scatter, fast bullet speed, fast fire rate, 
- no auto targeting, fires constantly
- upgrades: rate of fire, range, bullet speed

New weapon acid bubble spray
- high scatter
- high rate of fire
- low range
- one turret fires forward
- large green globs as bullets (2x size of machine gun bullet)
- no autotargeting, fires constantly

Point defence turret
- Like a chain gun, but firing arc is 270 degrees pointing backwards
- slower projectile speed and fire rate than machine gun
- has autotargeting

# Phase 3 minor upgrade vs new weapon

Upgrades will now be selected at each level as follows:

Levels 1-4 minor upgrade
- Hp upgrade 2 points
- 3 random weapon upgrades for any weapon carried by player ship currently
- 1 random engine upgrade

Level 5 new weapon
- Hp upgrade 5 points
- 2 Random weapons

Weapons already carried may appear too. They will upgrade damage of the duplicated weapon and won't occupy a new slot. 
New weapons will occupy a weapon slot on the ship.

After level 5, the same upgrade cycle will repeat, so four minor upgrades, then weapon upgrade and so on.

## Phase 4 Engine upgrades

Engine upgrades:
- Forward acceleration 10%
- Faster turning 10%
- Faster side thrust 10%
- Faster backwards thrust 10%

## Phase 5 Upgrades defined with weapon types

WeaponTypes should include definition of what upgrades they can get.

Each possible upgrade should have:
- name
- list of upgrade changes consisting of: what stat can change and by how much and is it additive or multiplicative

Weapon type can have many different minor upgrade options and one major upgrade option which is applied if player chooses an existing weapon type
in new weapon upgrade.

When one of these weapon types is equipped on the player ship, it's upgrades are available to be randomly selected as one upgrade choice during minor level up.
## Follow-up (shipped, verified 2026-10-31) — Falcon hull


- New player hull, no new machinery: `assets/player-ships/falcon-{texture,normals,depth}.png` (a 1024² lit
  set, matched by `LitSpriteMatcher` and drawn by the existing `ShipSpriteRenderer`), plus data —
  `ShipType.Falcon` (`PlayerShipStats`, card 5), `TurretLayouts.Falcon`, `PlayerHullShapes.Falcon`.
- A deliberately slim hull: 1.01 R² of paint against the other four's 2.2-3.0, and only 1.22 R across against
  their ~2 R. `Radius` is therefore **74**, not the mediums' 58: at 58 the paint measured 71 px across and
  97 px long and the hull read clearly smaller in play, while at 74 it measures 90 × 124 px — the Fighter's
  length is 123 px, so the two hulls now stand the same size on screen even though this one is a third
  narrower across. Its longest axis was always in the medium band (Scout 92, Shadow 108, Fighter 123,
  Heavy 165); matching the *width* too would need R 94, longer than the Heavy, which is not what a
  slim-hulled ship should do to mass, spawn clearance and explosion radii.
- Shape: 6 capsules, 1.11 R² (6.1k px² at R 74, against the Fighter's 9.0k), reach 0.95 R, 90% of the paint
  covered with 0.20 R² (18% of the shape) over air — outside the 8-14% of the solid hulls, because its two
  flank pods are detached from the body and the one bar that joins them has to cross the see-through gaps
  between them. The bounding circle over-reaches by more than on any other hull (17.1k px² against a shape
  of 6.1k): that is what the grid and every circle reject in front of a shape test read, so it costs extra
  narrow-phase candidates, never extra hits.
- Mounts: main gun 60° ahead, plus one small mount on each flank pod (±37 px, painted at ±0.50 R) sweeping
  **180° each**. The pair covers the whole circle, so this hull has no aft mount at all — the Fighter's 270°
  `small_back` is simply gone. These are the widest small mounts in the game: a 400 px/s round in one can
  lead 400 px/s of cross-line drift where the Fighter's 45° side mounts manage 153 (plans/WEAPON_BALANCE.md
  open knob).
- Starts alone with `WeaponLoadout.InMainGun(MachineGun)` — `MachineGun` was the only tier-1 Large gun in the
  table when the hull landed (`RailGun`, `GravitonBurst` and the two lances are tier 2, `HavocTorpedo` tier
  3), so the Falcon's single big gun is the one Fighter and Heavy already carry. 12 DPS at the start against
  Fighter 22.5 and Heavy 45, paid for with the smallest hurtbox in the game and two free 180° mounts for the
  first pickup. (ScatterCannon, added below, is the second tier-1 Large gun.)


## Follow-up (shipped, verified 2026-10-31) — Eagle, Manta, Raptor hulls and the Streak/Carmine fleet


- Three player hulls and two enemy hulls, all pure data on the existing machinery: lit 1024² sets in
  `assets/{player,enemy}-ships/`, `ShipType` entries (cards 6-8), `TurretLayouts`, `PlayerHullShapes`,
  `EnemyShipType` entries (graphics ids 3-4) and `HullShapes` entries.
- New engine layout `EngineLayout.Crab` (side thrust ≈ forward): the PLAN's "crab with better side ways
  acceleration", carried by the Manta.
- **Eagle** (R 66, 16 hp, Slow): the wide-delta gunship. 4 mounts — scatter cannon main, 150° mounts on
  the wings (±45 px, painted to ±0.93 R), and a 180° `small_back`: it trades turning and retreat for
  never having to aim past itself. Shape 87% covered / 23% air, reach 0.98 R — the swept-wing tax.
- **Manta** (R 74, 12 hp, Crab): the strafing hull — 1.57 R across, 1.13 R long, all three mounts
  pointing ahead (main 60°, two 60° mounts at ±30°): it aims by sliding sideways. Least paint in the
  game (0.77 R² against the 3.14 disc), so the bounding circle over-reaches hardest here; the shape
  covers 94% with one bar across the wing slab and a capsule down each swept leading edge.
- **Raptor** (R 78, 14 hp, Balanced): the broadside hull — two `heavy_left/heavy_right` mounts on the
  detached wing pods, 160° each; the main gun ahead covers the fore gap, ~nothing but a 20° sliver
  astern is dead. Starts with one heavy mount deliberately empty: the second big gun is the build.
- **Streak** (id 3, R 36, 5 hp, 115 px/s, ring 520, 2.0/s): the dart — fastest and flimsiest gunner,
  must cross the whole no-man's land to shoot, survives by out-flying shots. Shape 89/20, reach 0.89 R.
  Spawn weight 0.8.
- **Carmine** (id 4, R 62, 22 hp, 55 px/s, ring 640, 1.3/s): the line-breaker — marches the firing line
  inward and wins on volume, escorting the long-line heavies. Gun-only (R20: the missile ladder stays
  with the hulls that introduced it). Shape 92/17, reach 0.97 R. Spawn weight 0.6.
- New tier-1 large gun **ScatterCannon** (range 1000, 3 slugs × 2 dmg at 3/s, ±0.18 cone, 400 px/s):
  the Shotgun's big brother, the second starter main besides MachineGun. At range a lone hull walks
  between the spread slugs, so it pays for close-range DPS 18 (R3: ≥ 1.25 × FlakCurtain's 12) with reach
  the cone cannot keep — the (close, slow, burst, 3-pellet, Large) identity cell. Eagle carries it; the
  level-1 card pool gets a real main-gun choice. No tier-2 gun was demoted for this.
- R12 check (starter firepower by hull size): Scout 17 < Manta 21 ≈ Fighter 22.5 ≈ Raptor 22.5 < Eagle
  27 < Heavy 45. `AtMostOneHullStartsFullyBooked` still holds (only Shadow books all mounts).
- `HullShapesTest` / `PlayerHullShapesTest` / `ShipRosterTest` widened to the 8 hulls / 5 enemy art ids;
  `WeaponBalanceTest` passes the new gun through R1-R13 unchanged.
- Cost (`--sim --iters 3/5` medians, host=sim): `baseline-idle` 0.27 vs 0.28, `hull-stream` 0.81 vs
  0.85, `hull-crowding` 0.39 vs 0.38-0.39, `ammo-spray` 1.33 vs 1.35-1.42, `impact-storm` 2.18 vs
  2.21-2.23 — the scenarios pin `LoadTestWeapon` and the crowding ring hulls, so a roster change
  cannot move them; the one hot row re-ran at baseline. Keep.

