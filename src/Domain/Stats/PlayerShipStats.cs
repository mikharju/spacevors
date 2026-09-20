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
    float TurnRate)
{
    public static EngineLayout Balanced { get; } = new("Balanced", 400f, 80f, 80f, 1.33f);
    public static EngineLayout Pursuit { get; } = new("Pursuit", 400f, 7f, 350f, 1.2f);
    public static EngineLayout Slow { get; } = new("Slow", 200f, 40f, 100f, 0.67f);
}

public readonly record struct ShipType(
    string Name,
    string Description,
    EngineLayout Engine,
    WeaponLoadout Weapon,
    int MaxHealth,
    float Radius,
    byte DrawR,
    byte DrawG,
    byte DrawB,
    int MaxWeaponSlots,
    float PickupRadius)
{
    public static ShipType Scout { get; } = new(
        "Scout",
        "Balanced engines, side shotguns",
        EngineLayout.Balanced,
        WeaponLoadout.SideShot, 
        MaxHealth: 8,
        Radius: 46f,
        DrawR: 80,
        DrawG: 220,
        DrawB: 80,
        MaxWeaponSlots: 1,
        PickupRadius: 120f);

    public static ShipType Fighter { get; } = new(
        "Fighter",
        "Pursuit engines, machinegun",
        EngineLayout.Pursuit,
        WeaponLoadout.RailGun,
        MaxHealth: 10,
        Radius: 58f,
        DrawR: 100,
        DrawG: 160,
        DrawB: 255,
        MaxWeaponSlots: 2,
        PickupRadius: 160f);

    public static ShipType Heavy { get; } = new(
        "Heavy",
        "Slow engines, side shotguns + machinegun",
        EngineLayout.Slow,
        WeaponLoadout.MachineGunShotgun,
        MaxHealth: 20,
        Radius: 84f,
        DrawR: 230,
        DrawG: 80,
        DrawB: 70,
        MaxWeaponSlots: 3,
        PickupRadius: 230f);

    public static ShipType Shadow { get; } = new(
        "Shadow",
        "Balanced engines, LoadTestWeapon",
        EngineLayout.Balanced,
        WeaponLoadout.LoadTestWeapon, // Intentionally kept while development is ongoing so manual load testing is easy
        MaxHealth: 10,
        Radius: 58f,
        DrawR: 140,
        DrawG: 150,
        DrawB: 170,
        MaxWeaponSlots: 2,
        PickupRadius: 160f);

    public static ShipType[] All { get; } = [Scout, Fighter, Heavy, Shadow];
}
