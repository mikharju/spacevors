# START_SCREEN_ONBOARDING.md — a start screen a new player can read in five seconds

Status: shipped (2026-11-06) — what landed, the deviations and the verification are in
`## Shipped` at the bottom. The body below is the design as planned.

## 1. What this plan builds

Two changes to the ship-select screen (which **is** the game's start screen — `SpaceVorsApp.RunNormalPlay`
goes straight into `ShipSelectScreen`, there is no menu in front of it):

1. **A fixed instructions panel on the left** — how to fly, how to shoot, how to upgrade. Roughly
   ten short lines, key-caps on the left of each, readable at a glance and never scrolling.
2. **Cards that say what a ship actually is** — a role line, what its engines *do*, and what guns it
   starts with. Today the card says `HP: 32 · Radius: 46` and `Balanced engines · 2/3 turret mounts`,
   which tells a new player nothing: they do not know what a radius is for, what "Pursuit" means, or
   what the two numbers in "2/3" are counting.

No new screens, no new keys, no tutorial modal, no forced first-run walkthrough. The player can still
press `1` on the first frame and be flying.

## 2. Current state (read in code, not remembered)

- `src/Game/SpaceVorsApp.cs:51-76` — ship select is the start screen; game over + `R` returns to it.
- `src/Game/ShipSelectScreen.cs` — cards 600×300 centred, ship preview in a 300 px left panel, text
  column starts at `x + 320` and is therefore ~280 px wide; the only instruction text on the whole
  screen is one bottom hint line (line 102). Card body is two lines built at line 92-93.
- `ShipType.Description` (`src/Domain/Stats/PlayerShipStats.cs`) is **written but never rendered
  anywhere** — grep finds no read site. It is free real estate: it is exactly the per-ship blurb the
  card needs.
- `EngineLayout` carries `Name` ("Balanced", "Pursuit", "Slow", "Crab") and four thrust numbers plus
  `TurnRate`. The names alone carry no meaning; the numbers are where the meaning is
  (`PlayerShipStats.cs:18-24`): Pursuit is 7 side thrust against Balanced's 80, Crab is 210.
- Confirmed behaviour the copy must not lie about (`GameSession.cs:302-362`): the hull always swings
  toward the cursor at the hull's turn rate; W/S/A/D are hull-relative thrust; boost multiplies **W
  only**; there is no brake — S is reverse thrust, momentum is Newtonian.
- `Ui.cs` has `DrawPanel`/`DrawRoundedRect`/`WithAlpha`; there is no key-cap primitive.
- Layout headroom at 1920 px: the card block leaves ~660 px of dead space on **each** side. A 360 px
  panel on the left fits with room to spare and needs no change to the card layout.

## 3. Design

