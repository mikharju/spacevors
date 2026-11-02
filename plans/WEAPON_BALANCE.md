# WEAPON_BALANCE.md

Status: shipped — pass 1 (delivery ladder, range ladder, identity cells) and pass 2 (the kill/support class
split, R24-R31, missile screens, class card filters) are both in the code and asserted by `WeaponBalanceTest`.
§6 (armour) is **future work, not implemented**.

Source of truth is `src/Domain/Stats/WeaponStats.cs` + the tests; this file is the rule set. Firing and
target acquisition: `src/Domain/Combat/TurretFiringSystem.cs`; damage: `src/Domain/Combat/CollisionSystem.cs`;
cards: `src/Domain/Stats/UpgradeStats.cs`, `LevelUpSystem.cs`, `GameSession.ApplyUpgrade`. Rule numbers are
stable identifiers — text gets revised, numbers are never renumbered, because `WeaponBalanceTest`,
`plans/MISSILES.md` (R15-R22) and `plans/WEAPON_IDENTITY_CELLS.md` (R23) cite them by number. This file
holds no stale anchors; superseded passes live in git history.

**Pass 2 in one line:** the large mount is how you kill things, the small mounts are how you make killing
possible. Only a Large gun may hold the top of the DPS table or the long range band; a Small gun gets
medium DPS *or* medium reach, never both, and it never carries a round big enough to matter on its own.
Small guns are also the ship's missile defence.

## 1. Anchors (what a number here actually means)

No world zoom: 1 world px = 1 screen px.

| Anchor | Value |
|---|---|
| Visible half-extent (1920×1024) | 960 × 512 px, plus up to 480 px of mouse camera drift |
| Hulls (radius / hp / mounts L+S) | Scout 46 / 8 / 1+2 · Fighter 58 / 10 / 1+3 · Shadow 58 / 10 / 1+2 · Falcon 111 / 10 / 1+2 · Eagle 66 / 16 / 1+3 · Manta 111 / 12 / 1+2 · Raptor 78 / 14 / 3+0 · Heavy 84 / 20 / 3+4 |
| Enemy opening-fire ring | `FiringRange + your radius`: 700 Default/Interceptor/HeavyCannon, 640 Carmine, 520 Streak; +12…25 px per tier, capped at `MaxFiringRange` 1000 |
| Enemy ammo | ≤ `MaxAmmoSpeed` 250 px/s |
| Enemy hull hp | Streak 5 · Interceptor 7 · Default 15 · Carmine 22 · HeavyCannon 30, × `1 + 0.25·tier` ⇒ ×3.5 at `MaxTier` 10 → **105 hp** for a tier-10 HeavyCannon |
| Mines | 2 hp, never scale |
| Range bands | aura 600 · close 900-1000 · mid 1350 · long 1500-1700; RailGun's clicked reach is 3× base, capped by its own ammo (2500 px/s × 2 s = 5000 px) |
| Guided rounds | leave at `MissileLaunchFraction` 0.7 of card speed, build at 200 px/s², health `MissileHealth` (player 1-3, enemy rack 2-3), ≤ 4 per mount per second |

Two floors already enforced and not to be renegotiated: no gun reaches under 600 px (it cannot answer
what is shooting it), and `AmmoSpeed × 0.85 × ShotLifetime ≥ Range` — the range on a card is what the
slowest round that gun fires can cross.

## 2. The two classes

| | **Kill class** (Large) | **Support class** (Small) |
|---|---|---|
| Raw DPS | ≥ 10.4 (non-guided) | ≤ 8 in aura/close, ≤ 6 in mid |
| Reach | any band; **only this class** may enter long (≥ 1500) or use the 3× clicked reach | ≤ 1350 px |
| Damage per shot | 2-4, plus two licences (RailGun 15, HavocTorpedo 10) | ≤ 2 — chips |
| Job | kills hulls | screens, cones, area denial, swarms, **missile defence** |
| `Damage` card | yes | **never offered** (R29) |
| Growth | damage + rate + range | rate + range (up to the class roof) + role |

The step between the classes is what the player is meant to *see*: 8 support DPS is the ceiling, 10.4 is
the weakest kill gun, a ratio of **1.30**. One support mount needs 13.1 s to kill a tier-10 HeavyCannon
(105 hp); the weakest kill mount needs 10.1 s. Support buys shape rather than damage — a cone that needs
no aim, a disc that covers the aura, a needle that pokes from safety, a screen that peels missiles off
the hull — and it is deliberately unable to win a fight by itself.

DPS is the kill currency; damage *per hit* is the armour currency (§6). Keeping them separate is what
makes both rules readable.

## 3. The roster as it stands

Kill class, raw DPS (fire rate × pellets × damage):

