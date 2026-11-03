# SHIELDS.md — a regenerating absorb pool in front of Health

Status: shipped (2026-11-03) — what landed, the deviations and the dated perf verdict are in
`## Shipped` at the bottom. The body below is the design as planned — three revisions the same day:
recharge became a **shock plus a rate** (a small hole closes fast, a spent pool takes its time) rather
than a fixed timer and an instant refill, and §7 made the graphic **an event, not an aura**: invisible
at full and at empty, a flash on the bearing that intercepted, a near-white shatter when the pool breaks,
a front of light while it refills, and one glint sweeping the ring every ~5 s while full.

## 1. What this plan builds

PLAN.md roadmap line "shields absorb a hit, go on cooldown", made concrete: a **Shield** — a bank of
absorb points in front of `Health`, scoped to gunfire exactly like armor, that **goes offline for 2 s
after any hit that spends it** and then refills at a fixed rate: a small hole closes almost at once,
a spent pool takes its time. Repeated hits keep re-arming the shock, so it is a burst buffer rather
than a dps tank.

This spends three reservations already on record: `plans/UPGRADE_HAND_COMPOSITION.md` §3's
"Hull | Hp (future: shield)" slot, `plans/ARMOR_AND_PIERCE.md` §7's "shields stay a separate plan",
and `plans/REROLL_TOKENS.md`'s "Shield cell" row.

The two questions this plan was opened with, answered up front:

- **No ship starts with a shield** — player or enemy. §3.
- **No shield-piercing round is needed.** §4. A store has no magnitude to cancel; the shield's
  counters (volume, burst, round size, impact) already exist.
- **And it has to be seen — without becoming an aura** (§7). The ring is an *event*: invisible when full
  and when empty, visible only while something is happening to it — a flash on the bearing that
  intercepted a hit, a shatter of near-white shards when the pool breaks, and a front of light crawling
  round the ring as it refills. A full shield shows itself once in a while: a faint glint that sweeps the
  ring every ~5 s. Enemy shields later are **elite-only**, and that same ring is what will tell the
  player why a ship took no damage.

## 2. The mechanic

Layer order is one formula in the place mitigation already lives (`ArmorRules` gains a sibling):

    mitigated = max(1, damage - max(0, armor - pierce))   // ArmorRules.EffectiveDamage — unchanged
    soaked    = min(shield.Points, mitigated)             // ShieldRules.Soak — new
    hull      = mitigated - soaked                        // → the per-frame health accumulator

Armor changes the *size of the number*; the shield is a *store* that number is paid out of. The
one-line read: **armor taxes a hit, the shield banks hits.**

- `Shield(float Points, int Max, float Offline)` component in `Domain/Components/CombatComponents.cs`.
  `Offline > 0` = no regen until it counts down; `Points < Max` with `Offline == 0` = refilling.
  `Points` is a **float on purpose**: it is its own fractional accumulator, so a fast rate
  (16 pts/s ≈ 0.13 point per 8.3 ms tick) needs no second field and no rounding trick. Damage is
  still whole numbers — a hit soaks `(int)min(Points, mitigated)` — and the read-out shows
  `ceil(Points)`. Hulls only: mines, rocks and rounds never carry one, same scoping as `Armor`.
- **Scope: gunfire** (`Ammo`, guided rounds included, and `LaserBeam`, either direction) — the same
  four sites in `CollisionSystem` that already call `MitigatedGunfireDamage`. **Impact damage and
  mine contact bypass the shield**, as they bypass armor: mitigation stays "a tax on shots".
- **Shock rule:** any hit that spends shield points sets `Offline = ShockSeconds`, and the spent
  remainder falls through to the hull. Repeated hits keep re-arming it, so **under sustained fire
  the shield never comes back.** That gate is what keeps hp the run's pressure budget: without it,
  regen races chip damage and a hull under light fire becomes immune to gunfire — a cliff no amount
  of tuning the rate fixes.
- **Proportional refill:** once the shock is gone, `Points` climbs at `RechargeRate` to `Max`.
  Time-to-full is therefore `Shock + (Max - Points) / Rate` — **a small hole closes almost at once,
  a spent capacitor takes its time**, and the second card (a 32 ceiling) is slower to pay back than
  the first. A partially drained shield works off the same rule as a spent one: there is no "broken"
  state and no refill-to-full jump. The only purchase rule left is §6's — a new card widens the pool
  and charges it at once.
- The one-line read of the whole mechanic: **the shield is a burst buffer, not a damage-per-second
  tank.** It buys the first few seconds of a bad engagement and then costs you the time it takes to
  leave the ring.