### 3.1 Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ SPACEVORS                                                                    │
│ Pick a hull. Your guns aim and fire themselves — you just fly.                │
│                                                                              │
│ ┌────────────┐   ┌───────────────────────────┐                               │
│ │ FLYING     │   │  [1] Scout                │  ← card list, unchanged centred│
│ │ 🖱 Mouse  turns the hull to the cursor│    │   and scrollable               │
│ │ [W][S]     ahead / reverse                 │                               │
│ │ [A][D]     slide sideways                  │                               │
│ │ [Shift]    boost ahead only                │                               │
│ │                                            │                               │
│ │ FIGHTING   │   │  [2] Fighter              │                               │
│ │ [Click]    lock a target — guns prefer it  │                               │
│ │ [Tab]      stats, guns, mounts (pauses)    │                               │
│ │ [1-5]      pick an upgrade when you level  │                               │
│ │                                            │                               │
│ │ You drift. Nothing brakes you: to stop,    │                               │
│ │ turn around and fire the engines back.     │                               │
│ │                                            │                               │
│ │ Survive. Kills give XP, levels give guns.  │                               │
│ │ First flight? The Scout forgives mistakes. │                               │
│ └────────────┘   └───────────────────────────┘                               │
│            Arrows/WASD or scroll to browse · Click or Enter to select         │
└──────────────────────────────────────────────────────────────────────────────┘
```

- The panel is a `Ui.DrawPanel` at `x = 32`, width 360, top aligned with the list viewport
  (`ListTopMargin`), so it reads as part of the list rather than floating.
- It does **not** scroll and does not take focus: it is decoration plus information. `Update()` is
  untouched.
- Hide it when the free space on the left of the card block is less than `PanelWidth + 2·Margin`
  (below ~1280 px window width) and lean on the existing bottom hint line instead. One boolean,
  computed in `Draw`.
- Fixed font sizes (14/16/18), like the rest of the screen. No `StatsScreenRenderer.UiScale` — the
  cards do not scale either, and scaling the panel would overflow its fixed width.

### 3.2 Panel copy (final — implement verbatim)

Key caps are drawn as small bordered boxes (`Ui.DrawKeyCap`), which is what makes the block
eyeballable. Two sections, six rows, three prose lines. Nothing else.

| Key | Text |
|---|---|
| Mouse | turns the hull to face the cursor |
| W · S | thrust ahead · reverse |
| A · D | slide sideways |
| Shift | boost — ahead only |
| Left click | lock a target: every gun prefers it |
| Tab | stats and turret mounts (pauses) |
| 1 – 5 | pick an upgrade at level-up |

Prose lines, in this order, under the rows:

- `You drift. Nothing brakes you — to stop, turn and fire the engines back.`
- `Kills give XP. Levels give guns. Survive as long as you can.`
- `First flight? The Scout forgives mistakes.` (accent colour)

The last line matches reality: `_highlightedIndex` starts at 0 and `Scout` is `All[0]`, so the card
that is already ringed is the one being recommended, and `Enter` starts it.

The rows live as one `static readonly (string Keys, string What)[]` in the copy file so PLAN.md's
Controls section can be checked against a single source instead than against scattered `DrawText` calls.

### 3.3 Card copy

Five short lines. The name and the role line are the two the player actually compares; the rest are
confirmation.

```
[1]  Scout                                                    ← 24 px, bright
     Small, quick, hard to hit — start here                   ← 16 px, accent
     32 hp · Armor 4                                          ← 16 px, bright, armor only if > 0
     Engines   Balanced — steady everywhere, quick to turn    ← 15 px, muted (wraps to 2 lines)
     Guns      Machine Gun ahead · Interceptor left flank     ← 15 px, muted (wraps to 2 lines)
     1 empty mount — room for another gun                     ← 15 px, muted, only when a slot is free
