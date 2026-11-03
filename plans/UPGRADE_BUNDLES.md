# UPGRADE_BUNDLES.md — two cards a level, dealt from priorities that leak

Status: shipped (2026-11-03) — implemented and verified in the run below; playtest questions at the end
are what is still open. See `## Shipped (2026-11-03)` for the deviations from this plan.

Supersedes the *hand shape* half of plans/UPGRADE_HAND_COMPOSITION.md (read that file's `## Shipped`
first: the bucket-by-kind machinery, the legality gates R10/R17/R18/R19/R23/R29/R33 and the shared
`Roll` all stay). What changes here: a level takes **two** cards instead of one, and the slot table
becomes a list of **priorities with a leak order** instead of five hard slots.

## 1. The complaint

From playtest (2026-11-03):

- Upgrades do not keep up with the difficulty ramp. The enemy ladder multiplies three axes at once
  (hp ×3.5, per-shot damage 4 → 24, fire rate ×2 by `MaxTier`) plus wave quota 6 → 40, while the
  player takes **one** card a level.
- Large weapons are the workhorses, so the kill-weapon card is the only pick worth taking: a card
  on a kill gun buys 3–4× what the same card buys on a support chip (+50% vs +25% base `Damage`,
  R24's 32/24 DPS roof, R29's range roof, R7 saturating a chip's rate card after 2 buys).
- Choosing between the classes therefore feels like missing out either way.

## 2. The decision: two cards a level, priorities not guarantees

**Take two cards per level (`UpgradeOffer.CardsPerLevel = 2`) out of the same five-card hand, and
keep `XpStepPerLevel = 5`.** Deliberate: this is a *buff*, not a bundle. Paying double XP
(`5L → 10L` marginal) would have kept the total cards per run identical and changed nothing about
the first complaint; the run currently reads as too hard, so the extra card is the point and the
feel is what has to be judged. Consequences that follow from it, and their knobs:

- Player DPS per level roughly doubles its growth rate and the `Hp` card is available every level,
  so EHP roughly doubles too. Knobs if it overshoots, in order: `CardsPerLevel`, then the enemy
  damage ladder (`4·(tier/2)` → `4·(tier/3)`), then XP cost.
- Levels still land on the XP clock, which is skill- and speed-driven (plans/UPGRADE_HAND_COMPOSITION
  .md §3C): two cards a level does not make level number and minute agree, it only makes each level
  worth more. The `[UPGRADE]`/`[THREAT]` rows are the instrument for that (plans/DIFFICULTY_SCALING.md R2.8).

**Hand = priorities + leak.** A slot whose kind has nothing legal left does not stay empty and does not
force a strong card: it **leaks**, in a fixed order. The order that survived writing it down: cards for
the guns the ship is actually shooting with come before filler, because a hand of three engine cards
reads as a short-changed level even when it is legal; the *strong* cards stay at the end of the list,
which is the part the playtest asked to ration. A saturated category therefore still turns into
engine/health filler — it just has to exhaust the ship's real guns first.

| | Standard hand | Milestone hand (`level % 5 == 0`) |
|---|---|---|
| 1 | Hull (`Hp`, or `Armor`/`Shield` when legal — unchanged) | Hull |
| 2 | Engine/utility | New **large** gun — the option to switch into, even with a full hull |
| 3 | Support-gun upgrade | New **small** gun |
| 4 | Kill-gun upgrade | `Damage` for a gun already aboard (kill class first) |
| 5 | A weapon upgrade for a gun the hand does not name yet | `Damage` for a gun of the other class |
| leak | fixed order, see below | same |

Both weapon classes are therefore in **every** standard hand, which is the FOMO fix: the pair the
player wants (large + small) is always on the table, and the fifth card plus the leak is what makes
the choice non-trivial. Milestone hands keep the build beat, and the `Damage` slots answer the two
cases the playtest named: a hull with every mount filled still gets a `Damage` option, and a hull
with a single large mount gets a new large gun *to swap into* plus `Damage` cards and different new
small guns instead of promising a second large gun it cannot wear.

**Leak order** (first one that has a legal card wins; `UpgradeOffer.LeakOne`):

1. a stat card for a gun of either class that the hand does not name;
2. a new gun of a class the hull does not mount at all, and only into a free mount of that size
   (the shipped D5 rule, plans/UPGRADE_HAND_COMPOSITION.md);
3. a second card on a gun the hand already names — the shipped spill's rule, firepower over filler;
4. engine/utility;
5. `Hp`, and only when the hand has no `Hp` yet (i.e. the hull slot drew a defence card);
6. `Damage` for a gun the hand does not name (strong: milestone-sized +50%/+25%);
7. a defence card (`Armor`/`Shield`) **only on a rare roll** (`RareDefenceLeakPercent`);
8. `Damage` for a gun already named.

Steps 6–8 are where the rationing lives: `Damage` never leaks ahead of an ordinary card, and defence
never leaks except on the roll. A hand with nothing left stays short, as `Math.Min(HandSize, pool)`
always allowed, and a hull with no turret is still dealt nothing (`Roll` returns null, the level is not
consumed).

At a milestone the ordinary stat buckets are not built at all (the shipped behaviour: a milestone hand
is hull, new guns and `Damage`), so a milestone leak draws new guns rather than stat cards — which is
why a kill-only hull's milestone hand offers a new small gun for its empty small mounts instead of a
second `Damage` card.

## 3. Rules that do not move

- **Every legality gate stays where it is**: R10 never a lie, R17/R18 volley budget, R19 round-hp
  and pierce ceilings, R23 one copy per type, R29 support range roof, R33 milestone-only class-sized
  `Damage`. The leak path runs the gauntlet too — it can only take what `SortIntoKinds` legalised.
- **`Damage` is still milestone-only.** The leak may reach it, but only after the filler (step 6),
  which is what keeps it from becoming the common card.
- **Mounts are not an upgrade.** No new turret slots, and no pre-upgraded new weapons in the pool
  (both deliberately left for later — see §6).
- **The hull slot still draws** `Hp`/`Armor`/`Shield` as plans/ARMOR_AND_PIERCE.md and
  plans/SHIELDS.md shipped it. Only *leaked* defence is rare.
- **Reroll re-deals through the same `Roll`** (plans/REROLL_TOKENS.md D5) and never re-offers a card
  already taken this level — with two picks, a charge must not buy the same card twice.

## 4. State the hand now carries

`PendingUpgradeOptions(Options, PicksLeft, Taken)`. `LevelUpSystem` deals
`PicksLeft = min(CardsPerLevel, hand.Length)`; the pick bookkeeping is the domain's two functions
`UpgradeOffer.SpendPick` (drop the taken card, append it to `Taken`, spend one pick) and
`UpgradeOffer.PruneCardsThatNoLongerApply` (drop cards the ship can no longer apply, clamp the picks),
both testable without a window; the app layer only decides when to clear the choice and what to draw.
The new-weapon round trip through the mount screen spends the card at the same moment the old code
cleared the choice, so mounting, storing to cargo and Esc-when-resolved all behave as they did.

The prune exists because two picks in one level can contradict each other: taking the new large gun
displaces the gun already in that mount into cargo, and `ApplyUpgrade` only reaches guns that are
firing. A `FireRate` card left on screen for the gun that just went into cargo would be a card that
does nothing, so when a pick changes what is aboard the remaining hand loses that gun's cards and the
level asks for fewer picks. R10's rule, applied to a hand instead of a card.

`SPACEVORS_DIAG_UPGRADES` is now **one entry per card taken** — two entries per level-up. A hull card is
written with its empty weapon name (`Hp:`, `Armor:`); a bare word is read as a new weapon's name.

RNG: the leak path adds one draw when it reaches the rare-defence gate (7), and the per-hand shuffle
order changes, so a seeded run shifts. Documented, per the precedent in plans/REROLL_TOKENS.md D3.

## 5. Tests

Shape (`HandCompositionTest`): both weapon classes in every standard hand when the hull mounts both;
one hull card and never two `Hp` cards; one engine/utility card; five cards while the hull can fill
them; no doubled gun while another gun has a legal card; the leak never crosses a closed gate
(R18/R19/R29); no `Armor`/`ArmorPierce` before level 5 — and the pierce sweep now runs over seeds as
well as levels, because the same seed at the same level deals the same hand.

Milestone (same file + `LevelUpSlotOfferTest` + `TwoPickLevelUpTest`): a new large gun while one is
unowned, including a fully-booked hull; a `Damage` card in every milestone hand; one `Damage` card per
class with the kill class first; with no chip aboard the chip's slot invents nothing and the freed slot
goes to a new gun of the missing class; with the roster exhausted the freed slots fill with `Damage` and
filler and the hand is still five.

Leak: a standard hand never carries a `Damage` card at all (the buckets are built empty), so the leak
cannot smuggle a strong card into an ordinary level.

Two picks (`TwoPickLevelUpTest`): a level-up is dealt `CardsPerLevel` picks and a short hand clamps
them; a spent pick shortens the hand and counts down; the second pick ends the level; a re-deal never
resells a taken card; a displaced gun's cards leave the hand and the picks shrink with them; the
scripted bench consumes two entries a level (`LevelUpScriptTest`).

## 6. Deliberately not here

- **Pre-upgraded new weapons** as a high-level bundle option — the playtest decides whether the
  bundle is enough first.
- **New turret mounts** as an upgrade. Explicitly ruled out for this change; until it exists, a hull
  with one large mount cannot wear two large guns and the milestone hand is written around that.
- **XP curve.** `XpStepPerLevel` stays 5 by request; that makes this a buff and the buff is the thing
  being judged.
- **Weapon evolutions** (PLAN.md future) and R34/R35/R36 (plans/WEAPON_BALANCE.md): the support-job
  cards are still the real answer to the class-value gap and they land independently.

## 7. Playtest questions, in order

1. Does two cards a level make the run feel even, or merely easier? Where is the wall now, from the
   `[THREAT]` `in`/`out`/`hp` walk at 5/10/15 min?
2. Does the leak read as variety, or as a short-changed level? Knob: the leak order (§2), or a
   `CardsPerLevel` of 3 for weapon levels.
3. Is defence too rare now (`RareDefenceLeakPercent`)? Knob: the percentage, or a defence priority slot.
4. Is the milestone hand's new-large-gun-to-swap-into worth a slot when the hull has one large mount,
   or should it become a third `Damage` card there?
5. Is a reroll charge still worth a capsule now that a hand is two cards instead of one?

## Shipped (2026-11-03): two picks a level, priorities that leak, and the pick bookkeeping in the domain

What landed:

- `UpgradeOffer.CardsPerLevel = 2`; `PendingUpgradeOptions(Options, PicksLeft, Taken)` with
  `PendingUpgradeOptions.Deal(hand, picks)` clamping the picks to the hand; `LevelUpSystem` deals the
  bundle and consumes two scripted bench entries a level.
- The slot table of §2 in `UpgradeOffer`: `StandardSlots` / `MilestoneSlots`, `TakeUpgradeForAnUnnamedGun`
  for the fifth standard slot, `LeakOne` for everything the kinds could not supply, and a new-weapon pool
  split by mount size (`NewGunOptions(owned, level, size, rng)`, still `MaxNewWeaponCardsPerClass = 3`).
  `TakeGunForAMissingClass` asks the mount layout for a free mount of the missing class's size instead of
  trusting "the hull has no turret of that class", which is what makes the new-large-to-swap-into rule work
  on a booked hull.
- `UpgradeOffer.SpendPick` and `UpgradeOffer.PruneCardsThatNoLongerApply` — the pick bookkeeping in the
  domain, so the two-pick rule and the dead-card rule are tested without a window. `GameSession` keeps the
  app half: `_cardInHand` rides through the mount screen, `FinishPick` spends the pick, `ReturnToCards`
  resets the step, and a card key that names no live card no longer abandons the level.
- `UpgradeMenuRenderer` shows the picks left; `HAND` and the hint row re-lay out as the hand shrinks.
- `WeaponType.FromName` fixed: it returned `All.FirstOrDefault(...)` over a **struct**, so a miss came back
  as a lifted default gun and every `is { }` / `?.` guard on it was dead — which is what made the scripted
  bench treat `Hp:` as a new weapon (TROUBLE_SHOOTING.md #76).

Deviations from the plan above:

- The leak order §2 first specified (filler before firepower) was written, run and then reversed: with
  filler first, a hull whose two guns are both already named in the hand got a hand of `Hp` plus three
  engine cards, which is exactly the "short-changed level" the leak was meant to avoid. Firepower for the
  guns aboard — an unnamed gun, a missing class, then a second card on a named gun — comes first, as the
  shipped spill had it; the rationing §2 asked for is intact, because `Damage` and defence sit at the end
  of the list and defence behind the 20 % roll.
- **New rule, not in the plan**: a pick that changes what is aboard prunes the hand. Two picks in one level
  can contradict each other — take the new large gun and the gun it displaced goes to cargo, where
  `ApplyUpgrade` cannot reach it — so the displaced gun's cards leave the hand and the picks owed shrink
  with it. Without it, the second pick would have been a card that does nothing (R10).
- A milestone hand still builds no ordinary stat buckets, so a milestone leak draws new guns rather than
  stat cards; a kill-only hull's milestone hand therefore offers a new small gun for its empty small
  mounts instead of a second `Damage` card. Documented in §2 rather than changed.

Verification and tests: 662 tests pass (`dotnet test`, `PerformanceBenchmark` excluded as usual for host
noise, TROUBLE_SHOOTING.md #28). New in `TwoPickLevelUpTest`: the deal, the clamp, the countdown, the
last-pick end, the reroll that cannot resell a taken card, and the displaced-gun prune; `HandCompositionTest`
gained the per-class milestone damage rule, the no-chip case, the never-two-`Hp` guard, the
standard-hand-never-carries-`Damage` guard and a seed sweep for the pierce card (the same seed at the same
level deals the same hand). A window run on the scripted bench showed the whole flow: two cards with
"2 to pick", `hit points +8` spent and the hand shrinking to one card with "last pick", the level ending on
the second pick, then a level-3 hand offering `new weapon RailGun (large)` with a stat card, the mount
screen displacing `MachineGun` into cargo, `[UPGRADE] hand pruned to 0 applicable cards`, and the game
resumed with the RailGun firing.

Perf verdict (2026-11-03): `.tools/scenario.sh ammo-spray --sim --iters 3` — median **1.43 ms** against the
previous rows' 1.38–1.45 ms band, budget pass, 0 of 357 frames over. **Unchanged — keep.** The offer path
itself is not reachable inside a gated sim (the player never leaves level 1 there, TROUBLE_SHOOTING.md #71),
so it is 0 % by inspection; the only hot-path change is `FromName`, which sits on the per-round colour and
radius lookups and is the same loop the LINQ call compiled to.
