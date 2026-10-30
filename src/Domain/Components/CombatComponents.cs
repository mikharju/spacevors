using Spacevors.Domain.Stats; // WeaponStats

namespace Spacevors.Domain.Components;

// DarkGreen is the acid spray's: a deep, toxic green that must not read as the health orb's mint.
public enum AmmoColor { Yellow, Green, DarkGreen, Blue, Red, Cyan, Violet, Orange }

// Radius is the capsule half-thickness: collision and rendering treat the bullet as the
// segment [Position - Velocity*dt, Position] inflated by it, so wide shots score grazing hits.
public readonly record struct Ammo(Vector2 Velocity, float Radius, float Lifetime, bool IsEnemy = false, int Damage = 1, AmmoColor Color = AmmoColor.Yellow);

public readonly record struct FireCooldown(float Timer);

// A guided round: the entity it is locked onto, the flight numbers it was launched with, and how hard it
// turned last tick (0..1) — the renderer draws the exhaust from that. Self-contained: the round carries
// its own numbers, so guidance never looks upstream.
// `Speed` is the round's own flight speed and guidance is the only writer: it climbs by `Acceleration`
// toward `MaxSpeed` and stops there. The turn radius is therefore `Speed / TurnRate` — a round is tightest
// just after launch and hardest to shake once it has its speed up.
public readonly record struct Missile(
    Entity LockedTarget,
    float Speed,
    float MaxSpeed,
    float Acceleration,
    float TurnRate,
    float Steering);

// The missile rack an enemy hull carries: its own gun stats and its own launch ring, separate from the
// hull's `Turret` so a hull can carry guns, a rack, or both, each on its own cooldown. The ring is
// deliberately wider than the hull's gun range (plans/MISSILES.md §9): missiles buy reach instead of
// changing how the hull flies.
public readonly record struct MissileWeapon(WeaponStats Weapon, float Range);

// Countdown between this hull's missile launches, ticked by EnemyMissileFiringSystem. Held by the hull
// rather than the round, because the limit that matters is how often a launcher can reload.
public readonly record struct MissileCooldown(float Timer);

// A weapon a turret entity carries. ArcAngle is only read for enemy turrets, which are aimed by
// their hull; a player turret's arc comes from the TurretSlot it is mounted in.
public readonly record struct Turret(
    WeaponStats Weapon,
    string WeaponName,
    float ArcAngle = MathF.PI / 2f,
    float Range = 360f,
    bool AutoTarget = true,
    bool IsEnemy = false);

// One turret mount point on the player hull. A slot holding a weapon also carries
// Turret, TurretAim, Position and Rotation; an empty slot carries only this.
public readonly record struct TurretSlot(TurretSlotDefinition Definition);

// A mounted player turret's aim: yaw relative to the hull, so turning the ship carries the mount
// with it and a hull that turns faster than the weapon's TurnSpeed drags its aim off target.
// Parked = already inside the firing tolerance, so the mount holds still instead of correcting.
public readonly record struct TurretAim(float Yaw, bool Parked);

// Player's manually selected target (enemy ship or mine), set by mouse click.
public readonly record struct PrimaryTarget(Entity Target);

// Last world time a player turret selected this entity as its current target; the renderer draws an auto bracket while fresh.
public readonly record struct AutoTargetMark(float LastTargetedAt)
{
    // How long after last selection the mark still counts (auto brackets, damage-graphics priority).
    public const float FreshWindow = 1f;
}

// Instant hitscan beam: static segment [Start, Start + Direction * Length], Width is half-thickness.
// CollisionSystem applies its damage exactly once (clearing DamagePending) to the first thing
// hit and shortens Length to that target's near side, so the beam never draws through what it hit;
// afterwards the beam lingers only as a fading visual until Lifetime expires.
public readonly record struct LaserBeam(
    Vector2 Start,
    Vector2 Direction,
    float Length,
    float Width,
    int Damage,
    bool IsEnemy,
    AmmoColor Color,
    float Lifetime,
    float InitialLifetime,
    bool DamagePending = true)
{
    // How long the visible flash lingers; damage always lands on the first tick.
    public const float FlashLifetime = 0.12f;

    public Vector2 End => Start + Direction * Length;
}