```

Changes against today's card:

- **`Radius` is gone.** A bare number with nothing to compare against. The preview already shows the
  hull; the size that matters is in the role line.
- **Engine name + blurb.** The blurb explains the engine, which is the ship's actual playstyle
  (the Crab hull *is* its engine).
- **Guns are named, with where they point.** This is the sentence that decides a pick: "guns all
  ahead" vs "answers in every direction" is a real difference at the start screen.
- **`2/3 turret mounts` becomes one line only when something is free** — the interesting fact is the
  empty mount, not the ratio.

### 3.4 Where each string comes from (no hardcoded per-ship tables in the renderer)

- **Role line** → rewrite `ShipType.Description` in `PlayerShipStats.cs` into these one-liners
  (it is unused today, so this is a content change with zero call sites to chase):

  | Ship | Description |
  |---|---|
  | Scout | Small, quick, hard to hit — start here |
  | Fighter | Medium hull, guns mostly ahead, hard brakes |
  | Heavy | An armored wall of guns — it does not dodge |
  | Shadow | Close-range brawler |
  | Falcon | Long thin hull: a small target with wide flanks |
  | Eagle | Armored gunship that answers in every direction |
  | Manta | Aims by sliding sideways — the best dodge in the game |
  | Raptor | Broadside guns — one big mount still empty |

- **Engine blurb** → a new `string Blurb` field on `EngineLayout`, beside `Name`, because that is the
  one place the four engines are defined (and `Name` already lives there as display text):

  | Engine | Blurb (matches the thrust numbers) |
  |---|---|
  | Balanced | steady everywhere, quick to turn |
  | Pursuit | fast ahead, hard brakes, no sideways slide |
  | Slow | heavy and slow to turn — armor buys the time |
  | Crab | slides sideways — it aims by drifting, not turning |

- **Weapon names** → `WeaponType.Name` is PascalCase (`MachineGun`). A ~4-line display helper in the
  Game layer turns it into `Machine Gun`. Deliberately **not** a new field on the 19 weapon types and
  not a Domain change — the same reasoning that keeps `DrawR/DrawG/DrawB` out of `WeaponType`.
- **Where a gun points** → a `switch` on the existing slot names
  (`TurretSlotName.MainGun` → "ahead", `SmallLeft`/`HeavyLeft` → "left flank", `SmallBack` → "aft",
  `SmallNose` → "nose"). Derived from data the hull already declares; nothing to keep in sync.
- **Empty mounts** → `ship.Slots.Count - ship.Loadout.Mounts.Count`, plus whether the free slot is
  Large (→ "room for a big gun"). Already computed on the card today.

### 3.5 Files to touch

- `src/Game/ShipSelectCopy.cs` (new, ~70 lines, static, stateless, no input): the hint rows, the
  card-line builder for a `ShipType`, the weapon-name and slot-position helpers. Copy in one readable
  file beats copy woven into a 224-line screen class.
- `src/Game/ShipSelectScreen.cs`: draw the panel, draw the card lines from `ShipSelectCopy`, widen the
  text column, cap the preview to the panel. Expected roughly break-even in lines.
- `src/Game/Ui.cs`: `DrawKeyCap(x, y, string label)` — a small rounded panel with centred text.
  Reused by the panel and the card digit badge.
- `src/Domain/Stats/PlayerShipStats.cs`: `EngineLayout.Blurb` + the eight rewritten descriptions.
- Card geometry: `ShipPanelWidth` 300 → 220, `CardWidth` 600 → 640, so the text column goes from
  ~280 px to ~400 px — needed for the engine and gun lines. The preview's fit cap changes from
  `CardHeight` to `ShipPanelWidth - 24` as `maxWidth`, so no sprite can bleed into the text column
  (`FittedWidth(ship, maxWidth, maxHeight)` already caps on both axes).

### 3.6 Optional extras (decide only after the two changes above are in front of you)

- **Honest relative hull size on the cards.** Today every preview is fitted to its own card, so the
  Heavy and the Scout look about the same size. One shared pixels-per-radius across the bench would
  make a Heavy read as a big target. Tradeoff: the Scout gets small (~half the panel), and the wide
  hulls (Falcon, Manta at R 111) set the scale, so this is a judgement call, not a free win.
- **First-flight hints in-game.** The panel disappears the moment you pick a ship. A single HUD line
  fading out over the first ~10 s of a live run — "Mouse turns you · W to thrust · Tab for stats" —
  closes that gap for ~5 lines (`GameSession` already has `_em.ElapsedTime`). Must be skipped when
  `_scenario is not null`, or it would show up in perf-scenario shots.
- **Difficulty tag per hull** ("easy / tricky / advanced"). Cheap to render, but it is another number
  a human has to keep honest as hulls change; the role line carries most of that already.

## 4. Explicitly out of scope

- No new screen or "Press any key" gate in front of ship select.
- No mouse-look inversion options, no remapping, no gamepad.
- No change to what any key does.
- No in-game tutorial, no forced objective text.

## 5. Verification

Menu path only — `ShipSelectScreen` is not reached by `--sim` (TROUBLE_SHOOTING #46) and not on the
measured render path of any scenario, so there is no perf verdict to earn here. Do:

1. `.tools/xvfb.sh` + `.tools/game.sh` as background jobs (recipe at the bottom of
   TROUBLE_SHOOTING.md), then `.tools/shot.sh select-onboarding`.
2. Crop both halves and check: panel text inside its box, no line overflowing the card, preview not
   bleeding into the text column, key caps legible. TROUBLE_SHOOTING #46's native-scale crop applies.
3. Resize the window to ~1100 px wide (`xdotool windowsize`) and confirm the panel is hidden and the
   bottom hint still reads — that is the branch that would otherwise never be seen.
4. Start a run (`bash .tools/input.sh key 1`) to confirm the digit shortcut still selects immediately
   with the panel drawn, and that game over → `R` returns to the same screen.
5. One `.tools/scenario.sh baseline-idle --sim --iters 3` run to confirm `host=sim` numbers did not
   move (they should not; it does not touch this code).

## 6. Docs to update in the implementation commit

- `PLAN.md` → Controls → "Ship selection (start)": add one line that the screen shows a Fly/Fight
  panel, and keep the wording matching `ShipSelectCopy`'s rows.
- `plans/START_SCREEN_ONBOARDING.md` → `Status: shipped`, a `## Shipped (date)` section noting what
  landed and any copy that changed on the way, plus a one-line entry under
  `PLAN.md` → Feature index → "Graphics and UI".
