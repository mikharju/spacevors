# WEAPON_BALANCE.md

Status: shipped for passes 1-2 and for R32/R33 — the delivery/range ladders and identity cells (pass 1), the
kill/support class split with R24-R31 (pass 2), and the ×4 damage/hp resolution with percent `Damage` cards
(R32/R33, landed 2026-11-02 — see `## Shipped (2026-11-02): pass 3 steps 1-2` at the end of this file).
**Pass 3's remaining rules R34-R36 — band-scaled `Range`, a `Scatter` card, a `MissileAgility` card — stay
proposed.** §6 (armour) is **future work, not implemented**.
Rule numbers R1-R31 are stable identifiers, so their text above was *not* rewritten when R32 landed: it
carries the pre-rescale constants, and the Shipped section at the end restates every literal the tests hold
(cap 16, support envelope 32/24, class floor 40, chip licence 8, round-hp ceiling 12).

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

## Pass 3 (2026-11-02): the resolution pass — ×4 scale, percent damage cards, R32-R36

> **R32 and R33 shipped the day this section was written** — see `## Shipped (2026-11-02): pass 3 steps 1-2`
> at the end of this file for what landed, the deviations and the perf verdict. **R34-R36 below are still
> proposals and are not in the code.** Pass 2's `## Shipped` record above stays true as written; where pass 3
> withdraws or restates a shipped rule it says so by number, and the number itself is never reused.

### What pass 2 left broken

Three symptoms, one root cause. The damage scale is too coarse for the game pass 2 built:

1. **`Damage` is `Additive: 1`** (`UpgradeStats.cs:21`) on chips that deal 1 damage, so the card is a ×2 —
   which is why R29 had to ban it from support guns outright. The fix pass 2 chose (ban the card) works, but
   it makes the support class's card economy two cards wide (rate, reach) against the kill class's three.
2. **A 15-damage RailGun slug on a 2 hp mine** wastes 13 damage — 7.5× the target — and mine hp never scales.
   R5's overkill discipline is asserted on the table but leaks badly in play, because the smallest target is tiny.
3. **`+1` is the only damage step there is.** Nothing in the card economy can say "this is a big gun's
   upgrade": a flat 1 is worth 50% to a sniper and 100% to a chip, and pricing it per gun needs a pile of
   licence rules to stop the chip crossing the class line.

The fix is two moves. **Raise the resolution:** every damage and hp number ×4, which changes nothing about the
shape of the table because every rule is a ratio. **Make the damage card a percent of the base**, added not
compounded, which prices the *gun* instead of the *step* — and lets the licence machinery go away instead of
growing.

### R32 — the rescale: damage and hp ×4, nothing else

Every integer that deals or absorbs damage goes ×4. Rates, fire intervals, ranges, ammo speeds, radii, arcs,
slew rates and turn rates do **not** — the pass is resolution, not feel.

