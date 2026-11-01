# Plan: Salvage Field and ECM Pulse

Status: superseded (2026-11-01) by plans/ECM_PULSE_AND_SALVAGE_FIELD.md — the design below is the original
survey and is **stale**: the salvage field is now a one-shot sweep of XP, currency and just enough health
to top the hull off (not a `PickupRadius` buff), and the ECM pulse is a 5 s world-wide jam of ships, mines
and missiles (not a ~300 px round delete). The W3 lull-magnet overlap question below is moot for that
reason. Only the closing "Still deferred, ranked after these two" list still stands as the loot menu.

Two pickups that were surveyed while designing reroll tokens and left out of that feature on purpose
(plans/REROLL_TOKENS.md, "What else could drop"). They are written down here so the shape is not lost,
not because either is next: nothing here is implemented.

## The rule both must obey

A **pickup is an instant, un-chosen reward**; a **card is a choice**. A pickup that hands the player a
decision is a menu that interrupted the run, and a pickup whose effect the player cannot see in one
frame is a mechanic, not a reward. Both items below are judged against that: the effect must be visible
and useful the moment the capsule-or-whatever touches the hull, with no follow-up UI.

They also share the drop-table constraint the reroll token established (`LootRoll.RollShipDrop`): one
roll per ship death, so a death always costs exactly one number from the world RNG and the bands stay
mutually exclusive. Adding a third pickup means re-splitting the rare-drop budget, not appending a
fourth independent 6 % — the total is the knob the reward-side difficulty lever turns
(plans/DIFFICULTY_SCALING.md R2.6 item 7).

## Salvage field (magnet burst)

**What it is.** A pickup that, on collection, widens the player's pickup radius for a few seconds:
nearby XP orbs, health orbs and salvage capsules all come in without the player walking over there.

**Why it is interesting.** It is the only loot idea that rewards *positioning* rather than damage: the
player who fought a wave in the middle of the field gets the pile back without breaking the retreat. And
it is nearly free — `PickupMagnetSystem` already reads `player.PickupRadius` once per orb, so the whole
feature is one extra component read.

**Shape that fits the codebase.**

- A `PickupBoost(float Lifetime)` component on the player, aged by `PickupMagnetSystem` the same way
  pickup lifetimes are aged today. The magnet radius becomes `player.PickupRadius + boost` when present.
- No new system, and no write to `Player.PickupRadius` itself: bumping and restoring a field is the
  stale-copy trap of TROUBLE_SHOOTING #58 (a second writer of the same component in the same phase wins
  by command order), and a lifetime component has no such failure mode.
- Radius multiplier and duration are `LootStats` constants. Suggested starting point: `×2` for 6 s —
  generous enough that one pickup visibly changes the field, short enough that it never becomes the
  default state.
- **It needs a cue.** Nothing in the world draws the pickup radius today (it is a stats-screen number
  and a card), so a boost nobody can see is a number going up. Cheapest honest cue: a faint ring at the
  boosted radius while the boost is live, same pass as the salvage capsules.

**Overlap to resolve first.** This is the player-triggered twin of option 2 in
plans/WAVES_AND_BREATHERS.md "Loot during lulls (W3)" (`PickupRadius × 1.5` during a lull, an automatic
"collect now" cue). Same lever, opposite trigger — one scheduled by the wave clock, one picked up. Pick
one; if both land, they multiply into a vacuum that empties the field and makes orb placement
irrelevant. If forced to choose, the lull version is cheaper (no drop-table band, no pickup entity, no
new component) and it fixes the actual problem W3 named (orbs expiring during a lull).

**Risks.** Value depends entirely on how much loot is on the ground, so it is dead weight in a sparse
run and strongest exactly when the player is already safe (the pile is behind them). It also nudges the
player to linger in a kill zone to catch the swept loot, which is the opposite of what the escape
mechanics teach.

## ECM pulse

