# PLAN.md

## Vision

A minimalist Vampire Survivors-style game in space.

The player pilots a spacecraft using Newtonian flight while surviving increasingly dangerous enemy waves.

Runs entirely from code with no editor-generated assets.

## Core gameplay

Player:
- thrust
- rotate
- inertia
- drift
- boost

Enemies:
- pursue player
- swarm
- collide
- attack
- arrive in timed waves (increasing size), separated by lulls: quiet gaps (ships pause, mines keep
  coming) and full lulls (nothing spawns) — the loot-collection window

Weapons:
- automatic
- upgrade over time

Progression:
- gain XP
- level up
- choose upgrades

Goal:
- survive as long as possible.

## Controls

### Ship selection (start)
- Arrows/WASD or mouse wheel: browse the scrollable ship list
- Click, Enter, or 1-8: choose ship

### In-game
- W: forward thrust
- S: backward thrust
- A: left sideways thrust
- D: right sideways thrust
- Shift: boost (forward only, 2.5x)
- Mouse: aim — the ship rotates toward the cursor (primary rotation)
- Left click: lock the clicked enemy ship, mine or asteroid as primary target (red corner brackets); all weapons then prioritize it at extended range. Clicking empty space clears the target. Rocks are targets only this way — the guns never shoot scenery on their own
- Tab: toggle ship stats screen (pauses the game; also works while picking an upgrade — cards are hidden behind it). Shows every upgradeable stat with its current value and how many times it was upgraded, how many hull mounts of each size are filled, and per weapon its hull mount with firing arc and turret turn speed. Layout: ship/engine stats top-left, weapon stats top-right, upgrade cards in a bottom row. UI text scales with window width (1x at 1920px, capped at 2x) and shrinks to fit vertically on short windows
- Upgrade choice: 1-5 or click a card. A new-weapon card marks the weapon's size in its prominent line (large / small) and lists its base numbers: range, damage (with pellet count when it fires several), attack speed and projectile speed — "beam instant" for hitscan guns. Choosing one opens the turret-mount screen — the hull drawn large, every mount shown as a circle with its firing arc and a number that never changes, mounts that can take the weapon ringed in green, and the cargo of inactive weapons listed at the left. With a weapon in hand: click a mount or press its number to fit it (a filled mount swaps, the displaced gun going into cargo and into hand), C stores the weapon in cargo, right-click puts it down, Esc leaves the screen (back to the same cards if the weapon was never placed)
- Turret mounts and cargo from the stats screen: Tab, then click "Manage mounts and cargo". With nothing in hand, click a mounted turret or press its number to take it down into cargo (it stops firing and stops being offered upgrades); click a cargo weapon to pick it up, then click a mount to fit it. Esc returns to the stats screen. Everything is free and unlimited — rearranging is not a choice the game charges for
- Salvage capsule (a grey capsule a killed ship drops, 6% of kills): magnetised to the hull like a health orb, stocks one reroll charge, shown as an icon and count under the level readout. While the upgrade cards are up, R or a click on the "R reroll ×n" panel spends one charge and re-deals the whole hand
- R: restart from game over (back to ship selection)

## Diagnostics

Debug keys and env vars: see DIAGNOSTICS.md.

## Feature index

Trust each plan file's `Status:` header and its Shipped/Follow-up sections; the plan body
below the header is frozen intent and may be stale (see plans/DOC_LAYOUT.md).

### Ships, hulls, mounts
- Ship types; all 8 player hulls, 5 enemy ships; follow-ups: Falcon, Eagle/Manta/Raptor, Streak/Carmine, Falcon+Manta resized ×1.5 — shipped — plans/SHIP_TYPES.md
- Turret layouts, mount management, cargo — shipped — plans/TURRET_LAYOUTS.md

### Weapons and combat
- Bullets as lines, lasers, particle accelerators — shipped — plans/BULLETS_AS_LINES.md
- Missiles and guided rounds — shipped — plans/MISSILES.md
- Weapon balance passes 1-2; rules R1-R31, open knobs — shipped — plans/WEAPON_BALANCE.md
  (pass 2 split the roster into a kill class that owns the top DPS and the long band and a support class of
  chips + missile screens; support guns lost the `Damage` card and their range roof is 1350 px)
