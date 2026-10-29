using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Domain.Scenarios;

// Applies a scenario's start state on top of the world WorldFactory produced.
//
// Seeded bullets are placed as a volley that has already been flying for a while: ages are uniform
// along the stream, so distance travelled, remaining lifetime and speed variation match what
// TurretFiringSystem produces. That matters because collision cost depends on where the bullets are,
// not merely on how many exist.
public static class ScenarioWorld
{
    public static void Apply(EntityManager em, Entity playerEntity, ScenarioSpec spec)
    {
        var playerPos = em.GetComponent<Position>(playerEntity).Value;

        // The offset is applied as a rotation around the turret, in target radii, so the stream passes
        // that many radii beside the target instead of through its centre.
        ImpactTarget? target = TryFindAimTarget(em, playerPos, spec.AimDirection);
        Vector2 aim = target is { } found && spec.AimOffsetR != 0f
            ? SpawnPlacement.Rotate(spec.AimDirection, spec.AimOffsetR * found.Radius / found.Along)
            : spec.AimDirection;

        List<Vector2> impactPoints = [];
        if (spec.SeededWeapon is { } weapon)
            SeedVolleyInFlight(em, playerPos, weapon, spec.BulletCount, aim, spec.StreamLength, em.Rng, target, impactPoints);

        if (spec.Effects is { } effects)
            SeedEffects(em, impactPoints, effects, em.Rng);

        if (!spec.WeaponFires)
            SilencePlayerTurrets(em);

        if (spec.PlayerHealthRatio < 1f)
            SetPlayerHealth(em, playerEntity, spec.PlayerHealthRatio);

        if (spec.Hulls != ScenarioHulls.None)
            PlaceHulls(em, playerEntity, playerPos, spec.Hulls, spec.AimDirection);
    }

    // ── Hull worlds (plans/NON_CIRCLE_COLLISION.md) ────────────────────────────────────────────
    // The rock-based scenarios price a bullet's narrow-phase test against a circle and never against a
    // hull, and no scenario puts two hulls in contact at all, so a change to how hulls collide would be
    // accepted without ever being measured. These two worlds close that gap.

    // Along and beside the aim axis: near enough that the hull lands in the grid cells the volley
    // travels through, far enough off-axis that almost every round misses it — so the bullet density
    // the scenario measures stays put instead of draining into the hull.
    private const float StreamHullAlong = 150f;
    private const float StreamHullAside = 40f;

    // A hull outlives the measured window however many rounds graze it: these worlds measure the cost
    // of testing, not the cost of a wreck.
    private const int ScenarioHullHealth = 100_000;

    // The hulls the game actually spawns, at the radius their shapes were authored for. A world of small
    // round swarmers would run the circle fast path and never price a capsule, so the stream fires at the
    // middle hull and each crowding ring is a different hull.
    private static readonly EnemyShipType StreamHullType = EnemyShipType.Interceptor;
    private static readonly EnemyShipType[] CrowdingRingHullTypes = [EnemyShipType.Default, EnemyShipType.Interceptor, EnemyShipType.HeavyCannon];

    // Rings sized from each hull's own width, so neighbours on a ring and the ring inside it start clear:
    // the grind has to be built by the chase, not by an instant pile-up of position corrections. The
    // stagger keeps the rings from lining up into channels the hulls could funnel through.
    private static readonly int[] CrowdingRingShipCounts = [6, 12, 18];
    private const float CrowdingRingGap = 50f;
    private const float CrowdingInnerClearance = 20f;
    private const float CrowdingRingStagger = 0.35f;

    // Grinding hulls deal impact damage every tick, and a ring of heavy hulls deals a great deal of it; a
    // player that dies mid-window would measure a different world than the one the scenario describes.
    private const int CrowdingPlayerHealth = 40_000;

