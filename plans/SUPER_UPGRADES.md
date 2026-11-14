# SUPER_UPGRADES.md — one super step on each side of every hand

Status: proposed — depends on plans/UPGRADE_BUNDLE_PAIRS.md (shipped, 2026-11-03) for the hand shape and on
plans/PREUPGRADED_NEW_WEAPONS.md (proposed) for D5; if that plan never ships, D5 falls away and D1-D4, D6-D8 stand
alone. PLAN.md carries this idea as one line under *Proposed, not implemented*; when this ships, that line becomes
the index entry and this header becomes `shipped`.

## Part 1 — the plan

### 1. The problem

Five bundles, and every bundle is the same size. A level's decision is *which direction*, never *how big* — the
player has no way to buy a bigger step, and plans/UPGRADE_BUNDLE_PAIRS.md §7 left exactly that as the open
question: one powerful half a level is the damage cadence the 2026-11-03 playtest called slow against the enemy
ladder. Its knob 2 was "make `Damage` an ordinary strong half", which spends R33 to fix pacing.

The other half of the complaint is legibility: a hand of five pairs reads flat. Nothing on screen says *this one
is the spike*, so the player compares ten small numbers instead of choosing between two obvious offers.

### 2. The insight

Put a **super** half on each side of the hand: one bundle carries a super **powerful** half, another bundle
carries a super **weak** half. Two of the five bundles stand out, one on each row, and because they sit in
*different* bundles the spike is never free — taking the big kill-gun step means taking the ordinary chip step
with it, and vice versa. The decision stays "which direction" and gains "how big, in which class".

A super half is **the same card at twice the step**, not a different card. One number, printed on the card,
granted from the same resolver. The "two different upgrades on one side" flavour is not folded into a stat card:
it already exists on exactly one card kind, the new-weapon card, whose arriving set is a *set* of upgrades
(plans/PREUPGRADED_NEW_WEAPONS.md) — and making that set bigger at a milestone is the spike that plan asked for.

### 3. The decisions

**D1 — one super half per side, in two different bundles.** A hand of five carries exactly one super powerful
half and exactly one super weak half, never both in one bundle. A bundle therefore carries at most one super
half, and the shipped line "the card above is the bigger step" is reworded to what is actually true (D8). Where
a side has no legal super candidate (D3, D5) that side simply gets none: fewer spikes, never a fake one.

**D2 — super = twice the step, one multiplier (`SuperStepMultiplier = 2`), applied to the delta:**

| card | ordinary | super | bound that still bites |
|---|---|---|---|
| `FireRate`, non-growing volley | ×1.15 | ×1.30 | — |
| `FireRate`, growing volley | +1 shot | +2 shots | R17/R18 round budget, re-checked for **two** shots |
| `Range` | ×1.15 | ×1.30 | R29 support roof read at ×1.30 |
| `ProjectileSpeed` | ×1.3 | ×1.6 | — |
| `ArmorPierce` | +4 | +8 | `PierceCeiling` 8 → only ever dealt at pierce 0 |
| `MissileHealth` | +50 % of base | +100 % of base | `MissileHealthCeiling` 12, re-checked at the doubled step (R19) |
| engine / `PickupRadius` | ×1.1 / ×1.2 | ×1.2 / ×1.4 | — |
| `Hp` | +8 | +16 | not dealt at milestones (shipped) |
| `Armor` | +1 ordinary / +2 milestone | **+2, ordinary levels only** | `ArmorCeiling` 12 |
| `Shield` | +8 / +16 | **+16, ordinary levels only** | `ShieldCeiling` 32 |
| `Damage` | +25 % / +50 % of base | **never super** | R33 |
| new gun | arrives with ⌈least/2⌉ upgrades | arrives with **`least`** | its legal-stat count (D5) |

A multiplier card's delta doubles, so ×1.15 becomes exactly ×1.30 and prints `+30 %` — the top of R10's 15-35 %
band, not a new band. That is the arithmetic reason the multiplier is 2 and not 3.

