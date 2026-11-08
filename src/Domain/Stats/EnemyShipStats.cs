namespace Spacevors.Domain.Stats;

// What a hull's weapon slot holds. The late-game escalation is an armament ladder over the same three
// hulls, not a fourth species: a hull keeps its radius, speed, hp, chase AI and firing ring, and only
// its armament changes (plans/MISSILES.md §9).
public enum EnemyArmament { Guns, Missiles, GunsAndMissiles }

// What a hull does instead of the plain chase (plans/ENEMY_BEHAVIORS.md): hold the bearing it arrived
// on, circle the ring, cut the player off, tuck behind a fitter friend, charge in, or save its shells
// for a burst. One value per hull — a hull carries exactly one of these, picked by the same weighted
// draw that picks its armament, so unlocking a behaviour costs no extra rng draw.
public enum EnemyBehavior { Chase, Spread, Strafe, CutOff, CoverTuck, Rammer, Burst }

// One rung of the ladder: from this composition tier on, this armament and behaviour pair takes this
// share of that hull's spawns. Rows are read in tier order and a later row for the same
// (armament, behaviour) pair replaces the earlier one, which is how "a few" turns into "an even mix"
// without a second table.
//
// Behaviour rows split the hull's *gun* budget, they do not add to it: the sum of the gun weights at
// any tier stays what it was before behaviours existed, so the rack shares plans/MISSILES.md R20
// documents keep meaning exactly what they say. EnemyMissileLadderTests is the guard.
public readonly record struct EnemyArmamentRow(
    EnemyArmament Armament, int MinCompositionTier, float Weight, EnemyBehavior Behavior = EnemyBehavior.Chase);

// One hull's missile rack. Damage is not here: it follows the shared enemy damage ladder, so a missile
// can never out-damage the bullet its hull fires at the same tier.
public readonly record struct EnemyMissileSpec(
    // The speed the round builds **to** (a rack throws it at WeaponStats.MissileLaunchFraction of this and
    // guidance ramps it up). Capped by MaxAmmoSpeed, so the readability ceiling is the top speed's ceiling.
    float CruiseSpeed,
    float CruiseSpeedPerTier,
    // Steering limit of the launched round, rad/s. Its turn radius is Speed / TurnRate — tightest just
    // after launch, widest at cruise — and that is what keeps an enemy missile dodgeable by out-turning it.
    float TurnRate,
    float Fuse,
    int RoundHealth,
    // Hull-role premium on top of the shared damage ladder (the large hull's rack hits harder than the
    // middle hull's).
    int DamageBonus);

