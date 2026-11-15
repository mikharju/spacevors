# PREUPGRADED_NEW_WEAPONS.md — a new gun dealt to a booked hull arrives partly upgraded

Status: shipped (2026-11-14) — the `## Shipped` section at the end records what landed, the two places
it deviates from this memo, and the perf verdict. The decisions below are unchanged.

## Part 1 — the plan

### 1. The problem

On a hull whose mounts are all taken, a new-weapon card is a trap. The player has spent a run
putting cards into one big gun; the milestone hand then offers a new big gun with **nothing on it**,
and taking it means dropping the ship's damage onto a base-stat weapon. plans/UPGRADE_BUNDLE_PAIRS.md
§10 and plans/UPGRADE_BUNDLES.md §6 both listed "pre-upgraded new weapons" as deliberately out of
scope; playtest says the punishment is real and is the reason new-weapon picks stop being picks.

The same arithmetic makes new-weapon *hands* thin. A milestone hand pairs one powerful half with one
weak half, and the deal refuses to put two new guns in one bundle, so a hand with three new large
guns spends its other three halves on filler the player did not ask for.

### 2. What "0.5 ×" buys, honestly

Worked numbers (table values, `WeaponType`): a `MachineGun` with four `FireRate` cards (a growing
volley: +1 pellet each) and three `Damage` cards fires 5 × 4/s × 30 = 600 dps. A fresh `RailGun`
(60 damage, 1/s) fires 60 dps; with a pre-applied set of four — one `Damage`, one `FireRate`,
`Range`, `ArmorPierce` — it fires 180 dps.

So the change is **not** a swap that costs nothing, and the plan should not claim it is. What it fixes:

- the new gun is no longer a naked base weapon — it arrives as a weapon the player has partly built
  already, and it keeps taking upgrade cards at the normal cadence afterwards;
- a booked hull's new gun is a *rotation* (the displaced gun goes to cargo with its upgrades and
  mounts back for free — plans/TURRET_LAYOUTS.md), and a half-built gun is worth rotating;
- the card reads as an offer with content, not as a reset.

0.5 × the **least** upgraded gun is a deliberately conservative anchor: it never makes the new gun
the strongest thing aboard, and it is bounded structurally (see §3 D2/D3 — at most one card per
legal stat, `Damage` at most once).

### 3. The decisions

**D1 — Pre-upgrade only when the hull cannot fit the gun.** A new gun dealt while a mount it could
fit is free stays a plain base weapon: a free mount is a pure addition, and the pain only exists
where the new gun must displace something. Trigger is `TurretMount.FreeSlots(view, size)`: a large
gun needs a free **large** mount; a small gun fits any mount, so it is boosted only once **every**
mount is occupied.

**D2 — How many upgrades: half of the weakest thing it would displace.** Round up, counted in cards:
the least-upgraded weapon standing in a mount this gun could fit, mounted guns only (cargo never
counts — it does not fire and is not offered upgrades). "Least", not mean or max, keeps a run of
constant swaps from compounding. The count is `UpgradeCounts` summed over that gun's name, so the
upgrades this change pre-applies are themselves counted next time.

**D3 — Which upgrades: random, legal, one card each.** Sampled without replacement from the gun's
own legal stat set at its table stats, through the gates that already govern stat cards (no
`ProjectileSpeed` on a hitscan beam, no dead "+1 shot" past the volley budget, no `Range` past the
support roof, no round-health card past the ceiling, pierce from level 5). Each pre-applied upgrade
is one ordinary card of that stat, so the numbers are the same numbers a purchased card grants.
`Damage` **is** in the pool — it is the card that actually closes the dps gap — at most one per
variant. That does not break R33's "damage cards are milestone-only": on the shipped deal, a new gun
with no free mount of its size is only ever dealt by a milestone hand, so the pre-applied set is
non-empty at milestones by construction. The count is capped by how many distinct legal stats the
gun has, so a big `N` cannot run away — 0.5 × N is a target, and R10 (never a card that promises
more than the rules can honour) is the bound.

