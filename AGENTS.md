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

Do not implement any code changes unless explicitly commanded to do so. Only exception is if already implementing code changes and a new problem is reported. 
If unclear, default to replies first, then documentation updates and only in very clear cases code changes.

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
5. Update PLAN.md if needed.
6. Do not commit or push any changes to git, only reading git diffs logs and such non change operations are allowed.

Never mix unrelated changes.

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