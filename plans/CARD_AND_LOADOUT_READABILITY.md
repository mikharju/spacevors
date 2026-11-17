# CARD_AND_LOADOUT_READABILITY.md — what this card gives, and which gun sits where

Status: shipped 2026-11-17 — presentation only, no deal and no grant changes (see `## Shipped` below). Builds on plans/PREUPGRADED_NEW_WEAPONS.md
(shipped 2026-11-14) and on plans/SUPER_UPGRADES.md (**shipped 2026-11-15**, `80b75e3`), whose landed
primitives this plan now reuses instead of proposing its own: read §5 before touching a card renderer.
Re-checked against that commit — every finding in §1 still holds, and two got sharper.

## Part 1 — the plan

### 1. The problem

**A pre-installed upgrade does not read as a gift.** It is a comma tail inside the card's *label*
(`new weapon Rail Gun — with rate, damage`): 17 px muted text, the same line that carries the gun's name,
in a third vocabulary. Three vocabularies for six stats today — the card label says `rate`, a stat card
says `attack speed`, the stats screen says `Fire rate`. And the number the spec block prints
(`range 1500`) is the *arriving* number: nothing on the card says the gun's base is 1000, so the gift is
invisible to anyone who does not already know the table.

Two hard failures behind it, both measured in the committed layout:

- the arriving set lives in the label, and the label is capped at `MaxUpgradeLabelLines = 3` lines with
  the extra lines **dropped silently**. A four- or six-stat set (what a milestone hands out) can be cut
  mid-list — a card that stops short of the truth, against R10.
- a new-weapon entry with a 3-line label and its two spec rows needs 57 + 10 + 26 + 8 + 33 = **134 px in a
  100 px half** (card 200, two halves; label 17 px at 20 px pitch, value 26 px, specs 15 px at 18 px). The
  centring pushes the block up and down across the divider, into the other half of the bundle.

Both numbers were re-read against `80b75e3`: `UpgradeCardHeight 200`, label 17 / value 26 / spec 15 px,
`MaxUpgradeLabelLines = 3` — the super change drew *over* those blocks and changed none of them, so the
overflow and the silent cut are still there, latent, waiting for the next plan that needs a word.

**The turret loadout screen has no identity.** Every ring carries only a number (`DrawNumberBadge`); the
gun's name appears nowhere on the hull. The stats screen does name a mount, but prints the internal slot
id (`heavy_left · 120°`) — so the player takes a gun off to find out what was where, which is the
complaint. `MenuCopy.MountPosition` already has the human words — *ahead, left flank, right flank, aft,
nose* — and they are used only on the ship-select cards.

**A hand still reads flat, super included.** The grade arrived as drawing — a lime band wash, a lime left
edge, a lime value and a small `SUPER` tag (`Palette.SuperStep/SuperBand/SuperEdge`) — which is the right
call: the spike is visible before a word is read. What it does not give the player is *magnitude*. A `+15 %`
card and a `+30 %` card are two numbers to compare across five bundles, and neither says what the gun does
today, nor how close that stat is to its ceiling.

**The super work exposed those hazards rather than fixing them.** It marked with colour precisely because the
label line had no room left — the same 3-line cap this plan removes — and `UpgradeLines` grew a fourth
sentence to explain the colour, so the screen now needs a legend for its own cards. With the `base → after`
pair the lime half reads `attack speed 1.8 → 2.3 /s` where the ordinary half reads `1.8 → 2.1 /s`: "twice the
step" becomes something you see instead of something you are told.

### 2. The insight

Two one-liners carry the whole feature:

1. **Every number on a card is printed as `base → after`.** The `after` value is produced by the same
   Domain call the grant uses — `WeaponUpgrade.ApplyStat(live, card)` and `ArrivingTurret(gun, mask)`, with
   the step carried by `card.Step` — so a printed number cannot disagree with what the player gets, and the
   pre-installed set, an ordinary stat card and a super card all take one row shape.
2. **Every mounted gun is named the same two-part way everywhere: position + size.** `left flank · large`,
   from one table shared by the hull rings, the mount legend, the stats screen and ship select.

