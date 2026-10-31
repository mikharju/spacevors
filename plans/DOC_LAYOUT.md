# DOC_LAYOUT.md — documentation reorganization

Status: active (implementing 2026-10-31)

Status: active (implementing 2026-10-31)

## Goal

PLAN.md, ARCHITECTURE.md and AGENTS.md stay brief and never lie. `plans/` becomes
the single store of feature detail: one file per feature, carrying its full lifecycle.

## Target layout (root)

| File | Content | Budget |
|---|---|---|
| `AGENTS.md` | Rules + doc-routing workflow (below) | ~80 lines |
| `PLAN.md` | Vision, core gameplay, gameplay controls, feature index, roadmap (one-liners), done criteria | ≤ 150 lines |
| `ARCHITECTURE.md` | Stack, layers, ECS, phases, determinism, perf budgets, **current invariants** incl. capsules; pointer per subsystem to its plan file | ≤ 200 lines |
| `DIAGNOSTICS.md` (new) | `SPACEVORS_DIAGNOSTIC` / `SPACEVORS_DIAG_UPGRADES`: what the keys and env vars do. Usage only — no implementation, no history | ≤ 60 lines |
| `TROUBLE_SHOOTING.md` | unchanged | — |
| `plans/*.md` | All feature detail, lifecycle in one file | — |

## Conventions

1. **Status header** — first line under each plan file's title:
   `Status: proposed | active | shipped YYYY-MM-DD | record`.
   Active files add `(reviewed YYYY-MM-DD)` and keep open items in a list under the header.
2. **Plan body is frozen intent.** Never rewrite it retroactively. Corrections and evolutions
   go in dated `## Follow-up (YYYY-MM-DD):` sections appended below.
3. **Shipped files** get `## Shipped (YYYY-MM-DD):` — what actually landed, deviations from
   the plan, and the dated perf verdict. Perf numbers there are dated records; the live numbers
   are `.tools/run/index.tsv`.
4. **No lying docs:** a status header that contradicts reality is a bug. When a plan is
   implemented, superseded, or abandoned, its header changes in that same commit.
5. **Current-behavior precedence:** code + tests + ARCHITECTURE.md > `## Shipped` section >
   plan body. Nobody verifies "is the plan up to date" — nobody reads plan bodies as reference.
6. **Feature index in PLAN.md** — one line per feature:
   `- Missiles — shipped 2026-10 — plans/MISSILES.md`
7. **`plans/MINOR.md`** (new, `record`) — dated 3–8-line entries for small shipped features
   that never needed their own plan (camera-follows-mouse, aim-line volley…). Own file only
   when the feature has a decision table, balance rules, or multi-commit design.
8. **Roadmap** — PLAN.md keeps one-line ideas; a detailed proposal gets a `plans/` file with
   `Status: proposed`. MVP and Phase 1–8 history is dropped from PLAN.md (transient; `git log`
   has it).

## ARCHITECTURE.md keeps these capsule points (brief)

- Primitive: capsule = segment + inflated disc; a hull is 1–4 capsules; one contact per body
  pair per tick.
- Shapes are authored data in `Domain/Stats/` — never derived from pixels at run time.
- Bounding-radius invariant: the grid indexes and culls by the disc; the disc is the
  conservative reject in front of every shape test; a shape can never reach past its query.
- Circles stay for rounds, mines and pickups; rocks use disc-or-capsules by variant.
- Mass and inertia come from the bounding radius (deliberate).

Everything else about capsules (why not polygons, fitting, pricing) belongs to
`plans/NON_CIRCLE_COLLISION.md`.

## Migration — ordered steps, docs-only, small commits

