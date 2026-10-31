# AGENTS.md

## Goal

Build a small, clean, highly maintainable Vampire Survivors-style space game.

Optimize for:
- readability
- simplicity
- performance
- deterministic behavior
- small commits
- easy LLM understanding

Never optimize prematurely.

## Code changes

Default: no code changes. Do not modify code unless explicitly commanded.

Exception: while already implementing an approved change, a newly reported problem may be fixed as part of that work.

When a request is unclear, escalate in this order and stop at the first level that suffices:
1. Reply (explain or ask)
2. Update documentation
3. Code changes — only when intent is unambiguous

## Rules

- Prefer deleting code over adding code.
- Avoid unnecessary abstractions.
- Keep functions short (~20 lines when practical).
- Keep files reasonably small.
- One responsibility per type.
- Avoid inheritance unless clearly beneficial.
- Prefer immutable data where practical.

## Architecture

Follow ARCHITECTURE.md.

Game logic must not depend directly on rendering, input, or audio.

## Coding style

- Modern C#
- Nullable enabled
- Clear names over comments
- No clever code
- No magic numbers
- Remove dead code immediately

## Workflow

For every task:

1. Understand existing code.
2. Make the smallest useful change.
3. Keep the project compiling.
4. Verify behavior.
5. Update documentation per the Documentation routing below.

When a feature is completed: append `## Shipped (YYYY-MM-DD):` to its plan file (what landed,
deviations from the plan, dated perf verdict), set its header to `Status: shipped`, and add or
update its one-line entry in the PLAN.md feature index. Never paste feature detail sections
into PLAN.md.

After changing simulation systems or rendering, measure instead of assuming:

    .tools/scenario.sh <scenario> --sim --iters 3

Headless, tagged host=sim, so it never disturbs the real-GPU baselines. Compare median, p99 and
perShapeNs against the previous row of `.tools/run/index.tsv` and put the verdict in the reply:
"this change costs X% - keep, optimize, or revert?" A budget breach or a `--check-baseline` failure
exits non-zero, so the check cannot be quietly skipped. For render changes also look at the run's
start/mid/end shots: a pass that got cheaper than its geometry can justify means the geometry is
not being drawn.

Never mix unrelated changes.

Check TROUBLE_SHOOTING.md for problems encountered before and avoid
similar problems. When encountering new problems, update TROUBLE_SHOOTING.md
with solutions.

Only record LLM workflow related problems in TROUBLE_SHOOTING.md, not project code problems that are not 
likely to be encountered in future while working on other features.

Headless build/run/screenshot recipe: see "General workflow that works" at the bottom of TROUBLE_SHOOTING.md.

## Communication

Be concise.

When proposing changes:
- explain why
- explain tradeoffs
- avoid long essays

Prefer bullet lists.

## Documentation

Root docs: AGENTS.md (rules) → ARCHITECTURE.md (invariants) → PLAN.md (roadmap + feature
index) → DIAGNOSTICS.md (debug helpers) → TROUBLE_SHOOTING.md (past problems).

plans/ holds one file per feature with all feature detail.

- **Before trusting a plans/*.md file, read its `Status:` header** (proposed / active /
  shipped / record). Below the header the file is frozen intent and **may be stale**.
  Trust order: code + tests + ARCHITECTURE.md > `## Shipped` / `## Follow-up` sections >
  plan body.
- Headers must be true: when a feature lands, is superseded or abandoned, update its plan
  header and its PLAN.md index line in the same commit.
- Never rewrite a shipped plan's body or Shipped sections; append a dated
  `## Follow-up (YYYY-MM-DD):` section.
- Routing for new detail: feature decisions → that feature's plan file; cross-cutting
  invariant → ARCHITECTURE.md; debug keys/env vars → DIAGNOSTICS.md; roadmap idea → one line
  in PLAN.md; perf verdicts → dated records, live numbers in `.tools/run/index.tsv`.
- Keep root docs brief: PLAN.md ~150 lines; ARCHITECTURE.md keeps overview + invariants +
  project layout. When a section outgrows that, move it to a plan file and leave a pointer.

Save plan files under plans.
- When user asks for plan, only make plan file, do not implement, change code, run tests.
- Start every new plan file with a `Status:` header line under the title.
- Web search is ok when planning
- If web search fails, report to user in reply which sites were not reachable

## Dependencies

Only introduce a dependency if it substantially simplifies the project.

## Testing

Prefer small deterministic tests for pure game logic.

Simulation should be testable without graphics.

## Performance

Prioritize simplicity along with performance.

Optimize only after measuring.

Performance goal is to have initially 10k objects at 120 fps and possibly in the future 100k objects.
Avoid yield return in hot path. Do not refactor existing ones out unless specifically asked, but do not make
new yield returns. 