| Gun | band (range) | delivery | DPS | note |
|---|---|---|---|---|
| ScatterCannon | close (1000) | slow ×3 | 18 | the biggest number in the game, and it only holds together inside ~700 px |
| SiegeSalvo | mid (1350) | slow ×3 | 16.8 | |
| GravitonBurst | long (1700) | fast ×5 | 16.5 | |
| RailGun | long (1500 / 4500 click) | fast, single | **15** (was 14) | the big round hits harder, and it lifts the long band's beam cap from 11.2 to 12 so LaserLance stops sitting 7% under its own ceiling |
| MachineGun | mid (1350) | medium, single | 12 | the loadout workhorse — see the coupling in §9 |
| PrismLance | mid (1350) | hitscan ×3 | **10.8** (was 8.4) | 4 damage × 3 beams @ 0.9/s (cap 13.44) — this used to be weaker than a support gun, which is what the pass exists to fix |
| LaserLance | long (1650) | hitscan, single | **10.4** (was 9.6) | 4 damage @ 2.6/s (cap 12 after RailGun, margin 13%) — the weakest kill gun, so it *is* the class floor R25 measures |
| HavocTorpedo | mid (1350) | guided, 10 dmg | 6 | pays the certainty tax (R15); exempt from the class floor, never from the ceiling |
| FarlockSalvo | long (1650) | guided ×2 | 5.4 | |

Support class:

| Gun | band (range) | delivery | DPS | note |
|---|---|---|---|---|
| FlakCurtain | aura (600) | slow disc | **8** (was 12) | the class ceiling, and the cheapest missile screen: 1 dmg @ 8/s in a 9 px disc |
| Shotgun | close (900) | slow ×3 | **7.5** (was 10.5) | 1 dmg × 3 @ 2.5/s |
| AcidBubbleSpray | close (900) | slow spray | **6** (was 12) | 1 dmg @ 6/s — area denial, and the natural armour-strip carrier (§6) |
| InterceptorTurret | close (900) | medium ×2 | **6** (was 9) | 1 dmg × 2 @ 3/s, still the fastest mount in the game (360°/s), which is what makes it a screen |
| FlechetteFan | close (900) | fast ×3 | 6 | almost no penalties to be paid for (R2), so it stays the cheapest travelling gun |
| RippleCutter | aura (600) | hitscan blade | **6** (was 8) | 2 dmg @ 3/s: chips, and it must stay under 0.8 × FlakCurtain (6.4) |
| Stiletto | mid (1350) | hitscan needle | **6** (was 8) | 2 dmg @ 3/s — the mid-band support ceiling, and the best anti-missile fire in the game |
| Cyclotron | **mid (1350)** (from long 1600) | fast needle | **5** (was 8) | 1 dmg @ 5/s, and it left the long band: that reservation belongs to the kill class |
| Swarmlet | aura (600) | guided ×3 | 4.2 | |
| SeekerPod | close (900) | guided | 3.6 | **2 dmg @ 1.8/s** (was 3 @ 1.2/s): same DPS, now a chip instead of a small bomb (R27) |

Ladder per reach band (a beam is priced against the band's weakest slowest-round gun, a guided round
against the cheapest kill gun of its own class): aura — FlakCurtain 8 (slow) over RippleCutter 6 (beam,
cap 6.4), Swarmlet 4.2 (guided, cap 4.8). Close — ScatterCannon 18, Shotgun 7.5, Acid/Interceptor/
Flechette 6, SeekerPod 3.6 (cap 4.8). Mid — SiegeSalvo 16.8, MachineGun 12, PrismLance 10.8, Stiletto 6,
HavocTorpedo 6 (cap 8.6), Cyclotron 5. Long — GravitonBurst 16.5, RailGun 15, LaserLance 10.4,
FarlockSalvo 5.4 (cap 8.3).

Starter loadouts, and the *kill share* each one now carries (kill-gun DPS over total):

| Hull | loadout | total DPS | kill share |
|---|---|---|---|
| Scout | **MachineGun (main) + InterceptorTurret** — was Cyclotron + InterceptorTurret | 18 (was 17) | 67% |
| Fighter | MachineGun + Shotgun | 19.5 | 61% |
| Shadow | ScatterCannon + RippleCutter | 24 | 75% |
| Eagle | ScatterCannon + InterceptorTurret | 24 | 75% |
| Manta | MachineGun + InterceptorTurret | 18 | 67% |
| Raptor | MachineGun + Shotgun (heavy_left) | 19.5 | 61% |
| Falcon | MachineGun | 12 | 100% |
| Heavy | MachineGun + **ScatterCannon (heavy_left)** + Shotgun + FlakCurtain — FlechetteFan left | 45.5 (was 40.5) | 66% |

The Scout's change is not decoration: `EnemyDifficultyTest.ToughestTierHullDiesInsideTheTtkBudget` gives
a 7 s budget, which pins the starter Scout at ≥ 15 DPS (105 hp ÷ 7 s). Two nerfed support guns would have
given it 11 DPS and 9.5 s — a hard fail. A hull whose main mount holds a support gun cannot clear its own
TTK floor once support is nerfed, which is the pass's thesis stated as a test. The Scout now shares
`WeaponLoadout.MachineGunRepeater` with the Manta; `CyclotronRepeater` was deleted rather than rewritten.

