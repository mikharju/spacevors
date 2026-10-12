namespace Spacevors.Domain.Systems;

// Impact damage rule (plans/IMPACT_DAMAGE.md): absorbed kinetic energy at the contact
// normal, split between the bodies by inverse-mass share — the lighter body absorbs more.
// Quadratic in closing speed, so a hit at double speed is four times as dangerous, and
// capped per hit so that no single crash is lethal on its own.
public static class ImpactDamage
{
    // Below this closing speed no damage at all: swarm jostling (enemy top speeds are 90 px/s,
    // closing speeds far below), asteroid drift and side-by-side flight all stay under it.
    public const float MinClosingSpeed = 160f;

    // Damage per unit of absorbed energy. Calibrated on the initial 8 hp Scout hull: a big
    // impact (800 px/s closing into a large asteroid) costs 3 hp, a full-speed ram (400 px/s) 1.
    public const float ImpactScale = 7.2e-9f;

    // Per hit per body: one hit is at most a big impact, never a kill on its own.
    public const int MaxImpactDamage = 3;

    // A hit where either body absorbs at least this much throws a spark burst at the contact point.
    public const int SparkDamageThreshold = 2;

    // Per-body damage from a collision of two masses with the given closing speed
    // (normal component of relative velocity, px/s). Zero below MinClosingSpeed.
    public static (int DamageA, int DamageB) ForMasses(float massA, float massB, float closingSpeed)
    {
        if (closingSpeed < MinClosingSpeed) return (0, 0);

        float invMassA = 1f / massA;
        float invMassB = 1f / massB;
        float totalInvMass = invMassA + invMassB;

        // Energy uses the reduced mass 1/(1/mA + 1/mB); each body absorbs the share
        // matching its own inverse mass.
        float energy = 0.5f * closingSpeed * closingSpeed / totalInvMass;
        float damage = energy * ImpactScale;
        return (Round(damage * invMassA / totalInvMass), Round(damage * invMassB / totalInvMass));
    }

    public static int SparkCount(int damage) => Math.Min(2 + damage / 3, 6);

    static int Round(float rawDamage)
        => rawDamage < 0.5f ? 0 : Math.Min((int)MathF.Ceiling(rawDamage), MaxImpactDamage);
}
