# ARMOR_AND_PIERCE.md — armor, armor-piercing rounds, armored hulls

Status: shipped (2026-11-03) — see `## Shipped` at the end for what landed, the deviations, and the
perf verdict.

## 1. What this plan builds

The design settled in `plans/WEAPON_BALANCE.md` §6 and the PLAN.md roadmap, now scheduled:

- Hulls can carry **Armor**: flat subtraction per gunfire hit, floored at 1 damage.
- At the start **only the largest, slowest player hulls are armored**: Heavy and Eagle —
  the two hulls on `EngineLayout.Slow`. Hulls that dodge by flying (Manta's Crab, Falcon's
  Pursuit) get none: armor is the compensation for the hull that cannot.
- **Enemy side**: HeavyCannon (the largest, slowest enemy hull, r 78, 50 px/s) starts armored.
  Without an armored target `ArmorPierce` is a card nobody can spend, so enemy armor ships in the
  same pass as the pierce card.
- **Enemies never get armor pierce, ever** (owner decision): armor's counter is not a stat on the
  enemy's gun but the enemy's *round size* — HeavyCannon fires a slightly larger, slower shot that
  out-damages every small enemy's bullet. That heavy slug, dodgeable by a fast hull, is the
  standing threat to armored player ships; the fast chip guns simply bounce off.
- Two new upgrade cards: `Armor` (hull) and `ArmorPierce` (per gun), **both offered only from
  level 5 up**, matching tier-2's `UnlockLevel: 5` and the first milestone hand. Armor cards are
  small (**+2** — one hull, bought slowly); pierce cards are bigger (**+4** — many weapons each
  buy their own line against the one armored hull, and pierce is worthless off that hull anyway).
- The enemy threat mix already fits the roadmap's "slow powerful shots threaten the armored
  hull, fast chip guns bounce off it": enemy ammo speed is capped at 250 px/s (dodgeable by fast
  hulls) and `DamageAdd` climbs +4 every two tiers. The only enemy change is HeavyCannon's shot
  (bigger radius, slower, +4 damage over every other hull) so the armored hull has one named
  thing to fear.

## 2. The mechanic

One formula, one place (`ArmorRules.EffectiveDamage` in Domain/Combat, used by every gunfire
hit site in `CollisionSystem`):

    mitigated  = max(0, target.Armor - round.Pierce)
    effective  = max(1, damage - mitigated)

Decisions and why:

- **Flat subtraction, floor 1** (§6 decision 1): a support chip is never zeroed, the arithmetic
  reads on screen, and armor is a tax on chips and almost nothing to a RailGun — which is the
  intended kill-class/support-class split, now with a second axis.
- **Armor is an `Armor(int Value)` component** on the hull entity only (player and enemy ships).
  Mines, asteroids and missiles (player and enemy) never carry it. A component, not a `Health`
  field, so unarmored entities pay one `TryGetComponent` at the hit site — hits are rare next to
  the broad phase, so the cost is nil (verify per §8).
- **Armor applies to gunfire only** (Ammo and LaserBeam, both directions). Impact damage
  (`ImpactDamage`, already mass/speed-derived and capped) and mine contact damage (flat 12)
  **bypass armor**. Keeping armor scoped to "a tax on shots" is the readable rule; arming
  impact/mine against it would double the rules for no read. Open knob for playtest.
- **Pierce rides the round**: `WeaponStats.Pierce` (new field, default 0) → `Ammo.Pierce` /
  `LaserBeam.Pierce` at fire time. A Pierce card raises the mount's `Weapon.Pierce` exactly like
  a Damage card raises `Weapon.Damage`; the card's step is additive `+4`, never compounding.
- **No gun starts with pierce.** R30 (a bonus is priced when it ships): no table gun was retuned
  "for pierce", and a gun with pierce it never advertised would be a lie. Pierce exists only as
  bought cards (and later, corrosion — §7).