**D4 — A differently upgraded gun is a different card.** A new-weapon card carries its upgrade set,
and card equality is (gun, set). That is what lets the same weapon appear in several bundles of one
hand with different sets, and it keeps every existing pair rule working: identical *cards* are still
refused, identical *pairs* are still refused, and R23 still says the ship owns one copy of a gun —
the hand may offer one gun three ways, a level takes exactly one bundle. Three cards reading
"new weapon Rail Gun" must not read as a bug, so the card prints its set and prints its **upgraded**
range and damage, not the table's.

**D5 — A bundle may carry two new weapons.** One large (the powerful half) plus one small (the weak
half) — the powerful/weak invariant already forbids two of one size, so this is a relaxation of the
pairing rule, not a new shape. Both guns are granted. The mount trip is solved by paying for the
weapons the way `Esc` already does: a taken bundle's new gun goes into cargo with its pre-applied
upgrades, and the mount screen opens holding the large one; the player fits it, and the second one,
in the same visit — cargo picking and mount swapping are already free and unlimited there.

**D6 — Milestone hands prefer new weapons, and no cap moves.** With D5, a milestone hand spends its
first bundles on (new large variant + new small variant) pairs and only then reaches `Damage`,
defence and the weak-pool fallback. `MaxNewWeaponCardsPerClass` stays 3, and the shipped guarantee
of one `Damage` card per class aboard survives.

**D7 — Ordinary hands keep dealing new guns only under the shipped rule** (a class the hull does not
fly at all plus a free mount of that size, D5 of plans/UPGRADE_HAND_COMPOSITION.md). Opening new guns
on ordinary levels is a separate hand-shape decision and a bigger balance change; it stays a knob.

**D8 — Only a gun the ship does not own may be a variant.** The variant pool is exactly the shipped
new-weapon pool: unowned — neither mounted nor in cargo (R23). A gun the ship already owns is never
re-offered as a new-weapon card wearing a different upgrade set; in a milestone hand an owned gun
reaches the selection only through its own `Damage` card. That bounds the variant idea from both ends:
several builds of one gun are on offer while it is unowned, and the moment the player takes one that
gun leaves the new-weapon pool for good.

### 4. Balance consequences, and the knob for each

- **Milestone hands stop containing filler** — which also means fewer `DamageSupport` cards reach the
  chip line at milestones. Knob: cap new-weapon variants at 2 per class instead of 3.
- **New guns on booked hulls are worth taking** — kill-line growth shifts from "more cards on one gun"
  to "a second gun that starts half-built". Knob: the multiplier (0.5 → 1.0, i.e. mirror the weakest
  gun exactly).
- **The `Damage` pre-apply is the sharpest single lever** (+50 % of base on a kill gun at the same
  moment the hand offers its `Damage` cards). Knob: drop `Damage` from the pre-applied pool, or
  restrict it to kill guns. Read it against R25's class floor and R11's through-armour TTK.
- **Counting the *least* upgraded gun** is what keeps this from compounding. Knobs: count the gun in
  the specific mount being taken, the mean, or the class-matched gun (small guns against mounted chips
  only) instead of "any mount it could fit".
- **Reroll charges get more interesting** — a re-deal re-rolls the variant sets, so a charge buys a
  better *build* of the same gun, not just a different gun.

### 5. Deliberately not here

- New turret mounts, or a mount count that grows (still in front of this question, per
  plans/UPGRADE_BUNDLE_PAIRS.md §10).
- Weapon evolutions (PLAN.md future).
- Refunding the upgrades of a gun that gets displaced — the gun keeps them and rides into cargo; that
  is the answer to the regret, and it is already shipped.
- A UI hint that the cargo gun can be mounted back. Worth doing, not part of this.

## Part 2 — implementation memo

Written for an implementer with no memory of the planning conversation. Line numbers are HEAD as of
writing; re-read the file before editing.

### Mechanics

**Boost count** (`UpgradeOffer`): for a new-weapon bucket of size `size`,