1. **Status headers everywhere + fix lying lines.** (PERF_SCENARIOS.md claims "not
   implemented" but shipped; MISSILES.md claims "near future" but shipped.)
2. **Lift PLAN.md "— done" sections into their plan files** as `## Shipped` / `## Follow-up`
   per the mapping below. PLAN.md untouched yet, so nothing is lost mid-way.
3. **New files:** `plans/MINOR.md`, `plans/DAMAGE_PERFORMANCE.md`, `DIAGNOSTICS.md`.
4. **Rewrite PLAN.md:** index + one-line roadmap; drop done sections, MVP/Phase history and
   the diagnostics block.
5. **Trim ARCHITECTURE.md** to invariants + pointers; confirm each removed narrative exists
   in a plan file first.
6. **Update AGENTS.md** (below) in the same commit as step 4/5, so the next session follows
   the new routing.
7. **Verify:** line budgets met; every plan file has a status header; every index line links
   to an existing file; no status contradicts the index.

## Mapping: PLAN.md section → destination

| PLAN.md section | Destination |
|---|---|
| Stats directory | `STATS_DIRECTORY.md` shipped |
| Camera follows mouse | `MINOR.md` |
| Mouse clicks set primary target | `TARGETING_HP_BARS.md` follow-up |
| Enemy hp bar on targeting | `TARGETING_HP_BARS.md` shipped |
| Graphical damage indicators | `DAMAGE_GRAPHICS.md` shipped |
| Hull damage shading | `DAMAGE_SHADING.md` shipped |
| Impact damage | `IMPACT_DAMAGE.md` shipped |
| UI polish | `UI_POLISH.md` shipped |
| Bullets as lines | `BULLETS_AS_LINES.md` shipped |
| Shadow weapon test ships | `MINOR.md` |
| Sim vs render cost logging | `PERF_SCENARIOS.md` follow-up |
| Perf scenario harness | `PERF_SCENARIOS.md` shipped |
| Render cost: shape batching | `MINOR.md` |
| Perf gates | `PERF_SCENARIOS.md` follow-up |
| Difficulty scaling (open section) | replaced by one roadmap line → `DIFFICULTY_SCALING.md` active |
| Damage affects ship performance | new `plans/DAMAGE_PERFORMANCE.md` proposed; one PLAN line |
| Turret layouts | `TURRET_LAYOUTS.md` shipped |
| Weapon balance pass | `WEAPON_BALANCE.md` active (open knobs) |
| Rocks are cover / small break | `MINOR.md` |
| Deadlier individuals + threat readout | `DIFFICULTY_SCALING.md` follow-up |
| Auto-aim lead / round speed | `MINOR.md` |
| Missiles | `MISSILES.md` shipped |
| Non-circle hull collision | `NON_CIRCLE_COLLISION.md` shipped |
| Lumpy rocks | `NON_CIRCLE_COLLISION.md` follow-up |
| Falcon hull | `SHIP_TYPES.md` follow-up |
| Eagle/Manta/Raptor + Streak/Carmine | `SHIP_TYPES.md` follow-up; ScatterCannon note in `WEAPON_BALANCE.md` |
| Volley keeps one round on aim line | `MINOR.md` |
| Waves and lulls (no PLAN section; plan exists) | `WAVES_AND_BREATHERS.md` shipped — verify against code during step 1 |

Other files: `PARALLEL_SIM.md`, `POP_AND_SWAP.md`, `LOOT_DROPS.md` → proposed;
`CODE_REVIEW.md`, `GRAPHICS_REVIEW.md`, `functional-ecs.md` → record.

## AGENTS.md changes

- Replace "Update PLAN.md and ARCHITECTURE.md if needed" with routing:
  feature decision/detail → that feature's `plans/` file (new dated section, never edit
  history); cross-cutting invariant → ARCHITECTURE.md; status change → plan status header +
  PLAN.md index line; debug keys/env → DIAGNOSTICS.md.
- Workflow step on completing a feature: append `## Shipped (date)` to its plan file,
  update header + index line, delete any PLAN.md detail section.
- Keep: plan files under plans; "plan files may become stale" replaced by — they are allowed
  to be stale **below** the status header and Shipped section; the header and Shipped
  section must be true.

## Decisions taken (2026-10-31)

- Combined `MINOR.md` for small features: yes.
- Review/design records stay in `plans/` (status `record`).
- Post-ship follow-ups **update the original plan file**, dated sections — no
  lie-by-staleness.
- Active plans updated to reflect current status, with a reviewed-date header.
- Capsule invariants keep a short home in ARCHITECTURE.md; rationale/history moves out.
- MVP/Phase history removed from the three root docs entirely.
- New `DIAGNOSTICS.md`, brief, usage only.
- AGENTS.md updated in the same change.
- Index format: one dated line per feature, linking the plan file.
