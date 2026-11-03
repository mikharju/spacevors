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
    // Half-angle (rad) of the cone a shot pattern covers: the volley keeps one round on the aim line
    // and fans the rest evenly across the cone, a lone pellet rolls randomly inside it.
    float Scatter,
    float ShotLifetime = 3f,
    int Damage = 4,
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
    // guided round is a body with health; dumb ammo is not, and this is unused there. 4 at the ×4 scale
    // (R32) is what a `MissileHealth` card's +50% is taken against, so it buys a whole 2 and not a
    // rounded-up half.
    int MissileHealth = 4,
    // Armor piercing of one round of this gun (plans/ARMOR_AND_PIERCE.md): it cancels the target's
    // Armor before subtraction, so a pierce card is +4 damage only against an armored hull and worth
    // nothing elsewhere — the honest shape for a situational card. No gun starts with pierce (R30:
    // the bonus arrives as the card, never pre-charged or pre-granted in the table).
    int Pierce = 0)
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
    // being a body worth shooting at and becomes a second magazine. At the ×4 scale (R32) that is 12 —
    // still four hits of the 4-damage swarm bullet, and still three times a pod round's 4 hp.
    public const int MissileHealthCeiling = 12;

    // R19: whether a `MissileHealth` card on this gun still has somewhere to go: only a guided round
    // carries hit points, and the next whole step must land at or under the ceiling — never a card the
    // rules would refuse to honour, the same reason the "+1 shot" card is withdrawn at R18. The step is
    // the gun's percent-of-base step (`WeaponType.MissileHealthStep`), not a percent of this live copy,
    // which is what stops the card compounding with itself.
    public bool CanToughenRound(int step) => Homing && step > 0 && MissileHealth + step <= MissileHealthCeiling;

    // The most armor-pierce one gun can reach with `ArmorPierce` cards (+4, plans/ARMOR_AND_PIERCE.md).
    // Pierce 4 already cancels HeavyCannon's armor 4 flat; the ceiling stops the card stacking into a
    // stat no enemy armor exists to spend it on — the same honesty rule as the missile-health ceiling.
    public const int PierceCeiling = 8;

    // R19's rule again: a `ArmorPierce` card is withdrawn at the ceiling, never dealt as a card the
    // rules would refuse to honour. The step is the flat card step, read the same way in the offer
    // (UpgradeOffer) and the apply (GameSession), so the card is never a lie.
    public bool CanPierceMore(int step) => step > 0 && Pierce + step <= PierceCeiling;

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
    // The two mount classes (plans/WEAPON_BALANCE.md §2, R24-R28). A Small mount's gun is *support*:
    // chips, screens, area denial — never the kill. It tops out at 8 DPS (6 in the mid band), fires rounds
    // of at most 2 damage, and never reaches past this ceiling: the long band and the top of the DPS table
    // belong to the kill class, and R25 keeps a visible 1.25x step between the two.
    public const float SupportRangeCeiling = 1350f;

    public bool IsSupport => Size == TurretSize.Small;

    // R28: missile defence is the support class's job. Every Small non-guided gun may acquire a hostile
    // guided round; a kill gun never swerves off the hull that is killing it to chase a pebble, and a rack
    // does not slew at all. Keyed on the mount class rather than a per-gun list, so a chip bolted into a
    // heavy mount screens too.
    public bool InterceptsRounds => IsSupport && !Stats.Homing;

    // R19: how many hit points one `MissileHealth` card buys this gun's rounds — 50% of the gun's **base**
    // round health, read off the table rather than off a turret that has already bought cards, which is what
    // keeps the card additive (+50% +50% = +100%) instead of compounding. The step grows with the missile: a
    // SeekerPod's 4 hp round buys +2, FarlockSalvo's 8 hp round buys +4.
    public int MissileHealthStep => UpgradeDefinition.MissileHealth.StepForBase(Stats.MissileHealth);

    // R33: how much per-shot damage one `Damage` card buys this gun — a percent of the gun's **base**
    // damage, sized by mount class (R24/R25: a support chip grows slower than a kill gun), and read off
    // the table rather than off a turret that has already bought cards, so the cards add instead of
    // compounding and the class line the table draws keeps widening over a run.
    public int DamageStep => UpgradeDefinition.PercentStep(Stats.Damage, UpgradeDefinition.DamagePercentFor(IsSupport));

    // Tier 1: the guns a starter hull carries. Damage per second (fire rate × pellets × damage) rises
    // as delivery gets less certain and reach gets shorter; a hitscan gun, which can never miss on
    // travel, sits under the projectile gun of its own band. Every damage number here is at R32's ×4
    // scale — the same game read in finer steps. Rules in plans/WEAPON_BALANCE.md.
    public static WeaponType MachineGun { get; } = new("MachineGun", new(4f, 600f, 8f, 1, Scatter: 0.033f, Damage: 12, TurnSpeed: TurnDeg(180f)), TurretSize.Large, 1350f, AmmoRadius: 3f);

    // The big brother of the Shotgun: three fat slugs in a real cone, one volley every third second.
    // Its reach stops short of the MachineGun's because the cone spreads — at range a lone hull walks
    // out between the slugs — so close-in it out-damages every other tier-1 large gun and far out it
    // out-deals nothing. The large-mount answer to a hull that fights at one range: point and hold.
    public static WeaponType ScatterCannon { get; } = new("ScatterCannon", new(3f, 400f, 6f, 3, Scatter: 0.18f, ShotLifetime: 3f, Damage: 8, TurnSpeed: TurnDeg(120f)), TurretSize.Large, 1000f, AmmoRadius: 3.5f);

    // ScatterCannon's cone thrown out to the line: three fat slugs still at 400 px/s, but a 4.2 s fuse
    // buys the mid band. The cone only holds together inside ~700 px, so the gun pays for its 1350 px
    // reach by missing what it leads — and it buys the biggest number in the band to do so (R3: it must
    // out-DPS AcidBubbleSpray by 25%). The most live rounds per mount in the table (≈18), which is the
    // number R7 guards.
    public static WeaponType SiegeSalvo { get; } = new("SiegeSalvo", new(1.4f, 400f, 10f, 3, Scatter: 0.22f, ShotLifetime: 4.2f, Damage: 16, TurnSpeed: TurnDeg(110f)), TurretSize.Large, 1350f, AmmoRadius: 4.5f, UnlockLevel: 5);

    // Three slugs in a cone: pointing close enough is enough, and a fire-rate card adds a fourth. A
    // support gun (R24): 30 DPS, and its job is the cone that needs no aim, not the kill.
    public static WeaponType Shotgun { get; } = new("Shotgun", new(2.5f, 400f, 2.5f, 3, Scatter: 0.12f, Damage: 4, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f);

    // Heavy gun: a slow mount punishes a spinning hull. RailGun is the one gun whose ammo can fly the
    // 3x reach a clicked target grants (2500 px/s × 2 s), which is what makes it the sniper — not its
    // base range. AutoTarget off here means no lead prediction: it aims where the target is. At 60 damage
    // it is also the clearest read of the class line: one of its slugs is worth ten of a support chip.
    public static WeaponType RailGun { get; } = new("RailGun", new(1f, 2500f, 25f, 1, Scatter: 0f, ShotLifetime: 2f, Damage: 60, TurnSpeed: TurnDeg(55f)), TurretSize.Large, 1500f, AmmoRadius: 3.75f, Color: AmmoColor.Blue, AutoTarget: false, UnlockLevel: 5);

    // Corrosive spray: slow bubbles in a wide cone, covering an area instead of a target. The lifetime
    // is what makes the 900 px on its card reachable at 400 px/s. DarkGreen keeps it clearly apart
    // from the bright mint health orbs. Its 1 damage is symbolic — the gun's job is the area it denies,
    // and it is the natural carrier for whatever strips armour when that lands (§6).
    public static WeaponType AcidBubbleSpray { get; } = new("AcidBubbleSpray", new(6f, 400f, 4f, 1, Scatter: 0.45f, ShotLifetime: 3f, Damage: 4, TurnSpeed: TurnDeg(140f)), TurretSize.Small, 900f, AmmoRadius: 5f, Color: AmmoColor.DarkGreen, UnlockLevel: 5);
    // Twin close-range snap: the fastest mount in the game, so it keeps its aim on a ship that is
    // circling the hull instead of the one it locked at the start. 24 DPS of chips, and the hull's second
    // missile screen (R28) — the mount that never stops turning is the one that catches a round.
    public static WeaponType InterceptorTurret { get; } = new("InterceptorTurret", new(3f, 1200f, 2f, 2, Scatter: 0.2f, Damage: 4, TurnSpeed: TurnDeg(360f)), TurretSize.Small, 900f);

    // Three needle-fast slivers in a tight fan: the close gun that needs neither lead nor luck. R2 pays
    // for penalties, and this one has almost none to be paid for — the round is fast, the cone is tight,
    // the reach is its band's — so it is the cheapest travelling gun in the game. Its 0.6 s fuse keeps it
    // at 3.6 live rounds per mount, the cheapest answer a slow hull can put on a flank it cannot turn to.
    // At 24 DPS it ties the close band's other two chips, and that shared floor is what prices a Small
    // missile's R15 ceiling.
    public static WeaponType FlechetteFan { get; } = new("FlechetteFan", new(2f, 2200f, 2f, 3, Scatter: 0.1f, ShotLifetime: 0.6f, Damage: 4, TurnSpeed: TurnDeg(200f)), TurretSize.Small, 900f, AmmoRadius: 2f);

    // Lasers (hitscan): instant beams out to turret range, damaging the first thing hit only. Instant
    // delivery is the cheapest delivery in the game, so beams buy the least damage per volley — R1 still
    // holds them under 0.8 × the band's slowest round, which is 48 once RailGun carries 60.
    public static WeaponType LaserLance { get; } = new("LaserLance", new(FireRate: 2.6f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 1, Scatter: 0f, Damage: 16, TurnSpeed: TurnDeg(90f), Hitscan: true), TurretSize.Large, 1650f, AmmoRadius: 3f, Color: AmmoColor.Blue, UnlockLevel: 5);

    // Three beams 70 px apart: the gun that hits a formation instead of one hull. It is the weakest
    // non-guided kill gun in the table (43.2 DPS), and that is the class step the player is meant to see:
    // the best support mount tops out at 32, so even the cheapest large mount buys 1.3x more kill.
    public static WeaponType PrismLance { get; } = new("PrismLance", new(FireRate: 0.9f, AmmoSpeed: 0f, KickbackForce: 12f, PelletCount: 3, Scatter: 0f, Damage: 16, TurnSpeed: TurnDeg(70f), Hitscan: true, BeamSpacing: 70f), TurretSize.Large, 1350f, AmmoRadius: 2.5f, Color: AmmoColor.Violet, UnlockLevel: 5);

    // Point-blank blade: 16 px wide, reaching into the enemy's firing ring, and never needing lead. A
    // support chip now (R27: 8 damage a sweep), which is also what keeps it under R1's cap — FlakCurtain's
    // 32 DPS benchmark sets the aura band's ceiling at 25.6, so the two guns move together or both fail.
    public static WeaponType RippleCutter { get; } = new("RippleCutter", new(FireRate: 3f, AmmoSpeed: 0f, KickbackForce: 10f, PelletCount: 1, Scatter: 0f, Damage: 8, TurnSpeed: TurnDeg(200f), Hitscan: true), TurretSize.Small, 600f, AmmoRadius: 8f, Color: AmmoColor.Violet);

    // The reach a small mount may have: an instant 1350 px needle, which is the support class's roof
    // (R26 — the long band belongs to the kill class). 24 DPS of chips: the best anti-missile fire in the
    // game, since a 4 hp round that walks into an instant beam dies before it arrives.
    public static WeaponType Stiletto { get; } = new("Stiletto", new(FireRate: 3f, AmmoSpeed: 0f, KickbackForce: 6f, PelletCount: 1, Scatter: 0f, Damage: 8, TurnSpeed: TurnDeg(160f), Hitscan: true), TurretSize.Small, 1350f, AmmoRadius: 2f, Color: AmmoColor.Violet);

    // Particle accelerators: bullets fast enough to draw streaks; swept collision keeps them honest.
    // Cyclotron has the most forgiving delivery in the game, so it pays the least DPS — and it now stops
    // at the support roof of 1350 px instead of reaching into the long band (R26).
    public static WeaponType Cyclotron { get; } = new("Cyclotron", new(FireRate: 5f, AmmoSpeed: 4200f, KickbackForce: 2f, PelletCount: 1, Scatter: 0.015f, ShotLifetime: 0.6f, Damage: 4, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 1350f, AmmoRadius: 1.5f, Color: AmmoColor.Cyan);
    public static WeaponType GravitonBurst { get; } = new("GravitonBurst", new(FireRate: 1.1f, AmmoSpeed: 3000f, KickbackForce: 15f, PelletCount: 5, Scatter: 0f, ShotLifetime: 0.8f, Damage: 12, TurnSpeed: TurnDeg(80f)), TurretSize.Large, 1700f, AmmoRadius: 4f, Color: AmmoColor.Cyan, UnlockLevel: 5);

    // Wide flak disc: the disc width and the cone do the aiming. The strongest support gun in the game
    // at 32 DPS, which is what R25 sets the whole class's ceiling with — and the disc is wide enough to
    // be a real missile screen (R28).
    public static WeaponType FlakCurtain { get; } = new("FlakCurtain", new(FireRate: 8f, AmmoSpeed: 500f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.5f, ShotLifetime: 1.5f, Damage: 4, TurnSpeed: TurnDeg(300f)), TurretSize.Small, 600f, AmmoRadius: 9f, Color: AmmoColor.Green);

    // Guided rounds. A missile is not a slower bullet: it is delivery that cannot be missed, so it
    // buys the *least* damage of its reach band (R15), and its turn radius — AmmoSpeed / HomingTurnRate
    // — has to stay wider than what it hunts, or a lock becomes a death sentence (R16). They are all
    // slow-firing: R17 caps a mount at 4 guided rounds in flight per second.
    // SeekerPod: the small hull's first missile. It leaves at 294 px/s and builds to 420 px/s in 0.6 s, so
    // its turn radius runs from 112 px at launch to 160 px at speed — wider than anything it hunts at both
    // ends of the flight (R16). Eight damage a round instead of twelve, fired more often: the same 14.4 DPS,
    // but now a chip-sized round like the support mounts around it (R27). Its FireRate card fires it more
    // often rather than more at once.
    public static WeaponType SeekerPod { get; } = new("SeekerPod", new(FireRate: 1.8f, AmmoSpeed: 420f, KickbackForce: 4f, PelletCount: 1, Scatter: 0f, ShotLifetime: 4f, Damage: 8, TurnSpeed: TurnDeg(180f), Homing: true, HomingTurnRate: TurnDeg(150f), SalvoGrows: false), TurretSize.Small, 900f, AmmoRadius: 4f, Color: AmmoColor.Orange, UnlockLevel: 5);

    // Swarmlet: the swarm missile, and the only homing gun allowed to grow its volley. Three cheap
    // locked rounds at once is the point; the growth stops at the R17 ceiling.
    // Its rate is 0.7, not 0.8, so the guided *large* guns have a legal home: R15 caps a guided large at
    // 0.8 × the cheapest kill gun of its own band — 33.28 for FarlockSalvo, 34.56 for HavocTorpedo — and a
    // faster small missile would drag that ceiling down with it.
    public static WeaponType Swarmlet { get; } = new("Swarmlet", new(FireRate: 0.7f, AmmoSpeed: 490f, KickbackForce: 3f, PelletCount: 3, Scatter: 0.25f, ShotLifetime: 2.5f, Damage: 8, TurnSpeed: TurnDeg(200f), Homing: true, HomingTurnRate: TurnDeg(170f)), TurretSize.Small, 600f, AmmoRadius: 4f, Color: AmmoColor.Orange, UnlockLevel: 5);

    // FarlockSalvo: the long-reach lock, and the long band's only guided round. Two rounds leave at
    // 560 × MissileLaunchFraction = 392 px/s and build to 560, so the turn radius runs from 187 px at
    // launch to 267 px at cruise — far outside the 38 px a tier-10 hull turns at, which is what keeps a
    // lock at this range breakable (R16). It buys something the long band cannot otherwise deliver: a
    // round needing no lead, no travel time and no clear line — and it pays the band's floor for that
    // (R15 prices a guided round against the cheapest kill gun of its own class: 0.8 × LaserLance's 41.6).
    // Its FireRate card is a faster rate, never a bigger salvo (R18).
    public static WeaponType FarlockSalvo { get; } = new("FarlockSalvo", new(FireRate: 0.9f, AmmoSpeed: 560f, KickbackForce: 8f, PelletCount: 2, Scatter: 0.08f, ShotLifetime: 4.5f, Damage: 12, TurnSpeed: TurnDeg(120f), Homing: true, HomingTurnRate: TurnDeg(120f), SalvoGrows: false, MissileHealth: 8), TurretSize.Large, 1650f, AmmoRadius: 5f, Color: AmmoColor.Orange, UnlockLevel: 10);

    // HavocTorpedo: the big slow hull-killer. 40 damage per shot breaks R5's per-shot cap the way
    // RailGun does — at 0.6/s nothing else in the game can carry a round that big — and it pays for
    // the licence with the least DPS in its band (R15). Its turn radius never drops below 250 px, so it
    // can only ever lock on hulls that cannot out-turn it.
    public static WeaponType HavocTorpedo { get; } = new("HavocTorpedo", new(FireRate: 0.6f, AmmoSpeed: 280f, KickbackForce: 15f, PelletCount: 1, Scatter: 0f, ShotLifetime: 8f, Damage: 40, TurnSpeed: TurnDeg(90f), Homing: true, HomingTurnRate: TurnDeg(45f), SalvoGrows: false), TurretSize.Large, 1350f, AmmoRadius: 6f, Color: AmmoColor.Orange, UnlockLevel: 10);

    // Perf scenarios only: it must aim instantly so seeded volleys keep looking like fired ones.
    public static WeaponType LoadTestWeapon { get; } = new("LoadTestWeapon", new(0.5f, 200f, 100f, 8000, ShotLifetime: 1.9f, Scatter: 0.15f, Damage: 4), TurretSize.Large, 580f);

    public static readonly WeaponType[] All = [MachineGun, ScatterCannon, SiegeSalvo, Shotgun, RailGun, AcidBubbleSpray, InterceptorTurret, FlechetteFan, LaserLance, PrismLance, RippleCutter, Stiletto, Cyclotron, GravitonBurst, FlakCurtain, SeekerPod, Swarmlet, HavocTorpedo, FarlockSalvo, LoadTestWeapon];

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

    // Fighter: a heavy chewer ahead and a cone of slugs covering the other forward arc.
    public static WeaponLoadout MachineGunShotgun { get; } = new("MachineGunShotgun", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun)]);

    // Shadow: a cone of slugs ahead and a blade on one flank, so the hull still has to fly into the
    // fight to be armed — everything it carries reaches at most 1000 px. It used to carry a Ripple
    // Cutter in each of its three mounts; R23 means three mounts now need three different guns, and the
    // cheapest third gun that keeps this 58 px hull below the 66 px Eagle's firepower is no third gun at
    // all — the right flank starts empty and is filled from the first milestone hand.
    public static WeaponLoadout ShadowCloseSuite { get; } = new("ShadowCloseSuite", [
        new(TurretSlotName.MainGun, WeaponType.ScatterCannon),
        new(TurretSlotName.SmallLeft, WeaponType.RippleCutter)]);

    // Heavy: the slow hull finally puts a kill gun in a heavy mount — the scatter cannon's cone needs no
    // lead, which is what a hull this slow cannot supply — and keeps two chips + the flak disc as support.
    // FlechetteFan leaves: it is the cheapest gun in the game, and a hull that already carries 45 DPS does
    // not need a 6 DPS chip taking its small_right mount. That mount is left for the first milestone hand.
    public static WeaponLoadout HeavyFlak { get; } = new("HeavyFlak", [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.HeavyLeft, WeaponType.ScatterCannon),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
        new(TurretSlotName.SmallBack, WeaponType.FlakCurtain)]);

    // Eagle: the scatter cannon answers what gets close, the fast flank turret answers what circles.
    public static WeaponLoadout ScatterCannonFlank { get; } = new("ScatterCannonFlank", [
        new(TurretSlotName.MainGun, WeaponType.ScatterCannon),
        new(TurretSlotName.SmallLeft, WeaponType.InterceptorTurret)]);

    // The two hulls that fight with one gun ahead and a snap turret on the near flank — the 46 px Scout
    // and the 111 px Manta. It is the smallest loadout that still kills: a machine gun is the main gun
    // because only a kill gun clears the TTK budget, and the repeater is the screen (R28).
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
