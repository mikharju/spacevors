# WEAPON_IDENTITY_CELLS.md

Status: shipped (2026-11-02) — built as planned, with the deviations in `## Shipped` at the end; the
body below is the frozen plan and one of its numbers (Stiletto's DPS) did not survive contact with the
balance test. It fills the PLAN.md roadmap line "Fill empty weapon identity cells".

Three decisions, one plan:

1. **R23 — the player's ship carries at most one copy of any weapon type** (mounted *or* in cargo).
2. **Four new guns** fill the (band × delivery) intersections that are still empty.
3. **Two hulls re-armed**: Shadow (3 × RippleCutter) and Heavy (2 × Shotgun) are the only loadouts
   that start with duplicates, and R23 forbids them.

Source of truth: `src/Domain/Stats/WeaponStats.cs`, `Stats/TurretLayout.cs`, cell function in
`src/Tests/WeaponBalanceTest.cs:199-209`, offer rules in `Progression/UpgradeOffer.cs`.

## 1. The cell model, as actually coded

`NoTwoGunsShareAnIdentityCell` keys each gun on
**(range band, delivery class, rate class, pellets-or-single, mount size)** where

- band: aura `< 750`, close `< 1100`, mid `< 1500`, long `≥ 1500`
- delivery: hitscan · guided · fast `≥ 2000 px/s` · medium `≥ 600` · slow `< 600`
- rate: stream `≥ 4/s` · burst `≥ 1.5/s` · volley `< 1.5/s`

15 real guns + `LoadTestWeapon`. Read at the coarser **(band × delivery)** level, which is what a
player actually perceives, the roster has holes:

| band \ delivery | hitscan | fast | medium | slow | guided |
|---|---|---|---|---|---|
| aura (600) | RippleCutter | **—** | **—** | FlakCurtain | Swarmlet |
| close (900-1000) | **—** | **—** | InterceptorTurret | Shotgun, ScatterCannon, AcidBubbleSpray | SeekerPod |
| mid (1350) | PrismLance | **—** | MachineGun | **—** | HavocTorpedo |
| long (1500-1700) | LaserLance | RailGun, Cyclotron, GravitonBurst | **—** | **—** | **—** |

Six intersections are empty; the long band holds 4 guns and close holds 5. Two of the empty
intersections matter more than the rest:

- **mid × hitscan on a small mount.** Every small gun reaches at most 900 px (three of them 600)
  except Cyclotron, and R23 means a hull with four small mounts (Heavy) or three (Fighter, Eagle) can
  no longer answer that by mounting several copies of one long gun.
- **long × guided.** The long band has four guns and not one guided round. Out past the visible
  half-width a lock is the only delivery that depends on neither lead, travel time nor a clear line.

## 2. R23 — one gun of each type aboard

**Rule.** At any moment the player's ship holds at most one copy of a weapon type, counting the
turrets on the hull *and* the weapons in cargo.

**Why it is a balance rule, not tidiness:** `GameSession.ApplyUpgrade` applies a weapon card to
**every turret with that name**, so a duplicate doubles the value of every `Damage`, `FireRate` and
`Range` card that gun is offered. One copy per type makes a card worth exactly the same in every
loadout, and caps stacking by the size of the roster instead of by how many mounts fit one gun.
It also removes the "three identical blades" build archetype, which is what makes the loadout
proposals in §4 necessary.

**What already enforces it:** `UpgradeOffer.Roll` builds `ownedNames` from mounted turrets *and*
cargo, and `NewWeaponOptions` filters on it — asserted today by
`MountedWeaponsAreNeverOfferedAgain` and `CargoWeaponsAreNeverOfferedAgain` (`LevelUpSlotOfferTest`).
A reroll re-uses `Roll`, so a salvage charge cannot buy back a duplicate either. **The rule is
already true for cards; only the starting data breaks it.**

**What has to change:**

- Data: `WeaponLoadout.RippleCutterTrio` and `WeaponLoadout.HeavyFlak` (§4).
- A domain predicate `TurretMount.AlreadyAboard(view, weaponName)` (mounted ∪ cargo) next to `Fits`,
  so the rule lives with the other mount rules instead of being re-derived in three places.
- `GameSession.ApplyMountClick` refuses to place a **fresh** (card) weapon whose name is already
  aboard, with the hint line saying so. Today that path is unreachable through real cards, but the
  `SPACEVORS_DIAG_UPGRADES=Shotgun,Shotgun` bench walks straight into it, and the guard is what makes
  the rule true for the bench too. Cargo↔mount moves stay free and unlimited — they move the single
  copy, they never clone it.
- Docs: R23 joins the rule list in `plans/WEAPON_BALANCE.md` §3; `WeaponBalanceTest` gains nothing
  (the rule is about loadouts, not the gun table) — the assertion lands in `ShipRosterTest` (§6).

## 3. Four new guns

Stats as they would be written in `WeaponType`. Arithmetic is the real test arithmetic
(`AmmoSpeedVariation = 0.3` → worst reach is `AmmoSpeed × 0.85 × ShotLifetime`; the tightest enemy
turn radius at tier 10 is 38 px — HeavyCannon's 50 px/s ÷ 1.3 rad/s, which is what
`GuidedRoundTurnsWiderThanAnythingItHunts` actually takes as the minimum, not the ~92 px Interceptor
the older notes quote). All four were checked against R1, R3, R4, R5, R6, R7, R8, R9, R13, R15, R16,
R17, R18 — all pass.

| Gun | tier | cell (band, delivery, rate, shot, size) | DPS | rounds/s | live rounds/mount |
|---|---|---|---|---|---|
| FlechetteFan | 1 | close, fast, burst, 3 pellets, S | 6.0 | 6.0 | 3.6 |
| Stiletto | 1 | mid, hitscan, burst, single, S | 7.2 | 2.4 | 0 (beam) |
| SiegeSalvo | 2 | mid, slow, volley, 3 pellets, L | 16.8 | 4.2 | 17.6 |
| FarlockSalvo | 2 | long, guided, volley, 2 pellets, L | 5.4 | 1.8 | 8.1 |

**FlechetteFan** — `Small, Range 900, FireRate 2.0, AmmoSpeed 2200, PelletCount 3, Scatter 0.10,
ShotLifetime 0.6, Damage 1, Kick 2, TurnSpeed 200°/s, Yellow, AmmoRadius 2`.
Three needle-fast slivers that hit where you point: the close-range gun that needs neither lead nor
luck, and the cheapest gun in the game. It is the answer for a hull that cannot turn to lead
(a Shotgun cone that does not open up on the way). R6: `2200 × 0.85 × 0.6 = 1122 ≥ 900` ✓. R7: 6 r/s ✓.
R3 exempt (no tier-1 large fast gun to compare). Shortest-lived rounds in the table: 3.6 live rounds.
*Side effect to record:* it becomes the cheapest gun in the close band, so SeekerPod's R15 ceiling
drops from 7.2 to 4.8. SeekerPod sits at 3.6, still legal — but the next close-band gun tightens it
again.

**Stiletto** — `Small, Range 1350, FireRate 2.4, PelletCount 1, Scatter 0, Damage 3, Kick 6,
TurnSpeed 160°/s, Hitscan, Violet, AmmoRadius 2`.
The reach small mounts never had: an instant 1350 px needle, no lead, no travel time, and it pays
R1's beam tax — 7.2 DPS against the mid band's benchmark 16.8 × 0.8 = 13.44. Thin, cheap, and it
dies to the first rock in the line (a beam damages one thing, once). R5 ✓ (3 dmg).
*Note for the rule list:* adding this gun makes R1's mid-band benchmark **SiegeSalvo**, not
MachineGun. R1 prices a beam against the *slowest-round* peer of its band, not the weakest one, so a
slow heavy gun loosens that band's beam cap (mid: 9.6 → 13.44). That reading was chosen on purpose —
see `plans/WEAPON_BALANCE.md`'s 2026-11-02 follow-up and §8 below.

**SiegeSalvo** — `Large, Range 1350, FireRate 1.4, AmmoSpeed 400, PelletCount 3, Scatter 0.22,
ShotLifetime 4.2, Damage 4, Kick 10, TurnSpeed 110°/s, Yellow, AmmoRadius 4.5`.
ScatterCannon's cone thrown out to the line: three fat slugs at 400 px/s that only stay together
inside ~700 px, so it buys the biggest number in the mid band and still misses what it leads. The gun
for a hull that holds its ground (the Heavy's two empty heavy mounts, Raptor's empty broadside).
R6: `400 × 0.85 × 4.2 = 1428 ≥ 1350` ✓. R7: 4.2 r/s ✓. R3 tier-2 slow: 16.8 ≥ 1.25 × AcidBubbleSpray
12 = 15 (12 % slack). R4: ≤ 1.4 × 18 ✓, and a step up on MachineGun ✓. R5: 4 dmg ✓. R8: 10 × 1.4 = 14
px/s² ✓. **Cost: 17.6 live rounds per mount** — the most expensive gun in the table, and the number
to watch (§5).

**FarlockSalvo** — `Large, Range 1650, FireRate 0.9, AmmoSpeed 560 (top), PelletCount 2,
Scatter 0.08, ShotLifetime 4.5, Damage 3, Kick 8, TurnSpeed 120°/s, Homing, HomingTurnRate 120°/s,
SalvoGrows false, MissileHealth 2, Orange, AmmoRadius 5`. `UnlockLevel: 10` (with HavocTorpedo, so
the level-5 hand does not turn into a missile hand).
Certainty at reach: two locked rounds out past the visible half-width, the least damage in the band.
R15: 5.4 ≤ 0.8 × Cyclotron 8 = 6.4 ✓. R17: 1.8 guided rounds/s ≤ 4 ✓. R16: launch turn radius
187 px, cruise 267 px — both far wider than the 38 px a tier-10 hull turns at, so a lock stays
breakable. R6: `560 × 0.85 × 4.5 = 2142 ≥ 1650` ✓.

- **It needs one retune: Swarmlet `FireRate 0.8 → 0.7` (DPS 4.8 → 4.2).** R3 floors every tier-2
  guided *large* at 1.25 × the best guided *small*, and R15 caps it at 0.8 × the cheapest long-band
  gun (Cyclotron, 8 DPS → 6.4). With Swarmlet at 4.8 that window is 6.0-6.4 — a knife-edge, exactly
  the "two numbers sitting on 0.8 × " trap `plans/WEAPON_BALANCE.md` §12 already documented. At 4.2
  the window is 5.25-6.4 and FarlockSalvo sits at 5.4. Swarmlet's own ceiling (9.6) is unaffected and
  its volley growth still has room (0.7 × 4 = 2.8 ≤ 4).

## 4. Starter loadouts: the two hulls that start with duplicates

R23 forbids both. Neither change may just add DPS: R12 says firepower follows hull size.

**Heavy** — `WeaponLoadout.HeavyFlak` (7 mounts, 4 filled, R 84):

| mount | today | proposed | DPS |
|---|---|---|---|
| main_gun (L) | MachineGun | MachineGun | 12 |
| small_left | Shotgun | Shotgun | 10.5 |
| small_right | **Shotgun** ← duplicate | **FlechetteFan** | 6 |
| small_back | FlakCurtain | FlakCurtain | 12 |
| | **45** | **40.5** | |

Live rounds drop from ~93 to ~65: the duplicate shotgun (10.5 rounds/s × 3 s fuse) is replaced by a
gun whose rounds die in 0.6 s. The flank it covers is exactly the arc the slow hull cannot bring to
bear by turning, and fast delivery is the class the Heavy has none of.

**Shadow** — `WeaponLoadout.RippleCutterTrio` (3 mounts: Large `main_gun` at 60°, two 120° flanks,
R 58, 10 hp). Shadow's identity is "fly into the fight to be armed": every gun must stay at close or
aura reach. Three options, all distinct weapons:

| option | main_gun | small_left | small_right | total DPS | verdict |
|---|---|---|---|---|---|
| **B (recommended)** | ScatterCannon (18) | RippleCutter (8) | *empty* | **26** | keeps the brawler and the big mount, keeps the ladder monotone, and hands the player one mount to fill at the first milestone |
| A | ScatterCannon | RippleCutter | FlechetteFan | 32 | fully booked, but Shadow now out-guns the bigger Eagle (27) — playtest before choosing it |
| C | Shotgun (small gun in the big mount) | RippleCutter | FlechetteFan | 24.5 | fully booked and cheap, but the hull then never wants a large gun |

Recommended R12 ladder after the change (option B): Scout 17 < Manta 21 ≈ Fighter 22.5 ≈ Raptor 22.5 <
**Shadow 26** < Eagle 27 < **Heavy 40.5** — monotone in hull radius, and the Heavy keeps its lead.
`StarterFirepowerFollowsHullSize` (which only reads Scout/Fighter/Shadow/Heavy) still passes.
`AtMostOneHullStartsFullyBooked` still passes either way; under B no hull is booked, under A/C
Shadow is the one.

**Fallback that ships without any new gun** (so the dedupe can land first):
Heavy `small_right` → InterceptorTurret (43.5 DPS, ~89 live rounds), Shadow `main_gun` → Shotgun
(24.5 DPS). Both are tier-1 and distinct, and the tree stays green with no roster change.

No other hull duplicates a weapon: Scout, Fighter, Falcon, Eagle, Manta and Raptor each carry
distinct types, and every diagnostic ship carries one gun.

## 5. Entity budget

The perf scenarios pin `LoadTestWeapon` and silence player turrets, so none of this can move them —
measure anyway per AGENTS.md and report the delta:

    .tools/scenario.sh ammo-spray --sim --iters 3
    .tools/scenario.sh impact-storm --sim --iters 3

What the scenarios cannot see is live rounds in real play. Per-mount estimates for the new guns:
SiegeSalvo 17.6, FarlockSalvo 8.1, FlechetteFan 3.6, Stiletto 0. Hull totals that move: Heavy 93 → 65,
Shadow 0 → 27-31 (it fires travelling rounds for the first time). All two orders under the 10k-object
goal; SiegeSalvo is the row to re-check if a later pass stacks mounts.

## 6. Tests to add or update

- `ShipRosterTest`: new `NoHullStartsWithTwoOfTheSameWeaponType` over `ShipType.All` +
  `DiagnosticShips` — this *is* R23. Rewrite
  `Shadow_CarriesARealWeaponNotTheLoadTestSpray` as "Shadow's guns are distinct and every one of
  them reaches ≤ 1000 px". Add the four new guns to
  `DiagnosticShips_AreShadowClonesWithOneWeaponEach` (that test asserts the exact list).
- `UpgradeOffer`: add `AHandNeverOffersTheSameWeaponTwice` (one card per name within a single hand),
  and cite R23 in the two existing owned-weapon tests — they are the rule's offer-side half.
- `TurretMountTest` / `CargoTest`: `AlreadyAboard` true for a mounted gun, true for a cargo gun,
  false for an unowned one; a cargo→mount move leaves nothing aboard twice.
- `WeaponBalanceTest`: the new guns are covered by the existing table rules automatically. Fold in the
  one assertion `plans/WEAPON_BALANCE.md` §9 promises and §12 admits is missing — **R5: an
  auto-acquiring gun stays at ≤ 4 damage per shot, with `RailGun` (14) and `HavocTorpedo` (10) as the
  only named exceptions.** Cheap, and it guards the two biggest rounds in the game.
- Swarmlet's rate change: `OnlySomeGunsMayGrowTheirVolley` and
  `GuidedVolleyAtItsCeiling_IsNotOfferedAnotherShot` stay green (the growth ceiling is derived from
  R17, not from 0.8).

## 7. Commit sequence (small, one concern each, tree green each time)

1. R23 as data + tests: dedupe Heavy and Shadow using existing guns (§4 fallback); add
   `NoHullStartsWithTwoOfTheSameWeaponType`; state R23 in `plans/WEAPON_BALANCE.md` §3 and point the
   PLAN.md roadmap line at this plan.
2. `TurretMount.AlreadyAboard` + the mount-screen guard + its tests.
3. `FlechetteFan` + `Stiletto` (tier 1), diagnostic entries, and Heavy's `small_right` swap to
   FlechetteFan; Shadow option B.
4. `SiegeSalvo` + `FarlockSalvo` + the Swarmlet `0.8 → 0.7` retune.
5. The missing R5 assertion; refresh `plans/WEAPON_BALANCE.md` §4 identity matrix and §2 coverage
   grid; `## Shipped` + `Status: shipped` here and the PLAN.md index line.

## 8. Open knobs

- **Shadow A vs B vs C** (§4) — the only real question is whether the fully-booked brawler should
  out-gun the bigger Eagle.
- **SiegeSalvo's cost.** 4.2 s of fuse is what makes 1350 px honest (R6); if 17.6 live rounds per
  mount reads as chaff, the pair to move together is lifetime 3.6 s + range 1150 px — lifetime alone
  breaks R6.
- **FarlockSalvo `UnlockLevel` 5 vs 10.** At 5 the first milestone hand can hold two missile cards out
  of three.
- **R1's benchmark tie-break — decided, implemented (2026-11-02).** The beam test used to take the
  slowest-ammo peer and keep table order on ties, so the close band benchmarked ScatterCannon
  (18 DPS → cap 14.4) instead of Shotgun (10.5 → 8.4). It now takes the **weakest** gun among those
  sharing the band's slowest ammo speed, which makes the close-band cap 8.4 and the rule independent
  of table order; nothing in the weapon table moved, because the close band has no beam yet. The
  stricter reading (weakest travelling peer of any speed) was rejected: with Cyclotron at 8 DPS it
  would cap the long band at 6.4, under LaserLance's 9.6, and R2 keeps Cyclotron cheap on purpose.
  Rationale recorded in `plans/WEAPON_BALANCE.md`.
- **The aura band is still thin** (RippleCutter, FlakCurtain, Swarmlet). `aura × medium` and
  `aura × fast` stay empty on purpose: at 600 px the instant beam and the wide disc already own the
  "what is on top of me" role, and R3 forces any large aura gun to ≥ 15 DPS just to clear its mount.
  If the brawler hulls feel under-served, an `aura × medium` stream (≤ 9.6 DPS so MachineGun keeps its
  R3 lead) is the next fill.
- **`long × medium`, `long × slow`, `mid × fast`, `close × hitscan`** stay empty: each is a band-shift
  of a gun that already exists (MachineGun, ScatterCannon, Cyclotron, RippleCutter) rather than a new
  play pattern.
- **Viewport-gated auto-fire** (`plans/WEAPON_BALANCE.md` §8) is what makes a 1650 px guided gun
  readable rather than a kill the player never saw coming. It is not required here, but FarlockSalvo
  is the gun that will make it feel like a bug.
- **New-weapon cards and free mounts.** With one copy per type the pool is the whole roster — 19 real
  guns, of which a level-5 milestone hand draws from 16 (9 tier-1 + 7 at unlock 5) and level 10 adds
  HavocTorpedo and FarlockSalvo. The hand still offers ≤ 3 new-weapon cards, so the "prefer a gun that
  fits a free mount" idea in WEAPON_BALANCE §8 stays a nice-to-have, not a correctness fix.

## Shipped (2026-11-02): R23, four guns, and the two duplicate loadouts re-armed

**What landed** (`Stats/WeaponStats.cs`, `Stats/PlayerShipStats.cs`, `TurretMount.cs`,
`Game/GameSession.cs`).

- **FlechetteFan** — close (900 px) × fast (2200 px/s) × burst (2/s) × 3 pellets × Small, 6.0 DPS,
  tier 1. Spread `Scatter: 0.10`, `ShotLifetime: 0.6`.
- **Stiletto** — mid (1350 px) × hitscan × burst (2/s) × single × Small, 8.0 DPS, tier 1, Violet.
- **SiegeSalvo** — mid (1350 px) × slow (400 px/s) × volley (1.4/s) × 3 pellets × Large, 16.8 DPS,
  unlock 5. `ShotLifetime: 4.2` is the fuse that lets a 400 px/s round reach 1350 px at all (R6).
- **FarlockSalvo** — long (1650 px) × guided × volley (0.9/s) × 2 pellets × Large, 5.4 DPS, unlock 10.
  1.8 guided rounds against `HomingRoundBudget: 4`, launch turn radius 187 px, cruise 267 px — both
  inside the 38 px radius a tier-10 HeavyCannon can hold, so R16 is comfortably met.
- **Swarmlet** 0.8 → 0.7 shots/s (4.8 → 4.2 DPS), which opens the tier-2 guided-large window R3 and
  R15 squeeze: floor 5.25, and HavocTorpedo 6.0 / FarlockSalvo 5.4 both fit.
- **R23 enforcement.** `TurretMount.AlreadyAboard(view, name)` reads both halves of the ship — non-enemy
  turrets and `WeaponCargo`. `GameSession` refuses a *fresh* copy in three places: `ApplyMountClick`
  (before anything is displaced, so a refused click does not silently unmount the gun that was there),
  `StoreHeldInCargo`, and `MountIntoFirstFit`. Cargo↔mount moves stay free: they relocate the one copy.
  `UpgradeOffer.Roll` already filtered owned names, so the hand needed nothing.
- **Shadow** now flies `WeaponLoadout.ShadowCloseSuite` (renamed from `RippleCutterTrio`): ScatterCannon
  in the large mount ahead, RippleCutter on the left flank, right flank left empty — option B, 26 DPS.
  **Heavy**'s `small_right` went Shotgun → FlechetteFan, 40.5 DPS. Both descriptions updated, and the
  diagnostic Shadow clones went 16 → 20, one new gun each.

**Deviations from the plan.**

- **Stiletto was repriced 3 dmg @ 2.4/s → 4 dmg @ 2.0/s (7.2 → 8.0 DPS).** §3's arithmetic modelled
  R15's peer set as travelling rounds only; `GuidedGunDpsStaysUnderItsOwnBand` filters out *homing* guns
  and nothing else, so a mid-band beam is a peer and a 7.2 DPS Stiletto cut HavocTorpedo's ceiling to
  5.76. The existing test said so on the first run. Repricing the new gun rather than trimming a shipped
  missile also keeps Stiletto under PrismLance's 8.4, so the large mount still buys more than the needle.
  Recorded as a rule note in `plans/WEAPON_BALANCE.md`.
- The refusal needed three call sites, not the one §2 named — a held gun can also reach cargo or a
  perf-scenario mount.
- `AtMostOneHullStartsFullyBooked` survives as a rule, but no hull trips it now (Shadow gave its third
  gun up). Its comment says so.
- `StarterFirepowerFollowsHullSize` still checks four hulls, not the whole 8-hull ladder the audit
  sketched; the ladder is Scout 17 < Manta 21 ≈ Fighter ≈ Raptor 22.5 < Shadow 26 < Eagle 27 < Heavy 40.5
  and holds by hand.

**Tests.** 530 pass (was 526). New: `NoHullStartsWithTwoOfTheSameWeaponType` (over `ShipType.All` and
the diagnostic clones), `AlreadyAboard_SeesMountedGunsAndCargoAlike`, `AHand_NeverDealsTheSameWeaponTwice`,
`OnlyTwoGunsMayCarryABigRound` (R5, the rule §9 claimed and §12 admitted was unasserted). Rewritten:
`Shadow_CarriesDistinctCloseGunsNotTheLoadTestSpray`. The two offer tests now cite R23.

**Perf verdict (2026-11-02, `--sim --iters 3`, interleaved A/B against a clean HEAD worktree).**
ammo-spray: changed 2.76 / 1.61 ms, HEAD 2.56 / 1.70 ms. impact-storm: changed 3.71 / 2.96 ms,
HEAD 3.69 / 3.66 ms. Both trees also drifted 1.4 → 2.5 ms across the session on identical code, so the
spread is host noise and the paired difference flips sign. **This change costs nothing measurable —
keep.** That is the expected result, not a lucky one: every scenario sets `WeaponFires: false` and the
ammo scenarios pin `LoadTestWeapon`, so no player turret fires in any of them; the only sim delta a
scenario could see is one fewer turret entity on Shadow. A real playtest is what should judge
SiegeSalvo's 17.6 live rounds per mount.

Left for the human to commit (small change, per AGENTS workflow rule 6).
