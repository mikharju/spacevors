# UI_POLISH.md

Status: shipped (verified 2026-10-31)

Proposal: make the UI plain, elegant, pleasant — no new bitmaps, no fancy effects.
All techniques are raylib immediate-mode primitives (rects, circles, gradients, alpha).

## Status: done (2026-09-16)

Implemented style B (soft depth) with picks 1b, 2a, 3a, 4a, 5, 6, 7a in three
commits (HUD + game over; menus; targeting). Deviations from the plan:
- The upgrade menu **already dimmed** the world (`RenderPaused` → `StatsScreenRenderer.Draw`
  dims in both modes) — the plan's "not dimmed" claim was wrong; no extra dim was added.
- XP bar reads `Player.Xp` against `UpgradeStats.XpForLevel(Level)`.
- Fade state is renderer-local: game-over fade resets via `Renderer.Render` when not
  game over; card fade resets via `UpgradeMenuRenderer.ResetFade()` from the stats
  screen's no-cards branch.
Visual verification pending a display (TROUBLE_SHOOTING.md #25).

## Current state (verified in code)

- **HUD health bar** (`HudRenderer`): opaque grey rect, hard green→red switch at 1/3,
  1px light-grey border, hp text inside the bar.
- **Game over**: bare white "GAME OVER" text over the live scene; no restart hint shown.
- **Targeting** (`TargetingRenderer`): 2.5px corner brackets + always-on 4px hp bar.
- **Upgrade cards** (`UpgradeMenuRenderer`): flat opaque rects, 1px blue border, square
  key badge; **the world behind is not dimmed** (stats screen dims, upgrade menu doesn't).
- **Stats screen**: dim + panel + rows — already the most polished screen.
- **Ship select**: flat list rows, visible scrollbar track.
- **No XP/level indicator in the HUD at all** (level only visible on the stats screen).
- Colors are ad-hoc per renderer; text is pure white; corners square everywhere.

## Design language (applies to all alternatives)

Small, cheap rules that do most of the work:

1. **One accent color** (soft cyan `~(120, 200, 230)`) used only for: hover, key
   values, selection. Everything else neutral greys.
2. **Never pure white/black**: text `(230, 235, 245)`, muted labels `(150, 160, 175)`,
   panels translucent near-black `rgba(10, 12, 20, ~200)` so stars faintly show
   through — reads as "space UI" for free.
3. **1px borders everywhere** (brackets are 2.5px today), consistent padding rhythm
   (8/12/16/24 scaled by the existing `scale`).
4. **Soft depth**: 2px offset translucent-black shadow behind panels/cards.
5. **Gentle motion**: displayed values ease toward real values (hp bar), menus fade
   in over ~150 ms, hover states brighten instead of changing hue. All renderer-local,
   no domain changes.

New helper `src/Game/Ui.cs` (~60–80 lines): `DrawRoundedRect` (2 rects + 4 circles),
`DrawPanel` (shadow + translucent bg + 1px border), `EaseToward` for smoothed values,
color constants moved into `Palette`. No textures, no dependencies.

## Style alternatives (pick one)

| | A. Flat & quiet | B. Soft depth (recommended) | C. Hairline |
|---|---|---|---|
| Idea | Today's look, just made consistent: unified colors, 1px borders, one accent, no squares-vs-round mismatch | A + rounded corners, translucent panels, soft shadows, eased values | Ultra-minimal: thin lines and text only, no fills |
| Cost | ~1 h, touches colors only | + helper file, all renderers | full redesign of panels |
| Risk | still feels plain | none notable | poor contrast over starfield/explosions |
| Fit | smallest diff | "plain, elegant, pleasant" | too austere for a busy arcade game |

## Element-by-element alternatives

### 1. HUD health bar
- **1a. Refined bar**: rounded, translucent bg, green→amber→red *lerp* (today it hard
  switches), eased width on damage, hp text moved outside-right in muted color.
- **1b. Segmented bar (recommended)**: hp is a small integer (8–20) — draw one segment
  per hp point with a 2px gap; lost segments fade out over ~0.3 s instead of snapping.
  Discrete pips read instantly and feel crafted; no text needed (keep muted "8/8"
  beside it optionally).
- 1c. Ring around the player ship — rejected: collides visually with targeting
  brackets and hull damage graphics.

### 2. XP / level presence (currently missing)
- **2a. Thin top-edge xp bar (recommended)**: full-width 3px translucent track at the
  very top, accent fill, "LV 7" in small muted text at the right end. Classic
  genre idiom, zero clutter, answers "how close am I to the next upgrade?".
- 2b. Corner text "LV 7 · 340/500 xp" — simplest, but no at-a-glance progress.
- 2c. Nothing (status quo) — fine if you consider xp pacing private info.

### 3. Targeting brackets
- **3a. Polish only (recommended)**: 1.5px arms, manual bracket gets a very subtle
  alpha pulse (230↔255, ~1 Hz) so the manual lock reads without shape change; hp bar
  only when hp < max (reduces clutter, standard genre behavior), rounded, 3px.
- 3b. Rotating manual bracket — rejected as too flashy per request.
- 3c. Replace hp bar with segment pips like 1b — consistent, but too many pixels on
  small enemies; keep the thin bar.

### 4. Upgrade menu cards
- **4a. Dim + soft cards (recommended)**: add the missing full-screen dim
  `rgba(0, 0, 0, 120)` behind the cards (stats screen already does this — consistency
  and a big focus win); rounded cards, soft shadow, hover = 4px lift + shadow grows +
  border → accent; value text in accent color, label in muted grey; key badge rounded.
- 4b. Borderless cards (shadow-only, Material-style) — cleaner but hover affordance
  needs the lift animation to carry it.
- 4c. Cards slide up + fade in on open — nice, but keep to fade only (slide risks
  feeling "fancy").

### 5. Stats screen
- Already close: rounded panels, thin 1px rule under section headers, alternating row
  tint `rgba(255,255,255,6)`, title as white ship name + accent "LV n". No layout
  changes.

### 6. Ship select
- Rounded rows; selection = 3px accent stripe on the row's left edge (instead of only
  a lighter fill); scrollbar: drop the track, keep a rounded translucent thumb only.

### 7. Game over
- **7a. Fade overlay (recommended)**: over the 1.2 s death animation the scene already
  plays; when game over fires, fade a `rgba(0,0,0,140)` overlay in over ~0.5 s,
  "GAME OVER" in white, thin accent rule under it, and a muted **"Press R to
  restart"** hint below (currently not shown anywhere — usability win for free).
- 7b. Keep bare text — rejected: floats over busy scenes and reads as placeholder.

## Performance

All additions are a handful of rects/circles per frame for a dozen UI elements —
sub-microsecond. Eased values are one lerp per element. No textures, no shaders,
no new draw passes; batching of the world is untouched.

## Constraints honored

- No new bitmaps or fonts (raylib default font; hierarchy via size + color).
- No bloom/blur/particles in UI; motion limited to easing and fades.
- Renderer-only changes (`src/Game/`); no domain, no determinism surface.
- Existing `scale` handling in menus preserved (all new paddings scale).

## Suggested stages (when approved)

1. `Ui.cs` helper + `Palette` additions; apply design language to HUD health bar
   (1b) + game over (7a). Screenshot-check via the headless recipe when a display
   is available.
2. Upgrade menu (4a: dim + soft cards) + stats screen (5) + ship select (6).
3. Targeting polish (3a) + optional XP bar (2a) — 2a needs `Player.Xp/Level` reads
   in the HUD, still renderer-only.

## Shipped — UI polish (verified 2026-10-31)


Plain soft-depth UI language across HUD and menus: rounded translucent panels, soft shadows, one accent color, eased values — no new bitmaps or fonts.

Implementation (plan: plans/UI_POLISH.md):
- `Ui.cs` helper: rounded rects (2 rects + 4 circles), panels (shadow + translucent fill + 1px border), alpha/easing/fade helpers; `Palette` gains the design language (accent cyan, off-white/muted text, translucent panel colors, green→amber→red `HealthColor` gradient)
- HUD: hp bar is one rounded pip per hp point with eased fade-out on loss + muted "8/8" readout; new thin top-edge xp bar (accent fill, "LV n" readout — the HUD showed no xp before); game over fades a dark overlay + title + accent rule + "Press R to restart" hint (the hint never existed)
- Upgrade cards: rounded panels with shadow, hover lifts 4px + accent border, value in accent / label muted, rounded key badge, 150 ms fade-in; stats screen: rounded panels, accent headers with hairline rule, alternating row tint, accent LV readout; ship select: rounded cards, accent selection stripe, trackless rounded scrollbar
- Targeting: 1.5px arms, manual lock pulses at 1 Hz (alpha 229–255), hp bars rounded 3px and only when damaged
- Renderer-only; visual verification pending a display (TROUBLE_SHOOTING.md #25)