- **Enemies never get pierce, ever** (owner decision, not a v1 shortcut). The counter to player
  armor is round size, not an armor-ignoring stat: HeavyCannon's turret round gets a per-hull
  damage bonus above every other enemy (+4 at the ×4 scale), a larger radius (4 px vs 2.5) and a
  slower flight (140 px/s, the slowest in the fleet) — big, slow, dodgeable by a hull that can
  move, and the only enemy shot that reliably walks through armor 4.

### Numbers (R32's ×4 scale)

| Value | Number | Rationale |
|---|---|---|
| Heavy / Eagle start Armor | 4 | enemy base round is 4 → the swarm's chips floor to 1: "light enemies do not hurt it at start". `DamageAdd` +4 at tier 2 restores a real exchange |
| All other player hulls | 0 | dodge is their armor |
| HeavyCannon Armor | 4 | MachineGun 12→8, ScatterCannon 8→4, a kill class that still bites; a 4-damage chip floors to 1; RailGun 60 barely notices — §6's magnitude guidance ("under what a kill round lands") |
| HeavyCannon shot | +4 damage, radius 4, 140 px/s | 8 at tier 0 vs a chip's 4: through armor 4 it lands 4 while every other enemy lands 1. The named anti-armored-hull threat; fast hulls dance around it |
| Other enemy hulls | armor 0, pierce 0 | one armored hull is one behavior for the player to learn; enemy pierce never |
| `Armor` card | +2, ceiling 12 | one hull, slow investment: Heavy/Eagle cap in 4 cards, others in 6; enemy max per-shot at tier 10 is 20 (+4 HeavyCannon bonus), so armor 12 never makes the player untouchable |
| `ArmorPierce` card | +4, ceiling 8 | many weapons each buy their own line against the one armored hull; pierce 4 beats HeavyCannon flat, and the ceiling keeps the card from stacking into a stat with no buyer |

## 3. Upgrade cards and hand composition

`plans/UPGRADE_HAND_COMPOSITION.md` §3E reserved armor/pierce cards and its bucket map left room
for them; this is that reservation being spent. Both cards gate on `level >= 5` (one constant,
`ArmorUnlockLevel = 5`, matching `MilestoneLevelInterval` and tier-2 `UnlockLevel`).

| Bucket (`UpgradeOffer`) | Cards | Gate |
|---|---|---|
| Hull | `Hp`; **`Armor` from level 5** | Armor withheld at the ceiling (R19 pattern — never a card a rule would refuse) |
| Utility | unchanged | — |
| Support / Kill weapon | existing per-gun cards plus **`ArmorPierce` from level 5** | pierce at ceiling → card withdrawn |
| New weapon | unchanged | — |

- Armor joins the **Hull** bucket (the map's "Hull | Hp (future: shield)" slot is the defensive
  line): from level 5 the guaranteed hull-slot card is Hp *or* Armor, ~50/50. Tradeoff: hp card
  income halves exactly when choices get interesting. Alternative rejected: the Utility bucket —
  it keeps Hp guaranteed but buries armor under 5 engine cards and out of every milestone hand,
  where the defensive choice belongs.
- Milestone hands (5, 10, …) draw Hp-or-Armor in the hull slot; level 5 is the milestone where
  armor unlocks, which is the intended "armor arrives with tier-2".
- `ArmorPierce` is a normal-hand card (milestone hands carry Damage/new-gun cards as shipped).
  Both classes may buy it; against unarmored hulls it is worth 0, so R25's class line is
  untouched — §6 invariant (c) holds by construction.
