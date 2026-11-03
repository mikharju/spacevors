# UPGRADE_BUNDLE_PAIRS.md — five bundles of one strong and one weak card, pick one bundle

Status: shipped (2026-11-03) — implemented and verified; `## Shipped (2026-11-03)` at the end records
what landed and the six places it deviates from the plan below. The §7 balance question is what playtest
still has to answer. It supersedes the *pick
mechanic and slot table* of plans/UPGRADE_BUNDLES.md (its §2 and §4) and the shape sections of
plans/UPGRADE_HAND_COMPOSITION.md; the card-kind buckets, the shared `Roll` and every legality gate stay.

## 1. What changes, in one sentence

A level-up deals **five bundles of two cards** and the player takes **one bundle**; every bundle pairs
**one powerful card with one weak card**, so the choice stops being "how much power do I buy" and becomes
"which direction do I take" — and per level the player gets exactly one powerful card, always.

| | now (plans/UPGRADE_BUNDLES.md) | proposed |
|---|---|---|
| dealt | 5 cards | 5 bundles = 10 cards |
| taken | 2 cards, any two | 1 bundle = 2 cards, always one strong + one weak |
| powerful per level | up to 2 | exactly 1 |
| weak per level | 0–2 | exactly 1 |
| hand shape | priorities + leak | five composed pairs + leak that shortens the *bundle list* |

## 2. The axis the split rides on

The strong/weak split is not a new taxonomy: it is **R25's kill/support axis made visible** (plans/WEAPON
_BALANCE.md R24/R25: a kill gun is the damage dealer, a support chip is the screen-control tool, and R33
already sizes `Damage` by that same class — +50 % kill, +25 % support). A bundle is one side of that axis
plus the other, so "take the big gun's upgrade" and "take the chip's upgrade" stop being competing picks
inside one hand — every bundle pays you one of each, and the only decision left is *which* one of each.

**Classification** (one rule, keyed on what the card is about — never on the mount slot it sits in):

| side | cards | why |
|---|---|---|
| **powerful** | any stat card for a **kill-class** gun (`FireRate`, `Range`, `ProjectileSpeed`, `ArmorPierce`, `MissileHealth`); a new **large** gun; `Armor`; `Shield` — and at a milestone also `Damage` for a kill-class gun | these are the cards that change what the ship does or makes it survivable enough to stand still while doing it |
| **weak** | any stat card for a **support-class** gun; a new **small** gun; `Hp`; engine cards (`ForwardAcceleration`, `TurnSpeed`, `SideThrust`, `BackThrust`); `PickupRadius` — and at a milestone also `Damage` for a support-class gun and new small guns | the everyday growth: reach, control of the pickup field, chip behaviour, more hp |

**Defence cards come in two tiers, and the tier is the level's, not the card's.** The shipped steps are
`Armor +2` (`UpgradeDefinition.Armor`) and `Shield +16` (`UpgradeDefinition.Shield`). Those become the
**milestone** tier; an ordinary level may only deal the half-step tier, `Armor +1` and `Shield +8`. So a
strong half is `+1 armour` next to a kill-gun upgrade on an ordinary level, and `+2 armour` next to a new
large gun at a milestone. One multiplier (`MilestoneDefenceMultiplier = 2`) over two steps, in one place,
because the tiers have to be comparable or the milestone spike is unmeasurable:

| card | ordinary level | milestone level | ceiling |
|---|---|---|---|
| `Armor` | +1 | +2 | 12 (`ArmorRules.ArmorCeiling`) |
| `Shield` | +8 | +16 | 32 (`ShieldRules.ShieldCeiling`) |
| `Hp` | +8 (weak half) | not dealt | — |

Three calls inside that table, made here rather than left implicit:

- **`Hp` is weak, `Armor`/`Shield` are powerful.** `Hp` is dealt every ordinary level and is a rate of gain; armour
  and shields are gated (L5 / L10), capped (12 / pool) and change what a hit does. Grouping all defence as
  powerful would let a hand be five defence bundles; grouping all defence as weak would hide the scarce
  cards inside the filler half.
