# UPGRADE_BUNDLE_DUPLICATES.md — a thin side may repeat a card so the hand is still five bundles

Status: shipped (2026-11-03) — implemented and verified; `## Shipped (2026-11-03)` at the end records what
landed, the two places it deviates from the plan below, and the perf verdict. It extends the deal of
plans/UPGRADE_BUNDLE_PAIRS.md §3 (which it does **not** otherwise change: five bundles, one strong half and one
weak half each, one taken). Read that plan's `## Shipped` for the state this builds on.

## 1. The complaint and the fix

A hand is short when the strong side has fewer than five distinct cards to offer. Today a short strong pool
simply yields a short hand: three cards on a one-kill-gun hull at level 2, four from level 5, one on a hull that
mounts nothing but a chip. That is the "not enough to choose between" complaint again, one layer down.

The fix: **repeat a half rather than shrink the hand.** Two bundles may carry the same strong card as long as
their other halves differ, so the pairs are different cards and each is a real decision:

```
1  HeavyCannon range +15 %      2  HeavyCannon range +15 %      3  HeavyCannon attack speed +15 %
   MachineGun range +15 %           Hull HP +8                       Hull HP +8
4  HeavyCannon attack speed…    5  HeavyCannon projectile speed…
```

`Heavy range + MachineGun range` and `Heavy range + Hull HP` are different offers; the player is choosing which
weak half they want, and the repeated strong half is what the hull actually needs.

## 2. Where the short hands actually are (measured in the run of 2026-11-03)

The shape sweep and the live window run in `plans/UPGRADE_BUNDLE_PAIRS.md ## Shipped` give the real numbers:

| case | bundles today | why |
|---|---|---|
| every hull, level 10+ | **5** | three kill cards + `Armor` + `Shield` fill the strong side |
| one-kill-gun hull, levels 5-9 | 4 | 3 kill cards + one defence card (`Shield` is gated at 10) |
| any hull, levels 2-4 | 3 | the kill gun's 3 stat cards, no defence yet |
| hull with no chip gun, levels 1-2 | 3 | **the weak side** is thin: 2 engine cards + `Hp` |
| hull mounting only a chip (a small gun in a large mount) | 1 | strong side is `Armor` alone, or empty → the fallback pair |

So the strong-side repeat fixes rows 2, 3 and 5; row 4 is the mirror of the same problem and needs the same
mechanism pointed at the other side (§3.2). From level 10 on nothing changes — this reshapes levels 1-9.

## 3. The rule

### 3.1 Repeats are unlimited; the pair is the limit (decided 2026-11-03)

There is **no copy cap**. One card may sit in as many bundles as the hand needs, and the only thing that stops
a repeat is §3.3: the pair it would form must be a pair this hand has not dealt. Repeats are drawn from the
cards the hand already holds — the distinct pass runs exactly as now, so the promises, the per-gun caps and the
pool order are untouched — and only while the hand is below five.

Repeats go to the card with the **fewest copies so far** (ties broken by deal order), so extra bundles spread
across a side instead of piling on one card: that is what produces the §1 shape — range ×2, attack speed ×2,
projectile speed ×1 — rather than range ×3 next to two cards that appeared once. The invariant worth asserting:
within one side of the hand, no card carries more than one copy more than the least-repeated card on that side.

That gives the hand size a one-line formula, and it is what the tests assert:

    hand = min(BundlesPerLevel, distinct legal (strong, weak) pairs)

Two bundles that differ only in their bottom row are two real offers, so the hand is five whenever the two sides
together can name five different pairs. A hull with one legal upgrade and one legal weak card still gets one
bundle: `A + B` twice is not a choice, and §3.3 refuses it. A hull with one strong card and five weak cards gets
five — the decision is which weak half, which is a decision.

### 3.2 Either side may repeat, strong first

The repeat pass walks strong-major (strong card outer, weak card inner), so a repeated **strong** half is always
tried before a repeated weak half (decided 2026-11-03). That gives the user-visible shape above on a strong-thin
hull, and keeps early levels five-wide on a weak-thin hull (row 4 of §2). One rule, one loop.