- Honest rerolls come free: `UpgradeOffer.TryReroll` re-uses `Roll`, so the gate applies to both
  dealers (the single-dealer rule in UpgradeOffer's header).
- `SPACEVORS_DIAG_UPGRADES="Armor:,ArmorPierce:RailGun"` style scripting works without code —
  the parser is `Enum.TryParse<UpgradeOption>`; note it in DIAGNOSTICS.md when shipped.

Card text: hull card "Armor +2 — gunfire damage taken is reduced by 2"; weapon card
"Armor Pierce +4 — ignores 4 points of target armor". The stats screen gains an `Armor` row (with
its upgrade count, like Health) and each gun a `Pierce` row once non-zero; the ship-select screen
shows Armor on Heavy/Eagle so the hull difference is visible before the run starts.
`UpgradeCounts`/apply path need the two `ApplyUpgrade` cases; card labels go in
`UpgradeMenuRenderer`.

## 4. Why only the Slow-engine hulls

"Only the largest slowest player ships should have armor at start" reads onto the shipped table
as: **the hulls that cannot dodge are compensated with armor; the hulls that dodge are not**.

| Hull | Engine | Radius | Start Armor |
|---|---|---|---|
| Heavy | Slow | 84 | 4 |
| Eagle | Slow | 66 | 4 |
| Manta | Crab | 111 | 0 — huge, but the game's best lateral dodge |
| Falcon | Pursuit | 111 | 0 — longest hull, fastest engines |
| Raptor / Scout / Fighter / Shadow | Balanced | ≤78 | 0 |

Engine-based, not radius-based: radius would arm Manta and Falcon, which are exactly the big hulls
that buy survival with movement. Any hull may *buy* Armor from level 5 — "at start" is the
restriction, progression is the progression.

## 5. Implementation sketch (ordered as small commits)

1. **Domain mechanic**: `Armor(int Value)` component; `Ammo`/`LaserBeam` gain `Pierce = 0`;
   `WeaponStats` gains `Pierce = 0`; `ArmorRules.EffectiveDamage(damage, armor, pierce)`; wire it
   into the ~8 gunfire accumulation sites in `CollisionSystem` (ammo→player/ship/mine/missile,
   beam→same), computing effective damage once per hit and feeding it to both
   `_frameRemainingHealth` and the hit lists so sparks scale with damage actually dealt. Impact
   and mine-contact paths untouched.
2. **Data + spawn wiring**: `ShipType.Armor` (Heavy/Eagle 4), `EnemyShipType.Armor`
   (HeavyCannon 4); `WorldFactory` adds the component when `> 0`; same in `EnemyShipFactory`
   (both the live and batch component lists).
3. **Cards**: `UpgradeOption.Armor` (+2, ceiling 12) and `UpgradeOption.ArmorPierce` (+4, ceiling
  8); offer guards follow the R19 pattern (`WeaponStats.CanPierceMore(step)` mirrors
  `CanToughenRound`; armor card withdrawn at the ceiling); `UpgradeOffer` buckets and
  level gate; `ApplyUpgrade` cases; card labels and stats/select screen rows.
4. **Enemy heavy shot**: `EnemyShipType` gains a turret `DamageBonus` and `AmmoRadius`;
   HeavyCannon takes +4 damage, radius 4, and drops its ammo speed 160 → 140. Enemy ammo keeps
   `Pierce = 0` — by rule, forever.
5. **Docs + measurement**: §9 doc updates; scenario runs per §8.

Deliberately not touched: ThreatMeter (it reads incoming raw damage — armor is the player's
mitigation, not the world's threat), damage shading, hp bars, save (none), scenarios (no armor
in perf mixes; the broad phase never looks at Armor).

## 6. Tests (deterministic, sim-only)

New `ArmorCombatTest`:
- `AArmoredHullFloorsAChipToOne`, `APierceCancelsArmorBeforeItHitsHp`, `AnUnarmoredHullTakesFullDamage`
- `ImpactAndMineContactIgnoreArmor`
- `OnlyTheSlowHullsStartArmored` (Heavy/Eagle 4, all others 0), `HeavyCannonStartsArmored`
- `AGuidedRoundCarriesItsPierce` (HavocTorpedo with pierce cards vs HeavyCannon)
- `HeavyCannonShotOutdamagesEveryOtherEnemyShot` and through player armor 4 it lands 4 while a
  small-enemy chip lands 1 — the anti-armored-hull role, asserted

Offer tests (HandCompositionTest / LevelUpSlotOfferTest style):
- `NoArmorCardBeforeLevelFive`, `NoPierceCardBeforeLevelFive`, `LevelFiveMayOfferArmor`
- `ACardAtTheCeilingIsWithdrawn` (both cards, R19 pattern)
- reroll honesty rides the shared `Roll` (RerollOfferTest style)

Table/balance invariants from §6, to be asserted the day armor ships (WeaponBalanceTest):
- (a) kill-class DPS ordering vs HeavyCannon's armor does not invert by >15%
- (b) every gun lands ≥ 1 damage against the tier's max armor (the floor makes this structural)
- (c) a support gun with every pierce card still does not cross R25's class line (pierce scores 0
  against armor 0)