- **A new gun is powerful at large size, weak at small size.** A large gun changes the ship's damage
  profile; a small gun is a chip, and R24/R28 already define it as a screen tool, not a power spike.
- **`MissileHealth` is a weak card.** It rides on a support chip (the interceptor/flak round), so it follows
  its gun's class rather than its defensive sound.

## 3. Dealing the five bundles

For an ordinary (non-milestone) level, in order, each step a wish that may fail:

1. Build the two pools from the existing buckets (`SortIntoKinds` stays the single legality source), with
   **this level's defence tier** already applied to the defence cards:
   **powerful pool** = kill-gun stat cards + new large guns + `Armor` (+1) + `Shield` (+8); **weak pool** =
   support-gun stat cards + a new small gun (only under the shipped D5 rule: the hull lacks a support class
   and a small mount of that size is free) + `Hp` + engine/utility. Every card is legality-gated exactly
   as it is today (R10 never a lie, R17/R18 volley budget, R19 ceilings, R23 one copy per gun type,
   R29 support range roof, R33 milestone-only `Damage`, `Armor` ≥ L5, `Shield` ≥ L10).
2. Take five pairs: one powerful card + one weak card each.
3. **All ten cards distinct.** No card appears in two bundles, and no two bundles are the same pair. Five
   copies of "FireRate for the MachineGun" is not five directions.
4. **Spread caps** (the shipped "no three cards on one gun" rule, restated for pairs): at most **two** of
   the five powerful halves name the same gun; at most **one** bundle carries `Armor`, one carries
   `Shield`, and one carries `Hp`; at most **two** weak halves are engine/utility cards while any
   gun-specific weak card is legal.
5. **The tier is the level's, not the card's.** An ordinary hand never shows a `+2 armour` card and a
   milestone hand never shows a `+1 armour` one; the tier is decided by `IsMilestoneLevel` at deal time, so
   two cards on screen can never disagree about what a level is.
6. **A defence card never overpromises at the ceiling (R10).** `+2` armour against the 12 ceiling and `+16`
   shield against the 32 ceiling both land on a clamp when the headroom is smaller than the step — today's
   card is offered while `CurrentArmor < ArmorCeiling` and the grant is then clamped, so a `+2` card at
   armour 11 quietly delivers `+1`. With the step doubled at milestones that lie doubles too, so this change
   fixes it: withdraw a defence card whose headroom is zero, and label the one whose headroom is smaller
   than its step with the number it will actually grant.
7. **A pair may never contradict itself.** If a bundle's powerful half is a new gun, its weak half must be
   a ship-wide card (`Hp`, engine, `PickupRadius`, `Armor`/`Shield`) — never a card naming a gun. Mounting
   the new gun displaces whatever is in that mount into cargo, and `ApplyUpgrade` only reaches guns that
   are firing, so a gun-specific weak half could arrive dead. This rule replaces the shipped
   `PruneCardsThatNoLongerApply` (see §6).
8. **Short hands are honest.** If the powerful pool has fewer than five cards, deal fewer bundles — never
   pad a bundle with a second strong or a second weak card, because the whole legibility of the screen rests
   on "one of each, five ways". If the weak pool is short, repeat an engine/utility card (stat cards are
   repeatable by design, ceilings aside) before dealing fewer bundles. A hull with no turret is still dealt
   nothing and the level is not consumed.

So the leak still exists, but it changed job: it no longer fills a *slot* with a foreign kind, it decides
**which card from which pool** and **how many bundles** the level can honestly show.

## 4. Milestone levels: new guns, their damage, and the strong defence tier

A milestone keeps exactly what it is today — the build beat, dealt from **new weapons and their damage
upgrades** — and adds the doubled defence tier to the powerful mix. The pools swap, the invariant
(one powerful + one weak per bundle) does not:

| side | milestone pool |
|---|---|
| **powerful** | a new **large** gun; `Damage` for a kill-class gun already aboard (+50 % of base, R33); `Armor` **+2**; `Shield` **+16** |
| **weak** | a new **small** gun; `Damage` for a support-class gun already aboard (+25 % of base, R33) |

Two consequences of writing the pools that way:

- **No ordinary stat cards at a milestone** (no `FireRate`, `Range`, `Pierce`, engine card): that is already
  how a milestone hand is built, and it is what keeps the five bundles reading as five *build* directions.
- **`Hp` is not a milestone card.** Defence at a milestone arrives as the strong tier — `+2` armour, `+16`
  shield — so a milestone level trades its health step for a defence step or a gun. `Hp` stays the ordinary
  level's weak half, which is also where the shield's *pool* refill story lives.

Guarantees, where the pool allows them:

- at least one bundle whose powerful half is a **new large gun** (the option to switch into, even with every
  mount booked — the rule shipped in plans/UPGRADE_BUNDLES.md §2);
- at least one bundle whose powerful half is `DamageKill` for a gun already aboard;
- at least one bundle whose weak half is a **new small gun** or a `DamageSupport` card, while either is
  legal — the milestone must still offer a way to grow the chip line, not only the kill line;
- `Damage` cards stay **milestone-only** (R33 unchanged), at most one `Damage` card per bundle and never two
  in the same bundle.

**When the milestone weak pool runs dry** — every small gun owned and no support gun aboard, so neither a new
small gun nor a `DamageSupport` card is legal — fall back to the ordinary weak pool in its own order
(support-gun stat cards, then engine/utility, then `Hp`) rather than dealing a strong+strong bundle or
shrinking the hand. That keeps the one invariant true on every screen, which is the whole point of the
design; the alternative is open question 3.

Keeping the pairs 1+1 at milestones instead of making them strong+strong is a deliberate call: one
invariant is easier to read on screen and easier to reason about in the balance tests, and the milestone
beat comes from *which* kinds appear (`Damage`, new guns, the doubled armour) rather than from a different shape. If playtest
says milestones stopped feeling like an event, the knob is one milestone bundle pairing two powerful halves
(open question 3).

## 5. Rules that do not move

- Every legality gate and ceiling stays where it is; both pools are built from already-legalised cards.
- `XpStepPerLevel = 5`: unchanged, again by request. Levels stay on the XP clock.
- **Mounts are not an upgrade**: no new turret slots, no pre-upgraded new weapons (§8).
- The reroll token re-deals the five bundles through the same dealer; with one pick per level there is
  nothing to re-sell, so the taken-card filter goes away and the one-retry "don't pay for the same hand"
  rule stays.
- R23 (one copy per gun type, mounted ∪ cargo) still governs new-gun cards, so the powerful pool of new
  large guns shrinks as the roster is owned.

Two things this plan does add to the rules, both of them about honesty rather than balance:

- **A card's value may now depend on the level it was dealt at.** So far every card's step was a constant in
  `UpgradeDefinition`; the defence tier is the first exception, which is exactly why it goes on the *card*
  (§6) and not into a lookup at grant time.
- **A card must print what it grants, including at a ceiling** (R10). Today a `+2` armour card can sit in the
  hand at armour 11 and deliver `+1`; the doubled step makes that visible, so the deal withdraws a defence
  card with no headroom and relabels one with partial headroom.

## 6. State and code: what lands, what goes

- **New**: `UpgradeBundle(UpgradableOption Powerful, UpgradableOption Weak)` in
  `src/Domain/Components/GameplayComponents.cs`; `PendingUpgradeOptions(UpgradableOption[] Bundles)`;
  `UpgradeOffer.BundlesPerLevel = 5`; a `Classify(option) → Powerful | Weak` rule that is the single place
  the table in §2 lives; `DealBundles(pools, milestone, view, turrets)` next to the existing `DealHand`.