```
freeOfThatSize = TurretMount.FreeSlots(view, size).Count
least = min over TurretMount.MountedSlots(view) where TurretMount.Fits(slot.Size, size)
            of WeaponUpgrade.CountOn(player.UpgradeCounts, slot.WeaponName)
boost = freeOfThatSize > 0 ? 0 : (least + 1) / 2          // 0.5, rounded up; 0 when no such mount
```

`MountedSlots` (`src/Domain/TurretMount.cs:50`) already returns `(SlotEntity, SlotDefinition,
WeaponName)`; `SlotDefinition.Size` is what "the mounts this gun could take" means. `CountOn` sums
`UpgradeCount.Count` for a weapon name (`src/Domain/Components/GameplayComponents.cs:63-89`). Cargo
guns are not in `MountedSlots`, so they are excluded for free.

**Variant generation** (`NewGunOptions`, `src/Domain/Progression/UpgradeOffer.cs:446`): the unowned,
size-matched, level-unlocked pool is shuffled as today. Build `MaxNewWeaponCardsPerClass` cards by
walking that shuffled list round-robin (round 1 = every gun once, round 2 = the same guns again), and
for each card roll a set with `WeaponUpgrade.Pick(gun, min(boost, legalStats.Count), level, rng)`.
Skip a card whose (gun, set) already exists in the bucket. With `boost == 0` every set is `None` and
the behaviour is exactly today's.

**The set is a flags mask, not a list**: `[Flags] enum WeaponPreUpgrade { None, FireRate,
ProjectileSpeed, Range, MissileHealth, ArmorPierce }` next to `DefenceTier` in
`src/Domain/Stats/UpgradeStats.cs` (`Damage` is represented by its own bit — see the note below —
or kept out of the mask and added as a one-card flag; pick one and use it everywhere). A mask keeps
`UpgradableOption` a value-equal record struct, which the deal depends on: `strong == weak`,
`IsDealt`, `hand.Any(same pair)` and `SameBundles` (`UpgradeOffer.cs:176-200, 454`) all become
variant-aware without touching their logic. `Damage` cannot live in the same mask as the six stats if
`Pick` is meant to include it — put it in the mask (`Damage = 1 << 5`) and let `Pick` take it at most
once.

**Apply, once, in Domain.** `GameSession.ApplyUpgrade` (`src/Game/GameSession.cs:1239-1345`) is today
the only home of the per-weapon stat math, and it only reaches **mounted turret entities** — a cargo
`Turret` record can never be upgraded by it. Move the six per-weapon cases into
`src/Domain/Progression/WeaponUpgrade.cs`:

```csharp
public static Turret ApplyStat(Turret turret, UpgradeOption stat)   // the FireRate / ProjectileSpeed /
                                                                   // Range / Damage / MissileHealth /
                                                                   // ArmorPierce bodies, verbatim
public static Turret Apply(Turret turret, WeaponPreUpgrade mask)    // fold the set over ApplyStat
public static List<UpgradeOption> LegalStats(WeaponType gun, WeaponStats live, int level)
public static int CountOn(UpgradeCounts counts, string weaponName)
```

`ApplyUpgrade` keeps the hull-level cases and calls `ApplyStat` per matching turret; `AddStatCards`
(`UpgradeOffer.cs:382-417`) builds its cards from `LegalStats`, so the offer gates and the pre-apply
picker have one home and cannot drift. `FireRate` on a growing volley means +1 pellet, `Range` moves
both `Turret.Range` and `WeaponStats.ShotLifetime`, `Damage`/`MissileHealth` steps come from the
**table** base — all of that must survive the move unchanged; pin it with a test before moving.

**Granting.** A new-weapon card is granted by putting the pre-built carried turret into cargo:

```csharp
var carried = WeaponUpgrade.Apply(TurretMount.CarriedTurret(gun), card.PreApplied);
```

then the same R23 refusal the mount paths already use (`TurretMount.AlreadyAboard`, reachable only
from a scripted bench hand) and `WriteCargo(cargo.With(carried))`. Three call sites stop creating a
fresh gun from the table and start receiving that `Turret`:

- `ChooseBundle` (`GameSession.cs:1153`) — `SplitBundle` (1192) becomes "the new-weapon cards of this
  bundle (0, 1 or 2) and the rest"; apply the rest through `ApplyUpgrade` **first** (the shipped
  ordering rule: an upgrade spent on a gun the new gun displaces travels with it), then store each new
  gun in cargo, then open the mount screen holding the **large** one.
- `ApplyMountClick` (`GameSession.cs:992-1047`) — the `else if (WeaponType.FromName(placingName) is
  { } fresh)` branch disappears: the gun is already in cargo, so the existing cargo branch mounts it
  with its stats.
- `MountIntoFirstFit` (`GameSession.cs:1209`, scenario/auto path) — take the `Turret` instead of the
  `WeaponType`; free slot → `TurretMount.Mount(..., carried, ...)`, else cargo.

With that ordering `_heldIsCardWeapon` (`GameSession.cs:98, 1051-1091`) has nothing left to do — a
paid-for weapon is in cargo before the screen opens, so `DropHold`, `StoreHeldInCargo` and
`LeaveMountsScreen` collapse to clearing the hold. Delete it rather than keeping the flag. Adjust
`MountsHint` (`GameSession.cs:975-987`) wording only.

`UpgradeCounts` must be bumped for every pre-applied stat at grant time
(`counts.Increment(stat, gunName)`). Without it the Tab stats screen shows honest numbers but an `x0`
count column for the stats the gun arrived with (`StatsScreenRenderer.cs:256-278` reads live `Turret`
values and pairs each with `CountOf` → `UpgradeCounts.GetCount`), and D2's boost count next level would
not see them.

Keep the pre-apply **outside** `TurretMount.CarriedTurret` — that function is what
`CargoTest.FreshlyCarriedWeaponStartsAtItsBaseStats` (`src/Tests/CargoTest.cs:42-50`) pins as the base
starting point of a fresh gun, and every other mount path reads it too.