| ×4 | Now → after |
|---|---|
| Player gun `Damage`, all 19 guns | 1-15 → 4-60 (chips 1 → 4, needles 2 → 8, MachineGun 3 → 12, ScatterCannon/SiegeSalvo/LaserLance/PrismLance 16, RailGun 15 → 60, HavocTorpedo 10 → 40) |
| Player hull `MaxHealth`, 8 hulls | 8/10/12/14/16/20 → 32/40/48/56/64/80 |
| Enemy hull `Health`, then ×`HpMultiplier` | 5/7/15/22/30 → 20/28/60/88/120 (tier-10 HeavyCannon 105 → 420) |
| Enemy gun damage: base 1 + `DamageAdd(tier) = tier/2` | base 1 → 4, add → `2·tier` (tier-10 bullet 6 → 24 against a 32 hp Scout, as today's 6 against 8) |
| Enemy missile `MissileDamageAdd = tier/3`, rack `DamageBonus` | add → `4·tier/3`; bonus 0/1 → 0/4 |
| Round hp: player default / FarlockSalvo / ceiling | 1 / 2 / 3 → 4 / 8 / 12 |
| Round hp: enemy racks (Interceptor, HeavyCannon) | 2 / 3 → 8 / 12 |
| Mines (`MineType.Health`) | 2 → 8, **and now scales with tier** (`8 + 2·tier`, 28 at tier 10) — pass 2's last open knob, closed |
| Asteroids (`Asteroid.SmallHealth`) | 50 → 200 (still ~17 MachineGun hits, as today's 50 / 3) |
| `LootStats.HealthOrbHealAmount` | 3 → 12 |
| `MineStats.ContactDamage` | 3 → 12 |
| `ImpactDamage.ImpactScale`, `MaxImpactDamage`, `SparkDamageThreshold` | 7.2e-9 → 2.88e-8, 3 → 12, 2 → 8 (`MinClosingSpeed` 160 unchanged) |

**Card *steps* are not scaled ×4 — they are re-derived from the new bases, because a step is a fraction.**
`Hp` goes `+2 → +8` (still a quarter of a Scout's hull). `MissileHealth` stays `+1`: on a 4 hp round that is
+25% and honest, where on today's 1 hp round it is +100% — the rescale quietly fixes that card too. The
`Damage` card becomes a percent under R33. XP, level curve, spawn weights, pickup chances and the tier ladder's
speeds are untouched, so the pace of a run does not change. Round hp must scale with damage or the R28 screen
starts one-shotting every missile and R19's "a round eats several hits of ordinary fire" stops being true.

Mine scaling is the one thing this pass genuinely improves rather than rescales: a RailGun slug on a tier-10
mine goes from 7.5× the target's hp to 2.1×, so sniping mines stops being pure waste while still never being the
right tool.

**Every margin survives, unchanged.** Modelled against the live rule set at ×4:

- R1 beams: LaserLance 41.6 ≤ 48 (13% under, same as today), PrismLance 43.2 ≤ 53.8 (20%), RippleCutter
  24 ≤ 25.6 (6%), Stiletto 24 ≤ 53.8.
- R15 class-anchored: SeekerPod 14.4 ≤ 19.2, Swarmlet 16.8 ≤ 19.2, HavocTorpedo 24 ≤ 34.6, FarlockSalvo 21.6 ≤ 33.3.
- R3 within tier and delivery: medium Large MachineGun 48 ≥ 1.25 × InterceptorTurret 24 ✓; slow Large
  ScatterCannon 72 ≥ 1.25 × FlakCurtain 32 ✓; slow Large SiegeSalvo 67.2 ≥ 1.25 × AcidBubbleSpray 24 ✓.
- R4: ceiling 1.4 × 48 = 67.2 — SiegeSalvo sits at it exactly, as today; GravitonBurst 66 and RailGun 60 under ✓;
  floors unchanged. R12 hull ordering unchanged. R31 kill shares unchanged.
- R7 max 9 rounds/s per mount (budget 12) ✓ unchanged. R6 honest reach unchanged ✓. R16 turn radii unchanged ✓.
- **TTK unchanged**: the tier-10 HeavyCannon's 420 hp against the Scout's 72 DPS (MachineGun 48 + Interceptor
  Turret 24) is still 5.83 s, inside the 7 s budget that forced pass 2's Scout loadout change.

Absolute constants to restate when this lands, because the tests hold them literally: R5's per-shot cap
4 → **16** (licence list unchanged: RailGun 60, HavocTorpedo 40; SiegeSalvo, LaserLance and PrismLance sit at
the cap as they do today); R24's support envelope 8/6 → **32/24** (FlakCurtain, RippleCutter and Stiletto sit
at their caps, as today); R25's class floor 1.25 × 32 = **40** (weakest kill gun LaserLance 41.6 — ratio 1.30,
unchanged); R27's chip licence 2 → **8** damage per shot. R26 and R6/R7/R9/R13/R16-R23 do not move at all.

**Why ×4 and not ×2:** the damage card's percent step has to land on integers. At ×4 every damage base is a
multiple of 4, so +25% and +50% are exact on all nineteen guns (chips 4 → +1, needles 8 → +2, MachineGun 12 →
+6, RailGun 60 → +30) — no rounding drift, and the label never lies about what was bought. At ×2 a quarter of a
2-damage chip is 0.5 and the card's displayed value and its effect diverge. ×4 also gives the smallest numbers
in the game — round hp, mine hp, hull hp — enough headroom to carry their own scaling without collapsing into
one-hit things. What it costs: hp bars and the stats screen now read in the hundreds, and every absolute
hp/damage literal in the tests and the HUD moves.

### R33 — `Damage` becomes a percent card, additive on the base, sized by class

R29's damage half is **withdrawn**: every mounted gun is dealt a `Damage` card again, as a percentage of its
**table base** — **+25% for a support gun, +50% for a kill gun** — and each card adds
`round(base × percent)` where `base = WeaponType.For(name).Stats.Damage`. Two cards are therefore +50%, never
+56.25%: the base never moves, so additive-on-base is a property of the formula rather than a rule the code has
to remember. No counter field, no compounding state, no re-derivation from live stats. One helper,
`UpgradeDefinition.DamagePercentFor(bool isSupport)`, read by both the apply path and the label (R10: the number
on the card is the number bought).

**Should support get weaker upgrades? Yes — a quarter instead of a half, not a ban.** Banning the card was only
necessary while it was a flat `+1`, where one card was a ×2 on a chip. A percent is scale-safe: it prices the
gun, not the step, and the class difference is already sitting in the base — a 50% kill card buys three to six
times the DPS of a 25% chip card. The asymmetry is what makes the doctrine scale: with the same percent the
1.30 class ratio would stay pinned at 1.30 forever, and R25/R31 want the kill class to *pull ahead* over a run
— at eight picks MachineGun is ×5.0 and FlakCurtain ×3.0, so the ratio opens from 1.30 to 2.17. The named
alternative is one symmetric percent for everyone, which is simpler and holds the ratio exactly; it is rejected
because a chip's extra per-shot authority buys it nothing in role terms — it already screens, already chips, and
its growth is rate, reach and spread. Its screen does improve, which is a bonus rather than the point: a chip at
6 damage takes down a 12 hp rack round in two hits instead of three.

**No licence ceilings.** The earlier draft of this pass needed them (a flat add can cross a class line); a
percent cannot, and R5/R24/R25/R27 stay what they always were — rules on the weapon *table*. Deleting the
licence rules is the point of making the card relative. The ceiling stays a table rule, so a card-stacked chip
still walks through it by design: FlakCurtain with eight cards is 12 damage × 8/s = 96 DPS against a fresh
LaserLance's 41.6.

**Fire rate stays a non-issue next to damage, by construction.** `DPS = rate × pellets × base × (1 + Σpct)`:
there is no cross-term, so a rate card cannot change what a damage card is worth or vice versa. They stay
comparable and differently-timed: `FireRate` is the common card (+15%, `UpgradeStats.cs:16`) that also grows
live rounds and stops at R7's 12 rounds/s per mount and at R17/R18 for guided salvos; `Damage` is the milestone
card, dealt once every five levels (`UpgradeOffer.IsMilestoneLevel`, `MilestoneLevelInterval = 5`) against three
new-weapon cards in the same hand. Over one milestone interval four rate picks (×1.15⁴ = 1.75) beat one damage
card (×1.25 or ×1.50) *while R7 leaves headroom* — which is the honest shape: rate is the early, capped card and
damage is the late, uncapped one. How early depends on the gun: FlakCurtain (8 rounds/s) hits the R7 ceiling
after two rate cards, MachineGun (4/s) after seven.

R10's guidance is restated for this one card: a card dealt once every five levels may move DPS by up to +50%;
the 15-35% band stays for the cards that appear in every hand.

Long-run numbers worth watching: eight milestone picks into MachineGun is 12 + 48 = 60 damage, 240 DPS, and a
tier-10 HeavyCannon dies in 1.75 s — enemy hp stops compounding at `MaxTier` while the player does not, which is
the genre's power curve and the number a playtest should judge. The lever if it reads too strong is the 50% step,
not a cap.

New tests: `DamageCardsAddOnTheBaseAndNotOnEachOther` (MachineGun 12 → 18 → 24, explicitly not 27);
`DamageCardIsSizedByClass` (a Shotgun's next card is worth +1, a MachineGun's +6);
`PercentStepsAreExactOnEveryGun` (the ×4 invariant above — no rounding drift anywhere on the table).
R29's two support tests are deleted.

### R34 — `Range` gets band-scaled and loses its ceiling

One `Range` card, one multiplier today (×1.15, `UpgradeStats.cs:20`). It becomes **band-scaled**, keyed on the
band of the gun's **base** range so the step never compounds on its own growth:

| Base band | aura (< 750) | close (< 1100) | mid (< 1500) | long (≥ 1500) |
|---|---|---|---|---|
| Step per card | ×1.04 | ×1.06 | ×1.08 | ×1.10 |

Short-range guns get the weakest step because a range card on a 600 px gun is an identity change, not an
upgrade: a chip that walks its reach out stops being the thing that makes killing possible and starts being a
bad mid-range gun. A long gun that buys reach stays the same gun with more of it. Five cards, modelled: aura
600 → 730 (still aura), close 900 → 1204 (mid), mid 1350 → 1984 (long), long 1700 → 2738.

**No ceiling** (the user's instruction, and the right call): R29's support roof and its test are **withdrawn**,
and R26 becomes explicitly a base-table rule. A maxed Stiletto at 1984 px is a long-range needle that still
screens missiles — still support by every rule that matters, and it cost five cards to get there.

Two things make this safe without a ceiling. The card already multiplies `ShotLifetime` with `Range`
(`GameSession.cs:1181-1184`), so R6's honest-reach rule is invariant under it — reach always stays long enough
to cover the range it grants. And the label must show the gun's own step (`+4%` on a Shotgun, `+10%` on a
RailGun) or R10's "a card is never a lie" is broken on the spot; the step has to be resolved per option before
the menu renders it.

Perf: five cards are ×1.61 of lifetime against today's ×2.01, so this is **cheaper** in live rounds than the
card that ships today. A maxed Heavy is ~119 live rounds worst case against ~74 today — still two orders
under the 10k budget, and R7 is untouched.

### R35 — a new card: tighter spread

`UpgradeOption.Scatter`, **×0.8 on `Weapon.Scatter`**, offered only while the live scatter is **≥ 0.10 rad**
and the next step stays **≥ 0.08**. Eight of the nineteen guns have a cone wide enough to tighten: FlakCurtain
0.50, AcidBubbleSpray 0.45, Swarmlet 0.25, SiegeSalvo 0.22, InterceptorTurret 0.20, ScatterCannon 0.18,
Shotgun 0.12, FlechetteFan 0.10. The other eleven are never offered it — every beam, RailGun, GravitonBurst,
HavocTorpedo and SeekerPod at 0, MachineGun at 0.033, Cyclotron at 0.015, FarlockSalvo at 0.08 — because a card
that changes nothing is a lie (R10). The gate reads the live value, so the card spends itself: Shotgun and
FlechetteFan take one (0.12 → 0.096, 0.10 → 0.080), ScatterCannon three (→ 0.092), InterceptorTurret four
(→ 0.082), SiegeSalvo four (→ 0.090), Swarmlet and AcidBubbleSpray and FlakCurtain five (0.50 → 0.164).

What it really buys, honestly: **pellet convergence, whose value scales as 1/range.** Shotgun's three slugs at
0.12 rad land ~108 px apart at 900 px against a ~20 px hull — one hits; at 200 px a tightened cone lands all
three. So this is a close-range card, and it is the answer to R34 taking reach away from the guns that needed
it least: a close build buys accuracy, a long build buys distance. On a cone spray (FlakCurtain 0.50,
AcidBubbleSpray 0.45) it is a real trade — a narrower cone covers fewer angles — while the burst disc
(`AmmoRadius`) is untouched, so the flak still denies area.

It is a DPS upgrade in disguise on multi-pellet guns: more slugs on one hull is more damage on that hull.
That is accepted, and it is capped by the floor — no gun's cone ever tightens into a needle. Cost is nil:
`Scatter` is read once, at spawn (`TurretFiringSystem.cs:508`).

### R36 — a new card: missile agility

`UpgradeOption.MissileAgility`, **×1.15 on `Weapon.HomingTurnRate`**, for the four guided guns. It lands on
the rounds the mount fires *next*, never the ones already airborne, because the turn rate is copied into the
`Missile` component at launch (`TurretFiringSystem.cs:594`) — which is honest with the player and free.

**The guard is R16, read live.** A missile must still be out-turnable: the card is withdrawn when the next step
would put the **launch** turn radius under 46 px, which is the tightest tier-10 hull's radius (38.5 px, the
HeavyCannon — derived the way `WeaponBalanceTest.GuidedRoundTurnsWiderThanAnythingItHunts:97-113` derives it,
times a 1.2 margin). It must read the *live* ammo speed and turn rate, because a `ProjectileSpeed` card widens
the radius and an `Agility` card narrows it: only the combination can be checked, and it is exactly the sort
of rule a card economy breaks quietly.

