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

    // The one tracking rule every pickup follows, capsules included (plans/LOOT_DROPS.md, plans/
    // ECM_PULSE_AND_SALVAGE_FIELD.md). While the hull is outside its range a pickup lies where it fell; once
    // the hull comes inside that range the pickup **commits** — the `Chased` flag — and closes at a constant
    // speed re-aimed at the hull every tick instead of accelerating from rest. A committed pickup cannot be
    // outrun and never expires, so nothing that was briefly in range can drift off and be lost.
    //
    // The speed matches the hull with margin (a boosting hull reaches ~1500 px/s) under a cap that stops a
    // runaway hull being chased by a hypersonic orb. Below `CommittedChaseSpeed / ChasePlayerSpeedFactor`
    // (960 px/s) of hull speed the floor holds, which is the flat speed XP orbs have always used; above it
    // every pickup matches the hull, so a boosting hull cannot shake a capsule it has already pulled.
    // Same shape as `EnemyShipSystem.EffectiveSpeed`.
    public const float CommittedChaseSpeed = 1200f;
    public const float ChasePlayerSpeedFactor = 1.25f;
    public const float ChaseSpeedCap = 2400f;

    public static float ChaseSpeedFor(float playerSpeed) =>
        MathF.Min(MathF.Max(CommittedChaseSpeed, playerSpeed * ChasePlayerSpeedFactor), ChaseSpeedCap);
}
