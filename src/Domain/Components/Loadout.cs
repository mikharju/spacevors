namespace Spacevors.Domain.Components;

public readonly record struct EngineLayout(
    string Name,
    float ForwardThrust,
    float SideThrust,
    float BackThrust)
{
    public static EngineLayout Balanced { get; } = new("Balanced", 400f, 80f, 80f);
    public static EngineLayout Maneuverable { get; } = new("Maneuverable", 250f, 20f, 200f);
    public static EngineLayout Pursuit { get; } = new("Pursuit", 400f, 7f, 350f);
}

public readonly record struct WeaponStats(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount,
    float Scatter);

public readonly record struct WeaponType(
    string Name,
    WeaponStats Stats)
{
    public static WeaponType MachineGun { get; } = new("MachineGun", new(8f, 420f, 10f, 1, Scatter: 0.033f));
    public static WeaponType Shotgun { get; } = new("Shotgun", new(2f, 350f, 2.5f, 3, Scatter: 0.04f));
}

public readonly record struct TurretDefinition(
    Vector2 Offset,
    float ArcOffset,
    float ArcAngle,
    float Range,
    WeaponType Weapon);

public readonly record struct WeaponLoadout(
    string Name,
    IReadOnlyList<TurretDefinition> Turrets)
{
    public static WeaponLoadout MachineGun { get; } = new("MachineGun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 360f, WeaponType.MachineGun)]);

    public static WeaponLoadout Shotgun { get; } = new("Shotgun", [
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun)]);
}

public record GameChoice(EngineLayout Engine, WeaponLoadout Weapon);
