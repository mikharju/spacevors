namespace Spacevors.Domain.Components;

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
    float NoseLength,
    float WingSpread,
    int MaxWeaponSlots)
{
    public static ShipType Scout { get; } = new(
        "Scout",
        "Balanced engines, side shotguns",
        EngineLayout.Balanced,
        WeaponLoadout.Shotgun,
        MaxHealth: 8,
        Radius: 16f,
        DrawR: 80,
        DrawG: 220,
        DrawB: 80,
        NoseLength: 22f,
        WingSpread: 0.35f,
        MaxWeaponSlots: 1);

    public static ShipType Fighter { get; } = new(
        "Fighter",
        "Pursuit engines, machinegun",
        EngineLayout.Pursuit,
        WeaponLoadout.RailGun,
        MaxHealth: 10,
        Radius: 18f,
        DrawR: 100,
        DrawG: 160,
        DrawB: 255,
        NoseLength: 24f,
        WingSpread: 0.4f,
        MaxWeaponSlots: 2);

    public static ShipType Heavy { get; } = new(
        "Heavy",
        "Slow engines, side shotguns + machinegun",
        EngineLayout.Slow,
        WeaponLoadout.MachineGunShotgun,
        MaxHealth: 20,
        Radius: 24f,
        DrawR: 230,
        DrawG: 80,
        DrawB: 70,
        NoseLength: 26f,
        WingSpread: 0.5f,
        MaxWeaponSlots: 3);
}
