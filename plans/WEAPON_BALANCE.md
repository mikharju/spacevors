# WEAPON_BALANCE.md

Status: active (reviewed 2026-10-31; open knobs marked below)

Plan for a weapon balance pass: per-weapon identity, a DPS ordering rule (hitscan weakest), a
lifted range ladder, and a two-tier split — starter guns at level 1, slightly stronger guns only
on level-5 cards.

**Status: implemented** (2026-10-16, six commits: cone scatter, dead-card fixes, level-gated pool,
the ladder retune, InterceptorTurret + starter loadouts, `WeaponBalanceTest`). Deviations and the
rules that turned out unwritable are in §12. Earlier decisions on record: full re-tier of the
starter loadouts; PointDefenceTurret repurposed, not deleted; single-pellet `Scatter` made real;
**the whole range ladder lifted** (aura 600 / close 900 / mid 1350 / long 1600–1700); RailGun's
long-range identity carried by its 3× manual-target reach; ship firepower **scales with hull size**
instead of being normalized.

Source of truth: `src/Domain/Stats/WeaponStats.cs` (11 real weapons + `LoadTestWeapon`),
firing/targeting in `src/Domain/Combat/TurretFiringSystem.cs`, damage in `CollisionSystem.cs`,
cards in `LevelUpSystem.cs` + `UpgradeStats.cs`, upgrades applied in `GameSession.ApplyUpgrade`.

## 1. Spatial anchors (what a range number actually means here)

There is no world zoom — 1 world px = 1 screen px (`WorldRenderer` draws a ship at
`Radius * 2` px). So:

| Anchor | Value |
|---|---|
| Visible world half-extent (1920×1024 window) | 960 × 512 px, plus up to 480 px of mouse camera drift |
| Player hull radius (drawn diameter) | Scout 46 (92 px), Fighter/Shadow 58 (116 px), **Heavy 84 (168 px)** |
| Heavy turret offsets from hull centre | main 22 px, heavy sides 31 px, nose 38 px, back 34 px |
| Enemy gun engagement ring | `FiringRange 700 + player.Radius` → 746 px against Scout, **784 px against Heavy** |
| Enemy ammo speed | 160–200 px/s |
| Mine / pickup radii | mine 7.5–15, pickup 6–9, player pickup 120–230 |

Why today's 260 px / 220 px guns read as unusable: they *do* clear the hull (RippleCutter at 260 px
from the Heavy's main gun reaches ~198 px past its forward edge, 3.1× the hull radius) but they sit
**inside the enemy's own 700 px firing ring**, so a 220–260 px gun cannot answer what is shooting it.
The fix is a floor on range, not a DPS buff.

## 2. Range ladder (revision 2)

| Band | base range | what it buys |
|---|---|---|
| Aura | **600** | just inside the enemy ring: answers what is on you; ~3.6 Heavy hull radii |
| Close | **900** | just past the enemy ring; still inside the 960 px half-screen width |
| Mid | **1350** | past the visible half-width: you shoot at things drifting in |
| Long | **1600–1700** | clearly off the 512 px half-height; reach you pay DPS for |
| Sniper | **1500 base / 4500 manual** | RailGun only (3× click reach, ammo-capped at 5000) |

Shortest gun in the game goes from 220 px to 600 px, so the reach spread drops from 5.7×
(220 → 1260) to 2.8× (600 → 1700). Because every gun now reaches at least to the enemy ring, the
DPS premium that short-range guns used to need shrinks with it (§3 R2).

## 3. Balance rules (proposed invariants, each checkable in a test)

