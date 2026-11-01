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

// Loot lifetimes are 90 s — long enough for one wave's scattered loot to outlive the lull that
// follows it (plans/WAVES_AND_BREATHERS.md). `Chased` means the same thing for all three pickups: the
// orb or capsule has committed to the hull, so it closes at the committed chase speed and stops aging —
// a pickup already on its way that expired mid-flight would read as loot the game stole.
public readonly record struct HealthOrb(float Lifetime = 90f, float Radius = 8f, bool Chased = false);

public readonly record struct XpPickup(int XpAmount, float Lifetime = 90f, float Radius = 6f, bool Chased = false);

// Salvage that buys a re-deal of the upgrade hand: a short grey capsule tumbling slowly in place.
// Its `Spin` is what the renderer draws and what PickupMagnetSystem turns each tick, so the look is
// simulated rather than derived from wall-clock time (a seeded run screenshots the same way twice).
public readonly record struct RerollToken(float Lifetime = 90f, float Radius = 8f, float Spin = 0f, bool Chased = false);

// A salvage field: the same grey capsule with a slow pulsing ring (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
// Touching it sweeps the battlefield once — every XP orb, every currency capsule, and just enough of the
// nearest health orbs to fill the hull. The sweep is decided on the tick it is touched, so the capsule
// needs no state beyond its look.
public readonly record struct SalvageField(float Lifetime = 90f, float Radius = 8f, float Spin = 0f);

// The ECM capsule. It has no lifetime: an ECM capsule waits on the field for as long as the run does, and a
// salvage field deliberately never pulls one in, so using it always means flying somewhere dangerous.
// `Spin` and `Flash` are advanced by PickupMagnetSystem like `RerollToken.Spin`, for the same reason: a
// seeded run must render the same pose and the same flash phase twice.
public readonly record struct EcmPulse(float Radius = 8f, float Spin = 0f, float Flash = 0f);

// The ring that fires out of a collected ECM capsule. A cue only — the jam itself is world-wide, so the
// ring says "that just went off" rather than "this far is affected". It grows from nothing to `MaxRadius`
// over its lifetime, the inverse of the way an `Explosion` grows.
public readonly record struct EcmWave(float MaxRadius, float Lifetime, float InitialLifetime = 0.6f)
{
    public float CurrentRadius => MaxRadius * (1f - Lifetime / InitialLifetime);
}

public readonly record struct DebugMarker(float Lifetime, float InitialLifetime = 0.5f);

public readonly record struct ShipDeathExplosion(
    float TimeRemaining,
    Vector2 ImpactPoint,
    float ShipRadius,
    byte GraphicsId,
    Vector2 InheritedVelocity);