## 4. Rules

Kept, with revised text where pass 2 changes the meaning. Numbers are unchanged; nothing is renumbered.

- **R1 — delivery ladder.** A beam stays ≤ 0.8 × the *weakest* of the band's slowest-round guns (`BeamGunDpsStaysUnderItsOwnBand`). Guided guns are not peers here; they are priced by R15. A beam pays twice more for dying to the first rock in its line.
- **R2 — a penalty is paid for, and so is an advantage.** Reach, travel time and cone spread buy DPS. The ladder runs *inside* a class: an excellent-delivery support gun (FlechetteFan, Cyclotron) is meant to be cheap, and its cheapness is not a bug to compensate.
- **R3 — a large mount earns its scarcity** within one tier and delivery class: ≥ 1.25 × the best small gun of that class. Beams exempt (R1 caps them), guided guns exempt (R15 caps them).
- **R4 — tier 2 is a step, not a leap:** ≤ 1.40 × the strongest tier-1 gun of the same size, and (non-beam, non-guided) ≥ 1.15 × a tier-1 peer *or* equal DPS with 1.3× the reach.
- **R5 — per-shot damage needs a licence.** ≤ 4 for everything that acquires its own targets; RailGun (15) and HavocTorpedo (10) are the only two, and both fire 0.6-1.0 rounds a second. `AutoTarget: false` turns off **lead prediction** only — it is not a licence and never was.
- **R6 — honest reach**, **R7 — ≤ 12 rounds/s per mount**, **R8 — kickback is one currency** (sustained `kick × FireRate` ≤ ~0.12 × forward thrust), **R9 — only RailGun (55°/s) and PrismLance (70°/s) slew slower than a Balanced hull turns** (1.33 rad/s), **R10 — see R29** (the card rule is now a class rule), **R13 — one gun per identity cell** (band, delivery, rate class, pellets, size), **R14 — colour is identity** (yellow kinetic, blue precision, violet laser, green chemical, cyan particle, orange guided).
- **R11 — TTK budget.** The enforced half of it: a tier-10 HeavyCannon (105 hp) dies inside 7 s of a starter Scout's fire. Raw-DPS TTK is optimistic in play (travel, arcs, misses, your own movement), so this is a floor, not a schedule: a starter set kills a tier-0 Default in 0.3-0.9 s and a tier-0 HeavyCannon
in 0.7-1.8 s — the fast end is the Heavy's 45.5 DPS, the slow end the Scout's 18.
- **R12 — firepower follows the hull** — hurtbox and mount capacity, not bounding radius. Falcon (12, two free 180° flanks) and Manta (18, the game's best strafe) sit below their painted size on purpose; Raptor (19.5, one heavy mount deliberately empty) and Heavy (45.5, two heavy mounts still empty) buy their growth with mounts.
- **R15 — certainty is priced in damage, against its own class.** A guided gun stays ≤ 0.8 × the cheapest non-guided gun **of the same mount class** in its band (`GuidedGunDpsStaysUnderItsOwnBand` gains a `Size` filter). The old rule taxed a missile against the band's cheapest *anything*, so nerfing a support chip re-priced every missile in that band — a chained tax (the chip was itself already taxed by R1/R2). Pricing a missile against the kill guns it competes with is the same comparison R3/R12 make; margins after the pass: SeekerPod 3.6 ≤ 4.8, Swarmlet 4.2 ≤ 4.8, HavocTorpedo 6 ≤ 8.6, FarlockSalvo 5.4 ≤ 8.3.
- **R16 — a guided round out-turns nothing**: launch turn radius `AmmoSpeed × 0.7 / HomingTurnRate` stays wider than the tightest tier-10 enemy hull's 38 px. **R17 — ≤ 4 guided rounds/s per mount.** **R18 — volley growth is a privilege** (`CanGrowSalvo`); **R19 — a guided round is a body with 1-3 hp** (`CanToughenRound`); **R20-R22 — the enemy rack ladder** (adds only, reaches 1.4× its hull's guns but ≤ 1450 px, and 0/3/4/5/6/7 rounds in the air at composition tiers 3/8/10/14/20).
- **R23 — one gun of each type aboard**, mounted or in cargo (`TurretMount.AlreadyAboard`), because `GameSession.ApplyUpgrade` applies a card to *every* turret of that name.

New with pass 2 (shipped 2026-11-02):