**D3 — a super never stacks on a card that already carries a milestone-sized step.** No super `Damage` card
(R33 restates R10 for it: a card dealt once every five levels may move DPS by up to +50 %; ×2 of +50 % is a rule
violation, not a knob). Defence is super **only on an ordinary level**, where the super step *is* the milestone
step: `+1` → `+2` armour, `+8` → `+16` shield. The two ladders then meet at one number and the ceiling ladder
(12 / 32) is untouched — a doubled-doubled `+32` shield card would max the pool from nothing.

**D4 — the grade rides on the card, so a super card is a different card.** `UpgradableOption` grows a
`CardPower { Base, Super }` field beside the existing `Tier` and `PreApplied`, for the same reason they do: every
"same card / same pair / same hand" rule in `UpgradeOffer` compares whole records, so grading becomes
super-aware in `strong == weak`, `IsDealt`, the identical-pair refusal and the reroll's `SameBundles` without
touching any of them. Consequence, accepted: one hand can hold `Range:HeavyCannon +15 %` and
`Range:HeavyCannon +30 %` in different bundles — an honest pair of offers, and the per-gun per-side caps
(`NamesGunTimes`) still count by gun name.

**D5 — the spike on a new-weapon card is a bigger arriving set.** A super new gun arrives with as many upgrades
as the **least**-upgraded gun it would displace, where an ordinary one arrives with half of that
(plans/PREUPGRADED_NEW_WEAPONS.md D2), still capped by the gun's legal-stat count and sampled through the same
gates. Eligible only where the ordinary arriving count is already > 0, i.e. a hull with no free mount of that
size — which on the dealt hand means milestone hands. This is "more pre upgrades on milestone new weapon cards",
and it is the milestone side's spike now that `Damage` is out (D3).

**D6 — legality is re-read at the doubled step before a card is marked, and an illegal card is skipped, never
half-marked.** R10 is the reason the gates cannot keep reading the constants: a support chip at range 1150 takes
an ordinary `Range` (1322 ≤ 1350) and a super one at 1495, past R29's roof; a guided volley one shot below its
budget cannot take two. So the ceiling gates take *the step this card grants* — `LegalStats`, `CanToughenRound`,
`CanPierceMore`, the volley budget, the defence headroom. Skipping is the shipped rule (PAIRS Shipped
deviation 2: withdraw rather than relabel).

**D7 — marking is a pass over the dealt hand, after the distinct and fill passes and before the final shuffle,
walking bundles in deal order.** No new RNG in the pass, so existing seeded tests keep their first-pass contents
and the existing `Shuffle(bundles, rng)` hides which bundle was first. The one exception is D5: extending an
arriving set draws from `view.Rng`, at most one extra sample a hand — documented precedent,
plans/REROLL_TOKENS.md D3. Marking the hand rather than the pools is what keeps every pool rule (per-class caps,
per-gun caps, promises, repeats) untouched.

**D8 — the card says so.** A super half prints the word and its own number: `super Machine Gun range` / `+30 %`,
`hull armor` / `+2`, `new weapon Rail Gun — with rate, damage, pierce` / `large`. The number comes from the one
step resolver, so `super` is never decoration over an ordinary grant (R10). `MenuCopy.UpgradeLines` stops
claiming the top card is the bigger step — it is now the *kill-class* step — and gains the one line the player
cannot guess: one of the five bundles is spiked big, another spiked small.

**D9 — the scripted bench opts in per entry.** A bench hand is one bundle and never goes through the marking
pass, so entries stay ordinary and existing benches do not shift; a trailing `*` (`FireRate:MachineGun*`) marks
an entry super, which is how the grant math and the ceiling gates get benched. A milestone-stamped defence entry
loses its `*` in the same place it already gains its tier (`StampDefenceTier`), so D3 holds for benches too.

### 4. What it costs

