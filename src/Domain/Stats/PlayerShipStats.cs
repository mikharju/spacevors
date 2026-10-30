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
    // What actually gets hit, inside that bounding radius (PlayerHullShapes).
    HullShape Shape,
    byte DrawR,
    byte DrawG,
    byte DrawB,
    float PickupRadius)
{
    public static ShipType Scout { get; } = new(
        Name: "Scout",
        TextureKey: "scout",
        Description: "Balanced engines, particle stream and a flank repeater",
        Engine: EngineLayout.Balanced,
        Loadout: WeaponLoadout.CyclotronRepeater,
        Slots: TurretLayouts.Scout,
        MaxHealth: 8,
        Radius: 46f,
        Shape: PlayerHullShapes.Scout,
        DrawR: 80,
        DrawG: 220,
        DrawB: 80,
        PickupRadius: 120f);

    public static ShipType Fighter { get; } = new(
        Name: "Fighter",
        TextureKey: "fighter",
        Description: "Pursuit engines, machine gun and a side shotgun",
        Engine: EngineLayout.Pursuit,
        Loadout: WeaponLoadout.MachineGunShotgun,
        Slots: TurretLayouts.Fighter,
        MaxHealth: 10,
        Radius: 58f,
        Shape: PlayerHullShapes.Fighter,
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
        Shape: PlayerHullShapes.Heavy,
        DrawR: 230,
        DrawG: 80,
        DrawB: 70,
        PickupRadius: 230f);

    public static ShipType Shadow { get; } = new(
        Name: "Shadow",
        TextureKey: "shadow",
        Description: "Balanced engines, a ripple cutter in every mount",
        Engine: EngineLayout.Balanced,
        Loadout: WeaponLoadout.RippleCutterTrio,
        Slots: TurretLayouts.Shadow,
        MaxHealth: 10,
        Radius: 58f,
        Shape: PlayerHullShapes.Shadow,
        DrawR: 140,
        DrawG: 150,
        DrawB: 170,
        PickupRadius: 160f);

    // The slim hull: 1.22 R of paint across against the others' ~2 R, so its `Radius` is raised to 74 to
    // bring the painted length in line with the Fighter's (124 px against 123). A hurtbox of 1.11 R² at that
    // radius is still smaller in px than the Fighter's 2.67 — the big bounding circle is what tells the broad
    // phase where to look, not what gets hit — and it starts as the lightest shooter in the game.
    public static ShipType Falcon { get; } = new(
        Name: "Falcon",
        TextureKey: "falcon",
        Description: "Pursuit engines, a machine gun, two free 180° flank mounts",
        Engine: EngineLayout.Pursuit,
        Loadout: WeaponLoadout.InMainGun(WeaponType.MachineGun),
        Slots: TurretLayouts.Falcon,
        MaxHealth: 10,
        Radius: 74f,
        Shape: PlayerHullShapes.Falcon,
        DrawR: 255,
        DrawG: 176,
        DrawB: 96,
        PickupRadius: 205f);

    public static ShipType[] All { get; } = [Scout, Fighter, Heavy, Shadow, Falcon];

    // Extra ships on the select screen in diagnostics mode (SPACEVORS_DIAGNOSTIC=1): the Shadow
    // hull, engines and graphics with one weapon each, so any weapon can be play-tested from a
    // fresh start — tiers are a card-pool rule, not a restriction on the bench. LoadTestWeapon lives
    // here now that Shadow carries a real weapon.
    public static ShipType[] DiagnosticShips { get; } =
    [
        ShadowCarrying(WeaponType.LaserLance),
        ShadowCarrying(WeaponType.PrismLance),
        ShadowCarrying(WeaponType.RippleCutter),
        ShadowCarrying(WeaponType.Cyclotron),
        ShadowCarrying(WeaponType.GravitonBurst),
        ShadowCarrying(WeaponType.FlakCurtain),
        ShadowCarrying(WeaponType.MachineGun),
        ShadowCarrying(WeaponType.Shotgun),
        ShadowCarrying(WeaponType.RailGun),
        ShadowCarrying(WeaponType.AcidBubbleSpray),
        ShadowCarrying(WeaponType.InterceptorTurret),
        ShadowCarrying(WeaponType.SeekerPod),
        ShadowCarrying(WeaponType.Swarmlet),
        ShadowCarrying(WeaponType.HavocTorpedo),
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