**What it is.** A pickup that, on collection, destroys every **enemy round** within a radius of the hull
— bullets, beams' short-lived equivalents, and guided rounds — leaving hulls, mines and rocks untouched.

**Why it is interesting.** It is the only idea on the list that answers a specific balance hole rather
than adding a reward: **R21** (plans/MISSILES.md) says an enemy missile can physically cross the ring it
launches from, so a lock obtained at close range cannot be dodged by flying away. Health and dodge
distance are the only current answers. A rare, ship-only pickup that deletes incoming rounds converts
"locked at 300 px" from a death sentence into a resource.

**Shape that fits the codebase.**

- Enemy rounds are `Ammo` with `IsEnemy = true`; guided ones also carry `Missile`, and they carry
  `Health`, which is what the shoot-down path already consumes. So the pulse does not need a new kill
  path: find enemy rounds inside the radius and take them down the way a player round takes them down,
  explosion effect included — the visual grammar of "shot down" is already there.
- Radius ~300 px, ship-only, rarer than the reroll token (the token is 6 %; ECM is the band to keep
  smallest, since it is the only one that removes a threat instead of granting something).
- Do it in the resolution phase, next to `PickupMagnetSystem` — the collection is a magnet hit like any
  other pickup, and its effect is writes to other entities, which is what the resolution phase is for.

**Risks.**

- **It is an escape valve, so it caps pressure.** A pulse that clears a whole volley makes missile waves
  free at the moment they were supposed to bite. That is the argument for a small radius and a low band,
  and the argument for putting it in *after* the missile difficulty curve has been measured rather than
  before.
- It rewards standing still at the moment of collection (the pulse is centred on the hull), which is the
  worst possible instinct under fire. A larger radius trades that away; a smaller one makes it miss the
  rounds that actually matter.
- A "delete enemy bullets" pickup is the kind of thing players expect to be reusable. It isn't — it is
  instant. If that reads as a wasted drop in play, the honest fix is a smaller radius, not a stock
  component; reroll charges already own that pattern and one of the two should stay unique.

## Cost and risk at a glance

| Item | New domain code | New render code | Drop-table cost | Main risk |
|---|---|---|---|---|
| Salvage field | one component + one read in `PickupMagnetSystem` | one ring while boosted | one band | dead in sparse runs; lingers in kill zones |
| ECM pulse | one collection branch + a radius scan | none (reuse the shoot-down effect) | one band, smallest | escape valve; rewards standing still |

## Order of work if either is picked up

1. Decide the W3 overlap first (salvage field vs lull magnet bonus — one, not both).
2. Split the rare-drop bands in `LootRoll` explicitly and re-run `LootRollTest` (the band bounds are the
   test of the split; the "exactly one RNG number per death" test must keep passing untouched).
3. Salvage field needs its ring cue before it is worth shipping; ECM pulse needs nothing new drawn.
4. Measure with `.tools/scenario.sh impact-storm --sim --iters 3` — ECM adds a radius scan per pickup
   (rare), the field adds one float read per orb per tick while boosted (cheap).

## Tests to write when implementing

- Salvage field: boost widens the magnet radius (an orb just outside the base radius is pulled only
  while boosted); boost expires and the radius returns; a boost collected while one is live refreshes
  rather than stacking; XP and boost collected in the same tick both land (the #58 pattern again).
- ECM pulse: an enemy round inside the radius dies, one outside survives, a *player* round inside the
  radius survives, a guided round is removed the same way (its `Missile` component goes with the entity),
  and the pulse itself is consumed once.
- Both: the drop table still spends one RNG number per ship death, and the bands stay mutually exclusive.

## Still deferred, ranked after these two

From the survey in plans/REROLL_TOKENS.md: discard token (salvage's sibling — spends charges to skip a
hand), heat sink, shield cell, overdrive (needs a timed-modifier pattern that nothing in the codebase has
yet), blueprint fragments, elite-guaranteed token, scan pulse, clone pod. Gravity well, turret,
time-slow and nova are skipped: each is a system, not a pickup.
