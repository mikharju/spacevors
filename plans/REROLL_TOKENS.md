# REROLL_TOKENS.md

Status: shipped (landed 2026-11-01) — see `## Shipped` at the end for deviations and the perf verdict.

Salvage dropped by killed enemy ships: a **reroll token** pickup. Picking it up stocks one charge;
during the paused upgrade-card screen the player spends a charge to re-deal the whole hand.
Section "What else could drop" is the menu of further loot kinds and which of them are worth the code.

## The hole this fills

- The upgrade hand is the game's only decision point, and the player has no agency in it. A bad hand
  (five cards for a gun you barely use) is eaten. `LevelUpSystem` deals 5 from a shuffled pool;
  nothing lets the player say "not these".
- PLAN.md's roadmap carried "Loot/reward loop: salvage currency buys rerolls" (now pointing here), and
  plans/LOOT_DROPS.md:206 records that the loop "was never written up in this file; it now stands as a
  roadmap line only". This file writes it up. This is greenfield: `git log` has no commit touching
  reroll / salvage / currency / token and `grep -ri reroll src/` finds nothing, so there is no earlier
  decision here to contradict — only the constraints listed below to respect.
- **A currency would have nothing else to buy.** plans/TURRET_LAYOUTS.md shipped mount swaps and cargo
  as explicitly free ("rearranging is not a choice the game charges for"). With rerolls as the only
  product, a scrap counter is one number with one button on it — pure overhead. So: a **token**, not a
  currency. Currency returns in §"What else could drop" only once there are ≥2 things to buy.
- Reward-side tuning needs the same shape: plans/DIFFICULTY_SCALING.md R2.6 item 7 wants
  `LootStats.HealthOrbChance` to fall off late in a run. A single banded drop roll (§D3) is the place
  that knob will hang, so shaping drops now avoids rewriting it later.

## v1 in one paragraph

An enemy-ship death rolls one number (as today) against a three-band table: nothing, a health orb, or
a reroll token. The token is a magnetised pickup like the health orb. Collecting it adds one charge to a
`RerollStock` component on the player. On the card screen, `R` (or a click on the reroll panel) spends
one charge and replaces `PendingUpgradeOptions` with a fresh hand dealt by the **same** code that dealt
the first one. Nothing else changes: no new screen, no new pause state, no new phase.

## Decisions

**D1 — A pickup you have to fly to, not a currency you bank.**
Keeps the loop inside the machinery that already exists (`PickupMagnetSystem` → component on player →
`GameSession` reads it), and makes the reward physical: you see the salvage ring lying where the ship
died and you have to go into the lull to get it, which is exactly the wave/lull rhythm
(plans/WAVES_AND_BREATHERS.md: the full lull *is* the loot-collection window). A currency adds a number,
a UI affordance and a spend model to buy the same one decision.