### 3.3 What may not repeat

- **A new-weapon card, on either side.** R23 allows one copy of a gun type aboard, and three bundles all
  offering `new:RailGun` reads as a bug, not as variety. The per-class new-weapon cap (`MaxNewWeaponCardsPerClass`)
  already supplies new-gun variety.
- **An identical pair.** `(strong, weak)` may appear once. This is the invariant the whole change rests on, and
  it is what makes the repeated card a decision instead of a duplicate.
- **Both halves at once.** A new pair differs in at least one half by construction; a pair where *both* halves
  are already paired together is refused by the identical-pair rule.

`Damage`, `Armor` and `Shield` **may** repeat like any other stat card (decided 2026-11-03): a second
`+50 % MachineGun` on a milestone hand is the promised card offered twice with different weak halves, which is
the same logic as the user's example. Excluding them would drop a one-kill-gun milestone hand back to four
bundles for no gain in honesty — each card is still exactly what it prints.

### 3.4 Determinism and cost

The pass draws **no RNG** — it walks the hand in deal order — so the number of random draws per deal is
unchanged and every existing seeded test keeps its first-pass contents. The final `Shuffle(bundles, rng)` is
untouched, so a repeated card is not a repeated *position*: card 1 and card 4 can both read `MachineGun range`
in one deal and swap in the next.

## 4. What does not move

- `XpStepPerLevel = 5`, two cards a level, one bundle taken: unchanged. A repeat adds no grants — the player
  still takes exactly one bundle and gets exactly two cards. This is a *choice-texture* change, not a balance
  change, and that is why it is cheap.
- The strong/weak classification (`IsPowerfulCard`), the defence tiers (`+1`/`+2`, `+8`/`+16`), the pairing rule
  (one new gun per bundle), and every legality gate (R19 ceilings, R24/R25 support roof, R18 volley budget, R33
  milestone-only `Damage`, R23 owned-gun refusal) — a repeated card was legal once and only one copy is ever
  taken, so no gate can be crossed by a repeat.
- The reroll rule (a charge may not buy back the same hand; one retry). A re-deal may repeat cards the same way;
  `SameBundles` still compares whole hands.
- The renderer. It already prints both halves of every bundle, so two cards with the same top row and different
  bottom rows need no new layout.
- The scripted bench (`SPACEVORS_DIAG_UPGRADES`, entries consumed in pairs).

## 5. The honest cost

A repeated top row is **nominal** variety: the interesting half of those bundles is the bottom row. Without a
copy cap the extreme is a hand whose five top rows all read `HeavyCannon range +15 %` and whose whole decision is
the bottom row — five real pairs, but a thin decision. That is the accepted trade of the no-cap decision (D1),
and it is what playtest has to judge; the escape hatch is §7 knob 0, which is one `if` and a constant.

The cheaper alternative, if they do: raise `MaxStrongCardsPerGun` from 3 to 4. A gun with four legal stat cards
(MachineGun at level 5+: FireRate, ProjectileSpeed, Range, ArmorPierce) then yields four distinct strong halves
and a one-kill-gun hull needs only one repeat. Do that first only if five-distinct matters more than the cap's
original purpose — keeping one gun from dominating a hand, which this change deliberately softens anyway.

## 6. State and code

One file, `src/Domain/Progression/UpgradeOffer.cs`, plus tests. No component, no renderer, no XP, no tier change.

`DealBundles` grows one call after the distinct pass and before the shuffle:

```csharp
        TopUp(bundles);                       // a thin side repeats a half instead of shortening the hand
        Shuffle(bundles, view.Rng);
```

