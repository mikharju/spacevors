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
    // Homing weapons fire guided Missiles instead of dumb Ammo: the round locks on whatever the gun
    // acquired at launch and steers toward it, so it needs no lead solution and no clear line — only
    // a fuse long enough to cross the distance and a turn limit good enough to catch what it hunts.
    // For a homing gun `AmmoSpeed` is the speed the round builds **to**, not the speed it leaves at:
    // see MissileLaunchFraction. The turn radius rule below therefore reads against the top speed,
    // which is the least forgiving end of the flight.
    bool Homing = false,
    // Steering limit of a homing round, rad/s. Its turn radius is AmmoSpeed / HomingTurnRate: below a
    // target's own turn radius the missile cannot catch it, which is what keeps a lock dodgeable.
    float HomingTurnRate = 0f,
    // Whether a `FireRate` card on this gun adds another shot to the volley (the Shotgun tradition) or
    // genuinely fires the same shots sooner. Guided rounds stay live for seconds, so a gun that grows
    // its volley is a standing claim on the round budget (R17): most homing guns do not grow.
    bool SalvoGrows = true,
    // Hitscan multi-pellet weapons fire parallel beams offset perpendicular to the aim by
    // this spacing per pellet step (0 = all beams from the same point).
    float BeamSpacing = 0f,
    // Hit points of one guided round (R19). The other side's fire can shoot the missile down, so a
    // guided round is a body with health; dumb ammo is not, and this is unused there.
    int MissileHealth = 1)
{
    // R17: how many guided rounds one mount may put in flight per second. Guided rounds live for
    // seconds and cannot be dodged by luck, so their budget is far tighter than the 12 rounds/s of
    // dumb ammo (R7). It is also the ceiling on a homing gun's growing volley (R18): a FireRate card
    // stops offering "+1 shot" once the next shot would break it.
    public const float HomingRoundBudget = 4f;

    // A guided round leaves its mount at this fraction of its card speed and builds up to that speed
    // under MissileLaunchAcceleration below. The slow start is what the growing exhaust shows, and it is
    // what makes firing along the hull's own nose honest: a round that has to turn is a round the target
    // can see leaving.
    public const float MissileLaunchFraction = 0.7f;

    // px/s² of that build-up. It is deliberately not an `Acceleration` component: guidance rewrites the
    // round's speed at the tick boundary, so the collision pass still reads a constant velocity between
    // ticks, and the round's speed stays a single number that the turn radius is computed from.
    public const float MissileLaunchAcceleration = 200f;

    // R19: how tough one guided round can be made. A round that eats four hits of ordinary fire stops
    // being a body worth shooting at and becomes a second magazine, so three is the ceiling.
    public const int MissileHealthCeiling = 3;

    // R19: whether a `MissileHealth` card on this gun has somewhere to go: only a guided round carries
    // hit points, and only up to the ceiling. Same reason the "+1 shot" card is withdrawn at R18.
    public bool CanToughenRound => Homing && MissileHealth < MissileHealthCeiling;

    // R18: whether a `FireRate` card on this gun still has somewhere to go as "+1 shot". Volley guns
    // grow forever as they always have; a guided volley stops when the next shot would break the
    // guided-round budget, and a gun that never grows its volley takes the card as a rate instead.
    // Both the offer (LevelUpSystem) and the apply (GameSession) read this, so a card is never a lie.
    public bool CanGrowSalvo => SalvoGrows && PelletCount > 1 && (!Homing || FireRate * (PelletCount + 1) <= HomingRoundBudget);
}

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

    // The big brother of the Shotgun: three fat slugs in a real cone, one volley every third second.
    // Its reach stops short of the MachineGun's because the cone spreads — at range a lone hull walks
    // out between the slugs — so close-in it out-damages every other tier-1 large gun and far out it
    // out-deals nothing. The large-mount answer to a hull that fights at one range: point and hold.
    public static WeaponType ScatterCannon { get; } = new("ScatterCannon", new(3f, 400f, 6f, 3, Scatter: 0.18f, ShotLifetime: 3f, Damage: 2, TurnSpeed: TurnDeg(120f)), TurretSize.Large, 1000f, AmmoRadius: 3.5f);

    // Three slugs in a cone: pointing close enough is enough, and a fire-rate card adds a fourth.
    public static WeaponType Shotgun { get; } = new("Shotgun", new(3.5f, 400f, 2.5f, 3, Scatter: 0.12f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f);

    // Heavy gun: a slow mount punishes a spinning hull. RailGun is the one gun whose ammo can fly the
    // 3x reach a clicked target grants (2500 px/s × 2 s), which is what makes it the sniper — not its
    // base range. AutoTarget off here means no lead prediction: it aims where the target is.
    public static WeaponType RailGun { get; } = new("RailGun", new(1f, 2500f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 14, TurnSpeed: TurnDeg(55f)), TurretSize.Large, 1500f, AmmoRadius: 3.75f, Color: AmmoColor.Blue, AutoTarget: false, UnlockLevel: 5);

    // Corrosive spray: slow bubbles in a wide cone, covering an area instead of a target. The lifetime
    // is what makes the 900 px on its card reachable at 400 px/s. DarkGreen keeps it clearly apart
    // from the bright mint health orbs.
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(12f, 400f, 4f, 1, Scatter: 0.45f, ShotLifetime: 3f, Damage: 1, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f, AmmoRadius: 5f, Color: AmmoColor.DarkGreen, UnlockLevel: 5);
    // Twin close-range snap: the fastest mount in the game, so it keeps its aim on a ship that is
    // circling the hull instead of the one it locked at the start.
    public static WeaponType InterceptorTurret { get; } = new("InterceptorTurret", new(4.5f, 1200f, 2f, 2, Scatter: 0.2f, Damage: 1, TurnSpeed: TurnDeg(360f)), TurretSize.Small, 900f);

    // Lasers (hitscan): instant beams out to turret range, damaging the first thing hit only. Instant
    // delivery is the cheapest delivery in the game, so beams buy the least damage per volley.
    public static WeaponType LaserLance { get; } = new("LaserLance", new(FireRate: 2.4f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 1, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(90f), Hitscan: true), TurretSize.Large, 1650f, AmmoRadius: 3f, Color: AmmoColor.Blue, UnlockLevel: 5);

    // Three beams 70 px apart: the gun that hits a formation instead of one hull.
    public static WeaponType PrismLance { get; } = new("PrismLance", new(FireRate: 0.7f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 3, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(70f), Hitscan: true, BeamSpacing: 70f), TurretSize.Large, 1350f, AmmoRadius: 2.5f, Color: AmmoColor.Violet, UnlockLevel: 5);

    // Point-blank blade: 16 px wide, reaching into the enemy's firing ring, and never needing lead.
    public static WeaponType RippleCutter { get; } = new("RippleCutter", new(FireRate: 2f, AmmoSpeed: 0f, KickbackForce: 10f, PelletCount: 1, Scatter: 0f, Damage: 4, TurnSpeed: TurnDeg(200f), Hitscan: true), TurretSize.Small, 600f, AmmoRadius: 8f, Color: AmmoColor.Violet);

    // Particle accelerators: bullets fast enough to draw streaks; swept collision keeps them honest.
    // Cyclotron has the most forgiving delivery and the longest auto reach, so it pays the least DPS.
    public static WeaponType Cyclotron { get; } = new("Cyclotron", new(FireRate: 8f, AmmoSpeed: 4200f, KickbackForce: 2f, PelletCount: 1, Scatter: 0.015f, ShotLifetime: 0.6f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 1600f, AmmoRadius: 1.5f, Color: AmmoColor.Cyan);
    public static WeaponType GravitonBurst { get; } = new("GravitonBurst", new(FireRate: 1.1f, AmmoSpeed: 3000f, KickbackForce: 15f, PelletCount: 5, Scatter: 0f, ShotLifetime: 0.8f, Damage: 3, TurnSpeed: TurnDeg(80f)), TurretSize.Large, 1700f, AmmoRadius: 4f, Color: AmmoColor.Cyan, UnlockLevel: 5);

    // Wide flak disc: the disc width and the cone do the aiming.
    public static WeaponType FlakCurtain { get; } = new("FlakCurtain", new(FireRate: 12f, AmmoSpeed: 500f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.5f, ShotLifetime: 1.5f, Damage: 1, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 600f, AmmoRadius: 9f, Color: AmmoColor.Green);

    // Guided rounds. A missile is not a slower bullet: it is delivery that cannot be missed, so it
    // buys the *least* damage of its reach band (R15), and its turn radius — AmmoSpeed / HomingTurnRate
    // — has to stay wider than what it hunts, or a lock becomes a death sentence (R16). They are all
    // slow-firing: R17 caps a mount at 4 guided rounds in flight per second.
    // SeekerPod: the small hull's first missile. It leaves at 294 px/s and builds to 420 px/s in 0.6 s, so
    // its turn radius runs from 112 px at launch to 160 px at speed — wider than anything it hunts at both
    // ends of the flight (R16). Its FireRate card fires it more often rather than more at once.
    public static WeaponType SeekerPod { get; } = new("SeekerPod", new(FireRate: 1.2f, AmmoSpeed: 420f, KickbackForce: 4f, PelletCount: 1, Scatter: 0f, ShotLifetime: 4f, Damage: 3, TurnSpeed: TurnDeg(180f), Homing: true, HomingTurnRate: TurnDeg(150f), SalvoGrows: false), TurretSize.Small, 900f, AmmoRadius: 4f, Color: AmmoColor.Orange, UnlockLevel: 5);

    // Swarmlet: the swarm missile, and the only homing gun allowed to grow its volley. Three cheap
    // locked rounds at once is the point; the growth stops at the R17 ceiling.
    public static WeaponType Swarmlet { get; } = new("Swarmlet", new(FireRate: 0.8f, AmmoSpeed: 490f, KickbackForce: 3f, PelletCount: 3, Scatter: 0.25f, ShotLifetime: 2.5f, Damage: 2, TurnSpeed: TurnDeg(200f), Homing: true, HomingTurnRate: TurnDeg(170f)), TurretSize.Small, 600f, AmmoRadius: 4f, Color: AmmoColor.Orange, UnlockLevel: 5);

    // HavocTorpedo: the big slow hull-killer. 10 damage per shot breaks R5's per-shot cap the way
    // RailGun does — at 0.6/s nothing else in the game can carry a round that big — and it pays for
    // the licence with the least DPS in its band (R15). Its turn radius never drops below 250 px, so it
    // can only ever lock on hulls that cannot out-turn it.
    public static WeaponType HavocTorpedo { get; } = new("HavocTorpedo", new(FireRate: 0.6f, AmmoSpeed: 280f, KickbackForce: 15f, PelletCount: 1, Scatter: 0f, ShotLifetime: 8f, Damage: 10, TurnSpeed: TurnDeg(90f), Homing: true, HomingTurnRate: TurnDeg(45f), SalvoGrows: false), TurretSize.Large, 1350f, AmmoRadius: 6f, Color: AmmoColor.Orange, UnlockLevel: 10);

    // Perf scenarios only: it must aim instantly so seeded volleys keep looking like fired ones.
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 1), TurretSize.Large, 580f);

    public static readonly WeaponType[] All = [MachineGun, ScatterCannon, Shotgun, RailGun, AcidBubbleSpray, InterceptorTurret, LaserLance, PrismLance, RippleCutter, Cyclotron, GravitonBurst, FlakCurtain, SeekerPod, Swarmlet, HavocTorpedo, LoadTestWeapon];

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
    // A single weapon in the main gun slot: what a diagnostics ship carries, and the Falcon too.
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

    // Eagle: the scatter cannon answers what gets close, the fast flank turret answers what circles.
    public static WeaponLoadout ScatterCannonFlank { get; } = new("ScatterCannonFlank", [
        new(TurretSlotName.MainGun, WeaponType.ScatterCannon),
        new(TurretSlotName.SmallLeft, WeaponType.InterceptorTurret)]);

    // Manta: the gun stays ahead while the hull slides sideways — a heavy chewer and a turret that
    // covers the near flank without ever having to cross to the other side.
    public static WeaponLoadout MachineGunRepeater { get; } = new("MachineGunRepeater", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.SmallLeft, WeaponType.InterceptorTurret)]);

    // Raptor: one gun ahead and a cone of slugs on one broadside — the other heavy mount starts
    // empty, because this hull is built to be handed a second big gun.
    public static WeaponLoadout MachineGunBroadside { get; } = new("MachineGunBroadside", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.HeavyLeft, WeaponType.Shotgun)]);

    public static WeaponLoadout LoadTestWeapon { get; } = new("LoadTestWeapon", [
        new(TurretSlotName.MainGun, WeaponType.LoadTestWeapon)]);
}