Legal steps today: SeekerPod 112 → 48.6 px (6 cards), Swarmlet 116 → 50.0 (6), HavocTorpedo 250 → 81.6 (8),
FarlockSalvo 187 → 61.2 (8). Every missile ends its build still wider than the hull it hunts.

What it costs the player is a slot that used to buy speed or reach; what it costs the world is nothing per
tick (steering already clamps to `Missile.TurnRate`). Counter-play survives intact: the R28 screen does not
care how tight the arc is, ECM still detonates the round, and hull hp still absorbs it. It raises no speed, so
R16's second half ("radius widens as the round builds") and the R22 enemy ladder are untouched. Enemy racks
get no such card — the enemy has no card economy, and that asymmetry is a feature of the genre.

### What has to move when this lands

- `WeaponStats.cs` (every `Damage`, `MissileHealth` default, `MissileHealthCeiling` 3 → 12), `PlayerShipStats.cs`
  (`MaxHealth` ×8), `EnemyShipStats.cs` (five `Health` fields, `DamageAdd` → `2·tier`, `MissileDamageAdd` →
  `4·tier/3`, two `DamageBonus`, both `EnemyMissileSpec.RoundHealth`), `MineStats.cs` (`Health` 8 plus a
  tier-scaled spawn at `MineRespawnSystem.cs:45`, `ContactDamage`), `EntityComponents.cs`
  (`Asteroid.SmallHealth`), `LootStats.cs` (orb heal), `ImpactDamage.cs` (scale, cap, spark threshold).