public readonly record struct EnemyShipType(
    byte GraphicsId,
    string TextureKey,
    float Radius,
    float Speed,
    float TurnRate,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    int Health,
    // What the tier ladder drifts each minute, applied at spawn (EnemyShipStats.FiringRangeFor,
    // SpeedFor, AmmoSpeedFor). Negative is a deliberate trade: the Interceptor gives up reach to buy
    // closing speed.
    float RangePerTier = 0f,
    float SpeedPerTier = 0f,
    float AmmoSpeedPerTier = 0f,
    // Relative share of enemy spawns (EnemyShipFactory.PickRandomType); equal weights give an even split.
    float SpawnWeight = 1f,
    // Which armaments this hull may roll from which composition tier, and each one's share of that
    // hull's spawns. Null means "guns, always".
    EnemyArmamentRow[]? Armaments = null,
    // What the hull's rack throws, or null when the hull never carries one.
    EnemyMissileSpec? Missiles = null,
    // Hull armor (plans/ARMOR_AND_PIERCE.md): gunfire subtracts this before damage, floored at 1.
    // Only HeavyCannon — the largest, slowest hull — carries it: the armored hull is the one the
    // player learns to answer with pierce and big rounds instead of chips.
    int Armor = 0,
    // Extra damage on this hull's turret round, on top of the shared damage ladder. HeavyCannon's is
    // the anti-armored-hull threat: the only enemy shot that walks through player armor 4 while
    // every chip floors to 1 (and it is the slowest round in the fleet, so a hull that can move
    // dances around it).
    int TurretDamageBonus = 0,
    // Capsule radius of the hull's turret round; HeavyCannon throws a visibly fatter slug.
    float TurretAmmoRadius = EnemyShipStats.AmmoRadius)
{
    public static EnemyShipType Default { get; } = new(0, "enemy-1", 20f, 65f, 1.0f, 700f, 1.5f, 200f, 45.0f, 60,
        RangePerTier: 10f, SpeedPerTier: 2f, AmmoSpeedPerTier: 2f,
        // The swarmer stays un-armed: with up to 100 of them live it is the one armament no weight
        // table tames, so the missile never comes from the smallest, fastest hull.
        // A fifth of them hold a spread bearing from composition tier 8 (plans/ENEMY_BEHAVIORS.md B1);
        // four fifths still converge, because the tight ball is what PrismLance, FlakCurtain and
        // RippleCutter are paid for. Never the majority.
        Armaments: [
            new EnemyArmamentRow(EnemyArmament.Guns, 0, 1f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.8f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.2f, EnemyBehavior.Spread)]);

    // The middle hull is the first missile carrier: it already stands off, and 28 hp makes it something
    // the player can kill before it launches. 0.43 against the guns' 1 is ~30 % of its spawns; from
    // composition tier 5 the two armaments are an even mix.
    public static EnemyShipType Interceptor { get; } = new(1, "interceptor", 45f, 90f, 1.0f, 700f, 0.6f, 200f, 85f, 28,
        RangePerTier: -15f, SpeedPerTier: 3f, AmmoSpeedPerTier: 2f,
        Armaments: [
            new EnemyArmamentRow(EnemyArmament.Guns, 0, 1f),
            new EnemyArmamentRow(EnemyArmament.Missiles, 3, 0.43f),
            new EnemyArmamentRow(EnemyArmament.Missiles, 5, 1f),
            // The stand-off hull is the first to spread: it already holds a ring, so holding *its own*
            // bearing instead of joining the chase line is the same role, one bearing wider
            // (plans/ENEMY_BEHAVIORS.md B1). Gun weights sum to 1 at every tier, so the rack share
            // stays 0.30 at tier 3 and an even mix from tier 5.
            new EnemyArmamentRow(EnemyArmament.Guns, 3, 0.8f),
            new EnemyArmamentRow(EnemyArmament.Guns, 3, 0.2f, EnemyBehavior.Spread),
            // From tier 5 some of it stops shooting and drives in instead (B6): it is fast enough to
            // reach the hull and 28 hp means the player can still kill it on the way in.
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.65f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.15f, EnemyBehavior.Rammer),
            // From tier 8 it also aims where the player is going (B4), which is the cheap answer to
            // the ~290 px/s escape valve.
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.35f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.1f, EnemyBehavior.Rammer),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.35f, EnemyBehavior.CutOff)],
        Missiles: new EnemyMissileSpec(165f, 4f, Angle.FromDeg(70f), 6f, RoundHealth: 8, DamageBonus: 0));

    // The large hull gets the rack last and gets it alongside its guns: it is the slow long firing
    // line, so a missile on it reads as the execution of a role the player already knows.
    // It is also the fleet's armored hull and its heavy gunner: armor 4 against its 120 hp, and a
    // turret slug with +4 damage over every other hull on the shared ladder, a wider round and the
    // slowest flight (140 px/s) — the roadmap's "slow powerful shot that threatens the armored hull
    // and is easy to dodge for a fast one" (plans/ARMOR_AND_PIERCE.md).
    public static EnemyShipType HeavyCannon { get; } = new(2, "heavy-cannon", 78f, 50f, 1.0f, 700f, 0.8f, 140f, 45.0f, 120,
        RangePerTier: 25f, AmmoSpeedPerTier: 1.6f,
        Armaments: [
            new EnemyArmamentRow(EnemyArmament.Guns, 0, 1f),
            new EnemyArmamentRow(EnemyArmament.GunsAndMissiles, 8, 1f),
            // From composition tier 10 the long firing line almost always carries its rack: 4 to 1 over
            // gun-only heavies, so a bare heavy is the rare sight rather than the norm.
            new EnemyArmamentRow(EnemyArmament.GunsAndMissiles, 10, 4f),
            // The two hulls with hp to spare (B5): below a quarter of its hp a heavy stops trading,
            // tucks behind the biggest fit hull it can reach, and keeps firing from there. Its own gun
            // budget still sums to 1 at every tier, so the rack shares are untouched.
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.8f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.2f, EnemyBehavior.CoverTuck),
            // From tier 8 the long line fans out (B1) and spends its shells in a telegraphed burst
            // (B7) instead of a drizzle. The rack hulls keep firing normally: a rack is a rack.
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.4f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.2f, EnemyBehavior.Spread),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.2f, EnemyBehavior.Burst)],
        Missiles: new EnemyMissileSpec(200f, 5f, Angle.FromDeg(50f), 6f, RoundHealth: 12, DamageBonus: 4),
        Armor: 4,
        TurretDamageBonus: 4,
        TurretAmmoRadius: 4f);

    // Streak: the dart. The fastest and flimsiest gunner in the fleet — it trades all its reach (the
    // shortest firing ring, so it must cross the whole no-man's land to shoot) for closing speed and a
    // spray rate that punishes standing still. It survives by out-flying shots, never by eating them.
    public static EnemyShipType Streak { get; } = new(3, "streak", 36f, 115f, 1.15f, 520f, 2.0f, 170f, 110f, 20,
        RangePerTier: 12f, SpeedPerTier: 3f, AmmoSpeedPerTier: 3f,
        SpawnWeight: 0.8f,
        // The dart never parks (B3): 20 hp means a still Streak is a dead Streak, so from tier 5 it
        // circles the ring facing the player, and from tier 8 some of it cuts off the escape line (B4).
        Armaments: [
            new EnemyArmamentRow(EnemyArmament.Guns, 0, 1f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.6f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.4f, EnemyBehavior.Strafe),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.25f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.35f, EnemyBehavior.CutOff)]);

    // Carmine: the line-breaker. A wide gunship that trades the HeavyCannon's reach and bulk for a
    // faster hull and a busier gun: it marches the firing line inward to just past the point-blank
    // band and wins the exchange on volume, escorting the slower heavies that hold the long line.
    // Gun-only — the missile ladder stays with the two hulls that introduced it (plans/MISSILES.md R20).
    public static EnemyShipType Carmine { get; } = new(4, "carmine", 62f, 55f, 0.8f, 640f, 1.3f, 160f, 40f, 88,
        RangePerTier: 18f, AmmoSpeedPerTier: 2f,
        SpawnWeight: 0.6f,
        // The line-breaker has 88 hp, so it has a crippled phase worth using: tuck (B5) from tier 5,
        // then fan out and burst with the heavies from tier 8. Gun weights still sum to 1 per tier.
        Armaments: [
            new EnemyArmamentRow(EnemyArmament.Guns, 0, 1f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.75f),
            new EnemyArmamentRow(EnemyArmament.Guns, 5, 0.25f, EnemyBehavior.CoverTuck),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.15f),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.3f, EnemyBehavior.Spread),
            new EnemyArmamentRow(EnemyArmament.Guns, 8, 0.3f, EnemyBehavior.Burst)]);

    public static readonly EnemyShipType[] All = [Default, Interceptor, HeavyCannon, Streak, Carmine];

    public static EnemyShipType? FromGraphicsId(byte graphicsId) => graphicsId < All.Length ? All[graphicsId] : null;
}

