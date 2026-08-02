namespace Spacevors.Domain.Components;

public readonly record struct Explosion(float Radius, float Lifetime, float InitialLifetime = 0.5f);

public readonly record struct UpgradeExplosion(float Radius, float Lifetime);

public readonly record struct Spark(float Lifetime);

public readonly record struct BlueSpark(float Lifetime);

public readonly record struct GreenSpark(float Lifetime);

public readonly record struct HealthOrb(float Lifetime = 30f, float Radius = 8f);

public readonly record struct XpPickup(int XpAmount, float Lifetime = 30f, float Radius = 6f, bool Chased = false);

public readonly record struct DebugMarker(float Lifetime);

public readonly record struct ShipDeathExplosion(
    float TimeRemaining,
    Vector2 ImpactPoint,
    float ShipRadius,
    byte GraphicsId);