- **Upper bound on the buff: one extra ordinary card's worth of step per level.** The two supers sit in different
  bundles and a bundle is taken whole, so the best a player can do is take one super a level — roughly +50 %
  upgrade intake if they always do. That is the pacing lever PAIRS §7 asked for, and it costs no rule: R33's
  percents, the defence tiers and every ceiling stay where they are.
- **The ceiling ladder runs faster, and stops itself.** `+16` shield against a 32 pool means two super buys max
  it; `+2` armour against 12 means six. Once the whole doubled step no longer fits the headroom the card leaves
  the deal (D6), so a spiked defence card is an early-and-mid-game offer, not a permanent one.
- **`+8` pierce against a ceiling of 8** makes a super pierce card a once-per-gun offer at best; in practice it
  just isn't dealt. Honest, and cheaper than moving `PierceCeiling`.
- **R25's class line is untouched**: both classes' cards double by the same factor, and the class-sized `Damage`
  cards are excluded (D3), so the kill line still grows faster than the chip line.
- **The arriving-set arithmetic cannot compound.** `WeaponUpgrade.CountOn` counts *cards*, and a super card is
  one card; an arriving upgrade is always one ordinary card of its stat (PREUPGRADED D3). A super card therefore
  never makes the next new gun arrive with a bigger set.
- Playtest, not the sim, judges the rest: read the `[UPGRADE]` rows (they print the grade) against `[THREAT]`
  over 15 minutes, the way PAIRS §7 did. Knobs, in the order to reach for them: (1) spike only alternate hands
  (`level % 2`), one line, halves the intake; (2) allow super `DamageSupport` at milestones only (+25 % → +50 %
  of base), which restores a second spike to milestone hands without touching the kill line; (3) `SuperStepMultiplier`
  — but note ×1.5 puts `Range` back at +22 % and stops being a *step*, it is a third card size.

### 5. Deliberately not here

- **A card that grants two different stats.** A bundle *is* the two-card unit; a super new gun already carries
  several upgrades; and a stat card with two numbers in a row that holds one is the legibility change this plan
  is trying to avoid.
- Weapon evolutions (PLAN.md future), more than two supers a hand, three-card bundles, a super *choice*
  (paying XP or a reroll charge for the spike) — the spike is a property of the deal, not a shop.
- Super hull/engine *values* decoupled from the card steps: every step in D2's table is the shipped definition
  read twice, and a second set of numbers is a second thing to keep honest.

## Part 2 — implementation memo

Written for an implementer with no memory of this conversation. **Cite symbols, not line numbers**: the tree is
being edited concurrently by plans/PREUPGRADED_NEW_WEAPONS.md. Re-read each file before editing it.

### Mechanics

**One step resolver.** Today "the printed number and the granted number are one value" is a convention across
`UpgradeDefinition.DefenceStep`, `DisplayValue`, `WeaponType.DamageStep`/`MissileHealthStep` and
`UpgradeMenuRenderer.GetUpgradeValue`. Add the resolver those four agree on, in `UpgradeDefinition`:

```csharp
public const int SuperStepMultiplier = 2;
public UpgradeDefinition Doubled() =>                                  // delta ×2, never the value ×2
    this with { Multiplier = 1f + (Multiplier - 1f) * SuperStepMultiplier,
                Additive = Additive * SuperStepMultiplier,
                PercentOfBase = PercentOfBase * SuperStepMultiplier };

public static UpgradeDefinition StepOf(UpgradableOption card)          // stat def + defence tier + grade
```

`StepOf` folds the defence tier into `Additive` (so `DefenceStep` becomes a case inside it), leaves `Damage` with
no percent in the definition as today, and applies `Doubled()` when `card.Power == CardPower.Super`. Every
reader — `GameSession.ApplyUpgrade`, `UpgradeMenuRenderer.GetUpgradeValue`, and the offer's gates — calls it.
`DisplayValue` formats a ×1.30 definition as `+30 %` (rounding is `MathF.Round`, so the float noise of
`1 + 2 × (1.15 − 1)` is invisible).

