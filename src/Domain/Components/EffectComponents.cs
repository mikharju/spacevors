namespace Spacevors.Domain.Components;

public readonly record struct Explosion(float Radius, float Lifetime, float InitialLifetime = 0.5f)
{
    public float CurrentRadius => Radius * (1f + (1f - Lifetime / InitialLifetime));
}

public readonly record struct DamageSpark(float Lifetime, float InitialLifetime = 1.4f);

// The flash where a beam stopped: one round glow, slightly wider than the beam, in a darkened copy
// of the beam's colour, centred exactly on the shortened beam's end. Fades over its lifetime.
public readonly record struct BeamImpact(float Radius, AmmoColor Color, float Lifetime = BeamImpact.FlashLifetime)
{
    // Lingers half a flash past the beam streak (LaserBeam.FlashLifetime): the hit point keeps glowing
    // after the streak is gone. The renderer keeps the glow core fully opaque while the beam still
    // lives, so the longer glow never exposes the beam's square end.
    public const float FlashLifetime = LaserBeam.FlashLifetime * 1.5f;

    // The glow scales with the beam that made it; the floor keeps thin beams' impacts visible.
    public static float GlowRadiusFor(float beamHalfThickness) => MathF.Max(3f, beamHalfThickness * 2.5f);
}

public readonly record struct HealSpark(float Lifetime, float InitialLifetime = 0.6f);

// Damage smoke: trails behind damaged ships, fades over its lifetime. Radius is set at spawn from the source hull size.
public readonly record struct SmokePuff(float Lifetime, float InitialLifetime = 1f, float Radius = 6f);

public readonly record struct HealthOrb(float Lifetime = 30f, float Radius = 8f);

public readonly record struct XpPickup(int XpAmount, float Lifetime = 30f, float Radius = 6f, bool Chased = false);

public readonly record struct DebugMarker(float Lifetime, float InitialLifetime = 0.5f);

public readonly record struct ShipDeathExplosion(
    float TimeRemaining,
    Vector2 ImpactPoint,
    float ShipRadius,
    byte GraphicsId,
    Vector2 InheritedVelocity);