- Weapon balance pass 3 steps 1-2; rules R32-R33: every damage and hp number ×4 (same hit counts, same TTK,
  same feel) and `Damage` cards become percents of the table base added not compounded (+25% support, +50% kill,
  milestone-only), `MissileHealth` +50% of the round's base, `Hp` +8, mines now walk the tier clock (8 + 2·tier)
  — shipped — plans/WEAPON_BALANCE.md
- Weapon balance pass 3 steps 3-5; rules R34-R36: band-scaled `Range` with no ceiling, a scatter-tightening
  card, a missile-agility card — proposed — plans/WEAPON_BALANCE.md
- Weapon identity cells; R23 one gun of each type aboard, FlechetteFan/Stiletto/SiegeSalvo/FarlockSalvo, deduped Shadow and Heavy loadouts — shipped — plans/WEAPON_IDENTITY_CELLS.md
- Impact damage — shipped — plans/IMPACT_DAMAGE.md
- Armor and armor piercing: gunfire minus armor floored at 1, pierce cancels armor first; Heavy and
  Eagle (the Slow hulls) and enemy HeavyCannon start armored 4; `Armor` (+2) / `ArmorPierce` (+4)
  cards from level 5 only, ceilings 12/8; HeavyCannon throws the fleet's heavy slow slug (+4 damage,
  wider, 140 px/s) — the only enemy round that walks through armor, enemies never pierce — shipped
  2026-11-03 — plans/ARMOR_AND_PIERCE.md
- Primary target lock and enemy hp bars — shipped — plans/TARGETING_HP_BARS.md
- Shields: gunfire is taxed by armor and then banked by a pool of absorb points in front of hp; it spends
  whole points, arms a 2 s shock whenever a hit spends any of it, and refills at 16 pts/s after that, so a
  small hole closes fast and sustained fire pays hp again; `Shield` (+16) in the hull slot from level 10,
  ceiling 32, no hull starts shielded, impact and mine contact bypass it, and the ring is an event not an
  aura (invisible at full and at empty, a ~5 s glint while whole) — shipped 2026-11-03 — plans/SHIELDS.md

### Enemies and difficulty
- Waves and lulls — shipped — plans/WAVES_AND_BREATHERS.md
- Loot drops: XP pickups, health orbs, magnet, XP/level-up — shipped — plans/LOOT_DROPS.md
- Reroll-token loot: salvage capsules off killed ships stock charges that re-deal the upgrade hand — shipped — plans/REROLL_TOKENS.md
- Salvage field and ECM pulse drops: one-shot field sweep (all XP, all currency, nearest health up to full hp) and a 5 s world-wide jam of ships, mines and missiles; the ECM capsule never expires — shipped — plans/ECM_PULSE_AND_SALVAGE_FIELD.md
- Difficulty scaling and threat readout; open items remain — active — plans/DIFFICULTY_SCALING.md
- Upgrade hand composition: every hand dealt from card-kind slots (hp + 1 engine/utility + 2 support + 1 kill; milestone hp + ≤3 new guns + damage) — shipped 2026-11-02 — plans/UPGRADE_HAND_COMPOSITION.md

### Physics and sim
- Capsule collision: hulls and lumpy rocks as their shape — shipped — plans/NON_CIRCLE_COLLISION.md
- Stats directory (Domain/Stats) — shipped — plans/STATS_DIRECTORY.md
- Pop/swap compact ECS storage (slot arrays, O(1) delete, no per-enumeration sort) — shipped — plans/POP_AND_SWAP.md

### Graphics and UI
- Damage graphics (smoke, sparks) — shipped — plans/DAMAGE_GRAPHICS.md
- Hull damage shading — shipped — plans/DAMAGE_SHADING.md
- UI polish — shipped — plans/UI_POLISH.md
- Off-screen edge indicators: distance-tapered chevrons, enemies red, pickups in their world colours — shipped — plans/OFF_SCREEN_INDICATORS.md
- Lit sprite rendering: skip transparent texels, pack depth into the normal map, 512 px mipmapped maps, count/colour the gathered lights, key+fill+rim+spec rig — shipped 2026-11-02 — plans/LIT_SPRITE_RENDERING.md
- Ship tint palette: neutral fill + cool rim, per-faction hull tint (ivory player, crimson enemies), faction engine glow; magenta reserved for elites, gold for bosses — shipped 2026-11-02 — plans/SHIP_TINT_PALETTE.md
- Thruster flame anchors: flame/light bases at the hull shape's support along the thrust direction instead of the bounding radius — shipped 2026-11-02 — plans/THRUSTER_FLAME_ANCHORS.md

