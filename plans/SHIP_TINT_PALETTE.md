# SHIP_TINT_PALETTE.md

Status: shipped (2026-11-02 — rig re-tune + per-faction hull tint landed and measured; elite/boss hues reserved, not implemented)

## The problem, measured

Every hull read blue: white and grey art, and the enemy art's own red and green accents, all
disappeared into navy. Simulated `Lighting.cs`' shading over the real sprite/normal/depth maps:

| rig | eagle mean | blue-dominant hull px | interceptor mean | blue-dominant hull px |
|---|---|---|---|---|
| old (fill `0.45,0.58,0.95` @ 0.32) | `#50566E` | **99%** | `#3D3B45` | **67%** |
| shipped (fill `0.74,0.76,0.80` @ 0.24) | `#5B5B5C` | **11%** | `#463E3B` | **11%** |

Three independent causes, none of them the art:

- **Fill.** `FillColor (0.45,0.58,0.95)` at 0.32 lands on every texel whatever the surface faces, so it
  recoloured the fleet. The source art is 64–99% near-neutral grey (mean albedo `#8A`–`#C6`).
- **Rim.** `RimColor (0.42,0.60,1.0)` is the silhouette-edge cue — the thing read at distance — and it was
  the same blue for friend and foe.
- **Engine light.** `LightGatherer` cast a blue-white `(0.55,0.78,1.0)` light from *every* accelerating
  ship's thruster — radius 3× the hull, intensity up to 0.8 — while the flame it came out of was orange.
  Hostile hulls were literally washed cyan by their own engines.

Blue was also overspent elsewhere: background `(15,15,25)`, XP orbs, the UI accent, ECM waves, thruster
light. A hull tint could not win against all of that, and should not try.

## The colour budget

Hue is a *limited resource* in a swarm game: every hue spent on decoration is a hue that cannot mean
something. This is the budget; the rule for future work is to spend from it, not to add to it.

| hue | who | lit hull tint (ambient × rim) | why |
|---|---|---|---|
| ivory / near-white steel | player | `1.06, 1.02, 0.94` | the player must be the brightest object on screen; its cool identity comes from its engine glow and from the warm fleet around it. A **blue hull** was measured at 97% blue-dominant pixels — the same bug, moved to the player |
| crimson | common enemies | `1.30, 0.60, 0.55` | matches the enemy edge indicator `(255,60,60)` and the enemy turret dot `(255,140,30)`; warm hostile against a cool background and a neutral player reads instantly |
| magenta | **reserved: elites** | `(1.32, 0.52, 1.12)` suggested | nothing large in the game uses magenta. Violet `(1.15,0.60,1.35)` was measured 28–98% blue-dominant and −23% luminance — take magenta, not violet |
| gold / amber | **reserved: bosses** | `(1.35, 1.05, 0.50)` suggested | +7% luminance: a boss gets *brighter*, which is the right direction. No other large gold object exists (loot is blue, green, grey) |
| untinted Neutral | asteroids, mines, wrecks | `1, 1, 1` | a tinted rock reads as a ship |

Off-limits for hulls: **green** (health orbs, turret arcs, and the universal "ally" colour — green hostiles
look like friendly ships), **saturated blue** (XP, ECM, UI accent), **pure white** (reserved for damage
flash and invulnerability).

Two rules that outlive any palette:

- **Hue is the second cue, not the first.** Damage soot mixes the albedo toward `(0.10,0.09,0.08)`, so a
  hue fades as a hull is beaten down; and red-vs-green is the exact pair colour-blind players cannot
  separate. The planned elite ring (plans/ENDLESS_DIFFICULTY.md P4) and a boss's size stay the primary
  signal, with hue confirming at speed.
- **Reserve early.** The expensive part is not the uniform, it is discovering later that elite gold
  collides with something already on screen.

## Shipped (2026-11-02):