// Shared enemy weapon and time-based stat tiers (plans/DIFFICULTY_SCALING.md P5).
public static class EnemyShipStats
{
    // Single enemy weapon shared by all ship types.
    public const string WeaponName = "EnemyWeapon";
    public const float WeaponArcAngle = MathF.PI / 8f;
    public const float WeaponScatter = 0.05f;
    public const int WeaponPelletCount = 1;
    public const float WeaponKickbackForce = 0f;
    public const float AmmoRadius = 2.5f;

    // Base damage of enemy turret ammo; tiers add on top via DamageAdd and one hull adds via its
    // TurretDamageBonus (HeavyCannon's slug — plans/ARMOR_AND_PIERCE.md). 4 at the ×4 scale (R32):
    // the same bite against four times the hull, and a hull round still eats four of them.
    public const int BaseDamage = 4;

    // Tiers are applied at spawn time only, so existing entities are never mutated mid-run.
    // Multipliers are capped by MaxTier — stats stop growing after 10 minutes instead of compounding forever against the player's upgrades.
    public const float TierDuration = 60f;
    public const int MaxTier = 10;

    public static int TierFor(float elapsedTime) => Math.Clamp((int)(elapsedTime / TierDuration), 0, MaxTier);

    public static float HpMultiplier(int tier) => 1f + 0.25f * tier;