    private static void PlaceHulls(EntityManager em, Entity playerEntity, Vector2 playerPos, ScenarioHulls hulls, Vector2 aim)
    {
        if (hulls == ScenarioHulls.StreamTarget)
        {
            var aside = new Vector2(-aim.Y, aim.X);
            AddUnarmedHull(em, playerPos, playerPos + aim * StreamHullAlong + aside * StreamHullAside, holdAtPlayerDistance: true, StreamHullType);
            return;
        }

        float ringRadius = em.GetComponent<Player>(playerEntity).Radius + CrowdingRingHullTypes[0].Radius + CrowdingInnerClearance;
        for (int ring = 0; ring < CrowdingRingShipCounts.Length; ring++)
        {
            var type = CrowdingRingHullTypes[ring];
            int count = CrowdingRingShipCounts[ring];

            if (ring > 0)
                ringRadius += CrowdingRingHullTypes[ring - 1].Radius + type.Radius + CrowdingRingGap;

            // Out far enough that the ring's own circumference holds every hull placed on it.
            ringRadius = Math.Max(ringRadius, count * (2f * type.Radius + CrowdingRingGap) / (MathF.PI * 2f));

            for (int i = 0; i < count; i++)
            {
                float angle = MathF.PI * 2f * i / count + ring * CrowdingRingStagger;
                AddUnarmedHull(em, playerPos, playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ringRadius, holdAtPlayerDistance: false, type);
            }
        }

        em.AddComponent(playerEntity, new Health(CrowdingPlayerHealth, CrowdingPlayerHealth));
    }