- **R24 — the support envelope** (`SupportGunsStayInsideTheirClassEnvelope`). A non-guided Small gun's raw DPS ≤ 8 in aura/close and ≤ 6 in the mid band; its range ≤ `WeaponType.SupportRangeCeiling` (1350 px). Support may buy medium DPS *or* medium reach, never both. A guided Small gun is priced by R15 instead, because certainty is what it bought.
- **R25 — the class step is visible** (`KillGunsHoldTheTopOfTheTable`). The strongest support gun × 1.25 ≤ the weakest non-guided Large gun (8 → 10.4 = 1.30), and the top of the DPS table is always a Large gun. Guided Large guns are exempt from the floor — they pay R15 — but never from the ceiling.
- **R26 — the long band belongs to the kill class** (`NoSupportGunReachesTheLongBand`). No Small gun reaches ≥ 1500 px, and a Small mount never takes the 3× clicked-reach premium: `TurretFiringSystem` gates the premium on `Turret.IsSupport` (`AClickedTargetBuysReachOnlyForTheKillClass`). Reach past the visible half-width is bought with a big round, not with a chip stream.
- **R27 — support guns fire chips** (`SupportGunsFireChips`): ≤ 2 damage per shot. A chip cannot kill a Default hull (15 hp) on its own and should never be the round that "should have been" the kill. The cap is on a *shot*, not on a volley: ScatterCannon's 2-damage slugs are kill-class fire because three of them arrive together at 18 DPS, which R24's ceiling already forbids a support gun from reaching.
- **R28 — missile defence is the support class's job** (§5, `OnlySupportGunsAreMissileDefence` + `MissileDefenceTest`): Small non-guided mounts may acquire a hostile guided round; Large mounts and guided mounts may not.
- **R29 — cards are class cards too** (`SupportGun_IsNeverOfferedADamageCard`, `SupportGunAtItsRangeRoof_IsNotOfferedARangeCard`). The `Damage` card is never offered to a support gun — the milestone hand's per-gun Damage loop skips `Turret.IsSupport`. The `Range` card is withdrawn from a support gun once its *live* range would pass 1350 px (the offer reads `mounted.Range × UpgradeDefinition.Range.Multiplier` against `WeaponType.SupportRangeCeiling`, the same "never a card the rules would refuse to honour" pattern as R18/R19; it stayed inline in `UpgradeOffer` because that loop is its only reader). `FireRate` stays open to both classes — it grows them at the same ×1.15, so it cannot invert the class line by itself. The reason is arithmetic, not tidiness: `Damage` is flat `+1` (`UpgradeStats.cs:21`), so on a 1-damage chip it is a *multiplier* — one card on FlakCurtain would take it from 8 DPS to 16, over the kill class's floor of 10.4. It also breaks R10's "15-35% relative jump" guidance, which no test asserts: on a 3-4 damage kill round the same card is the +25-33% card R10 asks for, and that is the only place it should live. (A new-weapon card reuses the `Damage` stat with `IsNewWeapon` set: it hands over a gun rather than upgrading one, so it stays open to both classes.) The Range card multiplies `ShotLifetime` as well as `Range` (`GameSession.cs:1181-1184`), so stopping a support gun at the roof also stops its live-round count from stretching with it — a free win on the class that keeps the most rounds in the air.
- **R30 — a bonus is priced when it ships, not before** (§6, which is future work). Nothing in the table is charged for armour-pierce it does not have.
- **R31 — a kill comes mostly from the kill class** (`StarterLoadoutsKillMostlyFromTheirKillGuns`). In every starter loadout the mounts that can kill out-DPS the screens by ≥ 1.25×: the weakest hulls are Fighter and Raptor at 1.60, the Heavy is 1.94, the Scout 2.0. In TTK terms against a tier-10 HeavyCannon (105 hp), one support mount needs 13.1 s where the weakest kill mount needs 10.1 s.

## 5. Anti-missile fire (shipped 2026-11-02)