1. **Light rig re-tuned** (`Lighting.cs`) — fill `(0.74,0.76,0.80)` @ 0.24, key `(1.00,0.95,0.86)` @ 0.80,
   rim `(0.46,0.66,1.00)` @ 0.20. Greys stay grey; the cool tone moved into the rim, where it belongs,
   because only grazing surfaces see it and it is what separates a dark hull from a black background.
   Measured: hull brightness unchanged, blue-dominant pixels 65–99% → 11–30%.
2. **One per-faction hull tint** — `uniform vec3 uHullTint`, multiplied into the **ambient and the rim
   only**, not into the albedo. A whole-colour multiply cost 25% luminance for crimson and left the metal
   looking painted; ambient+rim costs ~10% and leaves the key light and the specular glint reading as
   white metal under a coloured sky.
   The tint is a per-block uniform like the surface maps (`Lighting.BeginDraw`), so the lit batch key
   became `(LitSprite, HullFaction)` (`LitBlock`). Every faction but Neutral uses sprites no other faction
   uses, so in practice this is still one block per sprite variant and costs nothing.
   Faction lives in `Palette.HullTintOf` in the Game layer — the domain does not know about rendering.
3. **Engine fire and its light are the faction's colour** (`LightGatherer`, `ThrusterFlameRenderer`) —
   the player's plume and glow are cool `(70–150, 150–230, 255)` / `(0.55,0.78,1.0)`, every hostile's are
   warm `(255, 140–230, 30–120)` / `(1.00,0.66,0.42)`. A hull is now lit by the colour of the fire coming
   out of it, which was the single biggest source of the old cyan wash. Deviation from the first sketch:
   a hostile's glow is dimmer than the player's (0.5 vs 0.8), because every hostile in a wave thrusts at
   once and the 16 light slots fill with engine glow — at full strength a crowd became one orange field
   and the hulls lost their shape (seen in the `hull-crowding` shots).
4. **Fallback art carries the faction too** (`ShipSpriteRenderer`) — the no-texture placeholder triangle is
   drawn in its faction's colour, so a missing texture never puts the player in the enemy's red. The flat
   tint is derived from the lit tint rather than kept as a second colour that can drift.

## Perf verdict (2026-11-02)

`.tools/ab-lit.sh`, 5 interleaved reps, CPU ms/frame, min per side, `flat-200` as the untouched control:

| scenario | A (HEAD) | B (shipped) | delta |
|---|---|---|---|
| flat-200 (control) | 16.69 | 16.67 | −0.1% |
| lit-200-L0 | 35.19 | 35.16 | −0.1% |
| lit-200-L4 | 40.85 | 41.02 | **+0.4%** |
| lit-200-L16 | 57.24 | 58.31 | **+1.9%** |

The control did not move, so the subject's delta means something: the whole palette costs about nothing
at play-scale light counts and ~2% with 16 lights. **Keep.** Pixels moved as intended: mean 0.483/255, max
25, 6.4% of pixels changed. `hull-crowding --sim` median 0.41 ms against the 0.38–0.49 ms of its prior
index rows — sim untouched, as it must be for a render change.

`hull-crowding` in a **window** breaches its 8.33 ms budget here (8.81 ms with the change, 8.98 ms on
HEAD): pre-existing on this software-rasterised host, not caused by this change, and wall time on this
host is noise anyway (TROUBLE_SHOOTING 65). Worth a real-GPU re-measure by whoever owns that baseline.

The probe reported `FAIL` on the new side: it pixel-compares against a frozen oracle of the *old* shader,
so an intentional colour change is a mismatch by construction (RenderBench says so at its own line 226).
Run the A/B with `AB_PIXELS=different` for a palette change and read the pixel step, not the probe.

## Follow-up

- Elite and boss hues (magenta, gold) when those hulls exist: they need one more `HullFaction` value and
  one row in `Palette.HullTintOf`, plus the elite ring so the cue survives heavy damage.
- If a per-enemy-type hue is ever wanted (rather than one crimson for all five hulls), the batch key
  already carries the faction, so it becomes one faction value per `GraphicsId` — no new machinery, at the
  cost of more shader blocks.
- The player's cool identity is currently carried by its engine light alone; a separate rim hue for the
  player is the next lever if the edge read stays too weak at distance. One more vec3 uniform, same key.
