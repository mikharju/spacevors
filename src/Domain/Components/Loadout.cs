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
    public static EngineLayout Slow { get; } = new("Slow", 200f, 40f, 100f);
}

public readonly record struct WeaponStats(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount,
    float Scatter,
    float ShotLifetime = 3f,
    int Damage = 1);

public readonly record struct WeaponType(
    string Name,
    WeaponStats Stats)
{
    public static WeaponType MachineGun { get; } = new("MachineGun", new(8f, 420f, 10f, 1, Scatter: 0.033f, Damage: 1));
    public static WeaponType Shotgun { get; } = new("Shotgun", new(2f, 350f, 2.5f, 3, Scatter: 0.04f, Damage: 1));

    // Phase 2 weapons
    public static WeaponType RailGun { get; } = new("RailGun", new(0.667f, 900f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 100));
    public static WeaponType TwinChainGun { get; } = new("TwinChainGun", new(14f, 550f, 3f, 1, Scatter: 0.015f, Damage: 1));
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 200f, 8f, 1, Scatter: 0.4f, ShotLifetime: 1.2f, Damage: 1));
    public static WeaponType PointDefenceTurret { get; } = new("PointDefenceTurret", new(5f, 300f, 5f, 1, Scatter: 0.05f, Damage: 1));
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(2f, 200f, 100f, 1500, Scatter: 0.15f, Damage: 1));
}

public readonly record struct TurretDefinition(
    Vector2 Offset,
    float ArcOffset,
    float ArcAngle,
    float Range,
    WeaponType Weapon,
    bool AutoTarget = true);

public readonly record struct WeaponLoadout(
    string Name,
    IReadOnlyList<TurretDefinition> Turrets)
{
    public static WeaponLoadout MachineGun { get; } = new("MachineGun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 360f, WeaponType.MachineGun)]);

    public static WeaponLoadout Shotgun { get; } = new("Shotgun", [
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun)]);

    public static WeaponLoadout MachineGunShotgun { get; } = new("MachineGunShotgun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 360f, WeaponType.MachineGun),
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 360f, WeaponType.Shotgun)]);

    // Phase 2 weapon loadouts
    public static WeaponLoadout RailGun { get; } = new("RailGun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 500f, WeaponType.RailGun, AutoTarget: false)]);

    public static WeaponLoadout TwinChainGun { get; } = new("TwinChainGun", [
        new(new Vector2(-12f, 0f), ArcOffset: MathF.PI / 4f, MathF.PI / 8f, 360f, WeaponType.TwinChainGun, AutoTarget: false),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 4f, MathF.PI / 8f, 360f, WeaponType.TwinChainGun, AutoTarget: false)]);

    public static WeaponLoadout AcidBubbleSpray { get; } = new("AcidBubbleSpray", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 250f, WeaponType.AcidBubbleSpray, AutoTarget: false)]);

    public static WeaponLoadout PointDefenceTurret { get; } = new("PointDefenceTurret", [
        new(Vector2.Zero, ArcOffset: -MathF.PI / 4f, MathF.PI * 3 / 4f, 280f, WeaponType.PointDefenceTurret)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(Vector2.Zero, ArcOffset: -MathF.PI / 4f, MathF.PI * 3 / 4f, 280f, WeaponType.LoadTestWeapon)]);

}