- `UpgradeStats.cs`: `Damage` gains a percent instead of an `Additive` (with `DamagePercentFor(isSupport)` →
  25 / 50), `Range` loses its single multiplier in favour of `RangeStepFor(baseRange)`, `Hp` additive 2 → 8,
  two new `UpgradeOption` members (`Scatter`, `MissileAgility`), and a sign fix in `DisplayValue` — a ×0.8
  multiplier renders as `"+-20%"` today.
- `UpgradeOffer.cs`: R29's two filters deleted (percent needs no gate: a percent cannot cross a class line);
  the milestone deal goes back to one `Damage` card per mounted gun; the scatter card takes R18/R19's shape
  (`CanTightenSpread`) and the agility card a `CanTurnTighter` predicate that reads live speed and turn rate.
- `GameSession.ApplyUpgrade`: `Damage` becomes `+= round(base × DamagePercentFor(turret.IsSupport))` with the
  base from `WeaponType.For(name)`, not from the turret's live stats; `Range` step by base band; two new cases
  (`Scatter`, `MissileAgility`).
- `UpgradeMenuRenderer.cs:231+` and `StatsScreenRenderer.cs:254+`: labels and stat rows for the two new cards,
  the per-gun range step, and the per-gun damage percent — `+25%` on a Shotgun, `+50%` on a MachineGun, since
  the number the menu shows is the number the player is buying (R10).
