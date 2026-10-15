using Spacevors.Domain.Components; // AmmoColor

namespace Spacevors.Domain.Stats;

// Mount slew rates. `Instant` is faster than any hull can turn, so test weapons and the perf
// scenarios keep aiming instantly instead of having to catch up with their target.
public static class SlewRate
{
    public const float Instant = 1000f;
}

public readonly record struct WeaponStats(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount,
    float Scatter,
    float ShotLifetime = 3f,
    int Damage = 1,
    // How fast the turret's mount slews inside its arc, rad/s **relative to the hull**. The hull
    // carries the mount for free, so a turret rated below its ship's turn rate loses its target
    // while the ship spins and only catches up on straight flight.
    float TurnSpeed = SlewRate.Instant,
    // Hitscan weapons fire instant LaserBeams instead of travelling Ammo:
    // AmmoSpeed/ShotLifetime are unused, aim skips lead prediction, beam length = turret range.
    bool Hitscan = false,
    // Hitscan multi-pellet weapons fire parallel beams offset perpendicular to the aim by
    // this spacing per pellet step (0 = all beams from the same point).
    float BeamSpacing = 0f);

// Static weapon identity: what fits where, and what the weapon is. Everything the turret carries
// at runtime (fire rate, damage, slew rate) is in WeaponStats.
public readonly record struct WeaponType(
    string Name,
    WeaponStats Stats,
    TurretSize Size,
    // Base mount range in px; Range upgrades scale the turret's live range from here.
    float Range,
    float AmmoRadius = 2.5f,
    AmmoColor Color = AmmoColor.Yellow,
    bool AutoTarget = true)
{
    public static WeaponType MachineGun { get; } = new("MachineGun", new(8f, 420f, 10f, 1, Scatter: 0.033f, Damage: 1, TurnSpeed: TurnDeg(180f)), TurretSize.Large, 1260f);
    public static WeaponType Shotgun { get; } = new("Shotgun", new(2f, 350f, 2.5f, 3, Scatter: 0.04f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 860f);

    // Heavy guns: slow mounts, so they punish a spinning hull. RailGun range sits between the old
    // starting-ship 2000 px and the old level-up-mount 500 px now that one weapon has one range.
    public static WeaponType RailGun { get; } = new("RailGun", new(0.667f, 2500f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 15, TurnSpeed: TurnDeg(55f)), TurretSize.Large, 1200f, AmmoRadius: 3.75f, Color: AmmoColor.Blue, AutoTarget: false);
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 200f, 8f, 1, Scatter: 0.4f, ShotLifetime: 1.2f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 650f, AmmoRadius: 5f, Color: AmmoColor.Green, AutoTarget: false);
    public static WeaponType PointDefenceTurret { get; } = new("PointDefenceTurret", new(5f, 300f, 5f, 1, Scatter: 0.05f, Damage: 1, TurnSpeed: TurnDeg(220f)), TurretSize.Small, 680f);

    // Lasers (hitscan): instant beams out to turret range, damage the first thing hit only.
    public static WeaponType LaserLance { get; } = new("LaserLance", new(FireRate: 0.8f, AmmoSpeed: 0f, KickbackForce: 25f, PelletCount: 1, Scatter: 0f, Damage: 12, TurnSpeed: TurnDeg(90f), Hitscan: true), TurretSize.Large, 1400f, AmmoRadius: 3f, Color: AmmoColor.Blue);
    public static WeaponType PrismLance { get; } = new("PrismLance", new(FireRate: 0.6f, AmmoSpeed: 0f, KickbackForce: 30f, PelletCount: 3, Scatter: 0f, Damage: 5, TurnSpeed: TurnDeg(70f), Hitscan: true, BeamSpacing: 14f), TurretSize.Large, 1200f, AmmoRadius: 2.5f, Color: AmmoColor.Violet);
    public static WeaponType RippleCutter { get; } = new("RippleCutter", new(FireRate: 1.5f, AmmoSpeed: 0f, KickbackForce: 10f, PelletCount: 1, Scatter: 0f, Damage: 6, TurnSpeed: TurnDeg(200f), Hitscan: true), TurretSize.Small, 260f, AmmoRadius: 16f, Color: AmmoColor.Violet);

    // Particle accelerators: bullets fast enough to draw streaks; swept collision keeps them honest.
    public static WeaponType Cyclotron { get; } = new("Cyclotron", new(FireRate: 12f, AmmoSpeed: 4200f, KickbackForce: 2f, PelletCount: 1, Scatter: 0.015f, ShotLifetime: 0.6f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 1360f, AmmoRadius: 1.5f, Color: AmmoColor.Cyan);
    public static WeaponType GravitonBurst { get; } = new("GravitonBurst", new(FireRate: 1.2f, AmmoSpeed: 3000f, KickbackForce: 15f, PelletCount: 5, Scatter: 0f, ShotLifetime: 0.8f, Damage: 3, TurnSpeed: TurnDeg(80f)), TurretSize.Large, 1600f, AmmoRadius: 4f, Color: AmmoColor.Cyan);

    // Wide slow flak disc: the grazing width does the aiming at close range.
    public static WeaponType FlakCurtain { get; } = new("FlakCurtain", new(FireRate: 10f, AmmoSpeed: 220f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.5f, ShotLifetime: 1f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 220f, AmmoRadius: 9f, Color: AmmoColor.Green);

    // Perf scenarios only: it must aim instantly so seeded volleys keep looking like fired ones.
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 1), TurretSize.Large, 580f);

    public static readonly WeaponType[] All = [MachineGun, Shotgun, RailGun, AcidBubbleSpray, PointDefenceTurret, LaserLance, PrismLance, RippleCutter, Cyclotron, GravitonBurst, FlakCurtain, LoadTestWeapon];

    // Weapons a level-up new-weapon card may offer.
    public static readonly WeaponType[] NewWeaponPool = [.. All.Where(w => w.Name != LoadTestWeapon.Name)];

    public static WeaponType? FromName(string name) => All.FirstOrDefault(w => w.Name == name);

    static float TurnDeg(float degrees) => Angle.FromDeg(degrees);
}

// One weapon of a starting loadout, mounted into a named hull slot.
public readonly record struct WeaponMount(string SlotName, WeaponType Weapon);

// The turrets a ship starts with. Slot geometry (position, arc, size) comes from the ship's layout.
public readonly record struct WeaponLoadout(string Name, IReadOnlyList<WeaponMount> Mounts)
{
    // A single weapon in the main gun slot: what a diagnostics ship carries.
    public static WeaponLoadout InMainGun(WeaponType weapon) => new(weapon.Name, [new WeaponMount(TurretSlotName.MainGun, weapon)]);

    public static WeaponLoadout Cyclotron { get; } = new("Cyclotron stream", [
        new(TurretSlotName.MainGun, WeaponType.Cyclotron)]);

    public static WeaponLoadout LaserLanceCyclotron { get; } = new("LaserLanceCyclotron", [
        new(TurretSlotName.MainGun, WeaponType.LaserLance),
        new(TurretSlotName.SmallLeft, WeaponType.Cyclotron)]);

    // Four mounts, three distinct weapons: machinegun + twin side shotguns + close-in flak disc.
    public static WeaponLoadout HeavyFlak { get; } = new("HeavyFlak", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
        new(TurretSlotName.SmallRight, WeaponType.Shotgun),
        new(TurretSlotName.SmallBack, WeaponType.FlakCurtain)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(TurretSlotName.MainGun, WeaponType.LoadTestWeapon)]);
}