**What already works.** Dumb ammo shoots enemy rounds down across factions today
(`CollisionSystem.cs:309-318` candidate rule, `:383-396` apply), and player beams stop on an enemy round and
damage it (`:542-573`); enemy beams only ever test the player hull, so they pass through a player missile
(`:428-446`). A shot-down round is destroyed in smoke and **does not detonate** (`:666-684`, "Shot down, not
detonated") — interception near your own hull is therefore safe, not a way to bring a warhead closer. Enemy
rounds carry `RoundHealth` 2 (Interceptor's rack) and 3 (HeavyCannon's rack) at `EnemyShipStats.cs:69,81`,
closing at 165+4·tier and 200+5·tier px/s, capped at 250 px/s, from a rack that fires 0.33 rounds/s in
salvos of one (`MissileFireRate`, `MissileSalvo`). R22 caps the pressure at **0 / 3 / 4 / 5 / 6 / 7 rounds in
the air** for composition tiers 3 / 8 / 10 / 14 / 20 (`MissileCapFor`, `:204-210`).

The other anti-missile answer already shipped: the ECM pulse world-wide stuns every enemy round in flight
(`EcmSystem.cs:42-56`) — bought as a pickup, not as a build. This rule is the *built* counter to the same
threat, which is why it belongs to the support class and why it should not also get a damage bonus
(`plans/MISSILES.md:436` already recorded point defence as "role only, no multiplier").

`MissileCapFor`'s own comment is the argument for this whole section: *"Lifting the cap at all is only safe
because gunfire shoots missiles down."* Until this pass that premise was half-true — a round died if a bullet
happened to cross it, but nothing ever *aims* at one. `plans/MISSILES.md:445` rejected a round that *hunts*
enemy rounds because a per-round target search was too expensive; this is not that — it is one extra candidate
list per support mount per tick, scanned over at most 7 bodies, next to the hull-and-mine scan turrets already
do. It also finally makes **InterceptorTurret's name** true: pass 1 renamed it away from "PointDefenceTurret"
precisely because the sim had no anti-missile fire to promise.

**What was missing was acquisition, and that is all this changed.** `TurretFiringSystem.FindTarget` (`:247`)
used to enumerate enemy hulls and mines only (`:296-303`); enemy turrets still look only at the player hull
(`:305-352`). So the change is a candidate list and a priority, not physics.

**Shipped: every Small non-guided mount screens; kill mounts and racks never do.** Not a per-gun allow-list —
the class *is* the rule: `WeaponType.InterceptsRounds` (`Size == Small && !Homing`) is copied onto the turret
when it is mounted or carried, so a chip bolted into a heavy mount screens too and a test reads the rule
straight off the table.

Why every support gun qualifies (the maths holds at the nerfed DPS): a round closing at ≤ 250 px/s is
inside a 900 px support mount for ~3.4 s and inside a 600 px aura for ~2.2 s, and each mount delivers
2-3 damage early enough to matter — FlakCurtain 8 dmg/s in a 9 px disc with a 0.5 rad cone, InterceptorTurret
2 per volley at 3/s on a 360°/s mount, Shotgun 3 slugs in a cone with no lead needed, Stiletto 2 damage
instantly at 1350 px (the best interceptor in the game), FlechetteFan 3 per volley at 2200 px/s,
AcidBubbleSpray 6 hits/s, RippleCutter 2 per sweep, Cyclotron 1 at 5/s. Against 2-3 hp that is one round
down inside a second of aimed fire.

Why the kill class stays out of it: a 15-damage RailGun round on a 2 hp body is the overkill waste R5
already refuses, and a big mount swerving off the hull that is killing you to chase a pebble is a loss the
player cannot see coming. The split is also legible on screen — fat tracers keep hammering the hull while
thin ones peel the missile off — which is worth more than a few points of optimised interception.

Priority, three clauses and no more (as implemented):
1. a hostile guided round **inside the mount's reach and closing** outranks an auto-acquired hull
   (`MissileDefenceTest.ASupportMountEngagesAClosingRound`);
2. a round with no closing component is ignored — no shooting at things that are leaving
   (`ASupportMountIgnoresARoundThatIsLeaving`), and a round the ECM has already stunned is skipped because it
   is spent (`MissileStunned`): a doomed round is not worth a chip;
3. a clicked primary target outranks the screen, as it always has, **and hostile rounds are clickable now**
   (`PrimaryTargetPicker.RoundClickZone` = 26 px — a 4-6 px round is otherwise unclickable; `IsHostileRound`
   in the live-target check). A click is an order, so this is how a player tells the RailGun to delete a
   missile even though it will never volunteer (`AHostileRoundCanBeClickedAndAnyMountFiresAtIt`,
   `AClickedHullOutranksAClosingRound`). Guided mounts stay out entirely: they do not slew, their round leaves
   along the nose, and a missile cannot re-lock mid-flight (`MissileGuidanceSystem.cs:41-45`).

The implementation is one helper, not a rewrite: `TurretFiringSystem.FindClosingHostileRound` runs before the
auto-acquire branch for a mount with `InterceptsRounds`, reusing `EvaluateMovingTarget` (lead) or
`EvaluateStaticTarget` (a beam) and taking the round's radius from `Ammo.Radius` in `GetTargetRadius`. A
screened round gets **no `AutoTargetMark`** — a bracket means "the thing you chose", and nobody chose the
missile. Own-side rounds are filtered on `Ammo.IsEnemy`, so a support mount never empties its chips into its
own missile (`ASupportMountIgnoresItsOwnRounds`).

What keeps missiles threatening after this: R22 allows **7** rounds in the air against a hull's 2-4 support
mounts, so volume overruns the screen; and while a support mount is intercepting, its DPS is not on the
hull. The Heavy, with four small mounts, becomes the anti-missile hull — which is exactly right for the
hull that cannot dodge. Raptor (three large mounts, none small) has **no screen at all** until the player
bolts a support gun into a heavy mount: that is the split working as intended, and an open knob.

Enemy side stays asymmetric on purpose: enemy turrets keep targeting only the hull, so they do not swat
player missiles down — the player *buys* the counter with mount slots and support DPS, while the enemy's
counters remain R16 (out-turn the lock) and hull hp. Player missiles still die incidentally to enemy fire.
Revisit only if FarlockSalvo starts feeling free.

## 6. Future work — armour: should slow big rounds pay now for pierce they will get later?

> **Not implemented, and deliberately not part of pass 2.** There is no armour, pierce, shield or
> mitigation code anywhere in `src`, and pass 2 changed no stat for it: no gun was nerfed or buffed "for
> armour". What follows is the answer to hold on to when armour is actually scheduled, plus the invariants to
> write *then*. The one rule pass 2 did ship out of this section is R30 — a bonus is priced when it ships.

The only armour-shaped card today is `MissileHealth` (`UpgradeStats.cs:29`). The answer to the section's
question is **no: do not charge the slow gun today, and let the fast gun pay in cards later.**

The mechanic decides it. With flat per-hit subtraction, `max(1, damage − (armour − pierce))`, armour is a
**tax on the support class and a bonus for the kill class**: a 15-damage RailGun round barely notices
armour 3, while a 1-damage chip is floored to 1 and loses two thirds of its DPS. The slow big round has
already been charged twice for its size — once in the DPS ladder, where a fat round is never the top of its
band (RailGun's 15 sits under GravitonBurst's 16.5), and once in overkill waste (R5: mines are 2 hp, the
smallest hull 5). Charging it a third time for a bonus that does not exist yet would be a nerf with no
mechanic behind it, and R10's whole lesson is that a card, a stat or a penalty must never be a lie.

What to do instead, in the order the work will land:

1. **Armour as flat subtraction per hit, with a floor of 1 damage.** A floor keeps a support gun useful
   instead of zeroed, and it keeps the arithmetic readable on screen.
2. **`ArmorPierce` priced as a `Damage` card** — `Additive: 1`, the same shape as `UpgradeStats.cs:21`.
   Against armour, +1 pierce ≈ +1 damage; against unarmoured hulls it is worth nothing. That is the right
   shape for a situational card: same slot cost, zero early value, and no multiplier to compound.
3. **The fast gun pays in card slots, not in base DPS.** Its advantage (chips land on small, moving,
   dodgeable bodies and waste nothing) is already taxed by R1/R2/R15, and armour taxes it again — and the
   way it buys out of that tax is by spending hand slots on pierce. That is the price for "hits small
   targets easily", and it is paid where the player can see it.
4. **Magnitude guidance:** armour should stay under the damage a kill round lands, so the kill class always
   puts something through — with kill rounds at 2-4 damage per shot early on that means **armour 1-2**, and
   4-5 by tier 10 once `Damage` cards have stacked (a maxed MachineGun lands 8 per hit). ScatterCannon's
   2-damage slugs are the edge case: at armour 2 they floor to 1 each, and its answer is that it fires three
   of them per volley, which is the point of a support-class number on a kill gun.
5. **The support class's answer to armour should be a job, not a bigger round: corrosion.** AcidBubbleSpray
   is already an area gun whose 1 damage is symbolic; making its cone *strip* armour per hit gives support a
   role armour cannot tax, which is how "small = support" survives the update instead of becoming dead
   weight. Forward-looking only — no stat change in this pass.

Invariants to assert the day armour ships (write them then, not now): (a) the kill class's DPS ordering
does not invert by more than ~15% at the tier's armour; (b) every support gun still does ≥ 1 damage per
hit; (c) a support gun holding every pierce card it can buy still does not cross the class line (R25) —
pierce returns a chip toward its unarmoured DPS, never above it.

## 7. Verification

Rules asserted on the table, all in `WeaponBalanceTest` unless stated: R1 `BeamGunDpsStaysUnderItsOwnBand`,
R3 `LargeGunsEarnTheirMountWithDamage`, R4 `TierTwoIsSlightlyStrongerNotTwiceAsStrong`,
R5 `OnlyTwoGunsMayCarryABigRound` (RailGun 15, HavocTorpedo 10), R6 card reach, R7 round-rate budget,
R9 slew floor, R12 `StarterFirepowerFollowsHullSize`, R13 identity cells,
R15 `GuidedGunDpsStaysUnderItsOwnBand` (**class-anchored**: a guided round is priced against the cheapest
non-guided gun *of its own mount class*), R16 turn radius, R17 guided budget, R18/R19 card privileges,
R23 one gun per type, and pass 2's `SupportGunsStayInsideTheirClassEnvelope` (R24),
`KillGunsHoldTheTopOfTheTable` (R25), `NoSupportGunReachesTheLongBand` (R26), `SupportGunsFireChips` (R27),
`OnlySupportGunsAreMissileDefence` (R28's data half), `StarterLoadoutsKillMostlyFromTheirKillGuns` (R31).

Behaviour, in `MissileDefenceTest` (R28) and `LevelUpSlotOfferTest` (R29): a support mount engages a closing
round and ignores a leaving one, its own rounds, and rounds beyond its reach; a kill mount never volunteers;
a clicked hull outranks the screen; a hostile round is clickable and any mount fires at a clicked round; a
player round is not clickable; the clicked-reach premium is kill-class only; the `Damage` upgrade card is
never dealt to a support gun while the same hand still deals it to the kill gun beside it; a chip at the range
roof gets no `Range` card and a chip below it still does.

Existing tests that read the live table and had to be checked, all green: `ShipRosterTest`,
`EnemyDifficultyTest` (the 7 s budget is what forced the Scout's main gun; the 1350 px mid-band and
fastest-enemy-ammo assertions are untouched), `EnemyMissileLadderTest` (its longest-player-gun anchor is
GravitonBurst 1700, unmoved by Cyclotron dropping to 1350), `LevelUpSlotOfferTest`/`RerollOfferTest`
(Swarmlet's pellets). Tests that build their own `WeaponStats` — `ThreatMeterTest`, `AimLeadTest`,
`CollisionSystemTest`, `DeathLevelUpTest`, `LaserTest`'s synthetic hitscan turret — cannot be moved by a
table retune.

Still uncovered, and adjacent to this pass: the `MissileHealth` card and its 3-hp ceiling, missile-vs-missile
collisions (they work, nothing tests them), and no scenario exercises the screen — every perf scenario
silences the player's turrets (`WeaponFires: false`), so interception has never been measured at volume. If
the screen ever needs measuring, seed a scenario with enemy rounds *and* player support mounts firing.

Perf: the scenarios pin `LoadTestWeapon` and silence player turrets (`WeaponFires: false`), so a data retune
cannot move them; the interception pass costs one extra candidate list per *support* mount per tick, and no
scenario mounts one. The paired same-host A/B against HEAD is recorded under `## Shipped`, not against an old
row — this host drifts by 40% between hours.
What the scenarios do not see is live round count in real play, and there the support class went **down**:
FlakCurtain 18 → 12, Shotgun 31.5 → 22.5, AcidBubbleSpray 36 → 18, InterceptorTurret 27 → 18,
Cyclotron 4.8 → 3.0. The Heavy's shipped four guns sit at ~74 (MachineGun 12 + ScatterCannon 27 + Shotgun 22.5
+ FlakCurtain 12) — up from 65, because ScatterCannon carries the biggest live-round number on any mount in the
game, and it is now bolted into a heavy mount. R7 is a per-mount budget so nothing breaks, but the Heavy is the
hull to watch on that metric. Interception adds extra fire only at composition tier 3+, bounded by R22's ≤ 7
hostile rounds.
Playtest loop: `SPACEVORS_DIAGNOSTIC=1` gives one Shadow copy per gun;
`SPACEVORS_DIAG_UPGRADES="MachineGun,Damage:MachineGun,FireRate:FlakCurtain"` puts a kill build next to a
support build; a rack-carrying HeavyCannon from composition tier 8 is the interception bench.

## Shipped (2026-11-02): pass 2 — the kill/support class split

Landed in one pass, suite green at 547 tests:

- **Weapon table** (`WeaponStats.cs`). Support: FlakCurtain 12→8, Shotgun 10.5→7.5, AcidBubbleSpray 12→6,
  InterceptorTurret 9→6, RippleCutter 8→6 and Stiletto 8→6 (both now 2-damage chips), Cyclotron 8→5 and out of
  the long band (1600→1350), SeekerPod 3 dmg @ 1.2/s → 2 dmg @ 1.8/s at the same 3.6 DPS. Kill, where a rule
  left legal room: RailGun 14→15, PrismLance 8.4→10.8, LaserLance 9.6→10.4. MachineGun held at 12 (§9).
- **Class helpers**: `WeaponType.SupportRangeCeiling` (1350), `IsSupport`, `InterceptsRounds`; the `Turret`
  component carries `IsSupport`/`InterceptsRounds`, copied by `TurretMount.CarriedTurret`, so the class travels
  through cargo and remounting. Enemy turrets default to the kill class, which keeps them out of the screen.
- **R15 re-anchored to the gun's own class** in `GuidedGunDpsStaysUnderItsOwnBand`. Without it the Cyclotron
  nerf dropped the mid band's cheapest-*anything* to 5 DPS and pushed HavocTorpedo out of a ceiling it had
  never earned — the same chained tax that bit Stiletto in pass 1, closed.
- **Loadouts**: the Scout starts `WeaponLoadout.MachineGunRepeater` (MachineGun + InterceptorTurret, 18 DPS,
  5.8 s on a tier-10 HeavyCannon); `CyclotronRepeater` was deleted, not rewritten, because the Manta already
  used the repeater loadout. The Heavy puts ScatterCannon in `heavy_left` and drops FlechetteFan
  (40.5 → 45.5 DPS). Hull descriptions follow.
- **R28 interception**: `TurretFiringSystem.FindClosingHostileRound` before the auto-acquire branch,
  `PrimaryTargetPicker.RoundClickZone` (26 px) and `IsHostileRound` in the live-target check, `Ammo.Radius` in
  `GetTargetRadius`, no `AutoTargetMark` on a screened round.
- **R29 card filters** in `UpgradeOffer`: no `Damage` upgrade card for `IsSupport` guns, no `Range` card once
  the next one would pass the class roof.
- **Tests**: six rule tests in `WeaponBalanceTest`, nine behaviour tests in the new `MissileDefenceTest`, two
  offer tests in `LevelUpSlotOfferTest`.

Deviations from the plan, all small:

- **R31 became a loadout rule, not a TTK ratio.** The planned wording (a support mount takes ≥ 1.25× as long
  as a kill mount) is R25's step inverted, so it would have asserted the same arithmetic twice. What is
  asserted instead is what the doctrine is about: in every starter loadout the kill mounts out-DPS the screens
  by ≥ 1.25× — Fighter and Raptor at 1.60, Heavy 1.94, Scout 2.0, Falcon 1.00 of nothing to share.
- **No `CanExtendRange` helper.** The roof check is one comparison in `UpgradeOffer`'s loop and that loop is
  its only reader; a property would have been a rule with one caller.
- **The `Damage` filter skips new-weapon cards.** They reuse `UpgradeOption.Damage` with `IsNewWeapon` set, so
  R29 withdraws only *upgrade* cards — a fresh support gun may still arrive on a card, it just never gets a
  Damage upgrade afterwards.
- **ECM-stunned rounds are not screened**: `EcmSystem` already detonates them harmlessly, so a chip spent on
  one is a chip wasted.
- **The enemy side still does not intercept player missiles** — the asymmetry §5 documents. Player missiles
  keep relying on R16 (out-turn the lock) and hull hp.
- **Armour was left alone entirely** (§6 is future work), as instructed. No stat, card or rule in this pass
  mentions armour-pierce; R30 exists to keep it that way.

**Perf verdict (2026-11-02, host=sim, `--iters 3`, paired same-host A/B against a HEAD worktree):** this
change costs nothing measurable — keep. missile-384: 3.09 / 3.64 ms median on this tree against 3.27 / 3.13 ms
on HEAD; ammo-spray: 2.49 / 3.00 against 2.52 / 2.19; impact-storm: 2.25 / 3.29 against 3.30 / 3.65. The
round-to-round spread on one tree (2.49 vs 3.00, a 20% swing) is larger than any gap between the trees, so the
difference is host drift, not code. Note honestly what the scenarios cannot see: none of them mounts a support
gun (they pin `LoadTestWeapon` and set `WeaponFires: false`), so `FindClosingHostileRound` never runs there —
the screen's cost is *unmeasured*, not proven zero. §7 says what to seed if it ever needs measuring.

Playtest follow-ups worth doing before the next pass: watch whether the screen makes composition tier 8+
feel fair (the screen is meant to thin a volley, not cancel it), and whether a support-only build now feels
hopeless rather than specialised.

## 8. Open knobs

- **MachineGun cannot rise above 12 without dragging RailGun up**: R4's floor is 1.15 × a tier-1 peer, so at
  MachineGun 13.5 RailGun needs ≥ 15.5. If kill power needs to grow, grow ScatterCannon, SiegeSalvo or
  GravitonBurst — no rule blocks them. The class floor (R25) is set by LaserLance at 10.4, not by MachineGun.
- **RippleCutter sits 6% under its R1 cap** (6 against FlakCurtain's 6.4 benchmark). If FlakCurtain is ever
  nerfed below 7.5 DPS, the cutter has to fall to 5.
- Shadow and Eagle both start at 24 DPS; the Eagle's edge is 16 hp and a full aft arc rather than firepower.
  Deliberate, or does the Eagle want a third gun?
- Raptor has no small mount and therefore no missile screen. Accept it as the broadside hull's price, or
  give it a small mount in a layout pass.
- The screen is **always on** — a support mount with a closing round on it takes no order. A key or handoff
  that lets a player stop the screen eating their chip DPS during a hull fight is still unexplored: it is one
  `Turret` flag and one control in the mount screen. The default is right because a support mount that is not
  chipping a hull is nearly free anyway, and a missile that lands is not.
- Five `FireRate` cards are ×2.01, which lifts FlakCurtain (8 → 16 DPS) above the kill class's floor of 10.4.
  R29 blocks the `Damage` card but not the rate; whether support growth should be capped as well is a playtest
  question, and a capped rate card on support guns is the blunt answer.
- Mine hp is still 2 and never scales, and pass 2 makes kill rounds fatter: a 15-damage RailGun slug on a
  2 hp mine wastes more than it did. Scaling mine hp with the tier curve is the fix, or accept snipers as
  anti-ship tools only.
