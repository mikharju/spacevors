namespace Spacevors.Domain.Stats;

// Shared player ship tuning applied on top of each ShipType (GameInitializer).
public static class PlayerShipStats
{
    // Shift boost multiplier, forward only.
    public const float Boost = 2.5f;
}

public readonly record struct EngineLayout(
    string Name,
    float ForwardThrust,
    float SideThrust,
    float BackThrust,
    // Hull turn rate in rad/s. A turret mount rated below this loses its target while the ship spins.
    float TurnRate)
{
    public static EngineLayout Balanced { get; } = new("Balanced", 400f, 80f, 80f, 1.33f);
    public static EngineLayout Pursuit { get; } = new("Pursuit", 400f, 7f, 350f, 1.2f);
    public static EngineLayout Slow { get; } = new("Slow", 200f, 40f, 100f, 0.67f);
}

public readonly record struct ShipType(
    string Name,
    // Asset stem in assets/player-ships (ShipSpriteRenderer looks up "<key>", "<key>-texture", …).
    // Kept separate from Name so display text and filenames cannot drift together.
    string TextureKey,
    string Description,
    EngineLayout Engine,
    WeaponLoadout Loadout,
    // The hull's turret mounts; Loadout mounts weapons into some of them by slot name.
    IReadOnlyList<TurretSlotDefinition> Slots,
    int MaxHealth,
    float Radius,
    byte DrawR,
    byte DrawG,
    byte DrawB,
    float PickupRadius)
{
    public static ShipType Scout { get; } = new(
        Name: "Scout",
        TextureKey: "scout",
        Description: "Balanced engines, rapid particle stream",
        Engine: EngineLayout.Balanced,
        Loadout: WeaponLoadout.Cyclotron,
        Slots: TurretLayouts.Scout,
        MaxHealth: 8,
        Radius: 46f,
        DrawR: 80,
        DrawG: 220,
        DrawB: 80,
        PickupRadius: 120f);

    public static ShipType Fighter { get; } = new(
        Name: "Fighter",
        TextureKey: "fighter",
        Description: "Pursuit engines, laser lance + cyclotron",
        Engine: EngineLayout.Pursuit,
        Loadout: WeaponLoadout.LaserLanceCyclotron,
        Slots: TurretLayouts.Fighter,
        MaxHealth: 10,
        Radius: 58f,
        DrawR: 100,
        DrawG: 160,
        DrawB: 255,
        PickupRadius: 160f);

    public static ShipType Heavy { get; } = new(
        Name: "Heavy",
        TextureKey: "heavy",
        Description: "Slow engines, machinegun, side shotguns, flak curtain",
        Engine: EngineLayout.Slow,
        Loadout: WeaponLoadout.HeavyFlak,
        Slots: TurretLayouts.Heavy,
        MaxHealth: 20,
        Radius: 84f,
        DrawR: 230,
        DrawG: 80,
        DrawB: 70,
        PickupRadius: 230f);

    public static ShipType Shadow { get; } = new(
        Name: "Shadow",
        TextureKey: "shadow",
        Description: "Balanced engines, graviton burst",
        Engine: EngineLayout.Balanced,
        Loadout: WeaponLoadout.InMainGun(WeaponType.GravitonBurst),
        Slots: TurretLayouts.Shadow,
        MaxHealth: 10,
        Radius: 58f,
        DrawR: 140,
        DrawG: 150,
        DrawB: 170,
        PickupRadius: 160f);

    public static ShipType[] All { get; } = [Scout, Fighter, Heavy, Shadow];

    // Extra ships on the select screen in diagnostics mode (SPACEVORS_DIAGNOSTIC=1): the Shadow
    // hull, engines and graphics with one weapon each, so any weapon can be play-tested from a
    // fresh start. LoadTestWeapon lives here now that Shadow carries a real weapon.
    public static ShipType[] DiagnosticShips { get; } =
    [
        ShadowCarrying(WeaponType.LaserLance),
        ShadowCarrying(WeaponType.PrismLance),
        ShadowCarrying(WeaponType.RippleCutter),
        ShadowCarrying(WeaponType.Cyclotron),
        ShadowCarrying(WeaponType.GravitonBurst),
        ShadowCarrying(WeaponType.FlakCurtain),
        // LoadTestWeapon's arc is the perf scenario's, so it keeps its own layout.
        ShadowCarrying(WeaponType.LoadTestWeapon, WeaponLoadout.LoadTestWeapon),
    ];

    // Shadow hull with a single weapon in its main gun slot, named after it.
    static ShipType ShadowCarrying(WeaponType weapon, WeaponLoadout? loadoutOverride = null) =>
        Shadow with
        {
            Name = $"Shadow {weapon.Name}",
            Description = $"Balanced engines, {weapon.Name}",
            Loadout = loadoutOverride ?? WeaponLoadout.InMainGun(weapon),
        };
}