- HeavyCannon still dies inside the EnemyDifficultyTest TTK budget with starter loadouts
  (MachineGun: (12−4)×4 = 32 DPS vs 120 hp ≈ 3.8 s — check, don't assume)

Existing tests to re-check, not rewrite: `HandCompositionTest` hull-slot assertions (lines
asserting `Hp` is in every hand must accept "Hp or Armor" at level ≥ 5), `EnemyDifficultyTest`,
`ShipRosterTest`.

## 7. Follow-up (separate plans, not this one)

- **Corrosion** (§6 decision 5): AcidBubbleSpray pellets strip armor per hit — a timed
  `ArmorCorrosion(amount, expiry)` component, effective armor `max(0, armor − corrosion)`. This
  is the support class's answer to armor and the reason `PierceCeiling` may rise later. Deferred
  because it is the first timed per-entity state in combat; it deserves its own plan.
- **Armored Carmine / elite armor / boss armor** once the player reliably fields pierce.
- **Enemy pierce never** (owner decision). The sniper archetype (PLAN.md roadmap) may ship later,
  but as big slow dodgeable rounds à la HeavyCannon's slug, not as an armor-ignoring stat.
- **Shields** (PLAN.md: absorb a hit, go on cooldown) stay a separate plan; the Hull-bucket
  reservation in the hand map leaves its slot.

## 8. Performance

Mitigation is a few int ops per *hit*, plus one `TryGetComponent<Armor>` per hit at sites that
already do a `TryGetComponent<Health>`. Broad phase, firing and integration never see armor.
Expect ≤1%; verify, don't assume: run `.tools/scenario.sh swarm-1024 --sim --iters 3` (and the
missile scenario) before/after commit 1 and record the paired verdict under `## Shipped`. Armor
changes no live-round count, so the gates should not move at all.

## 9. Docs to update when this ships

- `plans/WEAPON_BALANCE.md`: append a dated Follow-up pointing here (§6 is superseded: it is now
  scheduled and partly implemented — decisions 1-4 land, corrosion stays open).
- `PLAN.md`: feature index line + strike the roadmap entry; Controls/ship-select wording only if
  the select screen changes.
- `DIAGNOSTICS.md`: scripted `Armor:` / `ArmorPierce:` upgrade entries work via the enum parser.
- This file: `Status: shipped`, `## Shipped` with deviations and the paired perf verdict.

## 10. Open knobs (playtest)

- Hp-vs-Armor odds in the hull slot from level 5 (start 50/50).
- Heavy/Eagle armor 4 vs 2 (does the early chip immunity read as "free" or as "interesting"?).
- Carmine armor 2; armor scaling with composition tier (v1 keeps it flat).
- Whether impact/mine damage should respect armor after all (v1: no).
- Pierce ceiling 8 vs corrosion depth once corrosion ships.
- HeavyCannon shot bonus +4 (vs armor-4 player it lands 4 at tier 0); raise toward +8 if the
  armored hulls still feel safe.

## Shipped (2026-11-03)