The two table-derived steps get the grade instead: `WeaponType.DamageStep` and `MissileHealthStep` gain a
`CardPower` (or take the definition percent) — a super `MissileHealth` card's step is
`PercentStep(base, 100)`. `Damage` never reaches that path with `Super` set (D3), but keep the parameter rather
than special-casing the enum in two places.

**Step-aware gates.** `WeaponUpgrade.LegalStats(gun, live, pierceUnlocked, CardPower power)` reads its three
numeric gates from `StepOf`: the R29 roof multiplies by the card's own `Range` multiplier, `CanToughenRound` and
`CanPierceMore` get the card's own step. Add `WeaponStats.CanGrowSalvoBy(int shots)` (the `CanGrowSalvo` test
with `PelletCount + shots`, so the homing `HomingRoundBudget` check bounds a super "+2 shots") and use it from
both the gates and D6. An arriving set (PREUPGRADED) always asks with `CardPower.Base` — an arriving upgrade is
one ordinary card of its stat.

**The marking pass** (`UpgradeOffer`, one new method called between `TopUp` and `Shuffle`):

```csharp
const int MaxSuperHalvesPerSide = 1;
static void MarkSuperHalves(List<UpgradeBundle> hand, WorldView view, Entity player, int level)
```

Walk the hand for the first bundle whose powerful half is `SuperCandidate(..., powerful: true)` and replace that
half with `card with { Power = Super }` (for a new-weapon card: also with a re-sampled `PreApplied`, below);
then walk from the next index, wrapping, for the first bundle — **other than that one** — whose weak half is a
candidate. Order of `IsPowerfulCard` is unchanged: grading never moves a card between sides.

`SuperCandidate` per kind, all cheap and all step-aware:

- gun stat card: `WeaponUpgrade.LegalStats(gun, liveTurret, pierceUnlocked, Super)` still contains the stat;
  `Damage` refused outright (D3);
- `Hp`: always (never dealt at milestones anyway);
- `Armor` / `Shield`: only when `!IsMilestoneLevel(level)` **and** the doubled step still fits the headroom —
  the same `current ≤ ceiling − step` shape `HullDefenceOptions` already uses;
- utility (engine, `PickupRadius`): always;
- new-weapon card: only when a bigger set is strictly bigger —
  `StatsOf(SuperArrivingSet(gun, ...)).Count > StatsOf(card.PreApplied).Count`, which is false once the ordinary
  set already covers every legal stat.

`ArrivingUpgrades(view, player, size, power)`: `Base` → `(least + 1) / 2` (shipped), `Super` → `least`, both
still 0 while a mount the gun fits is free and capped by the legal-stat count inside `ArrivingSet`. Sample the
super set **fresh** from `view.Rng` at marking time rather than pre-sampling a longer list at deal time: it costs
at most one draw a hand and needs no hidden deal-time state. Accepted consequence: a super new gun is a *bigger*
build of that gun, not necessarily a superset of the ordinary build two bundles over — the card prints its own
set, so the two reads cannot be confused.

**Grant path.** Nothing structural: `NewWeaponGrant.TryGrant` builds the carried turret from
`card.PreApplied` and counts one upgrade per stat bit, so a bigger set is free there. `GameSession.ApplyUpgrade`
must route each per-gun case through a card-aware `WeaponUpgrade.ApplyStat(turret, card)` (keep the
`turret, UpgradeOption` overload for arriving sets) and take the hull-wide steps from `StepOf(card)`.
`UpgradeCounts.Increment` still fires once per card — a super card is one card, and the count column of the stats
screen stays a count of cards, not of steps.

