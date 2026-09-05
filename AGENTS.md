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
5. Update PLAN.md and ARCHITECTURE.md if needed.

Never mix unrelated changes.

Check TROUBLE_SHOOTING.md for problems encountered before and and avoid
similar problems. When encountering new problems, update TROUBLE_SHOOTING.md
with solutions.

## Communication

Be concise.

When proposing changes:
- explain why
- explain tradeoffs
- avoid long essays

Prefer bullet lists.

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