```csharp
    // Top the hand up from the cards it already holds: a half may be offered twice as long as the pair is a
    // pair this hand has not dealt and neither half is a new gun (§3).
    static void TopUp(List<UpgradeBundle> hand)
    {
        while (hand.Count < BundlesPerLevel)
            if (NextRepeat(hand) is not { } next) return; else hand.Add(next);
    }

    static UpgradeBundle? NextRepeat(List<UpgradeBundle> hand)
    {
        foreach (var strong in Halves(hand, powerful: true))
            foreach (var weak in Halves(hand, powerful: false))
            {
                if (strong.IsNewWeapon || weak.IsNewWeapon) continue;
                if (AlreadyDealt(hand, strong, weak)) continue;
                return new UpgradeBundle(strong, weak);

        // …where both loops are `FewestCopiesFirst(Halves(hand, …), hand)`, so a repeat goes to the card that
        // has been offered least, not to the first card in the hand.
            }

        return null;
    }
```

with `Halves` and `AlreadyDealt` as two short helpers over the hand. There is no new constant: the hand's own
bound (`BundlesPerLevel`) plus the pair rule is the whole rule. Nothing is deleted except the tests that assert
short hands (§8).

The pass runs on **every** hand, including the `FallbackPair` hand of a hull with nothing powerful to offer —
which is what turns that hull's single weak+weak card into a hand of several. It stays the only hand where a
bundle has no powerful half, exactly as documented in `plans/UPGRADE_BUNDLE_PAIRS.md ## Shipped` deviation 4.

## 7. Knob order if the hand reads wrong afterwards

Unchanged from `plans/UPGRADE_BUNDLE_PAIRS.md` §7, with one entry added ahead of it:

0. A copy cap, if a repeating hand reads as padding in playtest: `MaxCopiesOfOneCard = 2` is one extra `if` in
   `NextRepeat` — 2 fills five from three distinct cards per side, 1 turns this change off again.
1. `MaxStrongCardsPerGun` 3 → 4 — more distinct strong halves before any repeat is needed.
2. `XpStepPerLevel` — still reserved; the user's standing rule keeps it at 5.
3. `Damage` as an ordinary strong half; then the defence multiplier; then one strong+strong milestone bundle;
   then the enemy ladder.

## 8. Tests

`src/Tests/HandCompositionTest.cs`, all seeded sweeps over the eight hulls:

- `ShortStrongPool_RepeatsAStatCardToFillTheHand` — Fighter with a single `MachineGun`, levels 5-9: exactly five
  bundles, and some strong half appears twice.
- `ShortWeakPool_RepeatsAWeakCardToFillTheHand` — a two-kill-gun hull with no chip gun at level 2 (weak side =
  2 engine cards + `Hp`): five bundles, and some weak half appears more than once.
- `ChipOnlyHull_FillsTheHandFromItsOneStrongCard` — `Stiletto` in `MainGun` at level 6 (`Armor` alone against the
  weak pool): five bundles whose top rows repeat, none of them an identical pair.
- `Hand_NeverCarriesAnIdenticalPair` — sweep, levels 2-21: the set of `(strong, weak)` pairs has no duplicates.
  **This is the rule the change is judged by.**
- `Hand_NeverRepeatsANewWeaponCard` — sweep: a new-weapon card sits in at most one bundle of a hand, on either
  side.
- `Hand_SpreadsItsRepeatsAcrossASide` — sweep: within one side, no card has more than one copy more than that
  side's least-repeated card (§3.1). Without it the pass would stack five copies of the first kill-gun stat.
- `Hand_IsNeverEmptyAndNeverLongerThanTheHand` — unchanged: five max, never empty while a card exists.
- `Hand_IsFiveBundles_WheneverTheSidesHoldFiveDistinctPairs` — sweep; replaces
  `Hand_IsNearlyFull_FromTheLevelArmorUnlocks` and generalises `Hand_IsFiveBundles_OnceBothDefenceCardsAreLegal`.
  Its predicate is the §3.1 formula, so the test states the rule instead of a level range.
- `ChipOnlyHull_WithNothingPowerfulToOffer_IsStillDealtOneBundle` (shipped test) is retargeted: the fallback hand
  is no longer *one* bundle, it is a hand of repeated weak pairs. It keeps asserting the bundle shape and that
  neither half is a new-weapon pair.
