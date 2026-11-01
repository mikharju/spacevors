namespace Spacevors.Domain.Stats;

// Pickups dropped on death and the magnet that pulls them in (CollisionSystem, PickupMagnetSystem).
public static class LootStats
{
    // XP dropped when an enemy ship dies.
    public const int ShipXpAmount = 3;
    public const float ShipXpPickupRadius = 18f;

    // Chance that a death also drops a health orb (mines and ships alike).
    public const float HealthOrbChance = 0.05f;
    public const int HealthOrbHealAmount = 3;
    public const float HealthOrbRadius = 20f;

    // Mine orbs are sized relative to the mine's XP pickup radius instead of using a fixed value.
    public const float HealthOrbRadiusOffset = 2f;

    // Salvage that buys a re-deal of the upgrade hand (plans/REROLL_TOKENS.md). Enemy ships only: mines
    // are the cheap kill and rocks pay nothing at all, so agency is priced against the thing that shoots
    // back. The band arithmetic lives in LootRoll, next to this number.
    public const float RerollTokenChance = 0.06f;

    // Collection radius, not the drawn size: the capsule is drawn shorter than this so it reads as a
    // piece of debris rather than a bright orb, while still being as easy to grab as the orbs.
    public const float RerollTokenRadius = 16f;

    // How fast the salvage capsule turns in place, rad/s. Slow, so it catches the eye as it tumbles.
    public const float RerollTokenSpinRate = 1.5f;

    // Magnet pull applied while a pickup is inside the player's pickup radius.
    public const float MagnetAcceleration = 800f;
    public const float MaxMagnetSpeed = 350f;
}
