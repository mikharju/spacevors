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
- Weapon balance pass; rules R1-R23, open knobs — active — plans/WEAPON_BALANCE.md
- Weapon identity cells; R23 one gun of each type aboard, FlechetteFan/Stiletto/SiegeSalvo/FarlockSalvo, deduped Shadow and Heavy loadouts — shipped — plans/WEAPON_IDENTITY_CELLS.md
- Impact damage — shipped — plans/IMPACT_DAMAGE.md
- Primary target lock and enemy hp bars — shipped — plans/TARGETING_HP_BARS.md

### Enemies and difficulty
- Waves and lulls — shipped — plans/WAVES_AND_BREATHERS.md
- Loot drops: XP pickups, health orbs, magnet, XP/level-up — shipped — plans/LOOT_DROPS.md
- Reroll-token loot: salvage capsules off killed ships stock charges that re-deal the upgrade hand — shipped — plans/REROLL_TOKENS.md
- Salvage field and ECM pulse drops: one-shot field sweep (all XP, all currency, nearest health up to full hp) and a 5 s world-wide jam of ships, mines and missiles; the ECM capsule never expires — shipped — plans/ECM_PULSE_AND_SALVAGE_FIELD.md
- Difficulty scaling and threat readout; open items remain — active — plans/DIFFICULTY_SCALING.md

### Physics and sim
- Capsule collision: hulls and lumpy rocks as their shape — shipped — plans/NON_CIRCLE_COLLISION.md
- Stats directory (Domain/Stats) — shipped — plans/STATS_DIRECTORY.md
- Pop/swap compact ECS storage (slot arrays, O(1) delete, no per-enumeration sort) — shipped — plans/POP_AND_SWAP.md

### Graphics and UI
- Damage graphics (smoke, sparks) — shipped — plans/DAMAGE_GRAPHICS.md
- Hull damage shading — shipped — plans/DAMAGE_SHADING.md
- UI polish — shipped — plans/UI_POLISH.md
- Off-screen edge indicators: distance-tapered chevrons, enemies red, pickups in their world colours — shipped — plans/OFF_SCREEN_INDICATORS.md

### Tools
- Perf scenarios, harness, gates, cost logging — shipped — plans/PERF_SCENARIOS.md
- Missile-loaded perf scenarios (missile-384/1024) — shipped — plans/MISSILE_PERF.md
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
- Armor and armor piercing bullets: Ships get armor value which reduces damage by small amount. Can make small weapons completely in effective. Large ships get more armor. Some weapons get reduced damage, but also armor piercing value which first decreases armor value. Large damage may still penetrate just by sheer amount of damage.

## Roadmap — future (new systems)

- Weapon evolutions: two tagged upgrades fuse into one stronger card
- Asteroid fields as terrain; procedural sectors from spawn/composition tables
- Elite enemies: mount/aura/hp modifiers on normal hulls
- Bosses: one huge hull, many mounts, phases as a small state table
- Deterministic replays before save/load
- 100k prep: SoA audit + a 50k scenario, before parallel sim (plans/PARALLEL_SIM.md)
- Mod-friendly stats: Domain/Stats tables exported to JSON/TOML

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