- `BundleChoiceTest`, `LevelUpScriptTest`, `RerollOfferTest`, `ShieldCombatTest`: expected unchanged — the repeat
  pass draws no RNG and consumes no extra pool cards, so the first-pass deal of a given seed does not move.

Then: the full suite (`FullyQualifiedName!~PerformanceBenchmark`, 659 today) plus the `Performance*` tests
separately, and a live window run reading the `[UPGRADE] level-up level=N bundles: …` rows — the rows now show
repeats, e.g. `Range:MachineGun + Hp | Range:MachineGun + PickupRadius | …` at level 2 — with a screenshot of a
five-card hand whose top rows repeat (`.tools/shot.sh`, remember `key Return` clears ship select first,
TROUBLE_SHOOTING.md #77).

## 9. Perf verdict expectation

Nothing on the hot path changes: the deal is off the sim step and is not reachable inside a gated sim
(TROUBLE_SHOOTING.md #71). Measure anyway per AGENTS.md — `.tools/scenario.sh ammo-spray --sim --iters 3`,
expected median unchanged in the 1.38-1.45 ms band (last row 1.43 ms), `budget pass`.

## 10. Decisions (2026-11-03)

- **D1 — no copy cap.** Repeats are unlimited and the identical-pair rule is the only bound (§3.1): the hand is
  five whenever the two sides can name five different pairs. Accepted consequence — a thin hull's hand can repeat
  one card five times with only the bottom row differing. §7 knob 0 is the cap, added later if playtest objects.
- **D2 — either side may repeat, strong first** (§3.2). Covers the thin-weak-side hulls of §2 row 4, which the
  strong-side rule alone would leave three-wide.
- **D3 — `Damage`, `Armor` and `Shield` repeat like any stat card** (§3.3). New-weapon cards never repeat.
- **D4 — `MaxStrongCardsPerGun` stays at 3** for now: land the repeat pass, read a hand in the window, and only
  then consider four distinct stat cards per gun (§5, §7 knob 1).
- **D5 — no visual mark for a repeated half.** The bottom row already differs and the hint row is spoken for; if
  it reads as a bug in playtest, the hint text is where that goes.

## 11. Landing checklist

1. `UpgradeOffer`: `TopUp`, `NextRepeat`, `Halves`, `AlreadyDealt`; one call in `DealBundles` before the
   shuffle. No new constant.
2. New sweep tests of §8; retarget the two shipped tests that assert short hands; leave everything else alone.
3. Full suite + `Performance*`; live window run with the `[UPGRADE]` rows and a screenshot of a repeating hand.
4. `ammo-spray --sim --iters 3` row and verdict.
5. Docs in the same commit: this plan → `Status: shipped` + `## Shipped (date)` with deviations and the perf
   verdict; `## Follow-up (date)` in `plans/UPGRADE_BUNDLE_PAIRS.md` pointing here for the deal's short-hand
   rule; PLAN.md index line (this one is already added as proposed) → shipped; `DIAGNOSTICS.md` only if the
   `[UPGRADE]` row wording changes (it does not).
6. TROUBLE_SHOOTING.md only if the run turns up a new LLM-workflow trap.

## Shipped (2026-11-03)

**What landed.** `UpgradeOffer.DealBundles` now ends in a fill pass: `DealDistinct` cuts the bundles the two
pools allow as before (and leaves what it did not reach in the pools), then `TopUp` fills the rest of the hand
out of `NextPair`, and only then is the hand shuffled. `NextPair` walks the candidates fewest-offered-first on
both sides and returns the first bundle that breaks none of the four refusals: the two halves are not the same
card, they are not both new guns, a new gun already on the table is not offered again, and the pair has not
already been dealt. No copy cap, no new constant, no component, no renderer change, and no RNG drawn — so the
first-pass contents of a seeded deal are exactly what they were, which is why `RerollOfferTest`'s determinism
test and every scripted bench still pass untouched.

**Deviations from the plan.**

1. **The fill pass also reaches the leftover pool, not only the hand's own halves.** §3 had the repeats drawn
   "from the cards the hand already holds". That strands cards: a strong half dealt with a new-gun weak half
   leaves the engine and `Hp` cards sitting in the pool while the hand repeats itself. `Candidates` therefore
   offers the pool's leftovers first (a card with zero copies beats a card with one), which is what §3.1's
   formula actually promises — the hand is five whenever the two *sides* can name five different pairs — and
   makes a genuinely new pair always win over a repeat.
2. **A fifth refusal the plan did not spell out:** a bundle whose two halves are the *same card* is refused.
   In a fallback hand (nothing powerful to offer) both slots draw from one pool, so `(Engine, Engine)` was
   reachable — one card granted twice, presented as a pair.

**Verified.** 664 tests pass (plus the 18 `Performance*` tests). The headline sweep
`Hand_IsFiveBundles_EveryHullAtEveryLevel` holds for all eight hulls at **every** level 2-21: a default hull is
now never dealt a short hand, replacing the old "five from level 10, four from level 5, three at the start"
result. The new invariant tests are `Hand_NeverCarriesAnIdenticalPair` (the rule the change is judged by),
`Bundle_NeverGrantsTheSameCardOnBothHalves`, `Hand_NeverRepeatsANewWeaponCard`,
`ShortStrongPool_RepeatsAStatCardToFillTheHand` (five at L6-9 with one kill gun aboard, one card repeated at
most twice — ordinary levels only, since a milestone hand fills its strong side from new large guns instead),
`ShortWeakPool_RepeatsAHalfToFillTheHand`, and the two chip-only fixtures: the fallback hand is no longer one
card but several ordinary pairs, and the level-6 chip-only hull is dealt five bundles whose whole decision is
the bottom row — the accepted cost of decision D1, asserted rather than hidden. The two shipped cap tests
(`Hand_NeverDrawsOneGunPastItsSideOfTheCap`, `Hand_CapsHullAndUtilityCards`) were retargeted to count the
**distinct** cards drawn, which is what their caps always bounded; a repeated half is the same card offered on a
second bundle, not a fourth upgrade for that gun.

A live window run read the deals: level 2 on the Scout —
`Range:MachineGun + Range:InterceptorTurret | Range:MachineGun + PickupRadius | FireRate:MachineGun +
FireRate:InterceptorTurret | ProjectileSpeed:MachineGun + SideThrust | FireRate:MachineGun + Hp` — which is the
§1 shape: two cards whose top row is `MachineGun range +15 %` with different bottom halves, two with attack
speed, one with projectile speed; level 3 the same way (`Range` ×2, `FireRate` ×2, `ProjectileSpeed` ×1).
`.tools/shots/dup-level2.png` shows the hand drawn: two cards reading `MachineGun range +15 %` side by side is
legible and does not read as a bug, so no visual mark was added (D5). Taking one bundle still grants both cards
(`bundle 4 taken: ProjectileSpeed:MachineGun + SideThrust`), with no exceptions in the run's stdout.

**Perf.** `ammo-spray --sim --iters 3`: median **1.39 ms** against the 1.43 ms row before it, `budget pass`, 0
of 357 frames over — inside the host's 1.38-1.45 ms band, so **unchanged, keep**. The offer path is off the sim
step and not reachable inside a gated sim (TROUBLE_SHOOTING.md #71); the only new cost is the fill pass's small
cross product over at most five halves, which the window run shows is invisible.

**Still open for playtest:** whether five bundles whose top rows repeat read as padding at levels 1-9. §7 knob 0
(a `MaxCopiesOfOneCard = 2` in `NextPair`, one `if`) and knob 1 (`MaxStrongCardsPerGun` 3 → 4) are both still
ahead of the XP-step knob, and nothing about the numbers a card grants moved with this change.
