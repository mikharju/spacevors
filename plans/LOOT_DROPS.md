# Plan: Enemy Loot Drops & Player XP/Leveling

## Problem

Dying enemies currently just disappear with no reward. Need a loot drop system where destroyed enemies leave behind collectible pickups that give XP and occasionally health. Players need an XP/level progression system with a pickup radius to magnetize nearby items.

## Design Decisions

### Loot Drops on Enemy Death

When an enemy is destroyed (health <= 1), spawn:
- **Always:** One XP pickup orb at the death position
- **Rarely (~5%):** One health orb at the death position (independent roll)

XP amount based on enemy type:
| Enemy | XP | Pickup radius |
|-------|----|---------------|
| Small mine | 1 | ~6px (slightly smaller than small mine's 7.5) |
| Large mine | 2 | ~9px (larger than small mine) |
| Enemy ship | 3 | ~18px (between large mine's 15 and 2xp's 9, visually larger) |

Pickup sizes scale with XP value so bigger = more XP.

### Player Stats

New `Player` component fields:
```csharp
public readonly record struct Player(
    float Thrust,
    float Boost,
    float Radius = 18f,
    int Xp = 0,           // total XP accumulated
    int Level = 1,        // current level (derived from XP)
    float PickupRadius = 60f  // magnet range for pickups
);
```

XP thresholds per level: `Level * 10` cumulative (level 1→2 needs 10xp total, level 2→3 needs 20xp total, etc.)

### Pickup Behavior

**Magnet attraction:**
- Pickups inside pickup radius accelerate fast toward player and are capped at high max speed
- Very easy to collect — just fly near them. No need to slow down or line up correctly
- Even flying past very fast will grab the pickup due to strong acceleration + high speed cap
- Once a pickup enters the pickup radius, it is marked as `Chased` and follows the player even if it falls outside the radius (until collected or lifetime expires)

**XP Pickups:**
- No particles on collection — just disappears and XP is applied
- Blue colored orbs (matching existing blue spark palette)

**Health Orbs:**
- Green colored orbs, slightly larger than equivalent XP orb
- On collection: spawns green explosion with sparks that home toward player (similar to blue upgrade explosion sparks but green)
- Restores 3 health points

### Upgrade System Changes — Keep PendingChoice

Remove the current **upgrade pickup orbs** from enemies. Upgrades now come from leveling up instead of random drops.

Each level grants one upgrade choice between:
1. Fire Rate (+15% per pick, stacks multiplicatively)
2. Projectile Speed (+30% per pick, stacks multiplicatively)
3. Pickup Radius (+20% per pick, stacks additively)

Level-up behavior mirrors the current upgrade pickup flow:
- Game pauses when player levels up
- `PendingChoice` marker component is added to a choice entity
- Player picks with Digit1/Digit2 keys (showing 2 of 3 options at a time, cycling through)
- Pressing both = no choice; player cannot die during selection (gameOver check skipped)
- Time accumulation frozen during pause

**Keep `PendingChoice.cs` and the upgrade card drawing code.** The existing UI infrastructure is reused — just change what triggers it (level-up instead of pickup).

### Upgrade Stats Applied

Upgrades apply to main weapon AND all turrets:
- **Fire Rate:** ×1.15 multiplier on Weapon.EffectiveFireRate + Turret.FireRate; for shotgun turrets (PelletCount > 1), adds +1 pellet per pick instead
- **Projectile Speed:** ×1.3 multiplier on Weapon.EffectiveAmmoSpeed + Turret.AmmoSpeed
- **Pickup Radius:** +20% additive increase to Player.PickupRadius

## Files to Change

### New Components
| File | Content |
|------|---------|
| `src/Domain/Components/XpPickup.cs` | `XpPickup(int XpAmount, float Lifetime = 30f, float Radius)` |
| `src/Domain/Components/HealthOrb.cs` | `HealthOrb(float Lifetime = 30f, float Radius = 8f)` |

### Modified Components
| File | Changes |
|------|---------|
| `src/Domain/Components/Player.cs` | Add Xp, Level, PickupRadius fields |
| `src/Domain/Components/XpPickup.cs` | Add `bool Chased` field (marked true once inside pickup radius) |

### New Systems
| File | Content |
|------|---------|
| `src/Domain/Systems/LootDropSystem.cs` | Spawn XP pickup (+ optional health orb) when enemy is destroyed. Replaces inline destroy in CollisionSystem. |
| `src/Domain/Systems/PickupMagnetSystem.cs` | Attract pickups toward player within PickupRadius, mark as Chased on entry, apply effects on contact. |
| `src/Domain/Systems/LevelUpSystem.cs` | Check if XP meets threshold for next level; spawn PendingChoice entity when leveling up. |

### Modified Systems
| File | Changes |
|------|---------|
| `src/Domain/Systems/CollisionSystem.cs` | Remove inline `em.DestroyEntity(enemyShipEntity)` — instead mark entity for destruction and let LootDropSystem handle spawning + cleanup. Or: spawn loot before destroy in CollisionSystem itself (simpler, fewer files). |
| `src/Domain/Systems/UpgradePickupSystem.cs` | Delete entirely — no more upgrade pickups from enemies. |
| `src/Domain/Systems/EffectSystem.cs` | Remove UpgradeExplosion lifetime handling only (keep BlueSpark if used elsewhere). |

### Deleted Files
| File | Reason |
|------|--------|
| `src/Domain/Components/Upgrade.cs` | Replaced by level-based upgrades |
| `src/Domain/Systems/UpgradePickupSystem.cs` | No more upgrade pickups from enemies |

### Modified Game Code
| File | Changes |
|------|---------|
| `src/Game/SpaceVorsApp.cs` | Remove UpgradePickupSystem from systems list. Replace upgrade choice handling with level-up choice handling (same PendingChoice flow, different stat application). Add LevelUpSystem to systems list. Update Player component initialization. |
| `src/Game/Renderer.cs` | Add `DrawXpPickups()` and `DrawHealthOrbs()`. Remove `DrawUpgrades()`, `DrawUpgradeExplosions()`. Keep `DrawBlueSparks()` (used by health orb collection). Keep `DrawUpgradeCards()` (reused for level-up choices). |
| `src/Game/GameInitializer.cs` | Update Player component creation to include new fields. |

## Execution Order

1. Create `XpPickup.cs` and `HealthOrb.cs` components
2. Modify `Player.cs` to add Xp, Level, PickupRadius
3. Add loot spawning in `CollisionSystem.cs` (inline: spawn before destroy)
4. Create `PickupMagnetSystem.cs` — magnet attraction + Chased marking + collection logic
5. Create `LevelUpSystem.cs` — check XP thresholds, spawn PendingChoice on level up
6. Update `EffectSystem.cs` — remove UpgradeExplosion lifetime handling
7. Delete `Upgrade.cs`, `UpgradePickupSystem.cs`
8. Update `SpaceVorsApp.cs` — remove old systems, add new ones, replace upgrade choice with level-up choice
9. Update `Renderer.cs` — add loot drawing, remove upgrade orb/explosion drawing, keep card drawing
10. Update `GameInitializer.cs` — new Player fields
11. Build + test

## Tradeoffs Considered

**Spawn loot in CollisionSystem vs separate LootDropSystem:**
- CollisionSystem is simpler (one place, no extra system) but mixes concerns slightly
- Separate system keeps collision pure but adds indirection
- Decision: spawn inline in CollisionSystem for simplicity — it's already tracking death events

**Magnet behavior — linear interpolation or acceleration?**
- Linear lerp toward player each frame = simple, predictable
- Acceleration-based with high cap = fast response, feels responsive even at high ship speeds
- Decision: use velocity-based attraction (accelerate toward player) capped at ~300px/s — very easy to collect

**Pickup radius marking (Chased flag):**
- Once a pickup enters the magnet range, it follows the player forever (until collected or lifetime expires)
- This means players can fly past pickups without stopping and still get them
- Prevents frustration from fast movement causing missed pickups

**Health orb drop rate:**
- 5% is rare enough to feel special but not so rare that players never see one
- Healing 3 HP makes each orb meaningful (player has ~10 HP based on current code)

**Three upgrade options vs two:**
- Fire Rate, Projectile Speed, Pickup Radius — three choices per level up
- Show 2 at a time and cycle through (e.g., show FRate+PSpeed first pick, then FRate+PickupRadius next, etc.)
- Or show all 3 as cards with keys 1/2/3

## Summary of What Changes vs Current

| Aspect | Before | After |
|--------|--------|-------|
| Enemy death | Just disappears | Spawns XP pickup (+ rare health orb) |
| Player stats | Thrust, Boost, Radius | + Xp, Level, PickupRadius |
| Pickups | Upgrade orbs with choice screen | XP orbs (blue) + health orbs (green), auto-collected via magnet |
| Collection | Manual key press during pause | Magnet attraction within radius, automatic on contact |
| Particles on collect | Blue sparks homing to player | XP: none. Health orb: green explosion + homing sparks |
| Upgrade source | Random enemy drops | Level-up choices (3 options) |
| PendingChoice | Used for upgrade pickups | Reused for level-up choices |
