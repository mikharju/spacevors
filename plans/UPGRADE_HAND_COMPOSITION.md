# UPGRADE_HAND_COMPOSITION.md — tighter upgrade schedule

Status: shipped (2026-11-02) — see `## Shipped` at the end for what landed, the deviations, and the
perf verdict. **Its hand shape is superseded** (2026-11-03) by plans/UPGRADE_BUNDLES.md — the card-kind
buckets, the legality gates and the shared `Roll` all still stand; the five hard slots became priorities
with a leak order, and a level now takes two cards. Read `## Follow-up (2026-11-03)` at the end.

## 1. The complaint, measured from the shipped code

PLAN.md: "Upgrades feel a bit too random. Some times get only one unattractive weapon upgrade and a
couple of engine or utility upgrades."

That is arithmetic, not vibes. `UpgradeOffer.Roll` (`src/Domain/Progression/UpgradeOffer.cs:29`)
builds one candidate list, runs a single Fisher-Yates over it and takes the first `HandSize = 5`. A
non-milestone candidate list is: one `Hp`, per **mounted** gun up to four cards (`FireRate`,
`ProjectileSpeed`, `MissileHealth`, `Range`), plus six always-present generic cards (`PickupRadius`
+ 4 thrust cards). So the weapon share of the pool is set by how many guns the hull starts with, and
a 5-card draw from a small pool is mostly generic filler. Exact hypergeometric numbers for the
shipped starter loadouts (hand 5, before any card is spent):

| Hull (starter guns) | pool | weapon cards | P(`Hp` offered) | E[weapon cards] | P(≤1 weapon card) |
|---|---|---|---|---|---|
| Falcon (1 gun) | 9 | 3 | 56% | 1.67 | **40%** |
| Shadow (2, one hitscan) | 11 | 5 | 46% | 2.27 | 18% |
| Scout / Fighter / Eagle / Manta / Raptor (2 guns) | 12 | 6 | 42% | 2.50 | 12% |
| Heavy (4 guns) | 18 | 12 | 28% | 3.33 | 2% |

What the table says:

- **The complaint is worst exactly where the run is most fragile** — the one- and two-gun hulls, in
  the first minutes, before milestone hands have added guns. Falcon gets a hand with at most one
  weapon card in 40% of its level-ups.
- **`Hp` is not guaranteed today**: one card in a shuffled pool, offered on 28-56% of hands.
  PLAN.md's "always the hp option" is a change, not a restatement.
- A hand can stack three cards on one chip gun (`FireRate:Shotgun` + `Range:Shotgun` +
  `ProjectileSpeed:Shotgun`) — the "unattractive" half of the complaint: three ways to improve the
  gun that was never the plan.
- **Level-ups drift in and out of the lulls.** A level costs `5·level` extra XP
  (`UpgradeStats.XpForLevel`) and income is 3 XP/ship, 1-2 XP/mine, so a level lands roughly every
  60-120 s — straddling the 114 s wave-pair period (`WaveSchedule.PairDuration`). The same level
  number therefore lands mid-wave in one run and in the full lull in the next.
  `plans/WAVES_AND_BREATHERS.md:36` already notes "the upgrade menu is already a full sim pause —
  level-ups are **accidental breathers**". Part of "feels random" is that the *when* is random too,
  and no card schedule keyed on the level number can fix that (§3 C).