    public static int DamageAdd(int tier) => 4 * (tier / 2);

    public static float FireRateMultiplier(int tier) => 1f + 0.1f * tier;

    // Deadlier individuals rather than a busier world (plans/DIFFICULTY_SCALING.md R2.3-R2.5): reach,
    // closing speed, tracking and a touch of ammo speed climb with the tier, while per-shot damage
    // stays on DamageAdd alone. The ceilings keep that inside the player's weapon ladder: an enemy
    // engages at `FiringRange + the player's hull radius`, and the ring must stay under the player's
    // mid-range band (1350) so mid and long guns keep trading favourably. Ammo stays dodgeable — the
    // genre's readability budget is reaction time, and enemy guns already lead their shots.
    public const float MaxFiringRange = 1000f;
    public const float MaxAmmoSpeed = 250f;
    public const float TurnRatePerTier = 0.03f;

    public static float FiringRangeFor(EnemyShipType type, int tier) =>
        Math.Min(type.FiringRange + type.RangePerTier * tier, MaxFiringRange);

    public static float AmmoSpeedFor(EnemyShipType type, int tier) =>
        Math.Min(type.TurretAmmoSpeed + type.AmmoSpeedPerTier * tier, MaxAmmoSpeed);

    public static float SpeedFor(EnemyShipType type, int tier) => type.Speed + type.SpeedPerTier * tier;

    public static float TurnRateFor(EnemyShipType type, int tier) => type.TurnRate + TurnRatePerTier * tier;

    // Two tier axes, deliberately separate. `TierFor` stops at 10 because hp, damage and fire rate must
    // stop compounding against the player's upgrades. Composition tier keeps climbing to 20 because what
    // appears, and how many missiles may be in the air, are the axes the late game escalates on.
    public const int MaxCompositionTier = 20;

    public static int CompositionTierFor(float elapsedTime) => Math.Clamp((int)(elapsedTime / TierDuration), 0, MaxCompositionTier);

    // The composition draw: every hull × armament this tier has unlocked, weighted by the hull's spawn
    // share × the armament's share of that hull. Unlocking an armament is therefore the same act as
    // drifting the composition, which is what plans/DIFFICULTY_SCALING.md P6 was holding back.
    public static (EnemyShipType Type, EnemyArmament Armament, EnemyBehavior Behavior) PickComposition(Random rng, int compositionTier)
    {
        float total = 0f;
        foreach (var type in EnemyShipType.All)
            foreach (var (_, _, weight) in UnlockedArmaments(type, compositionTier))
                total += type.SpawnWeight * weight;

        float roll = (float)rng.NextDouble() * total;
        foreach (var type in EnemyShipType.All)
        {
            foreach (var (armament, behavior, weight) in UnlockedArmaments(type, compositionTier))
            {
                roll -= type.SpawnWeight * weight;
                if (roll < 0f) return (type, armament, behavior);
            }
        }

        return (EnemyShipType.Default, EnemyArmament.Guns, EnemyBehavior.Chase);
    }

    // One entry per armament-and-behaviour pair: the highest row this composition tier has unlocked.
    // Relies on the rows being written in tier order, which is how the ladders above are written.
    // Public because the ladder rules are asserted against these weights rather than against a dice
    // roll (plans/MISSILES.md R20, plans/ENEMY_BEHAVIORS.md).
    public static IEnumerable<(EnemyArmament Armament, EnemyBehavior Behavior, float Weight)> UnlockedArmaments(EnemyShipType type, int compositionTier)
    {
        if (type.Armaments is not { } rows)
        {
            yield return (EnemyArmament.Guns, EnemyBehavior.Chase, 1f);
            yield break;
        }

        var shares = new Dictionary<(EnemyArmament, EnemyBehavior), float>();
        foreach (var row in rows)
        {
            if (row.MinCompositionTier > compositionTier) continue;
            shares[(row.Armament, row.Behavior)] = row.Weight;
        }

        foreach (var ((armament, behavior), weight) in shares) yield return (armament, behavior, weight);
    }

    // R22: how many enemy guided rounds may be in the air at once. One step at a time, four minutes
    // apart: the pressure the late game wants comes from mixing missiles into a fleet the player already
    // reads, not from volume. Lifting the cap at all is only safe because gunfire shoots missiles down.
    public static int MissileCapFor(int compositionTier) =>
        compositionTier >= 20 ? 7 :
        compositionTier >= 14 ? 6 :
        compositionTier >= 10 ? 5 :
        compositionTier >= 8 ? 4 :
        compositionTier >= 3 ? 3 :
        0;

