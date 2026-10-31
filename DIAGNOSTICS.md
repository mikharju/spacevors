# DIAGNOSTICS.md — debug helpers

Reference, usage only — no implementation details or history (plan files have those).

All helpers are active only with `SPACEVORS_DIAGNOSTIC=1`. It also turns on the cyan
collider overlay, the [FRAME]/[UPGRADE]/[FIRE] logs, and a fixed test asteroid at (0,-300).

Keys (in-game):

- L: force level-up
- M: spawn a test explosion at the fixed asteroid
- N: spawn a test enemy ship with huge health up-right of the player (click-targeting tests)
- H: set player health to 10000
- T: toggle a position pin freezing the player (stable visual tests)
- K: halve hp of the locked target or nearest enemy (reach damage smoke tiers quickly)
- J: halve player hp
- U: spawn a rack-armed test hull (enemy missiles)
- B: guided-round bench (8 headings x 2 plume sizes, drawn in screen space)
- V: plume anchor overlay on every guided round

Environment variables:

- `SPACEVORS_DIAGNOSTIC=1` — enables everything above
- `SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun"` — scripted upgrade choices, one
  entry per level-up; entry is a weapon name or `Stat:WeaponName`; falls back to the random
  pool when exhausted

Perf scenario env vars: plans/PERF_SCENARIOS.md. Headless build/run/screenshot recipe:
TROUBLE_SHOOTING.md, "General workflow that works".