- **R1 — delivery ladder (the user's rule).** Inside one tier and slot size, raw DPS must rise as
  delivery gets less certain: `hitscan < fast projectile < slow projectile`. Quantified: a hitscan
  gun stays **≤ 0.8 ×** the raw DPS of the slowest-projectile peer in the same reach band, and
  hitscan gets no `ProjectileSpeed` card. Its payment is delivery certainty, not damage.
- **R2 — a penalty must be paid for.** Reach, travel time and cone inaccuracy are all penalties;
  raw DPS must compensate for them. Consequence: the gun with the *best* delivery and the *longest*
  reach in a tier must have the *lowest* raw DPS there (today it is Cyclotron: 4200 px/s ammo,
  longest range, and tied for the highest DPS in the game).
- **R3 — large mounts cost more** (projectile guns): ≥ 1.25 × a small-slot peer of the same tier and
  delivery class (large mounts: 1 on Scout/Fighter/Shadow, 3 on Heavy). Hitscan guns are exempt —
  R1 caps them from the other side.
- **R4 — tier 2 is *slightly* stronger.** Ceiling: ≤ 1.40 × the strongest tier-1 gun of the same
  slot class. Floor: it must beat its tier-1 peer by **+15% raw DPS** *or* by **+30% reach at equal
  DPS** (AcidBubbleSpray is a reach upgrade, not a damage upgrade).
- **R5 — per-shot damage is sized against target HP.** Mines are 2 HP and never scale, enemies are
  7 / 15 / 30 (×3.5 at tier 10), so a 15-damage round throws most of itself away on whatever the
  targeter happened to grab. Every gun that acquires targets on its own therefore stays at
  **≤ 4 damage per shot**, and only RailGun carries a big round (14). The plan assumed
  `AutoTarget = false` made a gun manual-only; it only turns **lead prediction** off, so the rule had
  to be written around damage instead of the flag — see §12.
- **R6 — the range on the card must be honest.**
  `AmmoSpeed × (1 − AmmoSpeedVariation/2) × ShotLifetime ≥ Range` (worst-case bullet), hitscan
  exempt (beam length = range). `ProjectileSpeed` cards then genuinely extend reach.
- **R7 — entity budget.** `FireRate × PelletCount ≤ 12` rounds/s per mount (today's max), and
  lifetime keeps live rounds bounded. Lifting ranges raises lifetimes, so this is the rule that
  stops the retune turning into a big entity-count increase.
- **R8 — kickback is one currency.** It is an instantaneous Δv in px/s. 25–30 px/s only on
  single-shot big guns (RailGun, LaserLance: a movement tool), and sustained
  `kick × FireRate ≤ ~0.12 × forward thrust` (~50 px/s²) so no stream becomes a thruster.
- **R9 — only two guns may slew slower than the hull.** Hull turn rates are 76 / 69 / 38 °/s
  (Balanced / Pursuit / Slow). RailGun 55°/s and PrismLance 70°/s stay below the Balanced hull as a
  deliberate heavy-gun downside; everything else slews at or above it, and one gun (the interceptor)
  owns the top of the table at 360°/s.
- **R10 — upgrade cards should be comparable.** Keep `Damage` flat +1 (it is a *flat DPS* card: it
  adds `FireRate` DPS), but design base damage so the relative jump lands at 15–35% — damage 1–4
  for streams, 12–15 only on the manual snipers. Filter the dead hitscan `ProjectileSpeed` card.
  Label multi-pellet `FireRate` cards honestly ("+1 shot", not "+15%").
- **R11 — TTK budget against the enemy roster.** Starter loadout: mine (2 HP) in one hit,
  Interceptor (7) in ≈1 s, Default (15) in 1.5–2.5 s, HeavyCannon (30) in 4–6 s. At tier 10 (HP
  ×3.5) the same target should take ≈1.5–2× longer, not 3.5× — DPS from cards plus extra mounts must
  slightly outrun `EnemyShipStats.HpMultiplier`.
- **R12 — firepower scales with hull size; size is a handicap.** A bigger hull dodges worse: enemy
  guns open fire at `700 + your radius` (784 px against Heavy vs 746 against Scout), enemy ammo at
  160–200 px/s reaches a wide hull sooner, pickup radius (230 vs 120) drags mines in, and kickback
  steering weakens with r³. HP (20 vs 8) only partly pays for that, so the big ship is *allowed* —
  and expected — to carry more firepower from the start: **Scout (46) < Fighter = Shadow (58) <
  Heavy (84)** in starter firepower, and Scout sits slightly below the medium hulls. Mount count
  already follows size (3 / 4 / 3 / 7).
- **R13 — one cell per gun.** Every weapon owns a unique combination of
  (reach band, delivery, burst/stream, slot) plus one signature stat.
- **R14 — colour is an identity channel.** Yellow = kinetic, Blue = precision energy, Violet =
  laser, Green = chemical, Cyan = particle. A player should read a loadout from the tracers alone.
  Orange is the guided channel (plans/MISSILES.md): an orange **plume** is one of yours steering at
  something, a red plume is one hunting you. The body is light grey on both sides — the shape says guided,
  the flame says whose.

### The guided-gun rules (added with missiles, plans/MISSILES.md)

- **R15 — certainty is paid for in damage.** A guided gun's DPS stays at or below 0.8× the cheapest
  non-guided peer in its reach band. A lock spends the shot, so the compensation the rest of the ladder
  gets — spread, lead, dodge — does not apply to it. (`GuidedGunDpsStaysUnderItsOwnBand`)
- **R16 — a guided round must be out-turnable by what it hunts.** Its turn radius stays wider than the turn
  radius of the tightest enemy hull at its max tier — measured at the speed it **launches** at
  (`AmmoSpeed × MissileLaunchFraction / HomingTurnRate`), the tight end of a flight that ramps up to
  `AmmoSpeed` and widens as it builds. Under that line a lock is a sentence rather than a weapon.
  (`GuidedRoundTurnsWiderThanAnythingItHunts`)
- **R17 — the guided-round budget.** One mount may put at most `WeaponStats.HomingRoundBudget` (4)
  guided rounds in flight per second (`FireRate × PelletCount ≤ 4`). Guided rounds live for seconds and
  cannot be dodged by luck, so they get a budget of their own instead of R7's 12 rounds/s.
  (`GuidedGunStaysInsideTheGuidedRoundBudget`)
- **R18 — volley growth is a privilege, not a default.** A `FireRate` card adds a shot only where the gun
  can honour it: ordinary volley guns always grow; a growing guided volley stops at the R17 budget and
  the card is withdrawn rather than relabelled; a gun with `SalvoGrows` off takes the card as a rate.
  (`OnlySomeGunsMayGrowTheirVolley`, `GuidedVolleyAtItsCeiling_IsNotOfferedAnotherShot`)
- **R19 — a guided round is a body, and a shootable one.** It carries 1-3 hp; one `MissileHealth` card
  buys the whole mount +1 hp on the rounds it fires next, and the card is withdrawn at the ceiling. Only
  the other side's fire can hit it, so a missile never eats its own side's DPS and never collides with
  itself. (`MissileCollisionTests`, `WeaponStats.CanToughenRound`)
- **R20 — the enemy armament ladder only adds.** A hull never loses an armament as the composition tier
  climbs, its rack share never falls, and the small swarmer never carries a rack at all.
  (`EveryRungOfTheLadderAddsToTheOneBelowIt`, `TheSwarmerNeverCarriesARack`)
- **R21 — an enemy rack's reach stays honest.** It launches from farther than the hull's guns fire
  (× 1.4), but no farther than 1450 px — under the player's longest gun (1700) — and its fuse must cross
  its own launch ring at that tier. (`ARackOutreachesItsOwnGunsButNotThePlayersLongestGun`,
  `ARoundsFuseCrossesItsOwnLaunchRing`)
- **R22 — enemy guided rounds in the air are a ladder, not a flow.** 0 → 3 → 4 → 5 → 6 → 7 at
  composition tiers 3 / 8 / 10 / 14 / 20: one round at a time, never two rungs within two minutes, and
  none at all before three minutes of guns. (`TheRoundsAllowedInTheAirClimbOneStepAtATime`)

## 4. Identity matrix

| Gun | tier | slot | band (range) | delivery | rate | signature |
|---|---|---|---|---|---|---|
| Cyclotron | 1 | S | long (1600) | fast projectile (4200) | stream | needle (1.5 px), longest *auto* reach, no kick |
| MachineGun | 1 | L | mid (1350) | medium projectile (600) | stream | chunky chewer, kick you can lean on |
| ScatterCannon | 1 | L | close (1000) | slow projectile (400) | burst ×3 | the big shotgun: a cone of fat slugs that only holds together up close |
| InterceptorTurret (ex-PointDefence) | 1 | S | close (900) | medium projectile (1200) | burst ×2 | fastest mount 360°/s — tracks what circles you |
| Shotgun | 1 | S | close (900) | slow projectile (400) | burst ×3 | real cone, point-and-hold, no precision |
| FlakCurtain | 1 | S | aura (600) | slow projectile (500) | stream | 18-px disc aura that reaches into the enemy ring |
| RippleCutter | 1 | S | aura (600) | **hitscan** | burst | instant sweep, no lead, 32 px wide |
| RailGun | 2 | L | sniper (1500 / 4500) | fast projectile (2500) | volley | manual-only 14-damage delete button, slow mount 55°/s |
| LaserLance | 2 | L | long (1650) | **hitscan** | volley | instant beam, outranges every enemy gun |
| PrismLance | 2 | L | mid (1350) | **hitscan** | volley ×3 | three beams fanned over 140 px — the multi-target gun |
| GravitonBurst | 2 | L | long (1700) | fast projectile (3000) | volley ×5 | wall of 5; biggest number, best against big hulls |
| AcidBubbleSpray | 2 | S | close (900) | slow projectile (400) | stream | ±26° corrosive cone that covers an area, not a target |

Beams share the hitscan tax *and* an extra downside worth keeping: a beam damages the first thing it
touches, once, so an asteroid field eats a much bigger share of a one-entity volley than of a
12-round stream.

## 5. Proposed stats (current → proposed)

Tier 1 (starter guns; on hulls at level 1, still offerable on later cards):

| Gun | change | raw DPS | reach (auto / click) | R-check |
|---|---|---|---|---|
| Cyclotron | FireRate 12 → **8**, Range 1360 → **1600** | 12 → **8** | 1600 / 2520 | R2: best delivery + longest reach ⇒ lowest DPS in its tier |
| MachineGun | Damage 1 → **2**, FireRate 8 → **6**, AmmoSpeed 420 → **600**, Range 1260 → **1350**, kick 10 → **8**, AmmoRadius 2.5 → **3** | 8 → **12** | 1350 / 1800 | R3, R6, R8 |
| InterceptorTurret | AmmoSpeed 300 → **1200**, FireRate 5 → **4.5**, PelletCount 1 → **2**, Scatter 0.05 → **0.2**, Range 680 → **900**, TurnSpeed 220 → **360°/s** | 5 → **9** | 900 / 2700 | R9 (fastest mount); multi-pellet so `FireRate` cards add a 3rd barrel |
| Shotgun | FireRate 2 → **3.5**, AmmoSpeed 350 → **400**, Scatter 0.04 → **0.12**, Range 860 → **900** | 6 → **10.5** | 900 / 1200 | R1 (slow ammo pays least), R6 |
| FlakCurtain | FireRate 10 → **12**, AmmoSpeed 220 → **500**, ShotLifetime 1 → **1.5**, Range 220 → **600**, Scatter 0.5 (now a real cone) | 10 → **12** | 600 / 750 | R6 (500 × 0.85 × 1.5 = 637 ≥ 600), R7 (=12 rounds/s) |
| RippleCutter | Damage 6 → **4**, FireRate 1.5 → **2**, Range 260 → **600** | 9 → **8** | 600 (beam = range) | R1: 8 ≤ 0.8 × FlakCurtain's 12 = 9.6 |

Tier 2 (level-5 unlocks only):

| Gun | change | raw DPS | reach (auto / click) | R-check |
|---|---|---|---|---|
| RailGun | Damage 15 → **14**, FireRate 0.667 → **1**, Range 1200 → **1500** | 10 → **14** | 1500 / **4500** | R4, R5 (manual-only); only gun whose ammo (2500 × 2 = 5000 px) can actually fly its click reach |
| LaserLance | Range 1400 → **1650**, Damage 12 → **4**, FireRate 0.8 → **2.4**, kick 25 → **12** | 9.6 → **9.6** | 1650 (beam = range) | R1: 0.58 × GravitonBurst; R5: an auto-targeting gun stays at 4 per shot, so the lance became a fast cutter |
| PrismLance | Damage 5 → **4**, FireRate 0.6 → **0.7**, BeamSpacing 14 → **70**, kick 30 → **12**, Range 1200 → **1350** | 9 → **8.4** | 1350 | identity fix: at 14 px the three beams land on one hull; at 70 px they hit a formation |
| GravitonBurst | FireRate 1.2 → **1.1**, Range 1600 → **1700** | 18 → **16.5** | 1700 / 2400 | R4 ceiling: 1.375 × MachineGun; still the biggest number in the game |
| AcidBubbleSpray | ShotLifetime 1.2 → **3**, AmmoSpeed 200 → **400**, Range 650 → **900**, Scatter 0.4 → **0.45**, **AutoTarget false → true** | 12 → **12** | 900 / 1200 | R6 (400 × 0.85 × 3 = 1020 ≥ 900), R4 floor (equal DPS, +50% reach over FlakCurtain), R5 (1 dmg needs no manual aiming) |

Ladder check (R1 is per reach band, since the band is what the DPS pays for):

- Aura 600: RippleCutter **8** (hitscan) under FlakCurtain **12** (slowest ammo).
- Close 900: InterceptorTurret **9** (medium ammo) under Shotgun **10.5** (slow ammo).
- Mid 1350: PrismLance **8.4** (hitscan) under MachineGun **12**.
- Long: LaserLance **9.6** (hitscan) under GravitonBurst **16.5**; Cyclotron **8** is the cheapest
  delivery in the game and now has the lowest raw DPS of its tier.
- Sniper: RailGun **14** with 4500 px of click reach, nothing else comes close.

Rounds/s stay ≤ 12 per mount (R7); live-round estimate for the Heavy's four starter guns ≈ 100,
which is well inside the 10k-object goal but worth watching (the perf scenarios pin 5.5k bullets).

## 6. Tiers: initial guns vs level-5 guns

- Add `int UnlockLevel` to `WeaponType` (default `1`). Tier 1 guns = 1; RailGun, LaserLance,
  PrismLance, GravitonBurst, AcidBubbleSpray = `5`. An `int` (not an enum) so a level-10 third wave
  needs no new code later.
- `WeaponType.NewWeaponPool` becomes a lookup by level: `w.UnlockLevel <= newLevel` (LoadTestWeapon
  stays excluded). `LevelUpSystem.SpawnLevelUpChoice` already has `newLevel` at the call site, so
  `NewWeaponOptions` just takes one more argument.
- Level 5 is the first milestone (`MilestoneLevelInterval = 5`), and the hand offers 3 new-weapon
  cards — so the first new weapon a player can ever pick is tier 2. At level 10 the pool also
  re-offers tier-1 guns the ship never picked, which fills the empty small mounts.
- `ShipType.DiagnosticShips` keeps **every** weapon regardless of tier: that is the A/B bench.

Starter loadouts per R12 (firepower rises with hull size; mount geometry unchanged):

| Ship | hull | today (raw DPS) | proposed (raw DPS) |
|---|---|---|---|
| Scout | 46, 8 HP, 3 mounts | Cyclotron (12) | Cyclotron **main** + InterceptorTurret **small_left** (**17**), one small free |
| Fighter | 58, 10 HP, 4 mounts | LaserLance + Cyclotron (21.6) | MachineGun **main** + Shotgun **small_left** (**22.5**), two free |
| Shadow | 58, 10 HP, 3 mounts | GravitonBurst (**18**) | RippleCutter **×3** — main + both flanks (**24**) close-in brawler, and the one hull that starts fully booked |
| Heavy | 84, 20 HP, 7 mounts | MachineGun + 2× Shotgun + FlakCurtain (30) | same four guns (**45**), three mounts free |

Shadow loses the game's strongest gun and becomes a high-skill short-range hull (its guns now reach
600 px instead of 260, so that is finally playable). The Heavy is allowed to start far ahead:
45 raw DPS against a hull that cannot dodge, turns at 38°/s and gets engaged from 784 px.

## 7. Small mechanics changes these identities need

1. **Real cone for one-pellet guns** (confirmed): in `TurretFiringSystem.FireAtTarget`, when
   `PelletCount == 1 && Scatter > 0`, rotate the shot by a random angle in `±Scatter` using
   `view.Rng` (already seeded per run, so still deterministic; the existing `AmmoSpeedVariation`
   roll works the same way). Multi-pellet offsets stay exactly as they are. Cost: hit rate becomes
   luck-dependent; perf scenarios are unaffected (they seed `LoadTestWeapon` and silence turrets).
2. **Hitscan card filter**: `LevelUpSystem` must not offer `ProjectileSpeed` for a hitscan weapon
   (`AmmoSpeed 0 × 1.3` is a dead card).
3. **Honest `FireRate` card label**: `UpgradeMenuRenderer` prints "+15%" for multi-pellet guns that
   actually receive +1 pellet — print "+1 shot" there.
4. **Rename `PointDefenceTurret` → `InterceptorTurret`**: "point defence" promises anti-missile fire
   the sim does not have. The rename touches the static, `All`, the diagnostic roster and two or
   three test lines.

## 8. Out of scope for this pass (follow-ups, in rough order of value)

- **Mine HP does not scale** (`MineType.Health = 2`, fixed) while enemy HP grows ×3.5 — late runs
  are full of 2-HP pebbles that punish exactly the high-damage tier-2 guns. Either scale mine HP with
  the tier curve or accept snipers as anti-ship tools.
- **Long-range auto-fire at off-screen targets.** With mid/long bands past the half-viewport, the
  player will see kill effects they never saw the target of. Cheap knob if it feels wrong: only
  bracket/auto-target targets inside the viewport, keeping click-to-target as the way to reach
  farther. Not a stat change.
- **New-weapon cards do not prefer weapons that fit a free mount** — planned in PLAN.md ("most
  weapons offered must be of type fitting the free slot"), deliberately dropped in favour of cargo.
  Worth revisiting now that tier 2 is mostly large and the small ships have one large mount each.
- **No anti-projectile mechanic**, which is the only way `PointDefenceTurret` could have kept its
  original name.

## 9. Verification

- New deterministic test `WeaponBalanceTest` (pure data over `WeaponType.All`, no graphics):
  R1 (hitscan ≤ 0.8 × slowest-projectile peer in the same band), R3 (large ≥ 1.25 × small of the
  same tier and delivery class), R4 (tier-2 ceiling and floor), R5 (auto-target guns ≤ 4 damage per
  pellet), R6 (honest reach at worst-case `AmmoSpeedVariation`), R7 (`FireRate × PelletCount ≤ 12`),
  R13 (no duplicate identity cell). A test that reads the rules is what stops the next weapon from
  quietly breaking them. `LoadTestWeapon` is exempt from every rule (it is the perf scenario's
  8000-pellet spray, not a gun).
- Guided guns: `WeaponBalanceTest` also asserts R15 (certainty priced in damage), R16 (turn radius, measured
  at the launch end), R17 (guided-round budget) and R18 (volley growth), and `MissileGuidanceTest` covers
  what a lock actually does — turns onto it, builds to its top speed and stops there, straightens out when
  the lock dies, and leaves along the hull's nose at a target the slot's arc would have excluded. The enemy
  side of
  R19-R22 is asserted in `MissileCollisionTest`, `EnemyMissileFiringTest` and `EnemyMissileLadderTest`.
- Cone-scatter gets its own small test: one shot, seeded RNG, direction stays inside ±Scatter, and
  multi-pellet offsets are unchanged.
- Existing tests to update: `ShipRosterTest` (Shadow's gun, diagnostic roster after the rename),
  `LevelUpSlotOfferTest`, `CargoTest`, `TurretMountTest` (rename), `LaserTest` / `TurretFiringTest`
  where damage or fire-rate numbers are asserted.
- Perf: `Scenarios.cs` pins `LoadTestWeapon` with a fixed bullet count and silenced turrets, so the
  retune cannot move the baselines — but this pass raises ammo lifetimes (Flak 1 → 1.5 s, Acid
  1.2 → 3 s) and every gun fires for longer because ranges are bigger, so live round counts in real
  play go up. Run `ammo-spray` and `impact-storm` once
  (`.tools/scenario.sh ammo-spray --sim --iters 3`, host=sim) and report the median/p99 delta;
  expect no change in the scenarios themselves, and treat any movement as a signal that something
  other than data was touched.
- Playtest loop: `SPACEVORS_DIAGNOSTIC=1` for the per-weapon Shadow copies plus
  `SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun"` to force the builds worth comparing.

## 10. Suggested commit sequence (small, one concern each)

1. Cone scatter for one-pellet weapons + test.
2. Honest-reach fixes (AcidBubbleSpray lifetime, Flak/MachineGun ammo speeds) + hitscan
   `ProjectileSpeed` card filter + multi-pellet `FireRate` card label.
3. `UnlockLevel` on `WeaponType` + level-gated `NewWeaponPool` + offer test.
4. The range ladder and stat retune (§2, §5), data only.
5. Rename `PointDefenceTurret` → `InterceptorTurret` with its new stats, new starter loadouts,
   test updates.
6. `WeaponBalanceTest` invariants; PLAN.md + ARCHITECTURE.md notes.

## 11. Open knobs (playtest decides)

- Is the Scout too weak at 17 total DPS with 8 HP, or is that the correct price of a 46 px hull?
- Does the Heavy at 45 raw DPS feel right against the 784 px engagement ring, or should its free
  mounts be the place it earns the difference instead?
- PrismLance BeamSpacing 70 px: does a three-beam fan read clearly at 1350 px, or should the beams
  visibly diverge instead of staying parallel?

## 12. Implementation notes (what the code ended up doing differently)

- **`AutoTarget` is not manual targeting.** Its only read site is `TurretFiringSystem.FindTarget`,
  where it chooses `FindTargetWithoutPrediction` — the gun still auto-acquires, it just aims where
  the target is instead of where it will be. So R5's "high damage must be manual-only" could not be
  written; instead every self-targeting gun was cut to ≤ 4 damage per shot, which is why **LaserLance
  became 4 damage at 2.4 shots/s** (same 9.6 DPS, and it reads as a fast cutter now) while RailGun
  alone keeps a big round with prediction off. `AcidBubbleSpray` simply lost the flag, so its bubbles
  lead their target again. If manual-only snipers are ever wanted, that is a separate behaviour
  change in `FindTarget`, not a stat.
- **Two numbers moved to leave slack under the beam cap**: RippleCutter 2.25 → **2** shots/s (8 DPS)
  and PrismLance 0.75 → **0.7** (8.4 DPS). At the planned values both sat exactly on 0.8 × their
  band's benchmark, which any later tweak would break.
- **The identity cell needed a fifth axis.** RailGun and GravitonBurst both sit in (long, fast,
  volley, Large); the cell only became unique once it also carried pellets-per-shot (single vs 5).
- **Shadow starts fully booked**, which the old `EveryShipHasAtLeastOneFreeMountForANewWeapon` test
  forbade. With cargo a booked mount is a free swap, not a dead pick, so the test became
  `AtMostOneHullStartsFullyBooked` and Shadow is the one specialist that fills its three mounts.
- **The diagnostics bench now carries every gun** (11 Shadow copies + the load-test spray), which is
  how each retuned gun gets played in isolation regardless of its tier.
- **Measured**: the pinned scenarios cannot move (they use `LoadTestWeapon` with silenced turrets) and
  did not — `ammo-spray` sim median 1.33 ms against 1.31/1.33/1.36 on the previous commit,
  `impact-storm` 2.14 ms against 2.14–2.19. Live rounds in real play do move: the Heavy's starter set
  goes from ~52 to ~93 (rate × lifetime), which is the number R7 guards.
- **Missiles landed with three deviations from plans/MISSILES.md.** (a) The plan drafted its rules as
  R9-R16, which collided with the slew-rate, hull-size and identity rules already numbered here, so the
  guided rules are **R15-R22**; the plan file was renumbered to match. (b) The large hull's
  `GunsAndMissiles` rung reaches ~80 % of its spawns at composition tier 10 rather than the plan's
  "~100 %", so a bare heavy stays a rare sight instead of vanishing. (c) A rack launch skips the gun
  pass's ±15 % speed jitter, because on a guided round the speed *is* the turn radius and the fuse
  reach — jittering it would launch a different missile than the one the player just read. The rest of
  the launch plumbing (`AmmoSpawnOffset`, hull clearance, inherited velocity, `FiringArc`) is shared
  with the gun pass rather than copied.
- **Missiles got a throttle, an arc exemption and a longer body (2026-10-17).** A guided round now leaves at
  `MissileLaunchFraction` (0.7) of its card speed and builds to it at `MissileLaunchAcceleration`
  (200 px/s²) — so `AmmoSpeed` means the **top** speed, and the turn radius is tightest the moment the round
  leaves the rack, which is the end R16 measures at. The card speeds rose with it: SeekerPod 300 → **420**,
  Swarmlet 350 → **490**, HavocTorpedo 200 → **280** px/s, so a round that used to fly flat now finishes
  faster than it started. Launch radii 112 / 116 / 250 px and cruise radii 160 / 165 / 357 px all stay
  outside the ~92 px a tier-10 Interceptor turns at, and the guided round's speed is no longer rolled at all
  (the ±15 % variation is dumb ammo only), so those radii are facts rather than ranges.
  Guided mounts are gated by **range only** and do not slew: the round leaves along the hull's nose and
  guidance picks the lock up from there, which is what a rack graphic already said. The enemy rack keeps its
  cone and its aim, because a round launched backwards from a 980-1330 px ring cannot cross that ring inside
  a 6 s fuse — reach honesty (R21) is why the exemption is one-sided. Enemy rounds ramp too (launch = 0.7 ×
  the tier ladder, ceiling still D3's 250 px/s), and `EnemyShipStats.MissileReachFor` is the ramp-aware
  distance a fuse actually buys, which is what R21's test asserts. On screen the body draws 7.5 × the
  collision radius long with a fixed 5 px nose over a 25 px shaft: a torpedo dart measures ~45 px and ~240
  exact-colour pixels in a live frame, against ~15 px and ~65 before — the round read as a bright dot at the
  ranges missiles are fired across. Measured: `ammo-spray` 1.31-1.34 ms and `impact-storm` 2.19 ms against
  1.30 / 2.16 before the change; both scenarios are unarmed, so that spread is this host's noise band and the
  sim path for them is unchanged.

## Shipped — first weapon balance pass (verified 2026-10-31)


Plan: plans/WEAPON_BALANCE.md. Every gun now owns one cell of (range band, delivery class, fire rate, pellets per shot, mount size), and the DPS ladder prices delivery: a hitscan beam can never miss on the way, so it buys the least damage.

Range ladder, base auto-target range in px: **aura 600 → close 900 → mid 1350 → long 1600–1700**, with RailGun the one sniper whose click reach (3× base, capped by its own ammo at 5000 px) really flies 4500 px. The 600 px floor is not taste: enemy guns open fire at `700 + your hull radius`, so a shorter gun cannot answer what is shooting it — the old 220 and 260 px guns sat inside that ring.

Rules the data obeys, each asserted in `WeaponBalanceTest`: the range on a card is crossable by the slowest round that gun fires (`AmmoSpeed × 0.85 × ShotLifetime ≥ Range`); ≤ 12 rounds a second from one mount; a beam stays under 0.8× the slowest projectile gun of its band; a large gun out-DPSes the best small gun of its tier and class by ≥ 25%; tier 2 is ≤ 1.4× tier 1 of its size and must beat it in DPS **or** in reach; at most two guns slew slower than a Balanced hull turns; one gun per identity cell; starter firepower follows hull size (Scout 17 < Fighter 22.5 ≈ Shadow 24 < Heavy 45 raw DPS — a 168 px hull dodges worse than a 92 px one and gets engaged from 784 px instead of 746, so the big ship is allowed to start ahead).

Raw DPS (fire rate × pellets × damage). Tier 1: Cyclotron 8 (1600 px, 4200 px/s), RippleCutter 8, InterceptorTurret 9, Shotgun 10.5, MachineGun 12, FlakCurtain 12. Tier 2 (`UnlockLevel: 5`): PrismLance 8.4, LaserLance 9.6 at 1650 px instant, RailGun 14 (1500 / 4500 px), GravitonBurst 16.5, AcidBubbleSpray 12 (900 px cone).

- `Scatter` is now the half-angle of the cone a shot pattern covers: several pellets spread evenly across it, a lone pellet rolls randomly inside it (`view.Rng`, seeded). Before that the offset for `PelletCount == 1` was always 0, so Flak Curtain's 0.5 and Acid Bubble's 0.4 did nothing at all.
- New-weapon cards come from `WeaponType.NewWeaponPoolFor(level)`: tier-1 guns only until the first milestone hand, then the five tier-2 guns as well. `SPACEVORS_DIAG_UPGRADES` ignores tiers — a script is a bench — and so does `ShipType.DiagnosticShips`, which now carries one Shadow copy per gun.
- Dead and misleading cards fixed: `ProjectileSpeed` is never offered to a hitscan gun (it multiplied `AmmoSpeed 0` by 1.3), and a `FireRate` card on a multi-pellet gun says "+1 shot", which is what `ApplyUpgrade` does, instead of the "+15%" it promised.
- `PointDefenceTurret` became **InterceptorTurret**: a twin 2-pellet snap at 900 px on the fastest mount in the game (360°/s). "Point defence" promised anti-projectile fire the sim does not have, and the gun was dominated by two others.
- Starter loadouts: Scout = Cyclotron + InterceptorTurret, Fighter = MachineGun + Shotgun, Shadow = a Ripple Cutter in each of its three mounts, Heavy unchanged (machine gun, two side shotguns, flak disc). Shadow is the only hull that starts fully booked — cargo makes that a free swap, not a dead end — guarded by `AtMostOneHullStartsFullyBooked`.
- Naming trap found while writing the rules: `AutoTarget: false` (RailGun) turns off **lead prediction**, it does not make a gun manual-only. AcidBubbleSpray's flag is gone, so its bubbles lead their target again.
- Perf: the scenarios pin `LoadTestWeapon` and silence player turrets, so a balance pass cannot move them — measured anyway: `ammo-spray` sim median 1.33 ms against 1.31/1.33/1.36 on the previous commit, `impact-storm` 2.14 ms against 2.14–2.19. Free. What the scenarios do not see is live round count in real play: the Heavy's starter set goes from ~52 to ~93 live rounds (rate × lifetime) — still two orders under the 10k-object goal, and the number to watch when the next gun is added.


### Follow-up note (2026-10-31)

ScatterCannon (tier-1 Large, 3 slugs x 2 dmg, range 1000) landed with the Eagle hull; details in plans/SHIP_TYPES.md. It fills the (close, slow, burst, 3-pellet, Large) identity cell.