### Tools
- Perf scenarios, harness, gates, cost logging — shipped — plans/PERF_SCENARIOS.md
- Missile-loaded perf scenarios (missile-384/1024) — shipped — plans/MISSILE_PERF.md
- Ceiling test: the largest mixed world that still fits 8.33 ms, probed in sim and in window, recorded in `.tools/run/ceiling.tsv`, never gated — shipped 2026-11-02 — plans/CEILING_TEST.md
- Minor shipped features (camera-follows-mouse, rocks-as-cover, aim-line volley, ...) — record — plans/MINOR.md
- Documentation layout and migration — shipped — plans/DOC_LAYOUT.md

### Proposed, not implemented
- Endless difficulty escalation: behaviour first, distance-driven clock, duty cycle, elites — plans/ENDLESS_DIFFICULTY.md
- Parallel sim — plans/PARALLEL_SIM.md
- Damage affects ship performance — plans/DAMAGE_PERFORMANCE.md

Frozen reviews/design notes: plans/CODE_REVIEW.md, plans/GRAPHICS_REVIEW.md,
plans/functional-ecs.md.

## Roadmap — near future (data on existing machinery)

- New enemy archetypes: kamikaze, sniper, minelayer, shielded hull
- New mines: turns into a missile on approach; fires one delayed beam after lock-on
- Weapon identity cells still empty on purpose (aura × fast/medium, close × hitscan, mid × fast,
  long × medium/slow) and the playtest knobs left open — plans/WEAPON_IDENTITY_CELLS.md §8
- Difficulty scaling open items: pellet volleys, concurrency, mines, reward side (plans/DIFFICULTY_SCALING.md)
- Loot/reward loop: reroll charges and the salvage field / ECM pulse drops shipped
  (plans/ECM_PULSE_AND_SALVAGE_FIELD.md covers currency-by-pickup rather than a counter); a salvage
  *currency* only earns its keep once there is more than one thing to buy (free mount swaps and cargo
  already shipped — plans/TURRET_LAYOUTS.md)
- Run modifiers / seed flags: spawn-table toggles
- Asset nit: regenerate the four mismatched lit depth maps (`small-1`, `interceptor`, `fighter`, `heavy`) so the load-time warnings stop firing (plans/GRAPHICS_REVIEW.md)

## Roadmap — future (new systems)

- Weapon evolutions: two tagged upgrades fuse into one stronger card
- Asteroid fields as terrain; procedural sectors from spawn/composition tables
- Elite enemies: mount/aura/hp modifiers on normal hulls
- Bosses: one huge hull, many mounts, phases as a small state table
- Deterministic replays before save/load
- 100k prep: SoA audit + a 50k scenario, before parallel sim (plans/PARALLEL_SIM.md)
- Mod-friendly stats: Domain/Stats tables exported to JSON/TOML

## Roadmap - future (longer ideas)

### Armor and armor piercing bullets — shipped 2026-11-03

Built as plans/ARMOR_AND_PIERCE.md (read its `## Shipped`): flat per-hit subtraction floored at 1,
pierce cards on the player side only, HeavyCannon's heavy slug as the anti-armored-hull threat.
The corrosion answer (acid stripping armor) is still open there; the idea's "mix in slower more
powerful shots" shipped as that slug, not as enemy pierce — enemies never pierce, by rule.

## Roadmap — infrastructure / QA

- Perf gate in CI: `.tools/scenario.sh --check-baseline` on every push
- Balance fuzzing: random upgrade sequences assert no card is ever a lie
- Portability: Windows CI leg or a documented cross-platform run matrix

## Done criteria

The game is considered complete when:
- gameplay is fun
- architecture remains simple
- code is easy for local LLMs to navigate
- new weapons and enemies require minimal changes