- Milestone hands (`level % 5 == 0`) are the highest-variance hands and carry **no engine/utility
  card at all**: 3 new-weapon cards + one `Damage` per mounted gun + `Hp`, shuffled down to 5 — so a
  Heavy at level 5 has 8 candidates for 5 slots and luck decides which gun gets its damage card.
  `plans/REROLL_TOKENS.md` D8 priced the salvage capsule on exactly this ("the hands with the most
  variance").

## 2. What the code does today (the surface a change touches)

- `LevelUpSystem` turns XP into a level and calls `UpgradeOffer.Roll(view, player, newLevel,
  "level-up")`; a null hand does not consume the level (a hull with no turret).
- `Roll` is the **only** place a hand is composed, on purpose: a spent salvage capsule re-deals
  through the same call (`UpgradeOffer.TryReroll`), because "a re-deal must obey every rule that
  governs a first deal" (plans/REROLL_TOKENS.md D5, `ARCHITECTURE.md` cross-system bullet). A
  schedule that lives anywhere else drifts the moment a reroll is used.
- Every candidate is already filtered by the "never a card the rules would refuse to honour" gates —
  R7/R17/R18 (`CanGrowSalvo`), R19 (`CanToughenRound`), R29's range roof, hitscan's dead
  `ProjectileSpeed`, R23 (no owned gun re-offered; one copy per type), R33 (class-sized,
  milestone-only `Damage`). **Those gates do not move.** This change decides how many cards of each
  kind survive the cut, never whether a card is legal.
- Applying a card (`GameSession.ChooseUpgrade`/`ApplyUpgrade`), the mount/cargo screens and
  `UpgradeMenuRenderer` read `(WeaponName, Stat)` only, and `HandSize` stays 5 — **no app or renderer
  change is needed.**

One domain function, plus tests. That is why this is worth doing before any new system.

## 3. Options considered

**A. Guaranteed hand composition (recommended).** Each hand is dealt from fixed *slots* by card kind;
the shuffle moves inside each slot instead of across the whole pool. Keeps surprise, removes the dead
hand, and — the reason it is a floor rather than a buff — it barely touches the hulls that already
get a good hand (Heavy: 3.33 expected weapon cards today → 3 under the slots).

**B. Weighted draw.** One pool, weapon and `Hp` cards weighted up. Cheaper to write, but it buys a
*distribution*, not a guarantee — the 12%-of-hands bad hand (§1) just gets rarer — and weighted
sampling without replacement is more code than slot dealing for fuzzier semantics. Rejected.

**C. Fixed per-level schedule** (PLAN.md idea 1: lvl 1 small weapon, lvl 2 engine/utility, lvl 3
large weapon, lvl 4 small weapon, lvl 5 new weapons/damage, repeat). Rejected:

1. **The level number is not the clock.** Income is kill-driven and `SpawningStats.EnemyShips` scales
   the wave quota by player speed up to 3×, so the same "large weapon level" lands anywhere from
   minute 2 to minute 8 (estimate to verify: L5 ≈ min 2-4, L10 ≈ min 5-7, L15 ≈ min 8-11). If the
   real goal is "engine upgrades in the lull", the key is the wave clock (`WaveSchedule`), not the
   level — that is a different, bigger plan.
2. **It collides with the shipped milestone rule.** `level % 5 == 0` *is* already "new weapons /
   damage". Two level-keyed tables describing the same beat is drift waiting to happen.
3. **A scheduled kind can be empty.** At a ceiling the requested bucket has no legal card (a support
   gun at the 1350 px range roof until R34, a `Scatter` card that spends itself at 0.08 rad, a rate
   card dead at R7/R17/R18), so C needs the same spill rules as A and ends up as A plus a magic table
   to re-maintain every time a card is added — and R35/R36 are two new cards already proposed.
4. **"Engine and utility level" is a hand with no weapon in it** — the complaint, scheduled instead
   of random.

**D. Minimal floors (A-lite) — ship first.** No slot machinery: `Hp` always appears, ≥3 weapon-class
cards when the hull can offer them, at most one card per gun. Three rules, ~10 lines, and they
already fix the Falcon 40% case and the triplets. It is step 1 of A, not a rival design.

**E. Out of scope here:** hand size, the XP curve, rarity, armour/pierce and shield cards (their own
PLAN.md items; the bucket map below leaves room for them), a salvage *currency* shop (free mount
swaps and cargo already shipped — plans/TURRET_LAYOUTS.md), and the three unbuilt lull helpers
(pickup-radius bonus, orb-decay pause, last-ship orb) that plans/WAVES_AND_BREATHERS.md W3 left
unimplemented — do not re-propose them here.

## 4. Recommended design

**D1 — Bucket every card by kind.** Key the weapon buckets on the **gun's class**,
`WeaponType.IsSupport` (`Size == Small`), never on the slot: Raptor's `Shotgun` sits in `HeavyLeft`
and is still support, and "a chip bolted into a heavy mount screens too" (plans/WEAPON_BALANCE.md
§2).

| Bucket | Cards | Gate that still applies |
|---|---|---|
| Hull | `Hp` (future: shield) | none |
| Engine / utility | `ForwardAcceleration`, `TurnSpeed`, `SideThrust`, `BackThrust`, `PickupRadius` | none |
| Support weapon | any card naming a Small-class gun: `FireRate`, `ProjectileSpeed`, `Range`, `MissileHealth`, `Scatter` (R35) | R18/R19/R29 (+R34/R35 when they land) |
| Kill weapon | the same set for a Large-class gun, plus `MissileAgility` (R36) | R18/R19 (+R36's live R16 guard) |
| New weapon | milestone only, **except** the D5 spill: a hull with a free mount and no legal card for a class may be dealt one outside a milestone | R23 (pool minus mounted ∪ cargo), per-gun `UnlockLevel` |

**D2 — The slot table (5 cards).**

| Hand | Slots |
|---|---|
| Non-milestone | Hull 1 (`Hp`) · Engine/utility 1 · Support weapon 2 · Kill weapon 1 |
| Milestone (`level % 5 == 0`) | Hull 1 (`Hp`) · New weapon ≤3 · Weapon `Damage`: remaining slots, distinct guns, kill class first |

This is PLAN.md's second idea almost verbatim, and it keeps the milestone hand as the run's build
beat. Expected effect (two-gun hull): weapon cards 2.50 → 3, engine/utility 2.08 → 1, `Hp` offered
42% → 100%.

**D3 — Dealing rules.**

- Deal each slot by shuffling **that bucket only** and taking its count; shuffle the assembled hand
  once at the end so position still does not telegraph the composition (and `TryReroll`'s `SameHand`
  comparison keeps working).
- **Never two cards for the same gun** while another gun still has a legal card. A second card on one
  gun is a spill fallback for one-gun hulls only.
- **Spill order** for an unfilled slot: other weapon bucket (different gun) → new weapon **only if
  the hull has a free mount** (D5) → engine/utility → any legal card not already in hand. `Hp` is
  never duplicated.
- The hand may still be shorter than 5 (`Math.Min(HandSize, pool)`) and `Roll` still returns null
  with no turret, so the level is not consumed. Both are pinned by existing tests.
- Reroll needs no change: it calls `Roll` and inherits the composition (plans/REROLL_TOKENS.md D5).

```csharp
var buckets = CollectBuckets(view, turrets, ownedNames, level);   // legality gates unchanged
var slots   = IsMilestoneLevel(level) ? MilestoneSlots : StandardSlots;
var hand    = DealSlots(buckets, slots);                          // per-bucket partial shuffle
FillFromSpill(hand, buckets, freeMounts);                         // D3's order
Shuffle(hand, view.Rng);
return hand;
```

**D4 — `Hp` is guaranteed as an option, never as a purchase.** Nothing forces the survivable pick,
so hull hp in practice moves only for players who were previously dealt no alternative. Note the
collision: plans/DIFFICULTY_SCALING.md R2.6 item 7 and plans/ENDLESS_DIFFICULTY.md P5 name "a slower
`Hp` card cadence" as the *reward-side difficulty lever nobody pulls*. Guaranteeing the option every
hand pulls that lever the other way, so the honest default is "hp every hand, check it in playtest"
with "hp on alternate hands" as knob #1 (§8).

**D5 — A free mount is worth a card (spill only).** When a weapon slot has no legal card because the
hull lacks that class, prefer a new-weapon card that fits a free mount over generic filler — the
starved case is exactly where "unattractive hand" bites hardest (Falcon, one gun, two free 180°
flank mounts). It is a preference inside one slot, gated by R23 and `UnlockLevel`, never a rule: an
unowned gun you can park in cargo stays a legal card (`FullyBookedHull_StillOfferedNewWeapons`), and
plans/WEAPON_IDENTITY_CELLS.md §8 keeps "prefer a gun that fits a free mount" a nice-to-have. Trade:
tier-1 guns unlock at level 1, so this can hand a one-gun hull a second gun before its first
milestone. Keep it to the spill path, and let playtest decide (§8).

**D6 — Optional later, one line at a time.** Prefer a new-weapon card that fits a free mount over one
that does not (same nice-to-have, in the normal milestone path too); tag each card with its bucket in
the menu label ("engine", "support", "kill") so the structure reads — renderer only.

## 5. Balance consequences, with the knob for each

Expected weapon cards per non-milestone hand — this change **raises a floor, not the ceiling**:

| Hull | today | proposed | delta |
|---|---|---|---|
| Falcon (1 gun) | 1.67 | 2-3 (spill-dependent) | large ↑ |
| Shadow | 2.27 | 3 | +0.7 |
| Two-gun hulls | 2.50 | 3 | +0.5 |
| Heavy (4 guns) | 3.33 | 3 | −0.3 |

- **Weapon power grows faster on starved hulls.** The lever if playtest says too strong is already
  written down: plans/WEAPON_BALANCE.md §8 — five `FireRate` cards are ×2.01 and lift FlakCurtain
  over the kill class's floor, and "a capped rate card on support guns is the blunt answer". A
  gentler lever: make the third weapon slot a wildcard drawn from all buckets (one line in the table).
- **Engine/movement builds get fewer cards** (2.08 → 1 per hand). If Manta/Pursuit hulls feel
  strangled, raise the utility slot and drop a weapon slot — same single line.
- **`Damage` cards per milestone hand drop from "one per mounted gun, shuffled down" to one or two.**
  A Heavy at level 5 already cannot show all four guns' damage cards (8 candidates, 5 slots); the
  proposal only makes the limit explicit and stops the draw deciding which gun is upgraded. Late game,
  with the new-weapon pool exhausted, the freed milestone slots spill into damage cards for distinct
  mounted guns — which is a good late hand: "which gun gets stronger next".
- **Reroll tokens get less valuable** — they were sold on hand variance (plans/REROLL_TOKENS.md D8).
  They still buy "not these guns/stats", but a hand that cannot be junk is a less valuable rescue.
  If the capsule reads dead, widen the per-slot choice (offer two candidates for one slot) rather than
  restoring the free-for-all.
- **Card sizes do not move at all**, so no balance-test constant changes and R10's 15-35% band plus
  R33's milestone-only +25%/+50% `Damage` stay exactly as priced ("the 15-35 % band stays for the
  cards that appear in every hand" — plans/WEAPON_BALANCE.md:515).

## 6. Prerequisite drift to fix in the same change

HEAD (`8fc26ea "quicker level ups"`) halved `UpgradeStats.XpStepPerLevel` 10 → 5, so live thresholds
are 5/15/30/50/75 (L10 = 275 XP) while `src/Tests/XpCurveTest.cs` still asserts 10/30/60/100/550 and
"each level costs ten XP more" — that test is **red at HEAD**, and plans/LOOT_DROPS.md's
2026-11-02 XP follow-up (10/30/60/100/150, "level 10 ≈ 550 XP ≈ 180 kills") is stale. This plan is
keyed on the level number, so the curve it is keyed on must be true first: fix `XpCurveTest` and the
LOOT_DROPS follow-up (or revert the halving) before landing any hand change, per
plans/DOC_LAYOUT.md's "no lying docs".

## 7. Determinism, RNG draw count, perf

- Offers draw from the **one world RNG** (`EntityManager.Rng`, seed 42, `WorldView.Rng`);
  `ARCHITECTURE.md` puts "upgrade shuffles" inside that rule and `WorldRngTest` covers it.
- **The draw count changes and that must be written down** (the precedent is plans/REROLL_TOKENS.md
  D3 and plans/ECM_PULSE_AND_SALVAGE_FIELD.md: "document N extra draws because baselines are
  seeded"). Today: one Fisher-Yates over the pool = pool−1 `Next` calls (11 for a two-gun hull, 17
  for a Heavy, 7 for a Heavy's level-5 milestone hand). Slot dealing with a *partial* Fisher-Yates per
  slot is ~4 draws (1 engine + 2 support + 1 kill; the hull bucket holds exactly `Hp`, so 0) + 4 for
  the final hand shuffle ≈ 8, so a level-up draws fewer numbers than today and every later draw in
  the run shifts. A seeded run becomes *different*, not *wrong*.
- Sim scenarios drive `SimulationRunner` (which includes `LevelUpSystem`) with no `GameSession`, so
  they never pause and never reroll; if a scenario's hull kills enough to level up, its spawn stream
  shifts. Per AGENTS.md, still measure before/after on a scenario that can level up:
  `.tools/scenario.sh <scenario> --sim --iters 3`, read a same-order delta as stream perturbation and
  not cost (the offer is O(pool) over a pool of a few dozen entries even after R35/R36 land), and put
  the verdict in the shipped record. `--sim` cannot price the card screen at all
  (plans/REROLL_TOKENS.md:239-242).

## 8. Tests (sim-only, no graphics — the fixtures in `LevelUpSlotOfferTest` / `RerollOfferTest`)

Add:

- `Hand_AlwaysCarriesAnHpCard` — 30 hands, every hull.
- `Hand_AlwaysCarriesAtLeastThreeWeaponCards_WhenTheHullCanOfferThem` — the complaint's own test.
- `KillOnlyHull_SpillsItsSupportSlot_InsteadOfDealingAShortHand` (+ the support-only mirror).
- `Hand_PutsAtMostOneCardOnTheSameGun_WhileAnotherGunHasALegalCard`.
- `MilestoneHand_DealsAtLeastOneNewWeapon_WhenThePoolIsNonEmpty` — today the new-weapon cards can
  shuffle out entirely; the guarantee makes them structural.
- `MilestoneHand_WithEveryGunOwned_FillsWithDamageAndUtilityCards_NeverShort`.
- `FreeMountHull_WithNoCardForItsMissingClass_IsOfferedANewWeapon` (D5).
- `RerollOffer_RespectsTheSameCompositionAsALevelUpHand` — the shared-`Roll` invariant, proven.
- `Hand_NeverDealsAnIllegalCard_AfterEveryCeilingIsReached` — drive one gun to its range roof, salvo
  budget and round-hp ceiling, then assert the spill never re-offers a card R18/R19/R29 refuse. Keep
  this as the seed of PLAN.md's unbuilt "balance fuzzing" item.

Change:

- `MilestoneHand_DealsADamageCardToEveryMountedGun` → exactly one (or `slots available`) damage card,
  distinct guns, kill class first, and the *legal set* of damage cards unchanged. R33's sizing tests
  are untouched.

## 9. Commit sequence (small, tree green each time)

1. Fix `XpCurveTest` + the LOOT_DROPS XP follow-up to the live ×5 curve (§6). No gameplay change.
2. **D — floors**: `Hp` always, ≥3 weapon-class cards when possible, ≤1 card per gun; tests from §8.
3. **A — slot table** for non-milestone hands + spill rules; both spill directions tested.
4. **Milestone composition** + the §8 test change.
5. **D5 free-mount spill** — separate commit, it is the only rule with a real balance cost.
6. Docs: `## Shipped (date)` here with the before/after hand histogram, the level-vs-minute
   measurement and the scenario verdict; PLAN.md index line; a pointer note in
   plans/TURRET_LAYOUTS.md:198-209 (which today documents "Hand = 1 Hp + up to 3 new-weapon cards +
   existing-weapon damage cards, filled to 5" and a single Fisher-Yates) and in
   plans/WEAPON_BALANCE.md that the milestone hand shows one `Damage` card. **Never renumber an R.**

## 10. Playtest questions, in this order

1. Does a fixed shape make a run feel *less interesting*, or just less unfair? That is the whole bet.
2. Three guaranteed weapon slots — too strong? (knob: wildcard slot; or a capped support `FireRate`
   card per plans/WEAPON_BALANCE.md §8)
3. `Hp` in every hand or every other hand? (§4 D4 — it crosses the reward-side difficulty lever.)
4. Is `PickupRadius` an engine-tier card at all, or does XP vacuum deserve its own slot? It is the
   only utility card that is not about flying.
5. After the guarantee, is a reroll charge still worth a capsule? (§5)
6. Level vs minute: read the `[UPGRADE] level=… choices:` lines from a `SPACEVORS_DIAGNOSTIC=1` run
   and check §3's estimate (L5 ≈ min 2-4, L10 ≈ min 5-7). If reality is far off, the schedule key is
   wrong and this plan should be re-cut on the wave clock instead of the level number.

## 11. Cross-references and docs to touch

- **This file replaces** the PLAN.md longer-ideas stub; PLAN.md keeps the one-line roadmap pointer and
  a `Proposed, not implemented` index line (both already written). `plans/DOC_LAYOUT.md`: PLAN.md
  ≤150 lines — it is 172 now, still over budget, so keep detail out of it.
- plans/WEAPON_BALANCE.md: R10 (a card is never a lie; the 15-35% band for every-hand cards),
  R17/R18/R19 (offer gates), R23 (one gun per type — one copy per type is what makes "a card worth
  exactly the same in every loadout"), R24/R25 (the two classes = the two weapon buckets), R29 (range
  roof, until R34), R33 (milestone-only class-sized `Damage`, and the cadence pricing at :505-521),
  **R34/R35/R36 proposed** (the new `Scatter`/`MissileAgility` cards must be bucketed with this change
  or straight after it), §8 open knobs.
- plans/WEAPON_IDENTITY_CELLS.md §8: new-weapon pool size per milestone (17 guns at L5, +2 at L10),
  FarlockSalvo unlock 5 vs 10 ("so the level-5 hand does not turn into a missile hand"), free-mount
  preference.
- plans/REROLL_TOKENS.md D5/D6/D8 and plans/TURRET_LAYOUTS.md:198-209: shared `Roll`, whole-hand
  re-deal, milestone variance, and the current documented hand shape that this plan supersedes.
- plans/DIFFICULTY_SCALING.md R2.6 item 7 / plans/ENDLESS_DIFFICULTY.md P5: the `Hp`-card cadence as
  reward-side difficulty; plans/LOOT_DROPS.md: XP values, the (now stale) XP curve follow-up.
- plans/WAVES_AND_BREATHERS.md:36: "level-ups are accidental breathers"; the 114 s pair the level
  cadence straddles. plans/MISSILES.md:270: "a full hand never fills with cards that do nothing".

## Shipped (2026-11-02): hands are dealt from card-kind slots

**What landed** (`src/Domain/Progression/UpgradeOffer.cs`, `src/Tests/XpCurveTest.cs`,
`src/Tests/HandCompositionTest.cs`, `src/Tests/LevelUpSlotOfferTest.cs`).

- `Roll` now sorts the legal cards into five kinds — Hull, Utility, SupportWeapon, KillWeapon,
  NewWeapon — and deals one card per slot: `Hull · Utility · Support · Support · Kill`, or at a
  milestone `Hull · New · New · New · Kill`. Every R6/R10/R18/R19/R23/R29/R33 gate is exactly where
  it was; the slots only decide which legal card survives.
- Slots take with a *different gun* preference; unfilled slots fall through `SpillOne`: a weapon card
  for a gun the hand does not name yet → a new gun when the hull mounts no gun of one class at all
  and has a free mount (D5) → a second card on a gun it already names → an engine/utility card. A
  hand with nothing legal left stays short, as `Math.Min(HandSize, pool)` always allowed.
- `Hp` is in every hand (it used to be offered on 28-56% of them), three cards name a gun in every
  hand (was 40% of a one-gun hull's hands carrying at most one), and a non-milestone hand carries
  exactly one engine/utility card.
- `MaxNewWeaponCardsPerHand` caps the *bucket*, so "at most three new weapons" is structural rather
  than a matter of luck.
- `XpCurveTest` no longer hard-codes the XP step: it reads the scale off `XpForLevel(1)` and asserts
  the shape — thresholds rise, each level costs more than the one before, and the total is triangular
  in whatever the step is. It was red at HEAD after `quicker level ups` halved the step; it is green
  for any step now.
- Suite: 599 green (579 before, 9 of which were the red XP test). New file `HandCompositionTest`
  covers hp always present, ≥3 gun cards, five cards, one utility card, no doubled-up gun on a
  four-gun hull, new guns for the one-gun hull, full hands for a one-class hull, the late-game
  milestone with nothing left to unlock, the re-deal keeping the shape, and the spill never crossing
  a closed gate.
- Live check (headless, `SPACEVORS_DIAGNOSTIC=1`, force-level-up key `L`): Scout at LV 2 was dealt
  `machine gun projectile speed +30%` · `hit points +8` · `InterceptorTurret range +15%` ·
  `InterceptorTurret FireRate +1 shot` · `PickupRadius +20%` — hull + one utility + two support + one
  kill, and the card screen took the choice normally (`.tools/shots/hand-screen.png`).

**Deviations from the plan.**

1. **The spill puts a second card on a gun the hand already names *before* an extra engine/utility
   card** (§4 D3 had utility first). With one support gun aboard, "two support upgrades" is only
   satisfiable by doubling up, and a second weapon upgrade is a better card than a second filler one —
   which is the whole point of the change.
2. **The new-gun spill (D5) fires only when a class is unrepresented outright**, not whenever a weapon
   slot is unfilled. Otherwise every two-gun hull with a free mount would be handed a tier-1 gun on
   levels 2-4 and the milestone hand would arrive with its pool already spent. `StarvedForANewGun` is
   `a free mount && (all mounted guns are support || all are kill)` — Falcon is the only starter hull
   that qualifies.
3. §4 D6's bucket labels on the cards and the free-mount preference inside the milestone path were not
   built. Neither is needed by the shape; both stay open knobs.

**Perf verdict — this change costs 0%, keep.** The offer path does not run in any gated scenario: with
diagnostics on, `hull-stream`, `ammo-spray` and `missile-384` (`--sim --iters 1/3`) log **zero**
`[UPGRADE]` rows, so the gated worlds never level up and never draw from the changed code. Post-change
medians, same host as `index.tsv`: `hull-stream` 0.85 ms against the index's 0.82, `ammo-spray` 1.36
against 1.43, `missile-384` 1.79 — all `budget: pass`, all inside host noise. The RNG draw count per
hand did drop (a partial shuffle per slot plus a final shuffle, ~8 draws, against one Fisher-Yates over
the whole pool = pool−1), so a seeded run that *does* level up shifts its spawn stream — that is a
different run, not a wrong one, and no committed baseline depends on it today.

**Still open, in playtest (§10).** Whether a fixed shape makes a run less interesting; whether three
guaranteed weapon slots read as too strong (knob: a wildcard slot, or the capped support `FireRate`
card of plans/WEAPON_BALANCE.md §8); hp every hand or every other hand; `PickupRadius` in its own
slot; whether a reroll charge is still worth a capsule; and the real level-vs-minute curve, which the
`[UPGRADE]` rows can now answer — the game's own log lines, not an estimate.

## Follow-up (2026-11-03): hand shape superseded by plans/UPGRADE_BUNDLES.md

Playtest on the shipped shape said the ramp still outran the upgrades and that the kill-weapon card was
the only pick worth taking, so the plan's own knob (§10's "three weapon slots read as too strong?") was
answered from the other side: a level now takes **two** cards out of the same five, and the five hard
slots became **priorities with a leak order**.

What still stands from this plan, unchanged: the card-kind buckets (`SortIntoKinds`), every legality gate
(R10 never a lie, R17/R18 volley budget, R19 ceilings, R23 one copy per type, R29 support range roof, R33
milestone-only class-sized `Damage`), the single `Roll` shared by level-up and reroll, the hull-slot
`Hp`/`Armor`/`Shield` draw, and the "no new gun unless a class is missing and a mount of that size is
free" rule — which is now leak step 2 instead of spill step D5.

What changed: `CardsPerLevel = 2` and `PendingUpgradeOptions(Options, PicksLeft, Taken)`; a slot with
nothing legal leaks in a fixed order instead of taking the next hard slot; the new-weapon pool is split by
mount size (three per class still) so a milestone hand can promise a large gun to switch into on a hull
with one large mount; `Damage` is dealt one card per class at a milestone; and the leak keeps `Damage` and
defence at the end of its list, defence behind a 20 % roll. §2's slot table and §5's spill order should be
read against plans/UPGRADE_BUNDLES.md §2, which supersedes them.