    // Unarmed by construction: no `Turret` and no `MissileWeapon`, so nothing shoots back inside the
    // window and the world measures contact, not gunfire. A held hull sets FiringRange to its own
    // distance from the player, which makes the chase brake on the first tick and keeps the hull on its
    // post; a charging hull sets it to 0 and closes until it grinds.
    private static void AddUnarmedHull(EntityManager em, Vector2 playerPos, Vector2 position, bool holdAtPlayerDistance, EnemyShipType type)
    {
        float firingRange = holdAtPlayerDistance ? (position - playerPos).Magnitude : 0f;

        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(Vector2.Zero));
        em.AddComponent(entity, new Rotation(0f));
        em.AddComponent(entity, new AngularVelocity(0f));
        em.AddComponent(entity, new EnemyShip(
            type.Radius, type.Speed, type.TurnRate, FiringRange: firingRange,
            TurretFireRate: 0f, type.TurretAmmoSpeed, type.Acceleration, type.GraphicsId));
        em.AddComponent(entity, new Health(ScenarioHullHealth, ScenarioHullHealth));
    }

    private static void SeedVolleyInFlight(
        EntityManager em, Vector2 origin, WeaponType weapon, int count, Vector2 aim, float streamLength, Random rng,
        ImpactTarget? target, List<Vector2> impactPoints)
    {
        float fullStream = weapon.Stats.AmmoSpeed * weapon.Stats.ShotLifetime;
        float stream = streamLength > 0f ? MathF.Min(streamLength, fullStream) : fullStream;

        for (int i = 0; i < count; i++)
        {
            Vector2 dir = SpawnPlacement.Rotate(aim, (float)(rng.NextDouble() - 0.5) * 2f * weapon.Stats.Scatter);
            float speed = weapon.Stats.AmmoSpeed * (1f + (float)(rng.NextDouble() - 0.5) * TurretFiringSystem.AmmoSpeedVariation);

            // Resample the distance until the bullet is clear of every asteroid: one placed inside a
            // rock is destroyed on the first tick, which is a transient and not steady-state cost.
            float travelled = 0f;
            for (int attempt = 0; attempt < ResampleAttempts; attempt++)
            {
                travelled = (float)rng.NextDouble() * stream;
                if (!OverlapsAsteroid(em, origin + dir * travelled)) break;
            }

            var spawn = origin + dir * TurretFiringSystem.AmmoSpawnOffset;
            Vector2 start = spawn + dir * travelled;

            // Record where the bullet touches the target rock: the seeded particles sit on these
            // points, so the pre-aged impact area matches where the stream actually lands.
            if (target is { } rock && TryImpactPoint(start, dir, rock, out Vector2 impact))
                impactPoints.Add(impact);

            var entity = em.CreateEntity();
            em.AddComponent(entity, new Position(start));
            em.AddComponent(entity, new Velocity(dir * speed));
            em.AddComponent(entity, new Ammo(dir * speed, weapon.AmmoRadius, (fullStream - travelled) / speed,
                IsEnemy: false, weapon.Stats.Damage, weapon.Color));
        }
    }

    private const int ResampleAttempts = 4;
    private const float AsteroidClearance = 5f;

    // First touch of a unit-direction ray with the target circle (dir is unit length, so the
    // quadratic has unit leading coefficient).
    private static bool TryImpactPoint(Vector2 start, Vector2 dir, ImpactTarget target, out Vector2 impact)
    {
        impact = Vector2.Zero;

        Vector2 offset = start - target.Center;
        float halfProjection = offset.X * dir.X + offset.Y * dir.Y;
        float distanceSquared = offset.X * offset.X + offset.Y * offset.Y - target.Radius * target.Radius;
        float discriminant = halfProjection * halfProjection - distanceSquared;

        if (distanceSquared < 0f || discriminant < 0f) return false;

        float along = -halfProjection - MathF.Sqrt(discriminant);
        if (along <= 0f) return false;

        impact = start + dir * along;
        return true;
    }

    private static bool OverlapsAsteroid(EntityManager em, Vector2 position)
    {
        foreach (var (_, asteroidPosition, asteroid) in em.GetEntitiesWithComponents<Position, Asteroid>())
        {
            float clearance = asteroid.Radius + AsteroidClearance;
            Vector2 offset = asteroidPosition.Value - position;
            if (offset.X * offset.X + offset.Y * offset.Y < clearance * clearance) return true;
        }
        return false;
    }

    // The nearest asteroid the seeded stream is aimed at: its centre and radius define the impact
    // area, its distance defines the lever arm the lateral aim offset is divided by.
    private readonly record struct ImpactTarget(Vector2 Center, float Radius, float Along);

    private static ImpactTarget? TryFindAimTarget(EntityManager em, Vector2 origin, Vector2 aim)
    {
        ImpactTarget? nearest = null;

        foreach (var (_, position, asteroid) in em.GetEntitiesWithComponents<Position, Asteroid>())
        {
            Vector2 to = position.Value - origin;
            float along = to.X * aim.X + to.Y * aim.Y;
            if (along <= 0f || (nearest is { } current && along >= current.Along)) continue;

            Vector2 lateral = to - aim * along;
            if (lateral.X * lateral.X + lateral.Y * lateral.Y > asteroid.Radius * asteroid.Radius) continue;

            nearest = new ImpactTarget(position.Value, asteroid.Radius, along);
        }

        return nearest;
    }

    // The impact area already holds particles when the measured window opens, aged uniformly in
    // [0, lifetime] so the live count is flat from the first tick instead of ramping up and then
    // dropping off a cliff. They sit on the points the volley will actually hit, because a cloud
    // spread over the whole rock reads as collisions the stream never makes - and a stream aimed at
    // empty space has no impact points, so nothing is seeded.
    private static void SeedEffects(EntityManager em, List<Vector2> impactPoints, SeededEffects effects, Random rng)
    {
        if (impactPoints.Count == 0) return;

        for (int i = 0; i < effects.Sparks; i++)
        {
            float angle = (float)(rng.NextDouble() * MathF.PI * 2d);
            float speed = DamageEffectSystem.SparkSpeedMin +
                (float)rng.NextDouble() * (DamageEffectSystem.SparkSpeedMax - DamageEffectSystem.SparkSpeedMin);

            var entity = em.CreateEntity();
            em.AddComponent(entity, new Position(impactPoints[(int)(rng.NextDouble() * impactPoints.Count)]));
            em.AddComponent(entity, new Velocity(new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed));
            em.AddComponent(entity, new DamageSpark(
                RandomAge(DamageEffectSystem.EffectLifetime, rng), DamageEffectSystem.EffectLifetime));
        }

        for (int i = 0; i < effects.Explosions; i++)
        {
            var entity = em.CreateEntity();
            em.AddComponent(entity, new Position(impactPoints[(int)(rng.NextDouble() * impactPoints.Count)]));
            em.AddComponent(entity, new Explosion(ExplosionRadius, RandomAge(ExplosionLifetime, rng), ExplosionLifetime));
        }
    }

    // Typical mine-death blast, which is what the seeded impact area contains.
    private const float ExplosionRadius = 12f;
    private const float ExplosionLifetime = 0.5f;

    private static float RandomAge(float lifetime, Random rng) => (float)rng.NextDouble() * lifetime;

    // Player turrets are the ones mounted in a hull slot; enemy ship turrets are not.
    private static void SilencePlayerTurrets(EntityManager em)
    {
        foreach (var (entity, _, _) in em.GetEntitiesWithComponents<TurretSlot, Turret>().ToList())
            TurretMount.Unmount(em, entity);
    }

    private static void SetPlayerHealth(EntityManager em, Entity playerEntity, float ratio)
    {
        var health = em.GetComponent<Health>(playerEntity);
        int current = Math.Max(1, (int)MathF.Round(health.Max * ratio, MidpointRounding.AwayFromZero));
        em.AddComponent(playerEntity, new Health(current, health.Max));
    }
}
