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
    // Half-angle (rad) of the cone a shot pattern covers: several pellets spread evenly across it,
    // a lone pellet rolls randomly inside it.
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
    bool AutoTarget = true,
    // The first level a new-weapon card may offer this gun at. Starter hulls carry tier-1 guns only;
    // the stronger guns arrive with the first milestone hand.
    int UnlockLevel = 1)
{
    // Tier 1: the guns a starter hull carries. Damage per second (fire rate × pellets × damage) rises
    // as delivery gets less certain and reach gets shorter; a hitscan gun, which can never miss on
    // travel, sits under the projectile gun of its own band. Rules in plans/WEAPON_BALANCE.md.
    public static WeaponType MachineGun { get; } = new("MachineGun", new(4f, 600f, 8f, 1, Scatter: 0.033f, Damage: 3, TurnSpeed: TurnDeg(180f)), TurretSize.Large, 1350f, AmmoRadius: 3f);

    // Three slugs in a cone: pointing close enough is enough, and a fire-rate card adds a fourth.
    public static WeaponType Shotgun { get; } = new("Shotgun", new(3.5f, 400f, 2.5f, 3, Scatter: 0.12f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f);

    // Heavy gun: a slow mount punishes a spinning hull. RailGun is the one gun whose ammo can fly the
    // 3x reach a clicked target grants (2500 px/s × 2 s), which is what makes it the sniper — not its
    // base range. AutoTarget off here means no lead prediction: it aims where the target is.
    public static WeaponType RailGun { get; } = new("RailGun", new(1f, 2500f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 14, TurnSpeed: TurnDeg(55f)), TurretSize.Large, 1500f, AmmoRadius: 3.75f, Color: AmmoColor.Blue, AutoTarget: false, UnlockLevel: 5);

    // Corrosive spray: slow bubbles in a wide cone, covering an area instead of a target. The lifetime
    // is what makes the 900 px on its card reachable at 400 px/s.
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 400f, 4f, 1, Scatter: 0.45f, ShotLifetime: 3f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f, AmmoRadius: 5f, Color: AmmoColor.Green, UnlockLevel: 5);
    // Twin close-range snap: the fastest mount in the game, so it keeps its aim on a ship that is
    // circling the hull instead of the one it locked at the start.
    public static WeaponType InterceptorTurret { get; } = new("InterceptorTurret", new(4.5f, 1200f, 2f, 2, Scatter: 0.2f, Damage: 1, TurnSpeed: TurnDeg(360f)), TurretSize.Small, 900f);

    // Lasers (hitscan): instant beams out to turret range, damaging the first thing hit only. Instant
    // delivery is the cheapest delivery in the game, so beams buy the least damage per volley.
    public static WeaponType LaserLance { get; } = new("LaserLance", new(FireRate: 2.4f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 1, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(90f), Hitscan: true), TurretSize.Large, 1650f, AmmoRadius: 3f, Color: AmmoColor.Blue, UnlockLevel: 5);

    // Three beams 70 px apart: the gun that hits a formation instead of one hull.
    public static WeaponType PrismLance { get; } = new("PrismLance", new(FireRate: 0.75f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 3, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(70f), Hitscan: true, BeamSpacing: 70f), TurretSize.Large, 1350f, AmmoRadius: 2.5f, Color: AmmoColor.Violet, UnlockLevel: 5);

    // Point-blank blade: 32 px wide, reaching into the enemy's firing ring, and never needing lead.
    public static WeaponType RippleCutter { get; } = new("RippleCutter", new(FireRate: 2.25f, AmmoSpeed: 0f, KickbackForce: 10f, PelletCount: 1, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(200f), Hitscan: true), TurretSize.Small, 600f, AmmoRadius: 16f, Color: AmmoColor.Violet);

    // Particle accelerators: bullets fast enough to draw streaks; swept collision keeps them honest.
    // Cyclotron has the most forgiving delivery and the longest auto reach, so it pays the least DPS.
    public static WeaponType Cyclotron { get; } = new("Cyclotron", new(FireRate: 8f, AmmoSpeed: 4200f, KickbackForce: 2f, PelletCount: 1, Scatter: 0.015f, ShotLifetime: 0.6f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 1600f, AmmoRadius: 1.5f, Color: AmmoColor.Cyan);
    public static WeaponType GravitonBurst { get; } = new("GravitonBurst", new(FireRate: 1.1f, AmmoSpeed: 3000f, KickbackForce: 15f, PelletCount: 5, Scatter: 0f, ShotLifetime: 0.8f, Damage: 3, TurnSpeed: TurnDeg(80f)), TurretSize.Large, 1700f, AmmoRadius: 4f, Color: AmmoColor.Cyan, UnlockLevel: 5);

    // Wide flak disc: the disc width and the cone do the aiming.
    public static WeaponType FlakCurtain { get; } = new("FlakCurtain", new(FireRate: 12f, AmmoSpeed: 500f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.5f, ShotLifetime: 1.5f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 600f, AmmoRadius: 9f, Color: AmmoColor.Green);

    // Perf scenarios only: it must aim instantly so seeded volleys keep looking like fired ones.
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 1), TurretSize.Large, 580f);

    public static readonly WeaponType[] All = [MachineGun, Shotgun, RailGun, AcidBubbleSpray, InterceptorTurret, LaserLance, PrismLance, RippleCutter, Cyclotron, GravitonBurst, FlakCurtain, LoadTestWeapon];

    // Weapons a level-up new-weapon card may offer at `level`: never the load-test spray, and never
    // a gun whose tier has not unlocked yet.
    public static WeaponType[] NewWeaponPoolFor(int level) =>
        [.. All.Where(w => w.Name != LoadTestWeapon.Name && w.UnlockLevel <= level)];

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

    // Scout: the longest-reach gun it can carry plus the fast-mount twin covering one flank.
    public static WeaponLoadout CyclotronRepeater { get; } = new("CyclotronRepeater", [
        new(TurretSlotName.MainGun, WeaponType.Cyclotron),
        new(TurretSlotName.SmallLeft, WeaponType.InterceptorTurret)]);

    // Fighter: a heavy chewer ahead and a cone of slugs covering the other forward arc.
    public static WeaponLoadout MachineGunShotgun { get; } = new("MachineGunShotgun", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun)]);

    // Shadow: every mount holds a 600 px blade, so the hull has to fly into the fight to be armed.
    public static WeaponLoadout RippleCutterTrio { get; } = new("RippleCutterTrio", [
        new(TurretSlotName.MainGun, WeaponType.RippleCutter),
        new(TurretSlotName.SmallLeft, WeaponType.RippleCutter),
        new(TurretSlotName.SmallRight, WeaponType.RippleCutter)]);

    // Four mounts, three distinct weapons: machinegun + twin side shotguns + close-in flak disc.
    public static WeaponLoadout HeavyFlak { get; } = new("HeavyFlak", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
        new(TurretSlotName.SmallRight, WeaponType.Shotgun),
        new(TurretSlotName.SmallBack, WeaponType.FlakCurtain)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(TurretSlotName.MainGun, WeaponType.LoadTestWeapon)]);
}