- Tests: `WeaponBalanceTest` constants (R5 16, R24 32/24, R25 40, R27 8) plus the three new R33 tests;
  `EnemyDifficultyTest` and `EnemyMissileLadderTest` absolute hp/damage; mine and asteroid hp tests;
  `ImpactDamageTest`; `MissileGuidanceTest` round hp and the combined speed+agility radius check;
  `LevelUpSlotOfferTest`/`RerollOfferTest` hand shape (delete R29's two tests, add the percent, scatter-gate and
  agility-guard tests).
- Unaffected by design: `ThreatMeterTest`, `AimLeadTest`, `CollisionSystemTest`, `LaserTest`'s synthetic
  turret — they build their own stats.

### Order of work

1. **R32 alone.** Data and hp only, no card changes; every rule-test constant moves in the same commit. The
   suite is green with the same TTK and the same hit counts — the pass is only visible as bigger numbers.
2. **R33** the percent card (25% support / 50% kill), additive on the table base; delete R29's damage filter
   and both its tests; add the three percent tests.
3. **R34** the band-scaled range step; delete R29's roof and its test; fix the label.
4. **R35** the scatter card (with the `DisplayValue` sign fix).
5. **R36** the agility card and the live R16 guard.
6. Docs: this section → `## Shipped (date)`, the restated constants written into R5/R24/R25/R27 in place, R29
   and R10 revised to say what replaced them, PLAN.md line.