Both come out of Domain facts the game already computes. Neither adds a rule, a number, or an RNG draw.

### 3. The decisions

**D1 — one vocabulary, and it is already per-gun.** A stat's word depends on the gun and on the card:
`FireRate` is *attack speed* on a fixed-volley gun and *volley size* on one whose volley grows, decided by
`GrowsVolley(option)` — the same predicate the grant and the deal's gate use, so the word cannot drift from
the effect. That table exists in `GetUpgradeLabel`; what this plan adds is its missing half — one
abbreviation per stat for tight spots — and deletes `ShortStat`'s ad-hoc `rate` / `proj speed` / `round hp`
trio instead of keeping it alongside. The stats screen's row titles (`Fire rate`, `Range / bullet life`,
`Armor pierce`) are the third dialect today and fold into the same table. One new row follows: a guided gun's
**round hit points**, which `MissileHealth` cards buy and no screen currently shows.

**D2 — the arriving set leaves the label and becomes a strip.** A new-weapon card reads: gun name (never
broken) · size cap · `arrives with` + one cap per arriving stat. Label of that half is just
`new weapon · Rail Gun`. The set can no longer be truncated away, because it is no longer wrapped as prose:
the strip drops to one cap per line, then to abbreviations, before anything is lost.

A cap carries the stat's D1 word **at this card's step**, and an arriving set is always one *ordinary* card
per stat: on a volley gun the arriving `FireRate` buys a shot, so its cap reads `volley size +1 shot`, not
`attack speed +30 %`. `WeaponUpgrade.Apply` applies ordinary cards on purpose — a cap promising a faster
trigger on a volley gun is the lie R10 refuses, and a test pins it the way `LegalStats` pins the deal.

**D3 — print `base → after` for every number that changes.** `range 1000 → 1500` and `damage 60 → 90` on a
new-weapon card, `range 1150 → 1322` on a stat card, `armor 4 → 6` on a defence card. No arrow when nothing
changes. The after-value comes from the grant's own function — `WeaponUpgrade.ApplyStat(live, card)` for a
stat card (it now takes the **card**, so grade, volley-or-rate and percent-of-base travel with it) and
`WeaponUpgrade.ArrivingTurret(gun, mask)` for a new gun — never from arithmetic on a printed delta. That is
what makes the lime half self-explanatory: two cards for one gun print the same left number and different
right ones, and `SUPER` explains the difference instead of having to.

**D4 — show how full the gun's build is.** A pip row: one pip per stat this gun can legally take —
`WeaponUpgrade.LegalStats(gun, live, pierceUnlocked, CardPower.Base)` already answers that, now at a power —
filled for the stats the card arrives with. It makes "the same gun, a different build" visible at a glance, it
shows the ceiling without a sentence, and a spiked new gun (their D5: arriving with `least`, not `⌈least/2⌉`)
reads as more filled pips for free. The stats screen already prints a per-stat *count* column out of
`UpgradeCounts`; the pips are that same fact in one line, so the pip count is derived, never stored.

**D5 — caps stay neutral; the meanings already claimed stay claimed.** Meanings, not colours, are the scarce
thing on this screen: cyan `Accent` is "click this" (hover, an ordinary value), lime `SuperStep` is "this half
is spiked", the mount screen's green is "this gun fits here", and green, gold and magenta stay off hulls for
their own reasons (Palette header). So pre-installed caps and filled pips are neutral — `TextBright` text on
the existing key-cap fill, empty pips in `TrackDark` — and this plan adds no colour at all. A spiked
new-weapon card then says two independent things: the band and the `SUPER` tag say *bigger*, the neutral caps
say *what it already carries*.

**D6 — fix the fit instead of shrinking the type.** The bundle's two halves stop being 50/50: the split
follows the content (`height × 0.55 / 0.45` when one half carries a new-weapon block, else 50/50), and an
entry is measured before it is drawn, with an explicit drop order when it still will not fit: **spec rows →
abbreviate the strip → one stat per line**. The gun's name and the arriving set are never dropped. The silent
line cap is replaced by "shrink by the rules above, then say `+N more` in the strip's own words". The super
marking rides through untouched: `DrawSuperBand`/`DrawSuperTag` are drawn over a *half's* rect, so a moved
divider moves them with it, and the strip lives at the bottom of a half — the `SUPER` tag keeps its corner.

