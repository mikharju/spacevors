namespace Spacevors.Domain.Systems;

// Armor rule (plans/ARMOR_AND_PIERCE.md): gunfire — Ammo and LaserBeam, either direction — is
// reduced by the target's Armor component, and the round's Pierce cancels that armor first. Flat
// subtraction per hit with a floor of 1: armor taxes a support chip to the floor and barely
// notices a kill round, which is the class split with a second axis.
// Scope: only hulls carry Armor; mines, asteroids and rounds never do. Impact damage and mine
// contact bypass mitigation entirely, and enemy rounds never carry Pierce.
public static class ArmorRules
{
    // The most armor a hull's total can reach. The `Armor` card (+2) walks to it from the Heavy and
    // Eagle's starting 4 or an unarmored hull's 0; enemy per-shot damage tops out at 24 (tier 10 +
    // the HeavyCannon bonus), so 12 never makes a hull untouchable. The card is withdrawn at the
    // ceiling rather than dealt as a lie (R19's rule).
    public const int ArmorCeiling = 12;

    public static int EffectiveDamage(int damage, int armor, int pierce)
        => Math.Max(1, damage - Math.Max(0, armor - pierce));
}