**Card face** (`src/Game/UpgradeMenuRenderer.cs`): no test reads the renderer, and the card→bundle
mapping is by index only (`GetUpgradeCardRect` / `ReadChoiceSelection`), so nothing here needs new
hit-target work. `GetUpgradeLabel` (231) prints the set into the
label — `new weapon Rail Gun · rate, damage, range` — because the specs block is skipped below
`MinCardWidthForStats` (196) and the set must survive narrow cards. `NewWeaponSpecs` (302) must print
range and damage **from the upgraded gun**: build
`WeaponUpgrade.Apply(TurretMount.CarriedTurret(weapon), option.PreApplied)` and read `Range` /
`Weapon.Damage` off it. Reading the table there is the R10 lie this plan must not introduce.
`GetUpgradeValue` (274) keeps printing `large` / `small`. Card geometry is unchanged
(`UpgradeCardHeight = 200`, `MaxUpgradeLabelLines = 3`): the label grows by at most one wrapped line
and the specs block stays two lines. `MenuCopy.UpgradeWeaponLine` ("A new gun goes into your hand,
then into a mount", `src/Game/MenuCopy.cs:53`) must say what D5 actually does — the gun is paid for,
and one level can pay for two.

**Deal rules to change** in `UpgradeOffer`:

- `TryTakeWeakFor` (274) — drop the "two new guns" refusal entirely. Two new guns in one bundle is
  then only ever large (powerful) + small (weak), because `IsPowerfulCard` (106) makes a small gun
  weak and a large gun powerful, and `EveryBundle_IsOnePowerfulHalfAndOneWeakHalf` enforces that.
- `NextPair` (171) — drop the two `IsDealt(hand, new weapon)` refusals (178-179). Keep `strong == weak`
  and the identical-pair refusal (176, 180); the "fewest offered first" `Candidates` order (192) is
  what spreads repeats.
- `FallbackPair` (208) — keep its two-new-guns refusal: that path can pair two **small** new guns.
- Caps: `MaxNewWeaponCardsPerClass = 3` unchanged. The comment at 39-40 ("whether they fit a free hull
  mount is the player's problem") must be rewritten: mounts now change what the gun *arrives with*.
- **`Drain`/`NamesGunTimes` (253-268) count by weapon name, not by card.** So `MaxStrongCardsPerGun = 3`
  / `MaxWeakCardsPerGun = 2` bound how many *variants* of one gun reach a pool — three large variants of
  one gun is the maximum, and raising the milestone cap above that needs this per-gun cap to become
  variant-aware. Leave it as it is; do not silently let variants past it.
- `FormatChoice` (497) prints the set (`new:RailGun(rate+damage)`) so the `[UPGRADE]` rows are readable
  and the diagnostics bench can be checked against a variant.
- The class header comment (24-25) states "never a repeated new weapon" as an invariant; it becomes
  "never the same gun with the same set twice in one hand".

### Invariants to keep

1. **A card is never a lie (R10).** The range/damage/set printed on a card and the stats granted come
   from the same `WeaponUpgrade.Apply`.
2. **A pre-applied set is non-empty only when no mount the gun fits is free** — and on the shipped
   deal that means milestone hands only (an ordinary hand gates new guns behind
   `HasFreeMountForMissingClass`, `UpgradeOffer.cs:292`, which requires a free mount). Pin this with a
   test rather than assuming it: it is what keeps `Damage` pre-applies inside R33.
3. **R23**: one copy of a gun aboard (hull ∪ cargo) no matter how many variants one hand offers.
4. **One powerful + one weak half per bundle** (`IsPowerfulCard`), on every hull including the
   `FallbackPair` exception.
5. **A re-deal obeys every deal rule** — variant sets come from the same `Roll` and the same
   `view.Rng`. Never a `Random.Shared`.
6. **No two bundles of one hand are the same pair**, and no bundle carries the same card on both halves.
7. `Roll` returns null only when the ship has no turret; the level is not consumed.

### Tests (sim-only, fixtures from `HandCompositionTest.CreateHull` / `LevelUpSlotOfferTest`)

Add a new file `PreUpgradedNewWeaponTest` plus `WeaponUpgradeTest`:

- `NewGun_IsPlain_WhenAMountItFitsIsFree` and `NewGun_ArrivesUpgraded_WhenNoMountOfThatSizeIsFree`
  (reuse the seven-mount `FullyBookedHull` fixture, `LevelUpSlotOfferTest.cs:30-58`).
- `Boost_IsHalfRoundedUpOfTheLeastUpgradedGunInTheMountsItCouldTake` — stamp `UpgradeCounts` on the
  fixture (2 on one large-mount gun, 7 on the other) and assert the boost is `(2+1)/2 = 1`; include
  `N = 0 → 0` and a `N` larger than the gun's legal-set size → capped.
- `CargoGunsAndGunsInOtherMounts_NeverSetTheBoost`.
- `PreAppliedSet_IsAlwaysLegal` — a hitscan gun never carries `ProjectileSpeed`, a support gun at the
  R29 roof never carries `Range`, a guided volley at its budget never carries `FireRate`, no pierce
  below L5, `Damage` at most once, no stat twice.
- `PreAppliedUpgrades_AreGrantedOnTheGunAndCounted` — deal, take, mount; assert the live `Turret`
  equals `WeaponUpgrade.Apply(CarriedTurret(gun), card.PreApplied)` (range, damage, pellet count,
  lifetime, pierce) and that `UpgradeCounts.GetCount(stat, gun)` reports each one.
- `SameWeapon_AppearsAsDifferentCards_InOneHand` — a booked Heavy at L10/15/20 sees one unowned gun
  offered with two different sets.
- `Hand_NeverOffersTheSameGunWithTheSameSetTwice`, and the pair rule still holds:
  `Hand_NeverCarriesAnIdenticalPair` stays green untouched.
- `Bundle_CanCarryANewLargeAndANewSmallGun` (deal side) + `BothNewGunsOfABundle_AreGranted`
  (`BundleChoiceTest`: after taking, both names are aboard or in cargo, each with its own set, and
  each got its `UpgradeCounts`).
- `MilestoneHand_FillsItsFirstBundlesWithNewGuns_AndStillDealsADamageCardPerClass` — D6.
- `RerollRevealsTheSameRule` — a re-deal on a booked hull never offers an owned gun and always
  computes the boost from the current mounts.
- `WeaponUpgradeTest.ApplyStat_MatchesTheShippedMath` — write this **first**, against
  `GameSession.ApplyUpgrade`'s behaviour through `BundleChoiceTest`-style grants, then move the code.
- `CargoTest.FreshlyCarriedWeaponStartsAtItsBaseStats` must stay green untouched: the pre-apply is a
  step on top of `CarriedTurret`, never inside it.

Retarget (these assert the rules being relaxed — do not delete them, invert their intent):

- `LevelUpSlotOfferTest.AHand_NeverDealsTheSameWeaponTwice` (85) → *the same gun may appear more than
  once, never with the same set*.
- `HandCompositionTest.Hand_NeverRepeatsANewWeaponCard` (79) → same as above.
- `HandCompositionTest.Bundle_NeverCarriesTwoNewGuns` (144) → a bundle may carry a large and a small
  new gun, and never two of one size.
- `RerollOfferTest.Redeal_MilestoneHand_StillCarriesAtMostThreeNewWeapons` (101) — still true, but
  count **cards**, not distinct names.
- `HandCompositionTest.MilestoneHand_OffersANewLargeGunAndADamageCardPerClassAboard` (264) — unchanged
  expectation, verify it still passes with new+new pairing eating the weak halves.

The scripted bench (`SPACEVORS_DIAG_UPGRADES`, `UpgradeOffer.ParseEntry`) keeps producing a **plain**
gun (no set): benches must stay reproducible, so a scripted entry has `PreApplied = None`.

### Determinism, RNG, perf

Variant sets draw from `view.Rng` — roughly one shuffle per new-weapon card plus the round-robin — so
the draw count per offer grows and a seeded run shifts. Documented precedent: plans/REROLL_TOKENS.md
D3, plans/UPGRADE_BUNDLE_PAIRS.md §6. `WorldRngTest` pins spawn determinism only, so nothing breaks.

Measure per AGENTS.md: `.tools/scenario.sh ammo-spray --sim --iters 3` — expect unchanged, because
the offer path is not reachable inside a gated sim (TROUBLE_SHOOTING.md #71). The render change is one
extra wrapped text line on new-weapon cards. Price the real change the way PAIRS did it: a
`SPACEVORS_DIAGNOSTIC=1` window run on a booked hull reading the `[UPGRADE]` rows at L5/L10/L15
(a `new:Gun(set)` row, then the `[MOUNT]` rows for both guns) and confirming the stats screen shows
the arrival upgrades.

### Commit sequence (tree green each time)

1. `WeaponUpgrade` extraction + `WeaponUpgradeTest` (no behaviour change), and `ApplyUpgrade` /
   `AddStatCards` calling into it.
2. `WeaponPreUpgrade` on the card, `LegalStats` + `Pick`, boost computation and variant buckets,
   `FormatChoice` — deal-side tests only; every card still prints as today when the boost is 0.
3. Grant path: cargo-first, `_heldIsCardWeapon` deleted, two new guns per bundle, `UpgradeCounts`
   bumped. App tests.
4. Renderer: set in the label, specs from the upgraded gun. Live window check.
5. Retarget the four relaxed tests; docs (§ below); this file → `Status: shipped` +
   `## Shipped (YYYY-MM-DD)` with the arithmetic actually observed and the perf verdict.

### Docs to touch when it ships

- This file's header + `## Shipped`, and its PLAN.md index line (currently under
  *Proposed, not implemented*).
- PLAN.md controls line 61: the new-weapon card's printed content (set + upgraded numbers) and the
  two-guns-in-one-bundle grant.
- plans/UPGRADE_BUNDLE_PAIRS.md: append a dated `## Follow-up` — §8's "no pre-upgraded new weapons"
  (:150), §10's "not here" (:299) and §3.7's one-new-gun-per-bundle are what this supersedes; do not
  rewrite its body. plans/UPGRADE_BUNDLES.md:92 says the same and is already marked superseded.
- plans/TURRET_LAYOUTS.md: a paid-for new gun enters cargo at grant time, so the mount screen places
  any number of them in one visit.
- plans/UPGRADE_BUNDLE_DUPLICATES.md: append a dated `## Follow-up` — its fill-pass rules "one new gun
  per bundle" (:98) and "a new gun already on the table is not offered again" (:245) are the two this
  replaces with "never the same gun with the same set twice".
- `MenuCopy.UpgradeWeaponLine` (`src/Game/MenuCopy.cs:53`) — the first-hands teaching line about where
  a new gun goes.
- ARCHITECTURE.md line 220 is already stale (it still describes the pre-bundle slot shape): when
  editing it, one line — a card may carry a pre-applied upgrade set, and the printed numbers and the
  grant read one Domain function.

## Shipped (2026-11-14):

**What landed**
- `WeaponPreUpgrade` (flags) rides on `UpgradableOption`, so a card is a **(gun, arriving set)** pair. The
  mask — not a nested object — keeps `UpgradableOption`'s value equality, which every "same card", "same
  pair", "same hand" rule in the offer already rests on.
- `src/Domain/WeaponUpgrade.cs`: one home for per-gun upgrade arithmetic — `ApplyStat` (what one card does
  to a turret), `Apply` (what a set does), `ArrivingTurret` / `WithArrivingUpgrades` (what a grant hands
  over), `LegalStats` (which cards that gun may be offered, now asked by both the stat cards and the
  arriving set), `CountOn`, `BitOf` / `StatsOf`. `GameSession.ApplyUpgrade` lost its six per-gun cases;
  `UpgradeOffer.AddStatCards` lost its own legality gates.
- The deal: `ArrivingUpgrades` is 0 while a mount of that size is free, else `(least + 1) / 2` over the
  guns standing in the mounts a gun of that size could take; `ArrivingSet` samples that many stats from
  the gun's legal set (plus `Damage` at a milestone) without replacement. `NewGunOptions` walks shuffled
  builds of unowned guns round-robin, so a booked hull sees one gun as several builds and never the same
  build twice.
- The grant: `src/Domain/Progression/NewWeaponGrant.cs` — cargo-first. The gun enters `WeaponCargo`
  carrying its set and the set is stamped into `UpgradeCounts`, so the stats screen's count column is
  true and the next arriving gun is measured against it. `GameSession` lost `_heldIsCardWeapon`, `DropHold`,
  `StoreHeldInCargo`, `LeaveMountsScreen`, `FreshCopyAlreadyAboard`, `MountIntoFirstFreeSlot` and
  `SplitBundle`: a paid-for gun is never thrown away, and one visit to the mount screen can place both
  guns of a two-new-gun bundle.
- The card face prints the set in its label (`new weapon Rail Gun — with rate, damage`) and its spec block
  prints the **arriving** numbers, because the grant runs off the same `ArrivingTurret` call — the card
  cannot print a number the player will not receive (R10).
- Milestone hands: `NextPair` no longer refuses a new+new pair, so on a booked hull the bundles lead with
  new guns while the `Damage`-per-class cards still land (D6 — no cap was changed).

**Deviations from the memo**
- The memo left the grant in `GameSession`; it moved to Domain as `NewWeaponGrant` so the cargo + counts
  half is testable without a window (the mount *placement* stays in the app, where the held-weapon state is).
- Verification needed a scripted arriving build, so `SPACEVORS_DIAG_UPGRADES` grew a `+Stat` suffix
  (`RailGun+FireRate+Damage`) in `UpgradeOffer.ParseEntry`, documented in DIAGNOSTICS.md and pinned by
  `LevelUpScriptTest`.
- D2 was implemented as written — `(least + 1) / 2` over mounted guns of the sizes the new gun fits, cargo
  excluded — and the rounding is integer `(n + 1) / 2`, i.e. half rounded up.

**Tests** — `src/Tests/PreUpgradedNewWeaponTest.cs` (10): plain while a fitting mount is free; boosted when
booked; the boost is half-round-up of the *least* upgraded gun in the mounts it could take, and neither a
heavily upgraded chip mount nor a cargo gun raises it; every arriving stat is one that gun could have been
sold (no projectile speed on a beam, no range past the R29 roof, no pierce below L5, `Damage` only at a
milestone and at most once); the grant hands over exactly `ArrivingTurret(card)` and counts it; an owned
gun is never offered again in any build and cannot be granted twice; two builds of one gun appear on a
booked hull and never the same build twice; a milestone hand on a booked hull carries new guns of both
sizes and still grows the guns aboard; both halves of a two-gun bundle arrive. `src/Tests/WeaponUpgradeTest.cs`
(9) pins the shared arithmetic itself: salvo growth against a faster trigger, range stretching reach and bullet
life together, damage adding a percent of the base instead of compounding, the `LegalStats` gates (round health
only for a guided round, no projectile speed on a beam, no range past the R29 roof, pierce from L5), the mask
round-trip, and `ArrivingTurret` / `WithArrivingUpgrades` being what the grant hands over. Retargeted:
`Bundle_NeverCarriesTwoNewGuns` → `...OfOneClass`, `Hand_NeverRepeatsANewWeaponCard` (now about builds),
`AHand_NeverDealsTheSameWeaponTwice` → `...TheSameBuildOfAWeaponTwice`. Suite: 747 passed (was 723).

**Perf verdict — this change costs 0%, keep.** The offer and the grant run only on a paused level-up, and
neither the scenario host nor `ScenarioWorld` references `UpgradeOffer` / `LevelUpSystem`, so the gated sim
cannot reach it (TROUBLE_SHOOTING #71). Measured anyway, same machine, `ammo-spray --sim --iters 3`:
without the change 2.45 / 2.28 ms median, with it 1.72 / 1.97 / 2.41 ms — the same noise band, budget pass
in every run. The render cost is one extra text line inside a 200 px card on a screen that already pauses
the world.

**Live check** — Heavy hull, `SPACEVORS_DIAG_UPGRADES="RailGun+FireRate+Damage,Hp:"`, forced level-up:
the card read `new weapon Rail Gun — with rate, damage` with `range 1500 / damage 90` (base 1000 / 60 plus
the printed set), and taking it logged `weapon RailGun arrives(FireRate+Damage)`, then
`MachineGun displaced from main_gun into cargo` + `RailGun mounted out of cargo in main_gun` +
`ScatterCannon displaced from heavy_left into cargo` + `MachineGun mounted out of cargo in heavy_left` —
two gun moves in the one visit the old code refused.

**Known limit, recorded deliberately** — this does not make a swap dps-neutral: a fully upgraded
`MachineGun` still out-dps a `RailGun` that arrived with one or two of its six possible upgrades. It
removes the naked-base-weapon feel and makes cargo rotation worth using. Playtest knobs are listed above:
the 0.5 multiplier, the least-vs-mean basis, whether `Damage` belongs in the arriving set, and a per-class
cap on how many builds of one gun may share a hand.

## Follow-up (2026-11-15): the arriving set is graded

plans/SUPER_UPGRADES.md D5 added a grade to upgrade cards, and a new gun is a card: when a hand spikes a new-gun
bundle half, that gun arrives carrying the **whole** `least` count instead of `⌈least/2⌉`. Everything else here holds
unchanged — the same `Fits` basis, the same legal-stat cap, and the same "a plain gun while a mount it fits is free",
because with nothing displaced `least` is 0 and there is no bigger set to offer, so the deal leaves the card unspiked
rather than inventing upgrades for a pure addition.