Landed in one pass, suite green at 624 tests (599 before). What exists now:

- `Armor(int Value)` component + `ArmorRules.EffectiveDamage` (Domain/Combat/ArmorRules.cs),
  applied at the four gunfire sites in `CollisionSystem` (ammo→ship, ammo→player, beam→ship,
  beam→player); mine, asteroid and round hit sites skip mitigation by rule, impact and mine
  contact bypass it. `Ammo`/`LaserBeam`/`WeaponStats` carry `Pierce`; `AmmoLifetimeSystem`
  rebuilds with `with` so pierce survives a fuse tick.
- Hulls: `ShipType.Armor` — Heavy and Eagle (the Slow engines) at 4, everyone else 0;
  `EnemyShipType.Armor` — HeavyCannon 4. HeavyCannon's slug: `TurretDamageBonus` 4,
  `TurretAmmoRadius` 4 (carried on `Turret.AmmoRadius` for enemy mounts), ammo speed 160 → 140.
- Cards: `Armor` +2 (ceiling 12) in the Hull bucket from level 5, `ArmorPierce` +4 per gun
  (ceiling 8, `WeaponStats.CanPierceMore`) in the class buckets from level 5; both withdrawn at
  the ceiling (R19's rule); `ApplyUpgrade` cases; stats-screen Armor row + conditional per-gun
  Armor-pierce row; ship-select shows `Armor: 4` on Heavy/Eagle; card labels "hull armor" /
  "<gun> armor pierce".
- Tests: `ArmorCombatTest` (rule, hits, rounds carrying pierce, tables, impact/mine bypass,
  §6 invariants a-c), four offer tests in `HandCompositionTest`, and the HeavyCannon assertion
  updated in `EnemyTierStatsTest`.

Deviations from the plan body:

- The card steps and the enemy side changed **before** implementation by owner instruction:
  `Armor` is +2 (not +4), `ArmorPierce` +4, enemies never get pierce ever, and HeavyCannon got
  the big slow slug (+4 damage, radius 4, 140 px/s) as the anti-armored-hull threat. The body
  above already carries those decisions.
- `EnemyDifficultyTest`'s TTK budget now runs the starter kit through the mitigation formula and
  widened 7 s → 12 s: the starter Scout really does pay 72 → 38 DPS against armor 4 (the tax §6
  predicted). The old raw-DPS model would have gone on lying (TROUBLE_SHOOTING entry 73).
- `HandCompositionTest`: the guaranteed hull-slot rule is now "exactly one hull card — `Hp` below
  level 5, `Hp` or `Armor` at or above it", replacing "always `Hp`".
- Dead field `CollisionSystem._ammoToPlayerHits` (declared and cleared, never read) removed while
  touching the same sites.
- Perf verdict (paired sim runs, same host within one minute, `--sim --iters 3`): ammo-spray
  median 1.41 → 1.41 ms (0%), impact-storm 2.24 → 2.22 ms (−0.9%, inside the host noise of entry
  69); both `budget pass`. Mitigation is a few int ops per hit against a hull that carries an
  Armor component — as predicted, the broad phase never sees it. Keep.

## Follow-up (2026-11-03): the armor card is dealt in two tiers

plans/UPGRADE_BUNDLE_PAIRS.md split the armour card's step by the **level that deals it**: +1 at an ordinary
level, +2 at a milestone (`DefenceTier`, `UpgradeDefinition.MilestoneDefenceMultiplier = 2`). The ceiling
(`ArmorRules.ArmorCeiling = 12`) is unchanged and still withdraws a card rather than shrinking it, so a card is dealt
only while its whole tier step fits: at armour 11 the ordinary +1 card is dealt and the milestone +2 card is
not, and at 12 neither is. The card's printed number is always the number it grants — `FormatChoice` and the
HUD read the card's own tier, not the constant. The arithmetic in this plan's body (`+2` per card) is therefore
now the **milestone** step; read an ordinary level's card as half that.