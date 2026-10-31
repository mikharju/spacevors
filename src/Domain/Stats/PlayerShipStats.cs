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

    // Crab: the strafing engine. Sideways thrust nearly matches forward — the hull slides out of the
    // line of fire without turning, which is the whole point of the mounts bolted to its wings.
    public static EngineLayout Crab { get; } = new("Crab", 260f, 210f, 90f, 1.1f);
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

    // Eagle (R 66): the wide-delta gunship. 1.85 R of wingspan, so a diameter of 122 px against the
    // Fighter's 116 — clearly a big hull without the Heavy's 165 px of target. It carries the slow
    // engine and the only hull-filling aft mount: it answers from every direction and gives up turning
    // and retreat speed for it. 27 starter DPS sits between the mediums' 22.5 and the Heavy's 45.
    public static ShipType Eagle { get; } = new(
        Name: "Eagle",
        TextureKey: "eagle",
        Description: "Slow engines, a scatter cannon, 150° flanks and a full aft arc",
        Engine: EngineLayout.Slow,
        Loadout: WeaponLoadout.ScatterCannonFlank,
        Slots: TurretLayouts.Eagle,
        MaxHealth: 16,
        Radius: 66f,
        Shape: PlayerHullShapes.Eagle,
        DrawR: 235,
        DrawG: 205,
        DrawB: 140,
        PickupRadius: 185f);

    // Manta (R 74): the crab. 1.57 R across against 1.13 R long — as wide as the Fighter is long —
    // and every mount points ahead: the hull aims by sliding sideways on the Crab engine, so its guns
    // never have to slew across the ship. The lightest starter firepower of the big hulls, paid for
    // with the game's best lateral dodge.
    public static ShipType Manta { get; } = new(
        Name: "Manta",
        TextureKey: "manta-ray",
        Description: "Crab engines that strafe, a machine gun and narrow forward mounts",
        Engine: EngineLayout.Crab,
        Loadout: WeaponLoadout.MachineGunRepeater,
        Slots: TurretLayouts.Manta,
        MaxHealth: 12,
        Radius: 74f,
        Shape: PlayerHullShapes.Manta,
        DrawR: 110,
        DrawG: 220,
        DrawB: 205,
        PickupRadius: 205f);

    // Raptor (R 78): the broadside hull. A body between two detached wing pods, and a heavy mount on
    // each pod sweeping 160° — 78 px radius, 88 × 100 px of paint, a medium hull that wants to be
    // beside you, not above you. It starts with one heavy mount empty on purpose: the second big gun
    // is the build.
    public static ShipType Raptor { get; } = new(
        Name: "Raptor",
        TextureKey: "raptor",
        Description: "Balanced engines, a machine gun, twin 160° broadside mounts — one empty",
        Engine: EngineLayout.Balanced,
        Loadout: WeaponLoadout.MachineGunBroadside,
        Slots: TurretLayouts.Raptor,
        MaxHealth: 14,
        Radius: 78f,
        Shape: PlayerHullShapes.Raptor,
        DrawR: 190,
        DrawG: 150,
        DrawB: 255,
        PickupRadius: 215f);

    public static ShipType[] All { get; } = [Scout, Fighter, Heavy, Shadow, Falcon, Eagle, Manta, Raptor];

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
        ShadowCarrying(WeaponType.ScatterCannon),
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