**Renderer.** `GetUpgradeValue` reads `StepOf(option)`, keeps the volley special case and prints `+2 shots` when
the card is super; `GetUpgradeLabel` prefixes `super ` — keep it in the label, not only in the spec block, which
is skipped on a narrow card (the same reason the arriving set is in the label); `NewWeaponSpecs` needs no change,
it already prints range and damage off `WeaponUpgrade.Apply(CarriedTurret, PreApplied)`, which is where a super
new gun's bigger set lands. No geometry, no new fonts, no new hit targets — the card→bundle mapping stays by
index. `MenuCopy.UpgradeLines` changes per D8.

**Diagnostics.** `FormatChoice` marks the grade (`FireRate:MachineGun(super)`, `Armor(+2 milestone)` stays as
is), so `[UPGRADE]` rows show which halves were spiked; add the `*` suffix in `ParseEntry` and mention it in
DIAGNOSTICS.md. `TryReroll`'s one-retry rule needs nothing: `SameBundles` compares graded records.

### Invariants to keep

1. **A card is never a lie (R10).** The printed number, the granted step and the gate that withdrew the card all
   come from `StepOf` / the step-aware gates.
2. **At most one super half per bundle; at most one per side per hand; the two are in different bundles.** A side
   with no legal candidate gets none — never a supered card that is illegal at its step.
3. **No super `Damage`, no super defence at a milestone** (D3), for dealt *and* scripted hands.
4. **Every shipped deal rule is unchanged**: five bundles, one powerful + one weak half, distinct pairs, repeats
   allowed, per-class and per-gun caps, the `FallbackPair` exception, the level consumed only when a hand exists.
5. **Ceilings bind the super step, not the ordinary one**: armour 12, shield 32, pierce 8, round hp 12, support
   range 1350 px, guided round budget 4/s.
6. **The grade is part of card equality**, so distinctness, repeats and reroll comparison are super-aware for free.
7. **A re-deal obeys every rule**, drawing only from `view.Rng`. Never `Random.Shared`.
8. `CargoTest.FreshlyCarriedWeaponStartsAtItsBaseStats` stays green: the arriving set is a step on top of
   `TurretMount.CarriedTurret`, graded or not.

### Tests