Steps 2-5 each stand alone; step 1 must land first, because the percent steps are exact only once every damage
base is a multiple of 4.

### Open knobs

- **A maxed chip beats a fresh sniper.** FlakCurtain at eight cards is 96 DPS against LaserLance's fresh 41.6.
  Intended under R33 (the ceiling is a table rule), and the playtest question is whether the kill/support *read*
  survives it; the lever is the support percent (25%), not a DPS cap.
- **How far the late game runs away.** Enemy hp stops compounding at `MaxTier` while damage cards do not: eight
  picks on MachineGun is 240 DPS and a 1.75 s HeavyCannon. That is the genre's curve; if it reads wrong, shrink
  the kill percent (50 → 40) before inventing a cap — the percent is the whole lever and the exactness survives
  as long as bases stay multiples of 4 (40% of 60 is 24 ✓, of 12 is 4.8 ✗, so a 40% step would reintroduce
  rounding drift: prefer 25/50, 25/75 or 50/50).
- **Rate and damage compose.** Both multiply DPS, so the worst case is both: FlakCurtain with five `FireRate`
  cards (×2.01, which is where R7 leaves it) and eight `Damage` cards (×3.0) is 32 × 6.03 ≈ 193 DPS. Nothing in
  this pass caps that pair; R7 caps the rate half, the milestone interval caps the damage half. If it reads too
  strong, the rate card on support guns is the one to shrink — it is the common card and the less legible half.
- **Percent is already in the card economy for round health** (see `## Shipped (2026-11-02)` at the end of this
  file): `MissileHealth` buys 50% of the gun's base round health, added. `Damage` (R33) and `Hp` are the two
  cards left with a flat step; making `Hp` a percent of the hull's base health is the same one-line shape, and
  it needs R33's base anchor to avoid compounding.
- **The scatter card is strictly good on sprays.** Tightening AcidBubbleSpray's 0.45 rad cone looks like pure
  gain until a swarm arrives and the coverage is missed. If playtest says it is pure gain, the answer is a
  smaller step on single-pellet guns (×0.9) — not a higher floor.
- **Close guns migrating bands by card 4** (Shotgun 900 → 1134 px) is intended under R34, but nothing asserts
  a card-stacked gun's band today. If it starts to matter for the threat display, assert the band on the table
  only and say so.
- **The `Hp` card stays additive, at `+8`** — a quarter of a Scout's 32 hp, a tenth of the biggest hull's 80, and
  that drift is unchanged by the rescale (today's `+2` has exactly the same spread). Making it a percent card
  would fix the drift, but it needs the same base anchor as R33 (`MaxHealth` from the hull table, not the live
  value) or it compounds like the old `+1` did; worth doing only if the card reads weak on the big hulls.

## Shipped (2026-11-02): the `MissileHealth` card became a percent of the base round health

**What landed.** `UpgradeDefinition.MissileHealth` is `PercentOfBase: 50` where it was `Additive: 1`
(`UpgradeStats.cs`). `UpgradeDefinition` gained a `PercentOfBase` field and `StepForBase(baseValue)`: a
percent of the stat's **table** value, added, floored at 1 hit point so the card can never round down into a
card that does nothing (R10). `WeaponType.MissileHealthStep` reads the gun's base round health off the table,
so the step is the same whoever has already bought the card — two cards are +100% of the base, never +125%.
The offer gate became `WeaponStats.CanToughenRound(step)`: the next **whole** step has to land at or under
R19's ceiling, which is exact where the old `hp < ceiling` test could sell a step the ceiling would refuse.
The menu reads `+50%` from `DisplayValue`, which now knows three card shapes (`+50%`, `+15%`, `+2`).

**Why ship a change that changes nothing today.** The table's guided rounds carry 1 hp (SeekerPod, Swarmlet,
HavocTorpedo) and 2 hp (FarlockSalvo), so a 50% step is 1 hp on all four and the card behaves hit-for-hit the
way it did. What changed is that the step is now *derived from the missile* rather than fixed: the same card
on a 4 hp round buys +2 and on an 8 hp round +4 — which is what R32's ×4 rescale needs, so a FarlockSalvo's
rack round does not end up as tough per card as a SeekerPod's. Landing it before the rescale costs nothing,
and it keeps R19's ceiling arithmetic honest through it (a base-2 round reaches the ceiling in one card, a
base-1 round in two — a tougher round is closer, and each card buys it the same honest step).

