using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

// Shield rule (plans/SHIELDS.md): a hull may carry a pool of absorb points in front of its Health.
// Gunfire — Ammo and LaserBeam, either direction — is mitigated twice: armor first (ArmorRules
// changes the size of the number), then the pool banks what is left.
//
//     mitigated = max(1, damage - max(0, armor - pierce))   // ArmorRules
//     soaked    = min(Points, mitigated)                    // Soak below
//     hull      = mitigated - soaked
//
// The pool is a burst buffer, not a damage-per-second tank: any hit that spends points arms a short
// shock during which the pool does not refill, so sustained fire turns a shielded hull back into
// plain hp. Once the shock is over the pool climbs at RechargeRate, which makes the payback time
// proportional to the hole: a small hole closes almost at once, a spent pool takes its time.
// Scope: only hulls carry a shield; mines, asteroids and rounds never do, and impact damage and mine
// contact bypass mitigation entirely — the same scope as armor. Pierce cancels Armor only.
public static class ShieldRules
{
    // The largest pool one hull can hold. Two `Shield` cards (+16 each) from a hull that starts
    // unshielded walk to it; +32 on top of 32-80 hp never makes a hull untouchable, which is the same
    // argument that caps armor at 12. The card is withdrawn at the ceiling rather than dealt as a lie.
    public const int ShieldCeiling = 32;

    // Seconds the pool stays closed after any hit that spends points of it. This is the gate that
    // keeps hp the run's pressure budget: without it, regen races chip damage and a hull under light
    // fire becomes immune to gunfire. It is also the knob that says how much being left alone is worth.
    public const float ShockSeconds = 2f;

    // Points restored per second once the shock is over. One card's pool per second: a 4-point hole
    // closes in ¼ s, a spent 16 pool in 1 s, a spent 32 ceiling in 2 s.
    public const float RechargeRate = 16f;

    // How long an intercept flash and a shatter ring are drawn (plans/SHIELDS.md §7). The flash says
    // "the field took that, there"; the burst says "the field just went out".
    public const float FlashSeconds = 0.15f;
    public const float BurstSeconds = 0.25f;

    // Shard lifetimes and count for the shatter. The shards are the shield's own particles — see
    // ShieldShard: they must not read as the amber sparks of a hull taking a hit.
    public const float ShardSeconds = 0.6f;
    public const int ShardCount = 6;

    // How far outside the hull's bounding radius the field is drawn and where shards fly from. A
    // draw-only number: it moves no collision radius and no broad-phase index.
    public const float RingRadiusFactor = 1.15f;

    public static float RingRadius(float hullRadius) => hullRadius * RingRadiusFactor;

    // What one hit spends of the pool. Whole points only, rounded up, so a nearly empty pool always
    // reaches exactly zero and the shatter fires once instead of leaving an unspendable fraction.
    public static int Soak(float points, int mitigatedDamage)
        => points <= 0f ? 0 : (int)MathF.Ceiling(MathF.Min(points, mitigatedDamage));

    // Seconds until the pool is whole again from `points`. The renderer never needs it — its front of
    // light rides Points — but it is the number the balance argument is made of.
    public static float SecondsToFull(float points, int max)
        => ShockSeconds + MathF.Max(0f, max - points) / RechargeRate;

    // What buying a `Shield` card does (plans/SHIELDS.md §5): the pool grows and the same amount is banked
    // at once — the card is worth its points on the spot, not only after a wait — while a shock already
    // running is left running. Buying a field in the middle of a fight does not make the hull untouchable.
    // The clamp is the offer's ceiling rule kept honest on the apply side too.
    public static Shield WithCard(Shield shield, int points)
    {
        int max = Math.Min(shield.Max + points, ShieldCeiling);
        return shield with { Max = max, Points = Math.Min(shield.Points + points, max) };
    }
}
