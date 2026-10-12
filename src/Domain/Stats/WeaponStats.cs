using Spacevors.Domain.Components; // AmmoColor

namespace Spacevors.Domain.Stats;

public readonly record struct WeaponStats(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount,
    float Scatter,
    float ShotLifetime = 3f,
    int Damage = 1,
    // Hitscan weapons fire instant LaserBeams instead of travelling Ammo:
    // AmmoSpeed/ShotLifetime are unused, aim skips lead prediction, beam length = turret range.
    bool Hitscan = false,
    // Hitscan multi-pellet weapons fire parallel beams offset perpendicular to the aim by
    // this spacing per pellet step (0 = all beams from the same point).
    float BeamSpacing = 0f);

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
    public static WeaponType RailGun { get; } = new("RailGun", new(0.667f, 2500f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 15), AmmoRadius: 3.75f, Color: AmmoColor.Blue, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 500f, AutoTarget: false));
    public static WeaponType TwinChainGun { get; } = new("TwinChainGun", new(14f, 550f, 3f, 1, Scatter: 0.015f, Damage: 1), AddOn: new(new Vector2(-12f, 0f), ArcOffset: 0f, MathF.PI / 8f, 360f, AutoTarget: false));
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 200f, 8f, 1, Scatter: 0.4f, ShotLifetime: 1.2f, Damage: 1), AmmoRadius: 5f, Color: AmmoColor.Green, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 250f, AutoTarget: false));
    public static WeaponType PointDefenceTurret { get; } = new("PointDefenceTurret", new(5f, 300f, 5f, 1, Scatter: 0.05f, Damage: 1), AddOn: new(Vector2.Zero, ArcOffset: MathF.PI, MathF.PI * 3 / 2f, 280f));

    // Lasers (hitscan): instant beams out to turret range, damage the first thing hit only.
    public static WeaponType LaserLance { get; } = new("LaserLance", new(FireRate: 0.8f, AmmoSpeed: 0f, KickbackForce: 25f, PelletCount: 1, Scatter: 0f, Damage: 12, Hitscan: true), AmmoRadius: 3f, Color: AmmoColor.Blue, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 1400f));
    public static WeaponType PrismLance { get; } = new("PrismLance", new(FireRate: 0.6f, AmmoSpeed: 0f, KickbackForce: 30f, PelletCount: 3, Scatter: 0f, Damage: 5, Hitscan: true, BeamSpacing: 14f), AmmoRadius: 2.5f, Color: AmmoColor.Violet, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 1200f));
    public static WeaponType RippleCutter { get; } = new("RippleCutter", new(FireRate: 1.5f, AmmoSpeed: 0f, KickbackForce: 10f, PelletCount: 1, Scatter: 0f, Damage: 6, Hitscan: true), AmmoRadius: 16f, Color: AmmoColor.Violet, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI * 2f, 260f));

    // Particle accelerators: bullets fast enough to draw streaks; swept collision keeps them honest.
    public static WeaponType Cyclotron { get; } = new("Cyclotron", new(FireRate: 12f, AmmoSpeed: 4200f, KickbackForce: 2f, PelletCount: 1, Scatter: 0.015f, ShotLifetime: 0.6f, Damage: 1), AmmoRadius: 1.5f, Color: AmmoColor.Cyan, AddOn: new(new Vector2(-12f, 0f), ArcOffset: 0f, MathF.PI / 8f, 1360f));
    public static WeaponType GravitonBurst { get; } = new("GravitonBurst", new(FireRate: 1.2f, AmmoSpeed: 3000f, KickbackForce: 15f, PelletCount: 5, Scatter: 0f, ShotLifetime: 0.8f, Damage: 3), AmmoRadius: 4f, Color: AmmoColor.Cyan, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 6f, 1600f));

    // Wide slow flak disc: the grazing width does the aiming at close range.
    public static WeaponType FlakCurtain { get; } = new("FlakCurtain", new(FireRate: 10f, AmmoSpeed: 220f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.5f, ShotLifetime: 1f, Damage: 1), AmmoRadius: 9f, Color: AmmoColor.Green, AddOn: new(Vector2.Zero, ArcOffset: 0f, MathF.PI * 2f, 220f));

    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 1));

    public static readonly WeaponType[] All = [MachineGun, Shotgun, RailGun, TwinChainGun, AcidBubbleSpray, PointDefenceTurret, LaserLance, PrismLance, RippleCutter, Cyclotron, GravitonBurst, FlakCurtain, LoadTestWeapon];
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
    // Single-turret loadout using the weapon's own AddOn mount — what a ship carries when it
    // picks that weapon at a level-up. Used by the diagnostics ships that exist to test one weapon.
    public static WeaponLoadout MountedAsUpgrade(WeaponType weapon)
    {
        var mount = weapon.AddOn ?? throw new InvalidOperationException($"{weapon.Name} has no upgrade mount");
        return new WeaponLoadout(weapon.Name, [
            new(mount.Offset, mount.ArcOffset, mount.ArcAngle, mount.Range, weapon, mount.AutoTarget)]);
    }

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

    public static WeaponLoadout Cyclotron { get; } = new("Cyclotron", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 6f, 1360f, WeaponType.Cyclotron)]);

    public static WeaponLoadout LaserLanceCyclotron { get; } = new("LaserLanceCyclotron", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 1400f, WeaponType.LaserLance),
        new(new Vector2(-12f, 0f), ArcOffset: 0f, MathF.PI / 6f, 1360f, WeaponType.Cyclotron)]);

    // Four turrets, three distinct weapons: machinegun + twin side shotguns + close-in flak disc.
    public static WeaponLoadout HeavyFlak { get; } = new("HeavyFlak", [
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 1260f, WeaponType.MachineGun),
        new(new Vector2(-12f, 0f), ArcOffset: -MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun),
        new(new Vector2(12f, 0f), ArcOffset: MathF.PI / 2f, MathF.PI / 4f, 860f, WeaponType.Shotgun),
        new(Vector2.Zero, ArcOffset: 0f, MathF.PI * 2f, 220f, WeaponType.FlakCurtain)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(Vector2.Zero, ArcOffset: -MathF.PI / 4f, MathF.PI * 3 / 4f, 580f, WeaponType.LoadTestWeapon)]);

}