New file `SuperUpgradeTest`, plus additions to `HandCompositionTest`, `WeaponUpgradeTest`,
`PreUpgradedNewWeaponTest` and `BundleChoiceTest` (fixtures: `HandCompositionTest.CreateHull`,
`LevelUpSlotOfferTest`'s `FullyBookedHull`).

- `Hand_CarriesOneSuperPowerfulHalfAndOneSuperWeakHalfInDifferentBundles` — sweep the hulls × levels 2-21; where
  a side has no candidate, assert zero for that side rather than an illegal spike.
- `Bundle_NeverCarriesTwoSuperHalves`, `Hand_NeverCarriesTwoSuperHalvesOnTheSameSide`.
- `SuperCard_StillPassesEveryLegalityGate` — a support chip near the R29 roof is never supered on `Range`; a
  guided volley one shot under its budget is never supered on `FireRate`; a round at the hp ceiling and a gun at
  pierce 4+ are never supered on those stats. Write these before the marking pass lands: they are the R10 tests.
- `DefenceCard_IsSuperOnlyOnAnOrdinaryLevel_AndGrantsTheMilestoneStep` (armour 11 → no super card; armour 9 →
  `+2` and the hull ends at 11), and the shield mirror at 16/32.
- Table-driven `SuperCard_PrintsTheStepItGrants` over every eligible stat family: deal → take → read the live
  stat, assert delta == 2 × the ordinary step == the printed number. Include the growing-volley `+2 shots` and
  `PercentStep` cards, where doubling the percent is not doubling the granted integer.
- `NewGun_SuperArrivesWithAsManyUpgradesAsTheLeastUpgradedGunItDisplaces` — booked hull at L5/10/15, assert
  `StatsOf(PreApplied).Count == min(least, legalStats)`, and the ordinary card in the same hand is
  `min(⌈least/2⌉, legalStats)`; `NewGun_IsNeverSuper_WhenAMountItFitsIsFree`.
- `GradeIsPartOfCardIdentity` — `Range:Gun(base) != Range:Gun(super)`, `Hand_NeverCarriesAnIdenticalPair` and
  `EveryBundle_IsOnePowerfulHalfAndOneWeakHalf` stay green untouched; `RerollOfferTest`'s same-hand retry still
  compares whole hands.
- `BenchEntry_WithStar_GrantsTheDoubledStep_WithoutItTheOrdinaryStep`, and
  `BenchDefenceEntry_AtAMilestone_IsNotSuper`.
- `HandCompositionTest` sweep additions: a spiked half never crosses `MaxStrongCardsPerGun` /
  `MaxWeakCardsPerGun` (the caps count by gun name, so this asserts it rather than assumes it).

### Determinism, RNG, perf

No RNG in the marking pass; at most one extra `view.Rng` sample a hand for a super new gun, so seeded runs shift
in their *grades*, not in their first-pass cards (precedent: plans/REROLL_TOKENS.md D3,
plans/UPGRADE_BUNDLE_PAIRS.md §6). `WorldRngTest` pins spawn determinism only, so it stays green.

Measure per AGENTS.md: `.tools/scenario.sh ammo-spray --sim --iters 3` — expect unchanged (the offer path is not
reachable inside a gated sim, TROUBLE_SHOOTING.md #71; the render change is a word per spiked card). Price the
real change the way PAIRS did: a `SPACEVORS_DIAGNOSTIC=1` window run reading the `[UPGRADE]` rows at an ordinary
level and at L5/L10 — a `(super)` row on each side, in two different bundles — then confirm the Tab stats screen
and the live turret show the doubled step.

### Commit sequence (tree green each time)

1. `StepOf` + `Doubled` + the step-aware gates, with `SuperCard_PrintsTheStepItGrants` and the gate tests
   written against `CardPower.Super` cards built by hand. No deal change yet: nothing can be super.
2. `CardPower` on `UpgradableOption`, `MarkSuperHalves`, `FormatChoice`, the shape tests.
3. New-gun super set (D5) — land after plans/PREUPGRADED_NEW_WEAPONS.md; if it has not landed, ship 1-2 and note
   here that D5 is parked.
4. Renderer label + `MenuCopy.UpgradeLines`, live window check.
5. Bench `*` suffix, DIAGNOSTICS.md, this file → `Status: shipped` + `## Shipped (YYYY-MM-DD)` with the observed
   deals and the perf verdict, and PLAN.md's one-line entry rewritten to the shipped form.

### Docs to touch when it ships

- This file's header and `## Shipped`, plus PLAN.md's *Proposed* line for super upgrades → an index line under
  the upgrade-plan cluster.
- `plans/UPGRADE_BUNDLE_PAIRS.md`: append a dated `## Follow-up` — §7's pacing question and `MenuCopy`'s "the
  card above is the bigger step" are what this answers and rewords. Do not rewrite its body.
- `plans/PREUPGRADED_NEW_WEAPONS.md`: append a dated `## Follow-up` when D5 lands (its arriving count is now
  graded); and `plans/UPGRADE_BUNDLE_DUPLICATES.md`: a follow-up noting a supered half is a different card, so
  its repeat rules compare grades.
- `plans/ARMOR_AND_PIERCE.md` and `plans/SHIELDS.md`: the super defence step is the milestone step, so the step
  ladders gain one rung and the ceilings do not move.
- `plans/WEAPON_BALANCE.md`: a dated follow-up recording that the super step is inside R10's band and that R33's
  `Damage` cards are excluded from it (D3). New cross-cutting wording goes in that plan, not in ARCHITECTURE.md —
  unless the implementer wants one sentence there that a card's step is resolved once, in Domain.
- DIAGNOSTICS.md: the `*` suffix and the `(...super)` row format.