- **The tier rides on the card**: a `DefenceTier { Ordinary, Milestone }` field on `UpgradableOption`
  (default `Ordinary`) and `MilestoneDefenceMultiplier = 2` beside the two steps in
  `src/Domain/Stats/UpgradeStats.cs`. The deal stamps it from `IsMilestoneLevel`, `ApplyUpgrade` reads it for
  the additive step, and `UpgradeMenuRenderer` reads the same field for the `+1` / `+2` label — one source, so
  the number printed and the number granted cannot drift (R10). `UpgradeCounts` keeps counting per
  `(stat, weapon)` as it does now: the tier is not a second card to count.
- **Deleted**: `PicksLeft`, `Taken`, `PendingUpgradeOptions.Deal`, `UpgradeOffer.SpendPick`,
  `UpgradeOffer.PruneCardsThatNoLongerApply`, `CardsPerLevel` (replaced by `BundlesPerLevel` + a fixed
  `CardsPerBundle = 2`), the second-pick re-read in `GameSession.HandleCardStep`, and the picks-left hint in
  `UpgradeMenuRenderer`. The whole two-pick bookkeeping added in plans/UPGRADE_BUNDLES.md §4 exists to let
  pick two see pick one's consequences; a bundle is chosen atomically, so it has no reason to exist.
- **`GameSession`**: one grant instead of a pick loop — apply the weak half, then apply or mount the
  powerful half (new-weapon cards keep their round trip through the mount screen; the weak half is applied
  at pick time, so the mounts screen stays a single-card affair). `_cardInHand` survives; `FinishPick`
  becomes `GrantBundle`.
- **Renderer**: a bundle card shows two rows — the powerful line first, then the weak line — each with its
  own label and value, and the card keeps its number key 1-5. The hand geometry (five cards across the
  bottom) does not change; `GetUpgradeCardRect` and the hover/click mapping stay, so the click-target work
  is already tested. Two rows per card means the value font and `NewWeaponSpecs` block shrink; a new gun
  row keeps its size line (large / small) but loses the four base-number lines to keep the card readable —
  the same numbers are on the stats screen. Defence rows print the tier's own number (`hull armor +1`,
  `hull armor +2`), so the milestone spike is visible before it is taken and never printed as a constant.
- **Diagnostics**: `SPACEVORS_DIAG_UPGRADES` stays **one entry per card granted** (two per level, strong
  then weak), so a scripted bench still pins both halves of a bundle. `[UPGRADE]` rows print the pair:
  `bundle 3: Damage:MachineGun + SideThrust`, and a defence card prints its tier: `Armor(+2 milestone)`.
- **RNG**: ten drawn cards a level instead of five, plus a pair loop — a seeded run shifts. Documented, per
  the precedent in plans/REROLL_TOKENS.md D3.

## 7. The balance consequence, stated plainly

**This is a nerf to raw damage growth relative to what just shipped, and it is the reason it needs a
playtest, not just a build.** One powerful half a level is one kill-gun upgrade a level — the damage cadence
of the *pre-bundle* game, which is the cadence the 2026-11-03 playtest called too slow against the enemy
ladder (hp ×3.5, per-shot 4 → 24, rate ×2, quota 6 → 40 by `MaxTier`). What the bundle buys instead is
variety and no dead picks: every level still grants two cards, the chip/engine half still grows, and defence
is now a visible direction rather than a rare leak.

**The two-tier defence is a buff to the defensive axis, and it has to be read against the ceilings.** With
`Armor +1` on ordinary levels and `+2` on milestones against `ArmorCeiling = 12`, armour takes 12 ordinary
buys or 6 milestone buys to max; `Shield +8` / `+16` against `ShieldCeiling = 32` takes 8 or 4. The hand caps
allow at most one `Armor` and one `Shield` bundle per level, so the max intake is one defence card a level
and the ceilings are what stop the doubled tier running away — the shield pool can be maxed by level ~18 by a
player who takes defence at every milestone and every ordinary level after L10, and past that the card simply
leaves the powerful pool. Two things to watch in the playtest, both of them numbers rather than feelings:

- **R11's through-armour TTK** (420-hp HeavyCannon ≤ 7 s, ≤ 12 s through armour): armour arrives faster than
  it did, and the budget test models damage through `ArmorRules.EffectiveDamage` (TROUBLE_SHOOTING.md #73),
  so re-run it with an armour-taking script rather than assuming the ceiling makes it moot.
- **Milestones no longer grant `Hp`** — the strong tier replaces the health step on 4 of the first 20 levels.
  EHP growth flattens slightly there and the doubled armour/shield is what pays for it; if the run reads as
  "die at the milestone", the knob is a `Hp` weak half at milestones (open question 5), not a bigger armour.

Knobs, in the order to reach for them:

1. **`XpStepPerLevel`** (5 → 4 → 3). Direct, and the only knob that fixes the ramp without touching what a
   card means. Reserved by the user; do not turn it without being asked.
2. **`Damage` as an ordinary strong half, at most one per hand.** +50 % kill / +25 % support per R33, but no
   longer milestone-only. Rationed structurally (one of five bundles, distinct, paired with a weak half),
   which is what R33's milestone-only rule was protecting: it must not become the auto-pick. **Needs R11's
   TTK re-check (420-hp HeavyCannon ≤ 7 s, ≤ 12 s through armour) and R25's class floor before it ships.**
3. **The defence multiplier** (`MilestoneDefenceMultiplier`, 2 → 3, or `+1`/`+2` → `+1`/`+3`). Cheaper than
   touching the ceilings, and it moves only the one axis the tiers introduced.
4. **One milestone bundle of two powerful halves.** Cheap, keeps the ordinary invariant, makes milestones
   feel like the build beat again.
5. The enemy ladder itself (`4·(tier/2)` → `4·(tier/3)`), as in plans/UPGRADE_BUNDLES.md §2.

## 8. Tests

- **Classification**: `Classify` answers the §2 table for every card kind the offer can produce, including
  the three judgement calls (`Hp` weak, `Armor`/`Shield` powerful, new-gun size split, `MissileHealth`
  weak) — a table test, because the whole design balances on it.
- **The tier**: an ordinary hand's `Armor`/`Shield` cards carry the ordinary tier and grant `+1`/`+8`; a
  milestone hand's carry the milestone tier and grant `+2`/`+16`; the card's printed number and the granted
  number come from the same field (one test per pair, deal → grant → read the stat); no hand mixes tiers;
  and at partial headroom the card is relabelled with what it delivers, at zero headroom it is gone
  (armour 11 → the `+2` card reads `+1`; armour 12 → neither tier is dealt).
- **Shape** (`HandCompositionTest` retargeted): every bundle is one powerful + one weak; all ten cards
  distinct; ≤2 powerful halves per gun; ≤1 `Armor`, ≤1 `Shield`, ≤1 `Hp`; ≤2 engine/utility weak halves
  while a gun-specific weak card is legal; a new-gun bundle's weak half never names a gun.
- **Legality survives pairing**: no bundle contains a card the gates already reject (R18 dead salvo card,
  R19 pierce/armor ceilings, R29 support range roof, `Armor`/`Shield` level gates, R23 owned-gun filter) —
  the existing per-card tests should keep passing untouched, which is the point of building pairs from
  `SortIntoKinds`.
- **Milestone pools**: a new large gun in at least one bundle while one is unowned (including a fully-booked
  hull); a `DamageKill` in at least one bundle while a kill gun is aboard; a new small gun or a
  `DamageSupport` in at least one bundle while either is legal; **no ordinary stat card, no engine card and
  no `Hp` in any milestone bundle** while the milestone pools can fill the hand; no `Damage` card in any
  non-milestone bundle. When the milestone weak pool is dry (every small gun owned, no support gun aboard)
  the weak halves fall back to the ordinary pool and the 1+1 shape still holds.
- **Short hands**: a hull whose powerful pool is exhausted is dealt fewer bundles, never a
  powerful+powerful or weak+weak bundle; a hull with no turret is dealt nothing and keeps its XP.
- **Grant** (`TwoPickLevelUpTest` → `BundleChoiceTest`): picking a bundle applies both cards, spends the
  level once, and a new-weapon strong half still opens the mount screen and can be stored in cargo; a
  scripted bench entry pair pins one bundle; reroll re-deals five bundles and keeps the shape.
- **Deleted-behaviour guard**: the dead-card test that justified `PruneCardsThatNoLongerApply` becomes the
  pairing-rule test — a bundle can never contain a card that its own other half would disable.

## 9. Open questions, with the choice this plan makes

1. **Does the ramp still outrun the player?** This plan does not answer it and honestly may make it worse
   (§7). Recommendation: ship the structure, playtest 15 minutes with `[UPGRADE]`/`[THREAT]` rows, then
   reach for knob 2 (`Damage` as a strong half) before knob 1 (XP step) so the fix lands in card meaning
   rather than in pacing.
2. **Is defence as a strong half too swingy early?** Below L5/L10 neither defence card is in the pool, so the
   first ~10 levels always pair a kill-gun card or a new large gun with a chip/engine card. Recommendation:
   accept it and watch whether L5+ hands read as "armour or nothing".
3. **Milestone shape**: uniform 1+1 (this plan) or one bundle of two powerful halves. Recommendation:
   uniform first; the knob costs one deal rule.
4. **How much does a bundle card show?** Recommendation: two rows, new-gun rows showing size + range +
   damage only; full base numbers on hover or on the stats screen. If the pair rows read as cramped at
   1920 px, the fallback is five narrow cards with a "strong / weak" divider rather than a smaller font.
5. **Does the weak half feel like a tax?** Five bundles each carrying a chip/engine card could read as
   "the game hands you filler you must carry". If so, the fix is not dropping the invariant but widening
   the weak pool's interest: the R34/R35/R36 support-job cards (plans/WEAPON_BALANCE.md) are exactly the
   cards that make the weak half a decision.
6. **Is the milestone shield too big a step?** `+16` against `ShieldCeiling = 32` means four milestone buys
   max the pool, and a single one doubles it from nothing. Recommendation: ship `×2` and watch the first
   milestone hand a hull with a shield is offered — if `+16` reads as the obvious pick over a new large gun,
   the honest fix is the multiplier (knob 3), not a ceiling raise, because the ceiling is what makes the
   defence axis finite.
7. **`Hp` absent from milestone hands** (the strong defence tier takes its place): is that felt as a real
   cost at 15/20/25 minutes? Recommendation: keep it out for the first pass — a milestone is where the ship's
   *shape* changes, and health is not a shape — but if the run dies at milestones, add `Hp` to the milestone
   weak pool rather than making the defence tier cheaper.

## 10. Deliberately not here

- **New turret mounts** and **pre-upgraded new weapons** — unchanged from plans/UPGRADE_BUNDLES.md §6; the
  first would change what a "large weapon upgrade" can mean, so it goes in front of that question, not here.
- **Weapon evolutions** (PLAN.md future) — a pair is already a two-card decision; fusing two cards into a
  stronger one is a different economy and should not be decided in the same change.
- **Three-card bundles**, six bundles, or a per-level bundle count that grows with level — one invariant
  first; the count is a knob, not a design.

## 11. Landing checklist

1. Classification + `DealBundles` in `UpgradeOffer`, with `Classify` as the only home for the §2 table, and
   the defence tier (`DefenceTier` on the card, `MilestoneDefenceMultiplier` in `UpgradeStats`) stamped at
   deal time and read by both `ApplyUpgrade` and the renderer.
2. The ceiling honesty rule (§5): withdraw a defence card with no headroom, relabel one with partial
   headroom. Small, and it belongs here because the doubled step is what makes the lie visible.
3. `GameSession` single grant, renderer two-row card, `SPACEVORS_DIAG_UPGRADES` rows.
4. Delete the two-pick machinery (§6) in the same commit, so no half-dead bookkeeping is left behind.
5. Retarget the shape/milestone tests (§8) and rename `TwoPickLevelUpTest`.
6. Docs, same commit: `plans/UPGRADE_BUNDLES.md` → `## Follow-up (date): superseded by
   plans/UPGRADE_BUNDLE_PAIRS.md` + header; `plans/UPGRADE_HAND_COMPOSITION.md` header; `PLAN.md` index
   lines and the controls line (one bundle of two, 1-5 keys); `DIAGNOSTICS.md` bench format;
   `plans/ARMOR_AND_PIERCE.md` and `plans/SHIELDS.md` follow-ups for the two tiers and their ceilings;
   `plans/WEAPON_BALANCE.md` follow-up if R33 moves.
7. Measure: `.tools/scenario.sh ammo-spray --sim --iters 3` — expected unchanged (the offer path is not
   reachable inside a gated sim, TROUBLE_SHOOTING.md #71); the R11 TTK test re-run with an armour-taking
   script (§7); plus a scripted 15-minute window run reading the `[UPGRADE]` bundle rows against `[THREAT]`
   for the §7 question.

## Shipped (2026-11-03)

**What landed.** A level is dealt five `UpgradeBundle`s and takes one, which grants both of its cards: the
powerful half (a kill gun's stat card, a new large gun, `Armor`, `Shield`, and at a milestone the kill gun's
`Damage` card) and the weak half (a chip gun's stat card, a new small gun, an engine or `PickupRadius` card,
`Hp`, and at a milestone the chip's `Damage` card). Defence comes in two tiers stamped on the card at deal time
(`DefenceTier`, `UpgradeDefinition.MilestoneDefenceMultiplier = 2`): `Armor` +1 / +2, `Shield` +8 / +16, and
`UpgradeMenuRenderer`, `ApplyUpgrade` and the `[UPGRADE]` rows all read the card's own step, so the printed
number and the granted number are the same value. `UpgradeOffer` grew `Roll → UpgradeBundle[]`, `DealBundles`,
`PowerfulPool`/`WeakPool` drained in promise order, and `IsPowerfulCard` as the single home of the §2 table.
Deleted with it: `PicksLeft`, `Taken`, `PendingUpgradeOptions.Deal`, `SpendPick`,
`PruneCardsThatNoLongerApply`, `CardsPerLevel`, the post-pick screen re-read, `LeakOne` and the rare-defence
roll, and the mounts screen's `_cardInHand`/`_cardResolved` bookkeeping — a bundle is chosen atomically, so the
whole "second pick must see the first pick's consequences" machinery has no reason to exist. The scripted bench
now consumes entries in pairs (strong, weak) and stamps their tier from the level.

**Deviations from the plan.**

1. **§3.7's pairing rule is weaker than planned, on purpose.** A bundle carries **one new gun at most**, and
   that is all: the plan's "the other half may name no gun the new gun could displace" turned out to strand new
   small guns (they pair with nothing but a ship-wide card), which starved milestone hands into engine cards.
   The stranding was never real — the weak half is applied **before** the strong half is mounted, so an upgrade
   spent on a gun that the new gun then displaces travels into cargo with that gun rather than being dropped.
   `PairSurvives`/`SurvivesMount` are deleted; `TryTakeWeakFor` only refuses a second new gun.
2. **The ceiling rule (§3.6/§5) landed as withdrawal, not relabelling.** A defence card is dealt only while its
   tier's whole step fits under its ceiling; at partial headroom it leaves the deal rather than printing a
   smaller number. That is the rule R19 already applies to pierce and round health, and it needs no per-card
   step override. So at armour 11 the ordinary +1 card is dealt and the milestone +2 card is not.
3. **The per-gun cap is split by side** (`MaxStrongCardsPerGun = 3`, `MaxWeakCardsPerGun = 2`) instead of a
   single 2. With 2 the strong pool could not fill five bundles for a hull with one kill gun: three of a kill
   gun's four stat cards are the variety a build decision is made of, and repeating a chip gun three times is
   what the old cap existed to stop.
4. **A hull with nothing powerful to offer is dealt one pair of two ordinary cards** (`FallbackPair`). Without
   it, a hull whose only gun is a chip in a taken large mount had an empty strong pool → zero bundles → the
   level-up was never consumed and the run hung on an unresolvable level forever. The bundle-pair invariant
   (one powerful + one weak) is checked on every hull's real loadouts; this one case is the documented
   exception and has its own test.
5. **`Classify` landed as `UpgradeOffer.IsPowerfulCard`** (a predicate on the card), and the milestone claim
   "no ordinary stat card, no engine card, no `Hp`" is true only *while the milestone weak kinds can pair*; what
   holds unconditionally — and what the tests assert — is that a milestone hand never deals an ordinary
   **kill-gun** stat card and never a stat/engine card as its **strong** half. `Hp` and engine cards reach a
   milestone hand only through the weak-pool fallback.
6. **A live-run bug, fixed in scope**: a new gun sitting on the **weak** half of a bundle was never granted —
   `ChooseBundle` applied the weak half with `ApplyUpgrade`, which for a new-weapon card is a no-op, and only
   the strong half went through the mount flow. The milestone hands are exactly where a weak half is a new
   small gun, so this was the common case, not a corner. `SplitBundle` now picks out the half that needs the
   mount screen whichever side it is on, and applies the rest first. Found by driving the real window
   (`[UPGRADE] bundle 1 taken: Armor(+2 milestone) + new:Swarmlet` followed by no `[MOUNT]` row).

**Verified.** 659 tests pass (plus the 18 `Performance*` tests on their own). The shape sweep
(`HandCompositionTest`) holds across all eight hulls and levels 2-21: every bundle is one powerful and one weak
card, never two new guns, never a card a legality gate refuses; **five bundles at every hull from level 10**,
4-5 from level 5, and short hands only where the strong pool is genuinely thin. A live window run read the
deals: L2 `Range:MachineGun + Range:InterceptorTurret | FireRate:MachineGun + FireRate:InterceptorTurret |
ProjectileSpeed:MachineGun + SideThrust`; the L5 milestone `Damage:MachineGun + new:Stiletto | new:RailGun +
SideThrust | new:ScatterCannon + Damage:InterceptorTurret | new:LaserLance + Range:InterceptorTurret |
Armor(+2 milestone) + new:Cyclotron`; an ordinary L6 `Armor(+1 ordinary) + ArmorPierce:InterceptorTurret` with
the card printed `hull armor +1` and the hull ending at armour 2 after the milestone `+2`; the L10 milestone
carrying `Shield(+16 milestone) + new:AcidBubbleSpray`, whose pool then shows in the HUD and the `world.shield`
render pass. Mounting a bundle's new gun still opens the mount screen, displaces into cargo, and `Esc` finishes
with the weapon stored rather than lost. Perf: `ammo-spray --sim --iters 3` median **1.43 ms**, inside the
1.38-1.45 ms band of the rows before it, `budget pass`, 0 of 357 frames over — **unchanged, keep**; the offer
path is not reachable inside a gated sim (#71) and the render change is ten text draws instead of five.

**R11 did not need re-running.** Its through-armour TTK is about the *enemy's* armour and the player's DPS; the
player's own defence tiers do not enter that budget, and the suite's TTK tests pass untouched. What is genuinely
unmeasured is the player's own effective HP: ordinary levels now buy half the armour and half the shield they
did before, milestones buy the same as before, and milestones no longer buy `Hp`. §7's knob order is unchanged
and §9's questions 6 and 7 (is the +16 milestone shield too big a step; is `Hp` missing from milestone hands
felt as a cost) are playtest questions, not code ones.