**D2 — Ships drop tokens; mines and rocks do not.**
Rocks already drop nothing (CollisionSystem comment: "a rock that is gone leaves a cloud of grey puffs
and nothing else"). Mines are the drone-kill — up to 23 of them respawning every 4-20 s, killed almost
without attention. Paying run-defining agency for the cheapest kill would devalue it. Ships are the
thing that shoots back: 6 per early wave up to 40, so a 6 % chance gives roughly one token per three
waves early and ~2 per wave late — scarce enough to hurt, frequent enough to be understood.
Knob: `LootStats.RerollTokenChance = 0.06`.

**D3 — One banded roll per ship death, not a second independent roll.**
Today `SpawnShipLootOnDeath` calls `rng.NextDouble()` exactly once. A second independent `if` would
(a) consume a second RNG number and shift the world's whole RNG stream — every later spawn, scatter
and shuffle in a seeded run would move, invalidating scenario baselines for no reason — and (b) let
one death drop everything at once, inflating total loot as kinds grow. Bands avoid both:

```
roll = rng.NextDouble()                       // still exactly one call per ship death
roll < 0.05              → HealthOrb          // unchanged: ships keep dropping health at 5 %
roll < 0.05 + 0.06       → RerollToken
otherwise                → nothing
```

Mines keep their existing single health-orb roll. When a third ship-drop kind appears it joins the bands
rather than adding an `if`, so the total rare-drop budget per death stays constant instead of creeping —
and the "keep the RNG **draw count** per kill unchanged, add no new RNG source" rule
(plans/ENDLESS_DIFFICULTY.md:46-47) holds by construction, which is what keeps seeded runs and the
`.tools/run/index.tsv` baselines comparable. It is also the shape the open reward-side knob wants
(plans/DIFFICULTY_SCALING.md:255-257, plans/ENDLESS_DIFFICULTY.md P5: `HealthOrbChance` 0.05 → 0.03 late):
that lands by scaling the **total band width** while keeping the weights, so loot rarity stays one number.
It must not ship in the same commit as a mine-supply change — both move the same healing economy.

**D4 — The stock is its own component on the player, not a field on `Player`.**
`PickupMagnetSystem` writes `Player.Xp` **by ref** (ARCHITECTURE.md "Write patterns") so `LevelUpSystem`
sees fresh XP in the same tick, while the same system reads `Player` once up front. Adding
`Player.Rerolls` and writing it through a queued `AddComponentCommand<Player>` built from that up-front
read would silently overwrite this tick's XP — exactly the stale-write class of TROUBLE_SHOOTING #58
("two systems rewriting one component in the same phase: the per-phase flush makes the stale write win").
A separate `RerollStock(int Count)` component cannot collide, mirrors how `WeaponCargo` and
`UpgradeCounts` already live on the player, and leaves the
11-field `Player` record alone. (If it were a `Player` field it would have to be a second ref write, and
ARCHITECTURE.md's "any new direct-write path requires updating this section first" rule applies.)

**D5 — The roll rules move out of `LevelUpSystem` into `UpgradeOffer`, and reroll calls that.**
The interesting constraint on this feature: a re-deal must obey every rule that governs a first deal —
no card for a cargo weapon, no projectile-speed card on a hitscan beam, no "+1 shot" past the guided
volley budget (R18), no missile-health card past the ceiling (R19), no new weapon already owned or in
cargo, milestone hands capped at 3 new weapons, `Math.Min(5, pool)` when the pool is short. Re-implement
that list next to the reroll and it drifts the first time someone fixes an offer rule. So the pool
builder is extracted (`LevelUpSystem` shrinks to "threshold reached → `UpgradeOffer.Roll` → spawn the
entity"), the scripted-diagnostic branch stays in `LevelUpSystem`, and reroll becomes a consumer.
Net LOC is close to zero because the code is moved, not duplicated.

**D6 — Re-deal the whole hand; never re-deal while a card is committed.**
Partial rerolls (keep 2 cards, re-deal 3) need "which cards did the player already read" state and make
the token's value depend on the hand — more state for a smaller effect. Reroll is offered only in the
card step: a new-weapon card already picked has put a gun in hand and opened the mount screen, and that
purchase is spent. Scenario runs never pause (GameSession:166) so they never reroll — a reroll stays a
player-driven act, and scenario determinism is untouched.

**D7 — Repeats are allowed; an identical re-deal is retried once.**
Excluding every card the player just saw sounds better and is wrong: with one mount the pool can be
smaller than the hand, so exclusion can starve the deal into fewer cards, and it adds a seen-set to
carry. Allow the same card to reappear — the player paid for a chance, not for an improvement. One guard
so no token is ever burned on a hand identical to the one on screen: re-roll a second time and keep
whatever comes (bounded at 2 rolls, so the RNG cost is still bounded and worst case is honest).

**D8 — Milestone hands (every 5th level) reroll too.**
They are the hands with the most variance — 3 new-weapon cards plus HP plus per-gun damage — so they are
where a token is worth most. The player also takes the most risk there: a re-deal can shuffle the good
gun out. That trade is the feature.

**D9 — No stock cap in v1.**
A cap has to either silently discard a drop (feels like a bug) or refuse the drop (invisible), so it
buys nothing until playtesting shows hoarding. `LootStats.MaxRerollsHeld` is listed as the knob to add
then; expected spend of one token per level-up keeps the pile bounded in practice.

**D10 — `R` to spend, plus a clickable panel in the hint row.**
`R` is already restart, but only on the game-over branch (GameSession:142), so the two never overlap:
game over has no hand, a hand is never game over. Tab, C, Esc, digits 1-5 and 1-8 (mounts), WASD, Shift,
L/M/N/H/T/K/J/U/B/V (diagnostics) and F11/F12 are all taken; `R` is the mnemonic players expect.
The panel is drawn in the hint row (cards already reserve that row via
`UpgradeMenuRenderer.GetHintY`) and its rect comes from one shared function so hit-testing and drawing
cannot disagree — the same rule `StatsScreenRenderer.ManageMountsButtonRect` follows. Putting it where
the manage-mounts button sits would overlap the card bottoms (card bottom == `GetHintY - HintGap`), so it
lives on the hint line itself. The icon is drawn as a ring, not a `⟳` glyph: raylib's default font is
ASCII-only.

**D11 — Diagnostics: `SPACEVORS_DIAG_REROLLS=<n>` grants n charges at session start.**
An env var (like `SPACEVORS_DIAG_UPGRADES`) rather than a key, so it works headless under
`.tools/smoke.sh` where key timing is fragile (TROUBLE_SHOOTING #27: a press between two 4 fps frames is
dropped). Optional extra: a diagnostics key `G` for +2 mid-run — `G` is free (the bound set is WASD,
Shift, Tab, R, Esc, C, digits 1-8, F11/F12, and diag keys L M N H T K J U B V; unused: Q E F G I O P Y Z X).
Document whatever lands in DIAGNOSTICS.md.

**D12 — One charge per physical press: a reroll needs a release-edge guard.**
`Raylib.IsKeyPressed` reports X11 auto-repeat as fresh presses, so holding `R` over a 0.4 s
`.tools/input.sh` hold — or a player leaning on the key through an llvmpipe stall — would burn the entire
stock in a second (TROUBLE_SHOOTING #49 documents the same trap on the ship-select list). Gate the spend
on a released key: spend on `IsKeyPressed`, then refuse further rerolls until `!IsKeyDown(R)`. Three lines
in `GameSession`, and it is the difference between "one deliberate choice" and "an accident that empties
the stock". The same guard belongs on the click path: one spend per `IsMouseButtonPressed`, which raylib
already gives.

## Where the code goes

| File | Change |
|---|---|
| `src/Domain/Components/EffectComponents.cs` | `+ RerollToken(float Lifetime = 90f, float Radius = 8f)` beside `HealthOrb`/`XpPickup` (same 90 s lifetime: it must outlive the wave that spawned it into the lull). The radius actually spawned is `LootStats.RerollTokenRadius`, passed at the call site exactly like the health orb — one number to tune, not two |
| `src/Domain/Components/GameplayComponents.cs` | `+ RerollStock(int Count)` beside `WeaponCargo`/`UpgradeCounts`; `With(int n)` helper in the same style |
| `src/Domain/Stats/LootStats.cs` | `+ RerollTokenChance = 0.06`, `+ RerollTokenRadius = 14f`; comment that the bands live in `LootRoll`; `+ MaxRerollsHeld` only when a cap is wanted (D9) |
| `src/Domain/Stats/LootRoll.cs` (new) | the band arithmetic, `LootStats`'s tuning counterpart the way `WaveSchedule` is `WaveStats`'s: `enum ShipDrop { None, HealthOrb, RerollToken }` + `static ShipDrop RollShipDrop(Random rng)` |
| `src/Domain/Combat/CollisionSystem.cs` | `SpawnShipLootOnDeath` calls `LootRoll.RollShipDrop(view.Rng)` and spawns on the band; `SpawnLootOnDeath` (mines) untouched |
| `src/Domain/Progression/PickupMagnetSystem.cs` | `+ ProcessRerollTokens` (accelerating pull, like the health orb) and a shared `PullTowardsPlayer` used by the orb and token loops; the XP loop stays as it is — it is the hot one and uses a constant-speed pull |
| `src/Domain/Progression/UpgradeOffer.cs` (new) | pool build + shuffle + hand size moved out of `LevelUpSystem`: `Roll(WorldView view, Entity player, int level) → UpgradableOption[]?` (null = nothing can be offered, so the level is not consumed), `IsMilestone(int level)`, and `TryReroll(EntityManager em, Entity player) → bool` which spends a charge and replaces the pending hand |
| `src/Domain/Progression/LevelUpSystem.cs` | calls `UpgradeOffer.Roll`; keeps the `SPACEVORS_DIAG_UPGRADES` scripted branch; shrinks |
| `src/Game/GameSession.cs` | in `HandleCardStep`, before `ReadChoiceSelection`: `R` or a click on the reroll rect → `UpgradeOffer.TryReroll`, log `[UPGRADE] reroll …`; pass the stock count + hover into the overlay |
| `src/Game/Renderer.cs` | `StatsOverlay` gains `int RerollCount, bool HoveredReroll` (same shape as `CanManageMounts`/`HoveredManage`) |
| `src/Game/UpgradeMenuRenderer.cs` | reroll panel draw + `RerollButtonRect(windowWidth, windowHeight, scale)`; hint line becomes `"Click a card or press keys — R rerolls (n)"`, dimmed at n = 0 |
| `src/Game/StatsScreenRenderer.cs` | hover hit-test for the reroll panel, drawn only when a hand is up |
| `src/Game/WorldRenderer.cs` | `DrawRerollTokens`: amber ring + bright core (blue XP orbs and mint health orbs are both filled discs — the ring is the differentiator, and amber is not used by any pickup today) |
| `src/Game/HudRenderer.cs` | stock readout under the `LV n` text, drawn when `Count > 0` |

## Execution order (each step leaves the tree compiling)

1. `RerollToken` + `RerollStock` components; `LootStats` knobs.
2. `LootRoll.RollShipDrop` + wire it into `SpawnShipLootOnDeath`. Drop → no effect yet.
3. `ProcessRerollTokens` + `PullTowardsPlayer` refactor; collection increments `RerollStock`.
4. `WorldRenderer.DrawRerollTokens` + `HudRenderer` stock. Feature is now visible but inert.
5. Extract `UpgradeOffer.Roll` out of `LevelUpSystem`; run the existing level-up tests green (pure move).
6. `UpgradeOffer.TryReroll`; unit-test it before wiring input.
7. `GameSession` + renderer plumbing: key, panel, hover, hint text.
8. Diagnostics env var + DIAGNOSTICS.md line.
9. New tests (below), `.tools/scenario.sh` run, then docs (§"Docs to touch").

## Tests (domain only, no Raylib — the spend path is a domain call so it is testable)

`src/Tests/LootRollTest.cs`
- 10 000 seeded rolls: health-orb share ≈ 5 %, token share ≈ 6 %, and every roll yields **at most one**
  drop. Pins D3's "one band per death" and the total-budget rule.
- `RollShipDrop` consumes exactly one `Random` number per call (assert on a seeded stream: the next
  number in the sequence is what an equivalent single `NextDouble()` would have produced) — this is the
  "the world's RNG stream does not shift" guard.

`src/Tests/RerollTokenTest.cs` (mirrors `PickupMagnetTest`)
- A token far from the player ages and expires at its lifetime.
- A token inside the pickup radius is pulled in and, on contact, `RerollStock.Count` goes 0 → 1 and the
  token entity is gone.
- Two tokens collected in the same tick → `Count` goes 0 → 2 (one write, not a lost update).
- Collecting a token in the same tick as XP orbs leaves **both** the gained XP and the stock correct —
  the D4 stale-write trap, asserted rather than trusted.

`src/Tests/RerollOfferTest.cs`
- No charges → `TryReroll` false, hand byte-identical, stock untouched.
- With a charge: the hand is replaced, stock decrements by one.
- The re-dealt hand obeys the same offer rules as a first deal, mirroring `LevelUpSlotOfferTest`: no card
  names a cargo weapon; a hitscan-only hull never gets a projectile-speed card; a guided volley at its
  round budget never gets another shot; a mounted or carried weapon is never offered as new; a milestone
  re-deal still offers at most 3 new weapons. These are the regression guards that make D5 worth its
  extraction.
- Determinism: same seed, same charge count, same rerolls → identical hand (`WorldRngTest` style).
- Identical-hand guard: when the re-deal matches the hand on screen, a second roll is taken (assert the
  roll happened by observing two draws from a scripted `Random`).
- **Fuzz the re-deal**: N levels × M rerolls from one seed, asserting every card that ever appears is one
  a first deal would also have been allowed to deal, and that no card is ever a lie (R6 honest range, R10
  no dead hitscan speed card, R18/R19 nothing past a ceiling). PLAN.md's QA roadmap already asks for
  "random upgrade sequences assert no card is ever a lie"; a reroll loop is the cheapest way to get it,
  because one test deals hundreds of hands.
- Where a test could hit the same-phase stale-write trap (token + XP collected in one tick, D4), run **both
  systems against one view and apply the commands once** — the shape of a phase, TROUBLE_SHOOTING #58 —
  not each system in isolation.

Manual/visual check (headless recipe: TROUBLE_SHOOTING "General workflow that works"):
`SPACEVORS_DIAGNOSTIC=1 SPACEVORS_DIAG_REROLLS=3` → `.tools/game.sh` as a background job, force a level-up
with `L`, `.tools/shot.sh` the card screen, `HOLD=0.05 .tools/input.sh key r` (a 0.4 s hold is a key
*repeat*, #49 — checking that the D12 guard still spends exactly one charge on a long hold is worth doing
on purpose), shoot again, `.tools/diff.sh` expecting the cards **to differ** and the stock to have gone
3 → 2. For any longer unattended run keep sending `key 1` in a loop: an unattended session stops at the
first level-up, not at death (#50).

## Perf

- Scenario mode never rerolls and never spawns a pending hand it will reroll, so the sim delta is
  "a few more pickup entities plus a third magnet loop over them". Expected ≈ 0.
- Measure, do not assume: `.tools/scenario.sh <scenario> --sim --iters 3` for a loot-bearing scenario
  (ships die in it) before and after, compare median / p99 / perShapeNs against the previous row of
  `.tools/run/index.tsv`, and put the verdict ("costs X % — keep / optimize / revert") in the reply.
  `--check-baseline` must still pass.
- Because the drop roll stays one `NextDouble()` per ship death, seeded scenarios should reproduce their
  existing worlds; if a baseline moves materially, that is the D3 guard failing — investigate before
  re-baselining.
- Render: a handful of tokens drawn as one ring + core with `Raylib.DrawRing`-class calls is fine (the
  batched `ShapeSprites` path exists for the thousands-of-particles passes, not for pickups; XP orbs
  already use plain `DrawCircle`).
- Keep the HUD stock a **fixed** draw count (one icon + one number), never one glyph per held token — the
  per-unit HUD loop is the landmine TROUBLE_SHOOTING #61 recorded with hp pips, and a stock that grows is
  exactly that shape.
- What the gate can and cannot price: `--sim` runs `ScenarioBench` with **no `GameSession`**, so it prices
  the drop and the magnet loop but not the menu, and the menu is paused UI work with no tick to price.
  Say so in the verdict rather than claiming the feature is free; if the drop path looks suspicious, A/B it
  directly (drop enabled vs disabled) instead of trusting a gate that cannot reach the code.

## Invariants and edge cases the implementation must respect

- Pickup collection writes `Player.Xp` by ref; everything else about the player goes through commands or
  separate components (ARCHITECTURE "Write patterns"). Update that bullet to mention the stock.
- `UpgradeOffer.Roll` returning null means "no offer possible"; `LevelUpSystem` must keep not consuming
  the level in that case. Reroll on a null hand spends nothing.
- A hand cannot exist with zero turrets, and the stats screen's manage-mounts button is disabled while a
  `PendingChoice` is up — so the turret set can never change underneath a pending hand. Do not "fix" that
  without revisiting D6.
- Death while a hand is up clears the pending choice (GameSession:151); the stock is simply irrelevant
  afterwards, and must not survive a restart (a new `GameSession` gets a fresh world).
- No new phase, no new system in the four-phase order, no new yield return, no `Random.Shared` anywhere.

## What else could drop

The rule that keeps this list honest: **a pickup is an instant, un-chosen reward; a card is a choice.**
Anything that needs the player to aim it, place it or pick a target belongs on the card/mount path, not in
the loot pile — that rule alone deletes most of a typical item list. Second rule: every new kind joins
`LootRoll`'s bands, so the total rare-drop budget per death stays constant instead of creeping.

| Item | What it does | Why it fits this game | New machinery | Cost | Balance risk |
|---|---|---|---|---|---|
| **Data core** | one fat XP lump (a big `XpPickup`) | literally `XpPickup(25)` with a bigger radius and a new colour; teaches "some kills pay more" | none | XS | XP curve only (`UpgradeStats.XpForLevel`) |
| **Salvage field** (magnet burst) | every pickup on screen comes to you now, or `PickupRadius ×8` for 6 s | the full lull is already the loot window (WAVES_AND_BREATHERS); the magnet and its `Chased` flag already do the work — a temporary radius bump needs **no new system** | temporary `Player.PickupRadius` bump with a timer | S | low; pure convenience, and it rewards flying into danger |
| **ECM pulse** | destroys every enemy round and guided round within ~300 px on pickup | makes the missile rule (R21, faction hit rules) survivable and readable; reuses the existing shoot-down path | loop over `Missile`/`Ammo` with `Health`; one destroy loop | S-M | medium: it is an escape valve; keep it ship-only and rare |
| **Heat sink** | clears every mount's `FireCooldown` instantly | an alpha-strike the player times by choice of when to grab it; the burst is exactly what `ThreatMeter`'s `out=…/s` row can measure | clear one component | S | medium: burst dps against rules R1-R13 |
| **Shield cell** | absorbs the next N damage before `Health` | planned survivability instead of the reactive health orb; pairs with the "reward side" knob | `Shield(int)` read by `CollisionSystem` before `Health` | M | high: hp is the whole economy of a VS-like run; `DamageShading` would not reflect a shield |
| **Overdrive** | ×2 fire rate or ×1.5 thrust for 8 s | the first **timed modifier**, and every future buff needs that pattern | timed component read at fire/thrust time, so stored `Weapon.FireRate` is never corrupted | M (the pattern) | medium: `ThreatMeter` and any dps readout must not treat it as permanent |
| **Blueprint fragment** | a fragment per ship; two fuse into the evolved version of a tagged gun | the roadmap's "weapon evolutions" (PLAN.md, future section) need exactly this hook | fragment count + an evolve rule + one card | M-L | medium, and it must respect the offer rules (R18/R19) like any card |
| **Elite carries one (guaranteed)** | an elite hull (plans/ENDLESS_DIFFICULTY.md P4, still proposed) always drops a token | the cleanest risk→agency exchange in the game: a visibly tougher ship, a visible reward. And a **guaranteed** drop adds **zero** RNG draws, so it obeys the "no new RNG source / unchanged draw count" rule for free | needs elites first (one marker component + a ring in the renderer) | S once elites exist, M otherwise | medium: it converts an elite from a threat into a shopping trip — tune the elite's hp, not the drop |
| **Discard token** (sibling of the reroll) | removes one card, deals one replacement | strictly gentler than a reroll: it never takes a good card away. Best cheap second token if playtests show rerolls feeling like a gamble | reuses `UpgradeOffer`, one extra "index to replace" input | S | low |
| **Salvage currency** | per-run scrap; buys rerolls, then other things | only worth it once there are ≥2 products to buy; until then it is a second layer over D1 | currency + a spend UI | M | low, but it is dead weight without a shop |
| **Scan pulse** | reveals off-screen threats for 10 s | the roadmap already wants off-screen indicators (PLAN.md, near-future section); this is that feature with a timer | needs the indicator renderer first | M | low |
| **Clone pod** | revive once, brief invulnerability | big emotional payoff for the rarest drop | touches the death/husk sequence (ARCHITECTURE "Cross-system signaling") | M-L | high: it silently doubles the run's hp budget |
| Gravity well, deployable turret, time slow, nova | spectacle items | none of them is cheap: new entities, new systems, or physics/AI scaling | L | L | high |

**Recommended next two, in order:** the salvage field (cheapest real item, and it makes the existing loot
window feel good), then the discard token (cheapest depth: one more way to spend agency at the hand).
Everything else should wait for the reroll's playtest numbers — how often tokens drop, how often they are
spent, and whether players reroll milestones.

Two things to not build twice, both already on record:
- plans/WAVES_AND_BREATHERS.md W3 listed three lull helpers that were **never implemented** — pausing orb
  decay during lulls, a lull magnet bonus (`PickupRadius × 1.5` while not in a wave), and a
  last-ship-of-the-wave guaranteed health orb. The salvage field below is the player-triggered version of
  the second one: pick a side. If both a passive lull bonus and a triggered burst exist, the burst is
  worth nothing.
- plans/MINOR.md leaves rocks-as-cover open on purpose ("clearing a rock pays nothing — no XP, no loot —
  so it is pure DPS spent on cover … whether rocks should drop something is a playtest call"). If rocks
  ever start paying, they join `LootRoll` as a table of their own — rocks are scenery-dense, and giving
  them the ship bands would flood the field.

## Rejected alternatives

- **Salvage currency now** (PLAN.md's wording): one product does not earn a currency, a counter and a
  spend screen. Token first; revisit when a shop has ≥2 things to buy.
- **Reroll charges on `Player.Xp`-style field on `Player`**: stale-write hazard against the ref-written XP
  (D4) for no gain.
- **One generic `LootPickup(LootKind)` component for all pickups**: fewer types, but it rewrites the
  shipped XP/health paths and both draw loops for a saving of one record struct; and the XP loop's pull
  differs on purpose. Not now.
- **A reroll that also re-rolls the mount screen / re-deals a committed new-weapon card**: the card is
  already paid for (that is how `ChooseUpgrade` + `_cardResolved` work today); unwinding a half-placed gun
  is more state than the feature is worth.
- **Excluding previously seen cards from a re-deal**: can starve a small pool into fewer cards and needs a
  seen-set; D7's bounded retry is the honest version.
- **Free reroll every N levels**: turns a loot reward into a mechanic everyone gets, and removes the drop's
  reason to exist.

## Docs to touch when this lands

- `plans/REROLL_TOKENS.md`: `Status: shipped`, a dated `## Shipped (…)` with deviations and the perf verdict.
- `PLAN.md`: one line in the feature index under "Enemies and difficulty"; move the loot/reroll roadmap
  line into the shipped section when it lands (it already points at this file); add the reroll key to the
  in-game controls list.
- `ARCHITECTURE.md`: the component list (`RerollToken`, `RerollStock`) and the `PickupMagnetSystem`
  write-pattern bullet (stock write path), per "any new direct-write path requires updating this section
  first".
- `DIAGNOSTICS.md`: the `SPACEVORS_DIAG_REROLLS` env var (and the `G` key if added).
- `plans/LOOT_DROPS.md`: append a dated `## Follow-up (…)` pointer to this file — never edit its body.
- Perf verdict: dated record here; live numbers in `.tools/run/index.tsv`.

## Shipped (2026-11-01)

What landed, in the order a reader meets it:

- `RerollToken(Lifetime, Radius, Spin)` — the capsule in the field; `RerollStock(Count)` — the charges
  the player holds (D4: its own component, not a `Player` field).
- `Stats/LootRoll.cs` — `RollShipDrop` returns one of `None / HealthOrb / RerollToken` from **one**
  `NextDouble`, so a ship death still spends exactly one world-RNG number (D3). The health-orb band is
  unchanged and first, so existing seeds keep dropping the same orbs.
- `CollisionSystem.SpawnShipLootOnDeath` — ships only, never mines or rocks (D2). The XP orb that every
  ship drops is untouched; the roll replaced the old health-orb-only `if`.
- `PickupMagnetSystem` — capsules are magnetised and collected like health orbs; collection queues one
  `RerollStock` write per tick holding the number collected (two capsules in one tick stock two).
- `Progression/UpgradeOffer.cs` — the hand-dealing code moved out of `LevelUpSystem` whole, and
  `TryReroll` lives beside it: the re-deal calls the same `Roll` a level-up calls (D5), so R6/R10/R17/R18/R19
  and the cargo rules cannot drift for the reroll path. `LevelUpSystem` is 47 lines, was 208.
- Card screen: a panel at the right end of the hint row — a grey capsule icon, the count, and the `R` key
  (D10); it is clickable too, and it is drawn even at zero charges so the key stays discoverable.
  `HudRenderer.DrawRerollStock` shows the same icon and count under `LV n` while flying.
- Diagnostics: `SPACEVORS_DIAG_REROLLS=<n>` stocks n charges at session start, `G` grants 2 mid-run.

Deviations from the plan above:

- **Look**: the token is a small grey capsule rotating slowly in place, not an orb. `Spin` is a simulated
  field advanced by `PickupMagnetSystem` (`RerollTokenSpinRate * deltaTime`, 1.5 rad/s) rather than a
  render clock, because a render-side time would make the same seed screenshot differently (TROUBLE_SHOOTING
  #33). The draw is `RenderHelpers.DrawSalvageCapsule`, shared with the HUD and panel icon (the panel and
  HUD icons hold still so the count next to them stays legible). Raylib's default font has no `⟳`, so the
  panel carries a shape and the letter `R` instead of a glyph.
- **Health orbs were refactored onto the capsule's helper**: `PullTowardsPlayer` now serves both, using the
  accelerate-and-cap pull the orb already used. The XP loop kept its own constant-speed pull — that path is
  the hot one and its numbers did not move.
- **The identical-hand retry (D7) is one retry**, not a loop: a second identical hand is accepted.
- **`SPACEVORS_DIAG_UPGRADES` parsing moved to `UpgradeOffer`** with the pool code, so scripted and rerolled
  hands cannot come from different rule sets.

Tests: `LootRollTest` (band distribution, one RNG number per death, the band sum is the reward-side knob),
`RerollTokenTest` (expiry, spin, collect-to-charge, two capsules in one tick, and capsule + XP orb in one
tick keeping both effects — the #58 trap), `RerollOfferTest` (no charges is a no-op, one charge spends
exactly one, re-deals obey the cargo / hitscan / volley-ceiling / milestone rules, and the re-deal is
deterministic for one seed). Full suite: 494 passing.

Perf verdict (2026-11-01, host=sim, `--iters 3`, `--check-baseline` ok on both):
`ammo-spray` median **1.38 ms** / p99 2.65 — earlier same-day rows on this host were 1.42 and 1.39;
`impact-storm` median **2.30 ms** / p99 4.83 — the same day's earlier row was 3.52. Entity counts moved by
well under 1 %. **This change costs ~0 % — keep.** Note plainly: `--sim` runs `ScenarioBench` with no
GameSession, so it cannot price the card screen at all; the reroll's cost is paused-frame work (one extra
`Prepare` on the frame a charge is spent) and is not measured by these numbers.

Verified by eye headless (`.tools/shots/reroll-0*.png`): the panel with its capsule icon and the count, the
count under `LV n` in the HUD, and a reroll swapping the whole hand. A deliberately long 0.4 s `R` hold
(TROUBLE_SHOOTING #49) spent exactly one charge, 5 → 4 → 3 (D12).

The two deferred items from this survey — **salvage field** and **ECM pulse** — are written up in
`plans/LOOT_ITEMS.md`.