- `DIAGNOSTICS.md`: nothing — no new keys or env vars.

## Shipped (2026-11-06):

The two changes in §3 landed, plus the scope the request grew when it was approved: **the upgrade
screen and the turret-mount screen got instructions too**, and the start screen was restyled.

**Start screen** (`ShipSelectScreen`, `ControlPanel`, `MenuBackdrop`, `MenuCopy`, `Ui`):
- Header (`SPACEVORS` + one line + an accent rule), then the panel and the card list **centred as one
  group** rather than a column adrift in a wide window. The panel hides below 1160 px of window width
  and the list goes back to centred, as §3.1 allowed.
- The panel is the planned FLYING / IN A FIGHT rows with key caps, the drift line, the XP line and the
  Scout line. **Boost is not shown** — it is a candidate for removal, so the screen does not teach a
  control the game may stop having. Shift still works in flight and PLAN.md still lists it.
- Cards: name + key cap, a role line (`ShipType.Description`, rewritten), then HULL / ENGINES / GUNS /
  MOUNTS. `Radius` is gone; the engine blurb is a new `EngineLayout.Blurb`; gun names and directions are
  derived from the loadout and the slot names.
- Extras that were not in §3 and came out of "make it look nice": the parallax **starfield** behind the
  menu (`MenuBackdrop`, own fixed seed so a world's RNG is untouched), the card list **clipped to its
  band** with a scissor rect so a card no longer draws through the hint line, and **mouse-hover
  highlighting** (the highlight follows the cursor only while it moves, so it cannot fight the keyboard).
- **A and D no longer browse the list.** They are the hull's strafe keys; a start screen that moved the
  list with them taught the wrong thing. Only Up/W and Down/S browse.

**Upgrade screen** (`UpgradeMenuRenderer`): a three-line block above the bundle row — one bundle taken
whole, strong card on top, Tab reads the ship while the game waits — plus a fourth line when the hand
holds a new gun. It is shown through **level 3 only**: past that a stats panel grown by the ship's guns
could sit under it, and a player who has taken three bundles has the rule. The bottom hint became
"Click a bundle or press its number". Card labels now name a gun the way the rest of the game names it
("Machine Gun range", "Interceptor Turret attack speed", "pickup radius"); the bespoke "side shot attack
speed" wording and the `GenericWeaponLabel` helper are gone rather than translated.

**Mount screen** (`MountMenuRenderer`, `GameSession.MountsHint`): a rules panel on the right (click a
ringed mount, its number, C to cargo, right-click to drop, Esc to leave, and "a cargo gun does not
fight"), and the contextual bottom line shortened to what to do *now*. Cargo rows show two-word gun
names, as do the stats panel's weapon headings.

**Verification.** `.tools/shot.sh`: `onboard-clean` (1920, real ships), `onboard-select2` +
`onboard-panel-crop` + `onboard-card-crop` (diagnostics list, native-scale text), `onboard-narrow`
(1100 px: panel hidden, hint clear), `onboard-hover2` (hovered card takes the highlight),
`onboard-upgrade2-crop`, `onboard-mounts2-crop`. Em dashes rendered as "?" (§79 is the new entry), so
every new line of copy uses a colon, a comma or `·`.

**Perf verdict (2026-11-06): nothing to earn, and nothing moved.** No menu screen is on a measured
path: `--sim` has no `GameSession` (§46) and the select screen is not in any scenario's render pass. One
`scenario.sh baseline-idle --sim --iters 3` run confirms it — median **0.32 ms** against the 8.33 ms
budget, 0 of 357 frames over, budget pass (no earlier `baseline-idle` row exists in `index.tsv` to
diff against; it is the cheapest scenario either way). Full `dotnet test`: 660 passed; the 4
`ScenarioPerformanceTest` failures were the background game stealing CPU on this machine — the filtered
re-run with the game stopped passed 18/18, which is §28 exactly.

**Left out, on purpose** (still open from §3.6): honest relative hull size across cards, a fading
first-flight line in the HUD, and per-hull difficulty tags.

## Follow-up (2026-11-06): text scales with the window, and the hull previews share one scale

Both asked for after the first pass was in front of the player.

- **Scaled text.** The screen's sizes are design pixels at a 1920-wide window, multiplied by
  `StatsScreenRenderer.UiScale(windowWidth)` (1x at 1920, up to 2x), so the header, the panel, the card
  text and the hint grow with the screen instead of staying 16 px on a 4K display. A card is a
  proportional unit, so the list, the scroll math and the hit rects follow the same factor. A short
  window shrinks a card instead of hiding half of it, down to `MinScale` 0.6, below which the list just
  scrolls. The panel already took a scale (the mount screen passes its own), so this made the start
  screen the last fixed-size UI on the bench.
- **Slack, because the font rounds.** The default bitmap font rounds each glyph advance up as it grows,
  so copy that exactly fits at 1x wraps at 1.33x: the card is 640 → 680 wide and the panel 420 → 440 to
  leave ~15% over. Recorded with the font's other quirk in TROUBLE_SHOOTING #79.
- **Honest hull sizes.** Every preview used to be fitted to its own panel, so the Heavy and the Scout
  looked the same size. They are now drawn at the game's own rule — `Radius * 2` wide, the width
  `WorldRenderer` uses in flight — at **one scale for the whole bench**, chosen so the largest hull
  (Manta/Falcon, R 111) still fits the panel in width and height. So the Scout draws at 46/111 of it and
  the Heavy at 84/111. Each sprite carries its own transparent margins, so the paint alone still
  understates the gap; the card now also draws the hull's **bounding circle** in its own colour at the
  same scale, which is both the honest comparison and the radius a hit is judged against in flight.
  §3.6's "honest relative size" tradeoff is real and accepted: small hulls look small, and that is the
  information.

Verification: `size-2560-top` / `size-1920` (same screen at 1.33x and 1x — text and cards proportional,
no wrapped-line surprises), `size-2560-bottom` (Manta against Raptor against Heavy: the size order
reads off the circles at a glance), `scale-*` for the pre-slack state. `dotnet test` 664 passed;
`scenario.sh baseline-idle --sim --iters 3` median 0.32 ms, identical to the row above, budget pass —
still no measured path touched.

The scaled path was then checked on the two other screens the block lives on, at 2560x1440 (scale 1.33):
`scale-upgrade-2560` — the bundle row, both stats panels, the hint and the teaching block all grow
together, and the block still sits clear of the stats panel above the cards; `scale-mounts-panel-2560c`
— the MOUNTING panel scales too and every row stays on one line. Getting there cost two near-misses
documented in TROUBLE_SHOOTING: `Escape` quits the game outside the mounts screen (entry 80), and click
coordinates must be measured in the scaled window, not carried over from 1920 (entry 6).