**Notes.**
- Nothing tested this card before — R19's was the one upgrade path with no test at all. Three went into
  `LevelUpSlotOfferTest`: the step, its floor and its label
  (`MissileHealthCardIsAPercentOfTheBaseRoundHealth`); that cards stack on the base and land exactly on the
  ceiling, with fewer cards for the gun that starts tougher
  (`MissileHealthCardsStackOnTheBaseAndLandOnTheCeiling`); and that the card is withdrawn at the ceiling but
  still dealt a hit point below it (`GuidedRoundsAtTheirHealthCeiling_AreNotOfferedMoreToughness`).
- Suite green at 550. No scenario run: the change is offer-time and card-time. The firing path still reads
  `turret.Weapon.MissileHealth` once at round spawn (`TurretFiringSystem.cs:589`), so nothing in the tick loop
  moved — there is nothing here to measure.
- `Damage` and `Hp` are now the only cards with a flat step; both become percents under R32/R33, on the same
  base anchor.

## Shipped (2026-11-02): pass 3 steps 1-2 — the ×4 scale (R32) and percent damage cards (R33)

**What landed.** Every integer that deals or absorbs damage went ×4; nothing else moved.

| Where | Before → after |
|---|---|
| `WeaponStats.cs` — 20 gun rows' `Damage`, and the record default | chips 1 → 4, needles 2 → 8, ScatterCannon 2 → 8, MachineGun/GravitonBurst/FarlockSalvo 3 → 12, SiegeSalvo/LaserLance/PrismLance 4 → 16, RailGun 15 → 60, HavocTorpedo 10 → 40 |
| `WeaponStats.cs` — round hp | default 1 → 4, FarlockSalvo 2 → 8, `MissileHealthCeiling` 3 → 12 |
| `PlayerShipStats.cs` — 8 hulls | 8/10/12/14/16/20 → 32/40/48/56/64/80 (the Fighter the HUD shows is **40**) |
| `EnemyShipStats.cs` — hulls, ladder, racks | 5/7/15/22/30 → 20/28/60/88/120 (tier-10 HeavyCannon 105 → 420); `BaseDamage` 1 → 4; `DamageAdd` `tier/2` → `4·(tier/2)` (tier-10 bullet 6 → 24); `MissileDamageAdd` `tier/3` → `4·(tier/3)`; rack rounds 2/3 → 8/12 with `DamageBonus` 0/1 → 0/4 (tier-10 HeavyCannon round 5 → 20) |
| `MineStats.cs` | hp 2 → 8 **plus the tier walk R32 asked for**: `HealthBase` 8 + `HealthPerTier` 2 → 28 at the cap; contact damage 3 → 12. Spawned through `MineRespawnSystem`, so pass 2's last open knob is closed |
| `EntityComponents.cs`, `LootStats.cs` | asteroid 50 → 200 (still ~17 MachineGun hits); health orb 3 → 12 |
| `ImpactDamage.cs` | scale 7.2e-9 → 2.88e-8, `MaxImpactDamage` 3 → 12, `SparkDamageThreshold` 2 → 8 (`MinClosingSpeed` 160 unchanged) |
| `UpgradeStats.cs` — card steps | `Hp` +2 → **+8** (still a quarter of a Scout's hull); `Damage` flat +1 → **percent of the base**; `MissileHealth` +50% of the base, which at these bases finally buys **+2** on a pod round and **+4** on a FarlockSalvo round |

The enemy ladder keeps its integer division *inside* the ×4 (`4 · (tier / 2)`, not `2 · tier`) so the ladder stays
the same run of values at four times the size — tier-10 bullet 24, tier-10 rack round 20 — instead of drifting.

**R33.** Every mounted gun is dealt a `Damage` card again at a milestone: **+25% of the table base for a support
gun, +50% for a kill gun**. `WeaponType.DamageStep` reads the base off the table and `UpgradeDefinition` carries
`PercentOfBase`/`PercentStep` shared with the round-health card, so cards add on the base (+50% twice is +100%,
never +125%) with no counter and no compounding state. The menu reads the percent off the gun (`+25%` / `+50%`),
which is what R10 asks: the number on the card is the number bought. R29's damage half is withdrawn; **its
range-roof half stays** until R34 replaces it.

**Constants the tests hold literally, restated (they supersede the numbers in the R5/R19/R24/R25/R27 text
above, which is left as written):** R5's per-shot cap **16** (licence list unchanged: RailGun 60, HavocTorpedo
40; SiegeSalvo, LaserLance and PrismLance sit at the cap as before); R19's round-hp ceiling **12**; R24's support
envelope **32 / 24** in the mid band (FlakCurtain, RippleCutter, Stiletto sit at their caps); R25's class floor
**40** against LaserLance's 41.6 (ratio 1.30, unchanged); R27's chip licence **8** damage a shot. R26 and
R6/R7/R9/R13/R15-R23 do not move at all, and **TTK is exactly what it was**: the tier-10 HeavyCannon's 420 hp
against the Scout's 72 DPS is 5.83 s.

