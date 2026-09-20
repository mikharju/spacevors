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

    // Magnet pull applied while a pickup is inside the player's pickup radius.
    public const float MagnetAcceleration = 800f;
    public const float MaxMagnetSpeed = 350f;
}