**D7 — the loadout screen names every mount.** Each ring gets a label next to its number: `1 · Rail Gun`,
or `3 · empty` dimmed. Below or opposite the hull, a **MOUNTS legend** lists every mount in the fixed
number order: `1  main mount · large · Rail Gun · 360°`, `2  left flank · large · empty`, … — hover a
legend row and its ring lights up, hover a ring and its row lights up. The numbers never change
(existing rule); the legend just makes them mean something.

**D8 — the stats screen stops printing internal ids.** The `Mount / firing arc` row prints
`left flank · large · 120°` instead of `heavy_left · 120°`, from the same table as D7. The internal id
stays in the logs, where it is useful.

**D9 — say what is in hand and why a ring did not take it.** The screen states `in hand: Rail Gun · large`
where the held gun is (cargo header row), not only in the bottom hint. Hovering a ring that cannot take
the held gun says why (`needs a large mount`), and hovering a filled one that can says what happens:
`swap: Rail Gun ↔ Machine Gun → cargo`. Cargo rows keep the existing "a cargo gun does not fight" wording
rather than relying on dim colour alone.

**D10 — the same `base → after` shape on the stat cards.** `Machine Gun attack speed +30 %` grows a small
second line `×1.00 → ×1.30 fired/s` or, cheaper, `2.4 → 3.1 shots/s`. This is the one proposal here that
buys legibility on *every* card, not just the new-weapon ones, and it is what makes SUPER_UPGRADES'
`+30 %` vs `+15 %` comparison instant.

**D11 — nothing about the hand changes.** No deal, no grant, no ceilings, no card count, no click model.
If a row cannot be drawn, the card is drawn without that row — a card never lies, and never becomes a
different card because of its layout.

### 4. Not doing

No new fonts, no sprite art, no animation, no icon set (caps and pips are drawn from existing primitives).
No new colours beyond `Accent`/`TextBright`/`TextMuted` and the cap fill. No re-layout of the stats-screen
panels beyond the mount row's wording. No hover tooltips that need a text-measure pass per frame beyond the
one `Prepared` already does. No change to what is dealt, so no balance risk and no new tests of the offer.

### 5. What shipped while this plan waited — and what it must reuse

plans/SUPER_UPGRADES.md shipped as `80b75e3` (suite **778**, 12 new tests). It landed the primitives §2 asked
for, so this plan no longer proposes them; it reads them:

- `UpgradableOption.Step` — the stat definition at the card's tier **and** grade — plus `.IsSuper`,
  `.SalvoShots`, `UpgradeDefinition.ScaledBy/Doubled`. One value is printed by the card, gated by the offer and
  granted by the game, so D3's after-value is `ApplyStat(live, card)`: the card carries its own step.
- `WeaponUpgrade.ApplyStat(Turret, UpgradableOption)` — it now takes the **card**, not the stat. Anything
  written against the old `(turret, stat)` shape is stale, and the display must not resurrect it.
- `WeaponUpgrade.LegalStats(gun, live, pierceUnlocked, CardPower)` and `MissileHealthStepFor(power)`, for D4.
- `Palette.SuperStep / SuperBand / SuperEdge`, `DrawSuperBand` / `DrawSuperTag`, `MenuCopy.UpgradeSuperLine`,
  `UpgradeMenuRenderer.GrowsVolley(option)`. The grade's look and the volley word are theirs: this plan styles
  nothing about the spike and renames nothing about the volley.
- Their deviation 1 is the best argument for this plan: they marked with colour *because* the label had no room
  left, and the screen gained a sentence to explain the colour. Taking the arriving set out of the label is what
  lets the cards explain themselves — and it stops the next plan hitting the same wall.

Two rules so the shipped work is not disturbed: the row model keeps `DrawEntry`'s super band, edge, tag and
value-colour calls exactly as they are, and it does not touch plans/SUPER_UPGRADES.md — that file belongs to
the agent that shipped it (see the ownership note under *Docs*).

## Part 2 — implementation memo

### Where the code goes