- `ShieldSystem` (Domain/Combat) counts `Offline` down and adds `Rate × dt` while it is zero, and ticks
  the two flash timers from §7 while they live.
  It goes **last in the Intent phase**, not beside `EcmSystem` in Resolution: in Resolution it would
  read the pre-hit `Shield` and its command would then *overwrite* the spend `CollisionSystem` queued
  the same tick (last-write-wins, the stale-copy trap of TROUBLE_SHOOTING #58). At the end of Intent it
  reads the settled value from last tick, so regen and spending never fight. ARCHITECTURE's **Phases**
  list gains the system — that list is an invariant, so it is updated with the code.
- **Per-frame accumulation** (`_frameRemainingShield`, beside `_frameRemainingHealth`): without it
  three pellets in one tick would each soak the same 16 points. This is the same reason the health
  accumulator exists (ARCHITECTURE "local accumulation + single flush").
- **Per-frame accumulation** (`_frameRemainingShield`, beside `_frameRemainingHealth`): without it
  three pellets in one tick would each soak the same 16 points. This is the same reason the health
  accumulator exists (ARCHITECTURE "local accumulation + single flush").

## 3. Question 1 — should any ship start with a shield? No

| Hull | Engine | hp | Armor | Shield |
|---|---|---|---|---|
| Scout / Fighter / Shadow | Balanced / Pursuit | 32-40 | 0 | 0 |
| Heavy | Slow | 80 | 4 | 0 |
| Eagle | Slow | 64 | 4 | 0 |
| Falcon / Manta / Raptor | Pursuit / Crab / Balanced | 40-56 | 0 | 0 |
| Enemy fleet (incl. HeavyCannon) | — | 20-120 | HeavyCannon 4 | 0 |

Why none:

- **Every hull's starting defence is exactly one answer today:** dodge (the nimble hulls), armor 4
  (the two Slow hulls that cannot dodge). A shield pays most to the hull that stands and gets hit —
  i.e. exactly the hulls that already got armor. A starting shield double-dips the mechanic's own
  favourite.
- **A regenerating pool gives back what the run's economy is made of.** HP is the pressure budget of
  a VS-like run, and the lull between waves exists because it is the loot-collection window
  (PLAN.md core gameplay). A shield that refills from level 0 lets the player skip the lull by pulling
  out of the firing ring for three seconds instead of flying into the field for orbs.
- **Nothing has to be true for the card to matter.** `ArmorPierce` needed an armored hull before it
  was a spendable card, which is why armor shipped on both sides in one pass (ARMOR_AND_PIERCE §1).
  A player shield is live against the fleet as it stands — so the enemy side is not a prerequisite,
  it is a separate threat design with its own TTK math (§12).
- Discovery is free: the stats screen already shows `Armor` at 0, so a `Shield 0/16` row advertises
  the mechanic before the first purchase.

Rejected alternatives, kept for playtest:

| Alternative | Why not now |
|---|---|
| A shield on one hull as its identity | a fourth starting defensive axis on a roster of 8 that differentiates by engine + mounts + hp; and the value concentrates where armor already sits |
| A tiny non-recharging pool on a big hull | a worse `Hp` card with more rules — `Hp` already buys additive survivability |
| Enemy hull starts shielded | a defensive layer on a fleet whose TTK tests model raw DPS-through-mitigation, dealt out before the player has the card that explains what a shield is. §12 |

## 4. Question 2 — is a shield-piercing shot needed? No

- **A store has no magnitude to cancel.** Against armor, `Pierce` has honest arithmetic (+1 pierce
  ≈ +1 damage: both are subtractions). Against a pool, "pierce" degenerates into a boolean "ignore
  the shield" — worth 0 against every unshielded target and everything against the one shielded
  hull. That is the card shape this codebase *withdraws rather than deals* (R19/R25: never a card the
  rules would refuse), and it would force a rename of the existing `Pierce`, which today means
  exactly "cancels Armor". One stat, one formula.
- **The shield's counters already exist, and none of them is a stat.**

| Counter | How it reads |
|---|---|
| Volume keeps it down | every hit that spends points re-arms the shock, so the refill only ever runs while the pressure has stopped — sustained fire turns a shielded hull back into plain hp |
| Burst above the pool | a kill round spends the pool and lands the rest on the hull the same tick; a support chip can never win a race against a recharge that does not run while it is being hit — R25's class split, free on the new axis |
| Round size | ARMOR_AND_PIERCE's owner rule: mitigation is answered with a bigger slower round, never with a mitigation-ignoring stat. A 24-damage slug leaves 8 on the hull through a 16 pool; a 4-damage chip leaves nothing |
| Bypasses | impact damage and mine contact already ignore mitigation by rule |

- Escalation ladder if playtest shows shields reading as unbeatable — in order, and none of them is
  a pierce stat: (1) slower rate or longer shock, (2) smaller pool, (3) no recharge on that hull's
  shield, (4) the ECM pulse knocks a shield down for its duration (reuses `EcmDisabled`, no new
  stat), (5) only then revisit a real `ShieldPierce`. Recorded as knobs (§11).

## 5. Numbers (R32's ×4 scale)

| Value | Number | Rationale |
|---|---|---|
| `Shield` card step | +16 Max | half the smallest hull's 32 hp, a fifth of the Heavy's 80; four tier-0 chips (4 dmg), one tier-0 HeavyCannon slug (8), or ⅔ of a tier-10 slug (24 → 8 to the hull) |
| Ceiling | 32 (two cards) | under half the hp a Heavy already carries; +32 on top of 32-80 hp never makes a hull untouchable — the same argument that caps armor at 12 |
| Shock | 2 s after any hit that spends points | the gate that makes regen safe at all: repeated hits keep re-arming it, so the shield pays out in a burst and then costs you the engagement. Same order as the 5 s ECM jam. This is the knob that says "how much is being left alone worth?" (§11) |
| Recharge rate | 16 pts/s (one full card per second) | fast enough that a 4-point hole closes in ¼ s and reads as "still there", while a spent ceiling (32) costs 2 s of refill on top of the shock. Time-to-full = `Shock + (Max − Points) / Rate`: 2.25 s / 3 s / 4 s for a 4-point hole, a spent card, a spent ceiling |
| Scope | gunfire, both directions | matches armor's single readable scope; impact and mines still bypass |
| Starting shield | 0 everywhere | §3 |
| Enemy shields in v1 | none | §3, §12 |

**What the pair is worth in dps.** The long-run absorb is `Max / (Shock + Max / Rate)`: **5.3 dps** at
one card (16), **8 dps** at the ceiling (32). Against the tier-0 fleet that reads as one card ≈ a
mid gunner's sustained output (Interceptor 2.4, Carmine 5.2 dps) and the ceiling ≈ a Streak's (8 dps)
— one ship's worth of pressure, never a wave's. Under real pressure the shock never clears, so the
long-run absorb is 0 and a shielded hull is exactly its hp: attrition and the orb economy stay intact.
That equivalence is the claim to *measure*, not assert (§10's `SustainedFireOutpaysTheShield`).

## 6. The card and the hand

- `UpgradeOption.Shield`, `UpgradeDefinition.Shield` (+16 additive), `ShieldRules.ShieldCeiling = 32`.
  Label "hull shield"; text: *Shield +16 — gunfire is soaked by the shield first; it refills 2 s after
  you stop being hit.*
- **Hull bucket**, gate `ShieldUnlockLevel = 10` — a beat that already exists (the second milestone
  hand and the `UnlockLevel: 10` guided guns), one tier after armor. Hull-slot draw becomes `Hp`
  below 5 · `Hp`-or-`Armor` at 5-9 · `Hp`/`Armor`/`Shield` from 10, each withdrawn at its own ceiling
  (R19's rule). Hp income: 100% → 50% → 33%. Knob §11: make the shield milestone-only if runs die
  before it pays.
- **A card, not a loot drop.** REROLL_TOKENS' own routing rule settles it — *a pickup is an instant
  un-chosen reward; a card is a choice* — and a shield is something the player times.
- Buying a card **widens the pool and charges it at once** (`Max += 16`, `Points = min(Points + 16,
  Max)`): a purchase made mid-refill must not be a card that does something three seconds later. The
  shock timer is left alone — buying is not an escape from being hit.
- `GameSession.ApplyUpgrade` gains a `Shield` case; `UpgradeCounts` already keys hull stats by an
  empty weapon name, and reroll honesty comes free (one dealer, `UpgradeOffer.Roll`).

## 7. The shield graphic

A mitigation the player cannot see reads as a bug — "my damage went somewhere invisible". So the look
ships in the same pass as the numbers, and it is also the only thing that will later explain why an
elite's hp bar did not move (§12).

**The ring is an event, not an aura.** Full is invisible, empty is invisible; the ring exists only
while something is happening to the shield.

| Shield state | Draw |
|---|---|
| Full (`Points == Max`) | **nothing**, plus the heartbeat below — one short bright segment sweeps the ring once every ~5 s |
| Chipped, shock live (`0 < Points < Max`, `Offline > 0`) | charge arc at faint alpha, no front — "spent, not refilling yet" |
| Charging (`Offline == 0`, `Points < Max`) | charge arc **plus** a bright refill front (~25°) crawling around the ring |
| Empty (`Points == 0`) | **nothing** — the shatter already announced it; the number lives in the HUD |
| Any hit the pool soaks | **intercept flash**: a ~40° bright arc centred on the bearing of the swept contact, ~0.15 s |
| The pool breaks | **shatter**: the ring pops out ~10% and fades over ~0.25 s, plus 6-8 near-white shards flying outward |

### Full shield: a heartbeat, not a permanent ring

**Is it better to show it or leave it invisible? Occasional, and rare.** Recommendation: invisible at
full except a glint that sweeps the ring every ~5 s.

- A permanent ring carries **one bit** — "shield up" — which the HUD segment already carries, and it is
  the one element that would sit on screen for every frame of every run. That frame is already busy:
  target brackets, ECM jam arcs, off-screen chevrons, thruster flames, the XP field, and the lit hull
  the whole `LIT_SPRITE_RENDERING` pass exists to make the star.
- A static ring also stops being seen. Change blindness makes a constant circle decorative in about a
  minute, so it would cost clarity *and* draw calls while saying nothing new.
- A travelling glint carries the same bit and adds one: it reads as "charged and alive". Motion is what
  the eye actually picks up off a still frame. Same language as the salvage capsule's pulsing ring
  (`RenderHelpers.DrawFieldRing`, pulse off simulated `Spin`) and the ECM capsule flashing to white:
  a charged object shows itself now and then instead of glowing all the time.
- Cost: ~0.25 s of one extra `DrawRing` per ~5 s — around 5% of frames instead of 100%.
- Deterministic: the sweep rides `view.ElapsedTime` (the world clock, so a seeded run draws it twice),
  with its phase offset mixed off the entity id the way `EcmRenderer.Mix` does — future shielded elites
  must not pulse in lockstep.
- The two dangerous states are already distinguishable **in motion**: you watch a shield break, and you
  watch it refill. If playtest still shows players confusing "pristine" with "broken", the fallback is a
  *very* faint full ring (alpha ≈40), not a brighter heartbeat — knob §11.

Decisions and where they land:

- **Geometry**: radius `hull.Radius × 1.15`, thickness `max(2 px, Radius × 0.05)`, and a segment count
  off `EcmRenderer.RingSegments(radius)` — round where it is big enough to need it, cheap where it is
  not.
- **The ring sits outside the bounding circle, and the bounding circle is not the hurtbox.** The arc is
  a field, not a hurtbox; `SPACEVORS_DIAGNOSTIC=1`'s cyan overlay draws the true hurtbox inside it, so
  anyone checking can see the ring lies outside what actually gets hit. The ring's radius is a
  **draw-only number** — nothing about collision radius, broad-phase indexing or the "a shape never
  reaches past its disc" invariant moves. Note both in DIAGNOSTICS.
- **Centred on screen-up and symmetric, not nose-anchored.** A nose-anchored arc reads as a directional
  shield — a different mechanic this game does not have.
- **The arc's span is what is left of the charge**, and the helper says nothing at full:
  `ShieldRenderer.ArcFor(points, max)` → `points >= max ? 0° : points / max × 360°`. "Invisible when
  full" is therefore one line in a pure function, not a branch scattered through the renderer.
- **The refill front costs no new state**: it rides `Points`, so it crawls while the pool refills and is
  gone the moment it is whole. No timer, and deterministic — the same discipline as
  `RenderHelpers.DrawFieldRing`, whose pulse rides the capsule's simulated `Spin` rather than wall-clock
  time. The heartbeat is the one thing that *does* need a clock, and it takes the world clock (above).
- **Flash and burst ride the hull, not a free-floating effect entity.** `ShieldFlash(float Lifetime,
  float BearingDeg)` and `ShieldBurst(float Lifetime)` are components **on the hull**, ticked by the
  shield system. A separate `Position`-bearing effect entity would visibly detach from a moving hull
  during its 0.15-0.25 s life, and a detached intercept flash blames the wrong thing.
- **A soaked hit shows the flash and nothing else.** Today the ammo→player site spawns an amber spark at
  `playerPos` and *discards* the swept contact (`TrySweep(..., out _, out ...)`): the contact gets kept
  for the bearing, and a fully soaked hit stops spawning that spark — amber must keep meaning "the hull
  took it". For a beam the intercept is already computed, the shortened end from `BeamEntryLength`. A
  **partial** soak shows both reads at once — flash on the ring, amber at the hull — which is the split
  the player is meant to feel.
- **Shatter shards get their own effect entity, because they must not read as sparks.**
  `ShieldShard(float Lifetime, float InitialLifetime)` on a Position + Velocity entity, 6-8 of them
  spawned on the ring at the break, velocity biased radially outward and carrying a fraction of the hull's
  velocity like `DamageEffectSystem.FollowFactor`; lifetime ~0.6 s, world RNG like every other particle;
  lifetime ticked by `EffectSystem`, which already ticks `DamageSpark`, `BeamImpact`, `Explosion` and
  `EcmWave`. Drawn as a **short streak along its own velocity** through `ShapeSprites.DrawStreak` (batch
  friendly) rather than a round dot — a shard is a splinter, a spark is a bead.
- **Why not reuse `DamageSpark`**: its amber is the game's "something is being hit/burning" colour —
  every hull hit, impact and fireball already speaks it, so amber shards would file the shatter under
  *damage taken* rather than *field broke*. And a colour field on `DamageSpark` would mean re-deciding
  the colour of every particle in the game, whose hue today is derived from its lifetime.
- **Shard colour = the shield's own near-white ice**, so nothing on screen can be mistaken for it:
  a white-hot core (≈225,240,255) lerping to the arc's (≈190,225,245) as it fades, alpha falling with
  life. Nothing else in the palette draws near-white *particles*: XP is saturated blue (50/150/255), the
  ECM wave and arcs are mid blues (120/180/255, 170/215/255), the UI accent is (120/200/230), health
  orbs are mint, diagnostics is cyan, ammo colours cover yellow/green/blue/red/cyan/violet/orange,
  sparks are amber and explosions are orange/red; magenta and gold stay reserved for elites and bosses.
  Shape backs the hue up: outward-moving streaks against the sparks' shrinking round dots.
- **One colour family, three entries in `Palette`**: `ShieldArc` (the faint charge arc, ≈190,225,245 at
  alpha ≈90), `ShieldFlash` (the intercept flash and the refill front — the same hue, brighter and more
  opaque), and the shard pair above. A cool, near-white **charged field** is the read; saturated blues
  are already spent (§below), and SHIP_TINT_PALETTE keeps magenta and gold reserved. Telling a shield
  apart from a jammed hull's ECM arcs leans on **shape**, not hue: the jam's are jittery line strips
  *on* the hull, the shield is a smooth ring *outside* it. Judge it off the run's screenshots (§9), not
  in the abstract.
- **Cost, now that the ring is an event**: a full shield draws **nothing** but the heartbeat glint on
  ~5% of frames, so the steady state is cheaper than a permanent aura and cheaper than the pre-shield
  frame. A hit adds one arc draw for ~0.15 s; a break adds one ring draw plus 6-8 streak draws for
  ~0.25-0.6 s; a refill adds the arc and the front until it is whole. Raylib shape draws flush the 2D
  batch (~1.1 µs each — the reason `ShapeSprites` exists) except the `ShapeSprites.DrawStreak` shards, so
  this stays a **per-event, per-entity** cost that must not scale with a crowd. In v1 the player is the
  only shielded hull. Forward rule: if enemy shields ever ship for more than a handful of hulls, the
  ring becomes a sprite drawn through `ShapeSprites` before anything else in that plan.
- **Elites.** Reserve the enemy-side shield for elites — the roadmap's elite idea is already "a marker
  component and a ring in the renderer", and the shield arc is the version of that marker that
  *explains itself*: a round dying on the arc, flashing where it touched, is the reason that hp bar did
  not move. Two rings on one hull (elite marker + field) would read as noise, so an elite's identity
  stays the **magenta hull tint** (reserved for exactly that) and its field stays the shield's arc.

## 8. Implementation sketch (small commits)

1. **Domain mechanic**: `Shield(Points, Max, Offline)`, `ShieldFlash(Lifetime, BearingDeg)`,
   `ShieldBurst(Lifetime)`, `ShieldShard(Lifetime, InitialLifetime)`; `ShieldRules` (`ShieldCeiling`,
   `ShockSeconds`, `RechargeRate`, `Soak(mitigated, points) → (absorbed, spill)`); `_frameRemainingShield`
   accumulation and one `Shield` write-back per hit at the four gunfire sites, plus the `ShieldFlash`
   write and — when the pool breaks — the `ShieldBurst` write and the outward shards; keep the swept
   contact at the player-ammo site so the flash knows the bearing; `ShieldSystem` (shock, regen, flash
   and burst timers) appended **last to Intent** in `SimulationRunner` — §2 says why not Resolution —
   while shard lifetimes tick in `EffectSystem`, beside `DamageSpark`, `BeamImpact`, `Explosion` and
   `EcmWave`.
2. **Card**: `UpgradeOption.Shield` + definition + ceiling; `UpgradeOffer.HullOptions` gate at 10
   with the R19 withdrawal at ceiling; `ApplyUpgrade` case.
3. **The ring**: `Game/ShieldRenderer.cs` — one pass called from `WorldRenderer` right after the hulls
   (over the player, under brackets, the way `EcmRenderer` sits after the enemy hulls) with one
   `RenderTimings.Pass("world.shield")` beside it; it draws the charge arc, the intercept flash, the
   refill front, the break ring, the heartbeat glint and the shards. Pure geometry in
   `ShieldRenderer.ArcFor(points, max)` (0° at full) so it is testable headless; `Palette.ShieldArc`,
   `Palette.ShieldFlash` and the shard core/trail. A soaked hit stops spawning its amber spark at the
   hull — the flash on the ring is the whole feedback, and amber must keep meaning "the hull took it".
4. **The numbers read-out**: HUD — a thin shield segment on the health bar's own scale, outline-only
   while the shock is live (`HudRenderer`); stats screen — a `Shield 12/16` row with its upgrade count,
   like `Armor`; ship-select stays as it is (no hull starts shielded).
5. **Docs + measurement**: this file's `Status`/`## Shipped`; ARCHITECTURE's **Phases** list gains
   `ShieldSystem` (an invariant list, so it moves with the code); PLAN.md index line and the struck
   roadmap line; a dated Follow-up in ARMOR_AND_PIERCE §7 pointing here; DIAGNOSTICS — `Shield:` in the
   scripted-upgrade note, `SPACEVORS_DIAG_SHIELD=<points>` to start with a shield, and the two graphics
   keys (`Y` grants and fills a shield, `X` spends it to nothing — so the shatter, the front and the
   down state are all checkable in a window without a 10-level run); §9's sim pair *and* its screenshots.

Deliberately untouched: ThreatMeter (reads raw incoming damage — mitigation is the player's business,
already decided for armor), `DamageShading` (stays health-driven: a hull with a full shield at full
hp renders pristine, and the ring is the readout), hp bars, save (none), scenarios.

## 9. Performance

Two int ops and one `TryGetComponent<Shield>` per **hit**, one dict entry per hit, a per-tick loop
over a component list that is empty in every scenario, and one component write per tick while a shield
is refilling or a flash lives (one entity, and no scenario has one). Broad phase, firing and
integration never see it. Expect the numbers not to move, and prove it the paired way:
`.tools/scenario.sh swarm-1024 --sim --iters 3` and `impact-storm --sim --iters 3` before/after
commit 1 on the same host, verdict in `## Shipped`. Armor's pass moved 0% and −0.9%; the same shape
is expected. A shielded-hull scenario is not needed until enemies carry shields.

The ring is the side that actually costs something, and it costs **shape draws**, not sim. So the render
leg gets measured too — a scenario run with `SPACEVORS_SCENARIO_SHOTS=start,mid,end` and the new
`world.shield` pass in `RenderTimings`:

- **A ~0 ms `world.shield` is the correct steady state now**, since a full shield draws nothing but the
  heartbeat. So the pass is read in two situations: a full-shield run (≈0, with the glint showing up in a
  screenshot every ~5 s — proof the pass runs) and a **chipped** shield under fire (arc + front + flash +
  shards on screen), where it should land in the 1-4 µs band. Reading only the first would hide a ring
  that never draws; reading only the second would hide that the player is full most of the run.
- The screenshots must show the arc **outside** the hull and inside the target brackets, faint but
  readable over the star field and the XP blue, and the shards unmistakably near-white against the
  amber sparks of the same moment.
- Nothing in the sim scenarios changes, so no baseline moves; the render verdict goes in `## Shipped`
  next to the sim one.

## 10. Tests (deterministic, sim-only)

`Tests/ShieldCombatTest`:

- `AChipSpendsShieldPointsBeforeHealth`, `OverflowSpillsOntoTheHull`, `AnEmptyShieldSoaksNothing`
- `AHitThatSpendsPointsStartsTheShock`, `ASecondHitReArmsTheShock`, `NoRegenWhileTheShockIsLive` —
  the gate the whole balance rests on
- `SustainedFireOutpaysTheShield` — chip fire at ~1 hit/s for 20 s still drains the hull to the point
  the pool cannot cover. This is the anti-immunity invariant, and it is the one to re-check first if
  the shock or the rate is ever retuned
- `ARegenRefillsToMaxAndStops`, `RegenGrantsWholePointsOnly`,
  `ASmallHoleClosesFasterThanASpentPool` — the proportional rule (¼ s against 3 s)
- `ARegenStepNeverDropsThisTicksSpend` — the phase-placement regression (regen last in Intent; see §2)
- `ShieldAndArmorCompose` (mitigate first, then soak), `PierceDoesNotTouchTheShield`
- `ImpactAndMineContactBypassTheShield`
- `ThreeRoundsInOneTickCannotSoakMoreThanThePool` — the frame accumulator
- `AHealthOrbHealsTheHullNotTheShield`
- `NoHullStartsShielded` — over `ShipType.All` and `EnemyShipType.All`

Offer tests (in `HandCompositionTest`, level-gate style): `NoShieldCardBeforeLevelTen`,
`LevelTenMayOfferShield`, `AShieldCardAtTheCeilingIsWithdrawn`, and `RerollOfferTest` honesty riding
the shared `Roll`. Plus `AShieldCardWidensThePoolAndChargesItAtOnce`.

Graphics **state** — what the renderer reads, tested headless like every other sim rule:

- `AnInterceptWritesAFlashOnTheInterceptBearing`, `AHitThatReachesTheHullWritesNoFlash`,
  `AFullySoakedHitSpawnsNoAmberSpark` — amber keeps meaning hull damage
- `ABreakingThePoolWritesOneBurstAndShards` (6-8 of them, spawned outside the ring radius and moving
  outward), `ANoBurstWhileThePoolIsAlreadyEmpty` — a hit on an empty shield must not fire the shatter
  every tick
- `FlashBurstAndShardTimersClear` — a leftover would weld a bright arc to the hull, or leave a shard
  hanging in the field forever
- `ShieldRenderer.ArcFor` over the pure helper (the `IndicatorRendererTest` pattern): **full → 0°**,
  half → 180°, empty → 0°, never past 360° whatever `Points` says — that one line is the whole
  "invisible when full" rule
- `TheGlintFiresOncePerPeriodAndTheSameWayTwice` — `GlintPhase(entityId, elapsed)` draws at most one
  sweep per period and gives the same answer for the same entity and the same world clock (the
  determinism rule, tested rather than asserted)

## 11. Open knobs (playtest)

- **Shock 2 s vs 1 / 4 s** — this is *the* shield knob: it decides how much being left alone is worth,
  i.e. whether the shield reads as "survive a burst, then disengage" or as a second hp bar. The rate
  only decides how the refill looks once the shock is over.
- Rate 16/s vs 8/s — "the hole snaps shut" against "watch a bar fill". At 8/s a spent ceiling costs 6 s
  round trip and the card starts feeling expensive.
- Shield at every level ≥ 10 vs milestone-only (the 33% Hp income is the worry).
- Should guided rounds be soaked at all, or bypass the shield to stay scary? v1: soaked — one rule,
  and the missile's answer is the support guns that shoot it down.
- ECM pulse dropping the player's shield for its duration (a three-line interaction: jam the shield as
  well as the gun). v1: no.
- Heartbeat: ~5 s period, ~0.25 s sweep, one bright segment. If players still cannot tell pristine from
  broken, shorten the period (≈2 s) **before** considering a permanent ring, and only then try the
  last-resort full ring at alpha ≈40. Judge it off a mid-scenario screenshot with stars behind it, not
  off a paused empty screen.
- Arc alpha ≈90 vs ≈60, thickness 2 px vs 3 px, flash width ~40°. Widen the arc before brightening it —
  the read is *where*, and brightness saturates first.
- Shards: 6-8, ~0.6 s, near-white. If they read as XP pickups rather than glass, make them **shorter and
  dimmer**, not bluer — the near-white is the thing that separates them from every particle in the game.
- Armor 12 + shield 32 on a Slow hull at tier 10: too safe? Measure with the ceiling test, don't
  reason about it.

Accepted cliff, by design: a hull that is genuinely **left alone** regains its whole shield in
`Shock + Max / Rate` (3 s at one card, 4 s at the ceiling), so a lone distant gunner cannot kill a
shielded hull. That is the mechanic's lesson, not a bug — the knob that bounds it is the shock, and
`SustainedFireOutpaysTheShield` is the line it may not cross.

## 12. Follow-ups (their own plans)

- **The shielded enemy hull** — PLAN.md's "shielded hull" archetype. This plan's §4 is its threat
  design: the player answers it with burst and round size, which is why the pool must be sized
  against the guns a run actually fields by then. The plan that owns it decides: which hull
  (Carmine, the line-breaker, reads better than the swarmer), pool size, and **whether it recharges
  at all** — recommendation: an enemy shield does *not* recharge. If that needs a rate the player's
  constant cannot express, the rate moves onto the component as a fourth field rather than becoming a
  second rules class. A non-recharging shield keeps a committed kill committed:
  `EnemyDifficultyTest`'s TTK model then gains one subtraction instead of a time-dependent
  term. The read comes from §7 unchanged — and it is the *reason* to keep enemy shields
  **elite-only**: the intercept flash on the arc is what tells the player why that ship's hp bar did not
  move, and it only reads cleanly while a crowd of them is not filling the screen with rings.
- **Corrosion** (ARMOR_AND_PIERCE §7) is the support class's answer to armor; burst is the answer to
  a shield. If both ship, the support class needs one named role against mitigation — a balance
  pass, not a new stat.
- **Shield graphics left over** (all of them cheap, none of them needed to ship): whether
  `DamageShading` should flicker the hull when the pool breaks; a shield segment inside an enemy hp bar
  (`TARGETING_HP_BARS`) once elites have one; the batched ring sprite that replaces `DrawRing` the moment
  shielded hulls come in crowds (§7's cost rule); and a look at the heartbeat once several shielded hulls
  are on screen at once — per-entity phase should keep them out of lockstep, but that is a screenshot
  judgement, not a code one.
- **Shield cells as loot** (REROLL_TOKENS' original framing): needs the timed-modifier pattern that
  Overdrive also needs.

## Shipped (2026-11-03)

What landed, in the order the plan asked for it:

- **The rule** — `ShieldRules` (`Domain/Combat/ShieldRules.cs`): ceiling 32, shock 2 s, rate 16 pts/s,
  `Soak(points, mitigated)` after `ArmorRules`, `SecondsToFull`, `RingRadius`, `WithCard`. The order is
  armor then pool, unchanged from §2: `mitigated = max(1, damage - max(0, armor - pierce))`, then
  `soaked = min(Points, mitigated)`, then `hull = mitigated - soaked`.
- **Components** — `Shield(Points, Max, Offline)`, `ShieldFlash(Lifetime, BearingDeg)` and
  `ShieldBurst(Lifetime)` on the hull (`CombatComponents.cs`), and `ShieldShard(Lifetime,
  InitialLifetime)` as its own particle entity next to `DamageSpark` (`EffectComponents.cs`), ticked by
  `EffectSystem` with its own `[SYSTEM] Effects: shield shard` row.
- **Combat** — one `MitigatedGunfireHit` helper at all four gunfire sites (ammo→hull, ammo→player,
  beam→hull, beam→player); the pool accumulates per tick like `_frameRemainingHealth` and is written
  back once in the resolution pass, so several rounds soaking one hull in one tick all land. The two
  player sites now keep the swept contact they used to discard, which is where the flash's bearing
  comes from. A fully soaked hit no longer spawns the amber spark at the hull.
- **The system** — `ShieldSystem` appended **last in Intent** (see ARCHITECTURE's Phases): in Resolution
  it would write a pre-hit copy of the pool back over the same tick's spend (#58). `ShieldCombatTest
  .ARegenStepNeverDropsThisTicksSpend` and `ShieldSystemRunsLastInTheIntentPhase` guard it.
- **The card** — `UpgradeOption.Shield`, +16, in the hull bucket from `ShieldUnlockLevel = 10`,
  withdrawn at the ceiling; `ShieldRules.WithCard` banks what it grants and leaves a running shock. No
  shield-piercing stat, no enemy shields, no hull starts with one.
- **The look** — `Game/ShieldRenderer.cs` on its own `world.shield` pass, `Palette.ShieldArc` /
  `Palette.ShieldLit`, the HUD segment above the health bar on the hp scale (dim while shocked), and the
  stats-screen row. `ArcFor` and `GlintAngleDeg` are pure, so the invisible-when-full rule and the
  once-per-period glint are tested rather than eyeballed.
- **Diagnostics** — `SPACEVORS_DIAG_SHIELD=<points>`, `Y` fills the pool, `X` spends it and fires the
  burst (DIAGNOSTICS.md).

Deviations from the plan, all deliberate:

- `Soak` **rounds up** (`ceil`), where §5 sketched `(int)min`. A fractional pool under `(int)` would
  leave an unspendable remainder that never reaches zero, never breaks, and hangs there invisible.
- Shards are a fixed **6**, evenly spread in angle under one random offset, at a constant 150 px/s with
  0.4 of the hull's velocity inherited. §7's "6-8 randomly placed" read as a puff; even spread under one
  offset reads as a ring coming apart.
- The purchase math moved into `ShieldRules.WithCard` rather than living in `ApplyUpgrade`, so it is
  testable without a window.
- One near-white (`ShieldLit`) for flash, front, glint and shard core instead of three near-identical
  constants: one identity, nothing to drift. §7's separate shard-trail colour is what the lifetime lerp
  to `ShieldArc` already produces.
- `HandCompositionTest`'s hull-slot filter gained `Shield`: the hull bucket now draws between `Hp`,
  `Armor` and `Shield`, and the slot still deals exactly one of them.
- `X` fires the burst but **not** shards. Shards are what combat produces when a pool breaks
  (`CollisionSystem`), and spawning them from the game layer would fork the rule.

Verification:

- **Tests**: 26 new (`ShieldCombatTest`, `ShieldGraphicTest`), whole suite 650 green. They cover the soak
  rule and its rounding, armor-before-pool, partial spill, the break, no shock on a dry pool, two rounds
  in one tick, the amber-spark suppression, the flash bearing, shard count and spawn radius, flash/burst
  lifetime cleanup, mine and impact bypass, `ContinuousFireReArmsTheShock_ThePoolPaysForFourHitsAndNoMore`,
  the proportional refill, the #58 ordering and the phase placement, `NoHullStartsWithAShield`, and the
  level-10 gate with the ceiling withdrawal.
- **Perf, paired on one host (2026-11-03, `--sim --iters`, interleaved A/B against a detached HEAD
  worktree, medians over 1080 measured frames per side)**. The first pairs disagreed wildly — 1.48 vs
  2.47 ms — which was host drift, not the change: the same-day `index.tsv` rows for these scenarios had
  already drifted 1.41→1.48 and 2.22→3.49 with no code between them. On the quiet host the pairs match:
  **ammo-spray A 1.44 / B 1.43 and A 1.44 / B 1.40**; **impact-storm A 2.25 2.27 2.24 / B 2.28 2.25
  2.23**. The new system row measures **0.00018 ms per tick**, and per-frame allocations are unchanged
  (296 KB). **Verdict: 0% — keep.** As §9 predicted, no scenario has a shielded hull, so the only sim
  cost is one failed component lookup per gunfire hit.
- **Render leg**: window run with `SPACEVORS_DIAG_SHIELD=16` — `world.shield` **0.00-0.01 ms** with the
  pool whole. That ~0 is the geometry being deliberately absent, not a ring that failed to draw: the same
  run with the pool spent draws the arc, the front and a flash (below).
- **Screenshots** (`.tools/shots/shield-full.png`, `shield-break-1/2.png`, `shield-spend-hud-1/2.png`,
  `shield-run.png`; 240×220 crops under `.tools/run/`): whole pool → **no ring at all** around the hull;
  spent pool → charge arc plus a bright front on the ring outside the hull, and the intercept flash on
  the side the rounds came from; the HUD segment reads about half the health bar's width for 16 points on
  a 32 hp hull. A ~40 px ring cannot be judged in a downscaled 1920×1024 shot, so the check was numeric:
  a throwaway `.tools/run/pngscan.py` (gitignored) counts pixels near `ShieldLit` and reports their
  bounding box — 0 near-white pixels at the hull when full, ~150 forming the arc when spent.
  **Not captured on a screenshot**: the shards. They last 0.6 s and every window frame caught either a
  whole pool or a GAME OVER screen; they are verified by the test instead (6 of them, spawned outside the
  ring radius, moving outward, cleared by `EffectSystem`).
- **Knobs to judge by eye later**: the ring sits at 1.15× the bounding circle, which reads slightly loose
  against a hull whose composite shape does not fill that circle; §11's alpha/thickness and heartbeat
  period are the knobs.

## Follow-up (2026-11-03): the shield card is dealt in two tiers

Same split as armour (plans/UPGRADE_BUNDLE_PAIRS.md §2): the card is still gated at level 10, but its step is
now +8 at an ordinary level and +16 at a milestone — 25 % and 50 % of `ShieldRules.PoolCapacity` (32). A card is
dealt only while its whole tier step fits under the ceiling, so at 25 pool the ordinary +8 card is dealt and the
milestone +16 card is not. The refill, shock and shatter rules are untouched: a smaller card buys a smaller
pool, and the pool still refills at 2/s from the frame the damage lands, so nothing about recharge timing moved
with the step.