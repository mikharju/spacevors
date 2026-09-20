using Spacevors.Domain.Components; // AmmoColor

namespace Spacevors.Domain.Stats;

public readonly record struct WeaponStats(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount,
    float Scatter,
    float ShotLifetime = 3f,
    int Damage = 1);

public readonly record struct AddOnMount(
    Vector2 Offset,
    float ArcOffset,
    float ArcAngle,
    float Range,
    bool AutoTarget = true);

public readonly record struct WeaponType(
    string Name,
    WeaponStats Stats,
    float AmmoRadius = 2.5f,
    AmmoColor Color = AmmoColor.Yellow,
    AddOnMount? AddOn = null)
{
    public static WeaponType MachineGun { get; } = new("MachineGun", new(8f, 420f, 10f, 1, Scatter: 0.033f, Damage: 1));
    public static WeaponType Shotgun { get; } = new("Shotgun", new(2f, 350f, 2.5f, 3, Scatter: 0.04f, Damage: 1));

    // Phase 2 weapons
    public static WeaponType RailGun { get; } = new("RailGun", new(0.667f, 900f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 15), AmmoRadius: 3.75f, Color: AmmoColor.Blue, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 500f, AutoTarget: false));
    public static WeaponType TwinChainGun { get; } = new("TwinChainGun", new(14f, 550f, 3f, 1, Scatter: 0.015f, Damage: 1), AddOn: new(new Vector2(-12f, 0f), ArcOffset: 0f, MathF.PI / 8f, 360f, AutoTarget: false));
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 200f, 8f, 1, Scatter: 0.4f, ShotLifetime: 1.2f, Damage: 1), AmmoRadius: 5f, Color: AmmoColor.Green, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 250f, AutoTarget: false));
    public static WeaponType PointDefenceTurret { get; } = new("PointDefenceTurret", new(5f, 300f, 5f, 1, Scatter: 0.05f, Damage: 1), AddOn: new(Vector2.Zero, ArcOffset: MathF.PI, MathF.PI * 3 / 2f, 280f));
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 1));

    public static readonly WeaponType[] All = [MachineGun, Shotgun, RailGun, TwinChainGun, AcidBubbleSpray, PointDefenceTurret, LoadTestWeapon];
    public static readonly WeaponType[] AddOnWeapons = [.. All.Where(w => w.AddOn != null)];

    public static WeaponType? FromName(string name) => All.FirstOrDefault(w => w.Name == name);
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
    public static WeaponLoadout SideShot { get; } = new("Side mounted canister shots", [
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun)]);

    public static WeaponLoadout MachineGunShotgun { get; } = new("MachineGunShotgun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 1260f, WeaponType.MachineGun),
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun)]);

    public static WeaponLoadout RailGun { get; } = new("RailGun", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 2000f, WeaponType.RailGun, AutoTarget: false)]);

    public static WeaponLoadout TwinChainGun { get; } = new("TwinChainGun", [
        new(new Vector2(-12f, 0f), ArcOffset: 0f, MathF.PI / 8f, 1360f, WeaponType.TwinChainGun, AutoTarget: false),
        new(new Vector2(12f, 0f), ArcOffset: 0f, MathF.PI / 8f, 1360f, WeaponType.TwinChainGun, AutoTarget: false)]);

    public static WeaponLoadout AcidBubbleSpray { get; } = new("AcidBubbleSpray", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 650f, WeaponType.AcidBubbleSpray, AutoTarget: false)]);

    public static WeaponLoadout PointDefenceTurret { get; } = new("PointDefenceTurret", [
        new(Vector2.Zero, ArcOffset: MathF.PI, MathF.PI * 3 / 2f, 680f, WeaponType.PointDefenceTurret)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(Vector2.Zero, ArcOffset: -MathF.PI / 4f, MathF.PI * 3 / 4f, 580f, WeaponType.LoadTestWeapon)]);

}