- `src/Game/MenuCopy.cs` — add `StatName(UpgradeOption)` / `StatAbbrev(UpgradeOption)` (the D1 table) and
  `MountLabel(TurretSlotDefinition)` → `position · size` built on the existing `MountPosition`. Plain
  string functions, no Raylib types in the signature, so a test can pin them.
- `src/Game/UpgradeMenuRenderer.cs` — `DrawEntry` stops computing three free-floating blocks and instead asks
  for `CardRow[]` from a new `UpgradeCardCopy.Rows(UpgradableOption, Turret liveOrNull)`: `name`, `strip`,
  `numbers`, `pips`. `liveOrNull` is the turret the card acts on — the live one for a stat card,
  `WeaponUpgrade.ArrivingTurret(gun, mask)` for a new gun — so every printed number is one Domain call deep
  rather than a re-derivation. The builder returns **text and heights only**; the renderer keeps the fade, the
  super band and the tag. Each row is drawn by one small function, the height is the sum, and D6's drop rules
  run while building the rows, not while drawing them.
- `src/Game/MountMenuRenderer.cs` — `SlotMarker` gains `PositionLabel` and `SizeLabel`; `Prepared` gains
  `LegendRow[]` (number, mount label, gun label or `empty`, arc degrees, plus the rect for hit-testing) and
  `GetHoveredLegendIndex`. `DrawCargo` marks the held row with the words from D9.
- `src/Game/StatsScreenRenderer.cs` — the mount row's value string via `MenuCopy.MountLabel`, plus the one new
  row D1 asks for: a guided gun's round hit points, drawn from `turret.Weapon.MissileHealth` and counted through
  the existing `CountOf(..., UpgradeOption.MissileHealth, weaponName)` — the stat a `MissileHealth` card buys and
  no screen shows today.
- No Domain change is needed for the display. `WeaponPreUpgrade`, `WeaponUpgrade.StatsOf`, `LegalStats`,
  `ArrivingTurret`, `TurretMount.Fits` and the slot definitions are already the right facts. If
  `UpgradeCounts` needs a per-gun "how many steps of this stat" readout for a future tooltip, that is a
  separate decision, not part of this plan.

### Invariants to keep

