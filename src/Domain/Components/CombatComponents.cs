using Spacevors.Domain.Stats; // WeaponStats

namespace Spacevors.Domain.Components;

public enum AmmoColor { Yellow, Green, Blue, Red, Cyan, Violet }

// Radius is the capsule half-thickness: collision and rendering treat the bullet as the
// segment [Position - Velocity*dt, Position] inflated by it, so wide shots score grazing hits.
public readonly record struct Ammo(Vector2 Velocity, float Radius, float Lifetime, bool IsEnemy = false, int Damage = 1, AmmoColor Color = AmmoColor.Yellow);

public readonly record struct FireCooldown(float Timer);

public readonly record struct Turret(
    WeaponStats Weapon,
    string WeaponName,
    float ArcAngle = MathF.PI / 2f,
    float Range = 360f,
    bool AutoTarget = true,
    bool IsEnemy = false);

// Player's manually selected target (enemy ship or mine), set by mouse click.
public readonly record struct PrimaryTarget(Entity Target);

// Last world time a player turret selected this entity as its current target; the renderer draws an auto bracket while fresh.
public readonly record struct AutoTargetMark(float LastTargetedAt)
{
    // How long after last selection the mark still counts (auto brackets, damage-graphics priority).
    public const float FreshWindow = 1f;
}

public readonly record struct WeaponSlots(int Used, int Max);

// Instant hitscan beam: static segment [Start, Start + Direction * Length], Width is half-thickness.
// CollisionSystem applies its damage exactly once (clearing DamagePending) to the first thing
// hit; afterwards the beam lingers only as a fading visual until Lifetime expires.
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

public readonly record struct ArcOffset(float Angle);

public readonly record struct TurretOffset(Vector2 Value);
