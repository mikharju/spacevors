using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    // Weighted pick over hull × armament for this composition tier (one RNG draw per call). Unlocking an
    // armament and drifting the composition are the same draw, so the ladder in EnemyShipType is the
    // only place the late-game mix is written.
    public static (EnemyShipType Type, EnemyArmament Armament) PickRandomType(Random rng, int compositionTier) =>
        EnemyShipStats.PickComposition(rng, compositionTier);

    public static void AddComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, EnemyArmament armament, float elapsedTime = 0f)
    {
        int tier = EnemyShipStats.TierFor(elapsedTime);
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, BuildEnemyShip(type, tier));
        AddWeapons(em, entity, type, armament, tier);
        int health = TieredHealth(type, tier);
        em.AddComponent(entity, new Health(health, health));
    }

    public static IInitialComponent[] CreateComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, EnemyArmament armament, float elapsedTime = 0f)
    {
        int tier = EnemyShipStats.TierFor(elapsedTime);
        int health = TieredHealth(type, tier);
        var components = new List<IInitialComponent>
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(BuildEnemyShip(type, tier))
        };

        if (armament != EnemyArmament.Missiles) components.Add(new InitialComponent<Turret>(BuildTurret(type, tier)));
        if (armament != EnemyArmament.Guns && type.Missiles is { } spec)
        {
            components.Add(new InitialComponent<MissileWeapon>(BuildMissileWeapon(type, spec, tier)));
            components.Add(new InitialComponent<MissileCooldown>(new MissileCooldown(1f / EnemyShipStats.MissileFireRate)));
        }

        components.Add(new InitialComponent<Health>(new Health(health, health)));
        return [.. components];
    }

    // A hull carries guns, a rack, or both — never a missile rack and no gun by accident, and a missile
    // hull has no `Turret` at all, which is what keeps "the guns stopped firing" readable.
    private static void AddWeapons(EntityManager em, Entity entity, EnemyShipType type, EnemyArmament armament, int tier)
    {
        if (armament != EnemyArmament.Missiles) em.AddComponent(entity, BuildTurret(type, tier));
        if (armament != EnemyArmament.Guns && type.Missiles is { } spec)
        {
            em.AddComponent(entity, BuildMissileWeapon(type, spec, tier));
            em.AddComponent(entity, new MissileCooldown(1f / EnemyShipStats.MissileFireRate));
        }
    }

    private static int TieredHealth(EnemyShipType type, int tier) =>
        (int)MathF.Round(type.Health * EnemyShipStats.HpMultiplier(tier), MidpointRounding.AwayFromZero);

    // The hull and its gun must carry the same grown range: the chase system brakes the hull at
    // FiringRange and the turret fires at it, so a drift on one alone would split the two.
    private static EnemyShip BuildEnemyShip(EnemyShipType type, int tier) =>
        new(type.Radius, EnemyShipStats.SpeedFor(type, tier), EnemyShipStats.TurnRateFor(type, tier), EnemyShipStats.FiringRangeFor(type, tier), type.TurretFireRate, EnemyShipStats.AmmoSpeedFor(type, tier), type.Acceleration, type.GraphicsId);

    private static Turret BuildTurret(EnemyShipType type, int tier) =>
        new(Weapon: new WeaponStats(type.TurretFireRate * EnemyShipStats.FireRateMultiplier(tier), EnemyShipStats.AmmoSpeedFor(type, tier), KickbackForce: EnemyShipStats.WeaponKickbackForce, PelletCount: EnemyShipStats.WeaponPelletCount, Scatter: EnemyShipStats.WeaponScatter, Damage: EnemyShipStats.BaseDamage + EnemyShipStats.DamageAdd(tier)), WeaponName: EnemyShipStats.WeaponName, ArcAngle: EnemyShipStats.WeaponArcAngle, Range: EnemyShipStats.FiringRangeFor(type, tier), IsEnemy: true);

    // The rack is a second gun with its own ring, its own slower damage ladder and its own reload, so
    // arming a hull changes what it throws without touching the hull's gun or how it flies.
    private static MissileWeapon BuildMissileWeapon(EnemyShipType type, EnemyMissileSpec spec, int tier) =>
        new(new WeaponStats(
            EnemyShipStats.MissileFireRate,
            EnemyShipStats.MissileSpeedFor(spec, tier),
            KickbackForce: 0f,
            PelletCount: EnemyShipStats.MissileSalvo,
            Scatter: 0f,
            ShotLifetime: spec.Fuse,
            Damage: EnemyShipStats.BaseDamage + EnemyShipStats.MissileDamageAdd(tier) + spec.DamageBonus,
            Homing: true,
            HomingTurnRate: spec.TurnRate,
            SalvoGrows: false,
            MissileHealth: spec.RoundHealth),
            EnemyShipStats.MissileRangeFor(type, tier));
}