- **R10 (a card is never a lie)**: the after-value is the value the grant produces, so it is obtained by
  calling the grant — `ApplyStat(live, card)` on a copy, `ArrivingTurret(gun, mask)` for a new gun. Never by
  arithmetic on a printed percent: a `Damage` card has no single percent (it is a share of the gun's base), and
  an arriving set applies *ordinary* cards even when the card carrying it is spiked, so its caps print the
  ordinary step.
- One name per stat, defined once (D1). A grep for `"rate"` and `"proj speed"` must come back empty after
  this lands.
- The mount number a key press maps to never changes (`SlotMarker.Number` is deal-order, and the legend
  uses the same order).
- The display draws no RNG and reads no clock: the pip row and the strip come from the card, not from a
  roll. Determinism tests are unaffected.
- Domain stays layout-blind: no width, no font, no `Scale` below `src/Domain`.
- No magic numbers: the split ratio, cap padding and pip size are named constants in the renderer that owns
  them, per the existing style of that file.

### Code shape notes

- `Ui.WrapText` + the `MaxUpgradeLabelLines` cap is the trap that truncates silently today. When the strip
  becomes its own row, keep a hard "never drop the gun name or a stat the card grants" assertion in the row
  builder, and end the strip with a worded overflow (`+2 more`) rather than a cut.
- The half-split change (D6) must not move the divider under the key badge, and the card's total height stays
  `UpgradeCardHeight` so `GetUpgradeCardRect`, `HintGap` and the reroll panel's slot stay where the
  collision comments in `UpgradeMenuRenderer` say they must be.
- Hover cross-highlight (D7): one hovered index for rings and one for legend rows, both from the same
  `Prepared`; the draw pass lights both when either points at the same slot. No new per-frame ECS walk —
  `Prepare` already iterates the slots once.
- `ControlPanel` is tempting for the legend and wrong: its rows are key-cap + text, and the legend wants
  four columns. Draw the legend as its own small table from `Prepared.LegendRow`s and reuse only
  `Ui.DrawPanel` / `Palette`.

### Tests

Small, deterministic, no window (the renderers' *strings* are testable; their pixels are not):

- `MenuCopy` stat words cover all six stats **and both `FireRate` spellings** — "attack speed" on a
  fixed-volley gun, "volley size" on one whose volley grows — and `MountLabel` covers every slot name in
  `TurretSlotName`, including the two that share a position word, which is why size is part of the label.
- `UpgradeCardCopy.Rows` for: (a) a stat card on a fixed-volley gun, (b) the super version of that same card —
  same left number, different right one — (c) a new-weapon card with an empty arriving set (no strip, no
  arrow), (d) a new-weapon card arriving with `FireRate+Damage` **on a volley gun**, where that cap must read
  `volley size +1 shot`, and (e) the widest arriving set the offer can deal, asserting the name line and every
  arriving stat survive. Cards are built with the current constructor, `CardPower` included.
- A fit assertion in the same test file: rows built for the narrowest card (`UpgradeCardMinWidth` scaled)
  fit their half height — the arithmetic from §1 written down so the overflow cannot come back.
- Existing Domain tests are untouched by wording — `FormatChoice` and the bench `+Stat`/`*` syntax are Domain
  strings, not card text — but the suite is 778 now, and `WeaponUpgradeTest`/`PreUpgradedNewWeaponTest` were
  retargeted to the card-taking `ApplyStat`. A display change must not reintroduce the old `(turret, stat)`
  overload just to make an expectation compile.

### Measurement

The sim gate cannot reach a paused screen, so `scenario.sh … --sim` proves only that nothing else moved: run
`ammo-spray --sim --iters 3` once before and once after and expect the median/p99 to be unchanged; any movement
means the change escaped the paused screens. Compare against the last row of `.tools/run/index.tsv` — as of
`80b75e3` that is median **1.39 ms**, p99 **3.92 ms**, 357 frames — and build through the scratch-pin recipe of
TROUBLE_SHOOTING #86, since the committed `global.json` pin (10.0.404) is unreachable in a sandbox without that
SDK. The before shot of a hand with spikes in it is `.tools/shots/super-cards.png`.

Text-only changes: check the before/after shots with `.tools/diff.sh`. A frame cost above the `ui.panel`
baseline of more than a few percent means the row model is measuring text per frame — move the measuring
into `Prepared` and cache the rows for the frame.

### Docs when it lands

`plans/CARD_AND_LOADOUT_READABILITY.md` → `Status: shipped` + `## Shipped (date)`; one line in the PLAN.md
feature index; a dated `## Follow-up` in plans/PREUPGRADED_NEW_WEAPONS.md (its D4 label wording is superseded
by the arrival strip) and in plans/TURRET_LAYOUTS.md (the mount legend and the mount wording). **Not**
plans/SUPER_UPGRADES.md: another agent owns that file and the multi-agent rules forbid touching it, so anything
this plan changes about how a super card displays gets recorded in *this* plan's `## Shipped` for the human to
relay. No PLAN.md detail, no ARCHITECTURE.md change — nothing here is a new invariant beyond R10, which already
covers it.

## Shipped (2026-11-17) — code `c56b2da`

**What landed.** One vocabulary and one row model.

- `MenuCopy` owns the words: `StatWord` / `StatAbbrev` (`attack speed` or `volley size` for `FireRate`
  depending on whether the gun's volley grows; `round hit points` for `MissileHealth`), `SizeWord` and
  `MountLabel` (`ahead · large`, `left flank · small`, `aft · small`). The mount screen, the stats screen and
  the cards now read from it, so the stats screen no longer prints `heavy_left`.
- New `src/Game/UpgradeCardCopy.cs` builds a card's **text** headless — name, step, the arriving caps in full
  and short words, the numbers before and after, and the pip counts — so `src/Tests/CardReadabilityTest.cs`
  pins it without a window. A gun's pair comes from `WeaponUpgrade.ApplyStat`; a hull card's pair from the same
  multipliers and ceilings `GameSession.ApplyUpgrade` writes.
- `UpgradeMenuRenderer` draws rows (name → step → caps → pips → numbers); the bundle's split comes from
  `UpgradeCardCopy.SplitHeight` (D6's content-driven half), and `FitRows` decides what the half can hold.
  `MaxUpgradeLabelLines` and `MinCardWidthForStats` are deleted, `GetUpgradeLabel`/`GetUpgradeValue`/
  `NewWeaponSpecs`/`ArrivingNames`/`ShortStat` with them.
- `MountMenuRenderer` labels every ring with the gun in it (`empty` when not), adds the **MOUNTS** legend under
  the cargo list with hit rects that light the ring they stand for, states `(in hand)` in the cargo row, and on
  hover says what a click there would do (`swap with Machine Gun`, `needs a large mount`).
- `StatsScreenRenderer`: `Mount / firing arc` prints the mount's name, `Fire rate` became `Attack speed`, and a
  guided gun's section gained the **Round hit points** row (gated on `WeaponType.Homing`, the Domain rule).

**Deviations from the plan.**

1. `→` became `->` and every separator is `·`: the default font has no dash-of-em glyph and draws `?`. The pips
   are drawn as small squares for the same reason.
2. D7 said "a label under the ring, on hover". Ring labels are under **every** ring — the complaint was that
   all four positions were unknown at once, and at the scales the screen draws at they do not collide. The
   hover-only part is the note.
3. D6's drop order runs exactly as planned (numbers rows → shorten the caps → drop the pips), which in practice
   means the widest arriving set keeps name, size, caps and pips and loses its arriving number rows. Measured,
   not assumed: with the font's real row heights the four rows fit a 355 px card's busy half; the numbers go.
4. The row model budgets with the **font's drawn height** (`MeasureTextEx`), not the point size: Raylib's
   `MeasureText` returns a width only. A layout built on the point size put the last row below the panel edge.
5. `DesiredHeight` / `FitRows` measure per frame (~30 `MeasureText` calls for five cards). Measured instead of
   guessed: `ui.panel` 0.49–1.00 ms with the cards up, so no per-frame cache was added.

**Bugs found on the way, fixed here.**

- A `MissileHealth` card printed its step as **`+0`**: `UpgradeDefinition.DisplayValue` prints the additive, and
  an R33 percent-of-base card has none. The card now prints `+50%`.
- The new-weapon label joined its arriving set with an **em dash**, which the default font renders as `?`.
- The old label cap silently **dropped** the tail of a wide arriving set, while its spec block ran past the
  divider into the other half of the card — the pair of problems §1 described, confirmed in a screenshot.

**Perf (2026-11-17).** `ammo-spray --sim --iters 3` through the scratch-pin recipe (#86): median **1.47 ms**,
p99 **3.09 ms**, 0 of 357 frames over the 8.33 ms budget, against this session's before-run 1.33 / 2.76 — same
`allocKbPerFrame.mean` 277.7 KB in both, so nothing in the sim path moved; the spread is box noise, and the sim
never draws these screens (a run taken with the game window open read 4.48 ms, which is what contamination
looks like). In-window at 1920×1024 with llvmpipe: `ui.panel` 0.49–1.00 ms on the card screen, `ui.hull`
0.35–0.62 ms on the mount screen. **Keep.**

**Tests.** `src/Tests/CardReadabilityTest.cs`, 14 tests: one word per stat with no leftovers of the old three;
every mount of every hull labelled and the two flank mounts distinguishable; an arriving `FireRate` on a volley
gun capped as `volley size +1 shot`; the widest arriving set keeping all six caps and the gun name; an ordinary
and a super card of one gun keeping the same left number and different right ones; no arrow when the mask does
not move the stat; and for every hull-wide card, the printed after-number equals what `GameSession.ApplyUpgrade`
leaves in the component. Suite: 774 green with `Performance` excluded, on SDK 10.0.112 (#86).

**Not touched:** `plans/SUPER_UPGRADES.md` — another agent owns it. Recorded here for relay: the super half's
marks (band, lime left edge, lime value, `SUPER` tag) are exactly as that plan shipped; a super half now also
prints its before/after pair, so the gap between an ordinary and a super card of one gun is readable as numbers.