**What did not move:** fire rates, ranges, ammo speeds, scatter, kickback, lifetimes, radii, arcs, slew and turn
rates, the XP curve, spawn weights and pickup chances, the tier ladder's multipliers, and therefore every ratio,
band, identity cell, class line and missile screen. The pass is resolution, not feel — a playtest should feel the
same fight with finer numbers on the bars.

**Rounding, honestly.** Impact damage is ceiling-rounded, so its numbers are not exactly four times the old
*rounded* ones: at equal masses 200 px/s went 0 → 2, 300 → 1 to 4, a 400 px/s pilot's ram 1 → 3, the tuned big
Scout hit 3 → 12 (it now lands on the cap), and an enemy driven into an asteroid 3 → 11. Everything else is
exact: hp, the enemy ladder, the racks, the mines, the asteroid, the orb, and every card step (R33's 25/50
divide each ×4 base exactly — asserted by `DamageCardPercentsDivideEveryGunBaseExactly`, so no card ever
drifts across a long run).

**Tests: 557 green.** Deleted `SupportGun_IsNeverOfferedADamageCard` (R29's damage half is gone); added
`MilestoneHand_DealsADamageCardToEveryMountedGun`, `DamageCardIsSizedByItsMountClass`,
`DamageCardsAddOnTheBaseAndNotOnEachOther` (12 → 18 → 24, explicitly not 27) and
`DamageCardPercentsDivideEveryGunBaseExactly` — the latter two are R33's `DamageCardIsSizedByClass` and
`PercentStepsAreExactOnEveryGun`, named for what they assert; `MineHp_RampsWithTheDifficultyTier` and
`LateMinesSpawnTougherThanOpeningMines`; restated the hull/enemy/mine/asteroid/orb/impact/round-hp literals and
the R5/R24/R27 ceilings. The collision fixture's 10 hp hull gained an `hp:` parameter, because at 12 contact
damage one large mine would otherwise kill it before the second hit could be counted.

**Perf verdict (2026-11-02): costs nothing measurable — keep.** Paired A/B against HEAD (`61df011`) in a second
worktree, `.tools/scenario.sh <s> --sim --iters 3`, same host: entity means identical to 0.1 (ammo-spray 11466.4,
impact-storm 23833.7, hull-stream 6250.2 — so no lifetime or churn changed), allocations identical to 0.1 KB/frame
(290.7 / 745.4 / 170.2), per-entity ns 218.8 → 225.5, 145.4 → 152.0, 291.9 → 300.8. The hull-stream pair was
repeated twice more and came out the other way (median 0.92 → 0.84 and 0.86 ms, per-entity 162.6 → 156.7 and
162.7), which is the size of the host's noise. Every run reported `budget pass`.

**Supersedes two lines above.** The missile-health record said `Damage` and `Hp` were "the two cards left with a
flat step" and described the ×4 scale as something that would land later: `Damage` is a percent now, and the
scale has landed. `Hp` remains the game's only additive card.
