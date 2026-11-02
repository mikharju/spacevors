namespace Spacevors.Domain.Stats;

// Pickups dropped on death and the magnet that pulls them in (CollisionSystem, PickupMagnetSystem).
public static class LootStats
{
    // XP dropped when an enemy ship dies.
    public const int ShipXpAmount = 3;
    public const float ShipXpPickupRadius = 18f;

    // Chance that a death also drops a health orb (mines and ships alike).
    public const float HealthOrbChance = 0.05f;
    public const int HealthOrbHealAmount = 12;
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

    // How fast a salvage capsule turns in place, rad/s. Slow, so it catches the eye as it tumbles. All
    // three capsules (reroll, salvage field, ECM) tumble alike, so they read as the same family of object.
    public const float CapsuleSpinRate = 1.5f;

    // How fast an ECM capsule's blue-grey-white flash cycles, rad/s. Advanced by the sim, not by a wall
    // clock, so a seeded run renders the same flash phase twice (the `RerollToken.Spin` reasoning).
    public const float CapsuleFlashRate = 6f;

    // A salvage field: one sweep of the battlefield (plans/ECM_PULSE_AND_SALVAGE_FIELD.md). It is the
    // rarer of the two new capsules because it collects what is already there rather than adding a
    // threat answer.
    public const float SalvageFieldChance = 0.03f;

    // An ECM capsule: the smallest band, because it removes a threat instead of granting something
    // (plans/LOOT_ITEMS.md argued this before the design changed, and the argument survived it).
    public const float EcmPulseChance = 0.025f;

    // Both new capsules are collected at the reroll capsule's radius but drawn differently, so the motion
    // says which one it is: a pulsing ring sweeps, a flashing capsule jams.
    public const float FieldCapsuleRadius = RerollTokenRadius;
    public const float EcmCapsuleRadius = RerollTokenRadius;

    // Magnet pull applied while a pickup is inside the player's pickup radius and has not committed yet.
    // This is the debris pull the salvage capsules use: gentle, capped below hull speed on purpose.
    public const float MagnetAcceleration = 800f;
    public const float MaxMagnetSpeed = 350f;

    // Once a pickup has committed to the hull — it came inside the magnet radius, or a salvage field
    // swept it up — it closes at this constant speed and re-aims every tick instead of accelerating from
    // rest. `MaxMagnetSpeed` is under most hulls' cruise speed, which is why health orbs used to lose the
    // chase and read as uncollectable; a committed pickup cannot be outrun, which is what makes XP orbs
    // effectively unmissable.
    public const float CommittedChaseSpeed = 1200f;

    // A health orb is worth more than a flat chase speed, so it matches the hull it is following with
    // margin (a boosting hull reaches ~1500 px/s) under a cap that stops a runaway hull being chased by a
    // hypersonic orb. Same shape as `EnemyShipSystem.EffectiveSpeed`.
    public const float HealthOrbChasePlayerSpeedFactor = 1.25f;
    public const float HealthOrbChaseSpeedCap = 2400f;

    public static float HealthOrbChaseSpeedFor(float playerSpeed) =>
        MathF.Min(MathF.Max(CommittedChaseSpeed, playerSpeed * HealthOrbChasePlayerSpeedFactor), HealthOrbChaseSpeedCap);
}