    // A rack's own ceilings. The launch ring stays under the player's longest auto-target band (1700),
    // so nothing fires at the player from a distance no player gun can answer; the turn rate stays under
    // what a player hull can out-turn, which is what makes "enemy missile" and "fast missile" mutually
    // exclusive by construction. Speed shares the existing enemy ammo ceiling (MaxAmmoSpeed).
    public const float MissileReachMultiplier = 1.4f;
    public const float MaxMissileFiringRange = 1450f;
    public const float MaxMissileTurnRate = MathF.PI / 2f;
    public const float MaxMissileFuse = 6f;
    // Per-hull reload, the soft knob under the world-wide cap: about one launch every three seconds.
    public const float MissileFireRate = 0.33f;
    public const int MissileSalvo = 1;
    public const float MissileAmmoRadius = 4f;

    // The missile's own, slower damage ladder: a steering round is the most certain delivery the enemy
    // ever gets, and certainty is paid for in damage (R1/R9 price it the same way for the player).
    public static int MissileDamageAdd(int tier) => 4 * (tier / 3);

    // The speed a rack's round builds to — not the speed it leaves at, which is a fraction of this.
    public static float MissileSpeedFor(EnemyMissileSpec spec, int tier) =>
        Math.Min(spec.CruiseSpeed + spec.CruiseSpeedPerTier * tier, MaxAmmoSpeed);

    // What that round can physically cross before its fuse ends: the ramp's trapezoid, then the cruise
    // that follows it (the enemy twin of the player's R6 reach rule, R21).
    public static float MissileReachFor(EnemyMissileSpec spec, int tier)
    {
        float top = MissileSpeedFor(spec, tier);
        float launch = top * WeaponStats.MissileLaunchFraction;
        float rampTime = Math.Min((top - launch) / WeaponStats.MissileLaunchAcceleration, spec.Fuse);
        return rampTime * (launch + top) / 2f + (spec.Fuse - rampTime) * top;
    }

    // The hull still brakes at its own gun range whatever it carries, so the rack's ring is the grown
    // gun range × 1.4: missiles launch from outside the gun line without the hull behaving differently.
    public static float MissileRangeFor(EnemyShipType type, int tier) =>
        Math.Min(FiringRangeFor(type, tier) * MissileReachMultiplier, MaxMissileFiringRange);

    // B7 volley cadence (plans/ENEMY_BEHAVIORS.md): a Burst hull holds fire for most of its cycle and
    // spends the whole allotment inside the window, so the rounds/s budget is redistributed and never
    // raised — the rate inside the window is exactly the ratio that makes the average come out equal.
    public const float BurstPeriod = 3f;
    public const float BurstWindow = 0.5f;
    public const float BurstRateMultiplier = BurstPeriod / BurstWindow;

    // How far before the window the wind-up glow starts charging: the tell that makes a burst fair.
    // An even drizzle cannot be read; a wall you can see coming can be dodged.
    public const float BurstTellLead = 0.6f;

    // The phase comes from the bearing the hull spawned on, which is why heavies spread around the ring
    // open up as a wave instead of all on one beat. No per-hull state and no rng draw.
    public static float BurstPhaseSeconds(Vector2 holdBearing) =>
        (Angle.Of(holdBearing) + MathF.PI) / Angle.FullTurn * BurstPeriod;

    public static bool InBurstWindow(Vector2 holdBearing, float elapsedTime) =>
        BurstPhase(elapsedTime, holdBearing) <= BurstWindow;

    // 0 outside the wind-up, 0 → 1 across the tell just before the window. What the renderer charges.
    public static float BurstTell(Vector2 holdBearing, float elapsedTime)
    {
        float phase = BurstPhase(elapsedTime, holdBearing);
        return phase < BurstPeriod - BurstTellLead ? 0f : (phase - (BurstPeriod - BurstTellLead)) / BurstTellLead;
    }

    private static float BurstPhase(float elapsedTime, Vector2 holdBearing)
    {
        float phase = (elapsedTime + BurstPhaseSeconds(holdBearing)) % BurstPeriod;
        return phase < 0f ? phase + BurstPeriod : phase;
    }
}
