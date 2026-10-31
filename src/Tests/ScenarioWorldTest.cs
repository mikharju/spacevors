using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;
using Spacevors.Domain.Stats;
using Xunit;

public class ScenarioWorldTest
{
    [Fact]
    public void SeededVolley_SeedsRequestedCount_AndAlmostNoneInsideRocks()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7, testAsteroid: true);
        ScenarioWorld.Apply(em, player, Scenarios.Find("impact-storm")!);

        Assert.True(em.TryGetStorage(out ComponentStorage<Ammo> ammo));
        Assert.Equal(5500, ammo.Count);

        // Seeded particles are what keeps the measured window at steady counts: without them the
        // first second measures particles piling up. They only stay flat where impacts replenish
        // them, which is why this belongs to the storm scenario.
        Assert.Equal(15000, Count<DamageSpark>(em));
        Assert.Equal(2000, Count<Explosion>(em));

        // A bullet starting inside a rock is destroyed on the first tick, which is a transient rather
        // than the steady-state cost the scenario is meant to measure. Resampling cannot always find a
        // clear spot - impact-storm drives the stream through the rock on purpose - so the invariant
        // is "a small fraction", not "none".
        var rocks = RockCircles(em);

        int inside = 0;
        foreach (var (_, position, _) in em.GetEntitiesWithComponents<Position, Ammo>())
        {
            foreach (var (rockPosition, radius) in rocks)
            {
                Vector2 offset = rockPosition - position.Value;
                if (offset.X * offset.X + offset.Y * offset.Y < radius * radius) { inside++; break; }
            }
        }

        Assert.True(inside < ammo.Count * 0.05, $"{inside} of {ammo.Count} seeded bullets start inside an asteroid");
    }

    [Fact]
    public void SeededParticles_SitOnTheRockTheStreamHits()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7, testAsteroid: true);
        ScenarioWorld.Apply(em, player, Scenarios.Find("ammo-spray")!);

        var rocks = RockCircles(em);

        // Particles spread over the rock - or over the square bounding it, which is worse - read as
        // impacts the stream never makes, so every seeded one must touch an asteroid.
        int seeded = 0;
        foreach (var (_, position, _) in em.GetEntitiesWithComponents<Position, DamageSpark>())
        {
            seeded++;
            Assert.True(AnyRockAt(rocks, position.Value), $"seeded spark at {position.Value} floats off every asteroid");
        }

        Assert.True(seeded > 0, "no particles were seeded, so the impact area is not being exercised");
    }

    private static List<(Vector2 Position, float Radius)> RockCircles(EntityManager em)
    {
        var rocks = new List<(Vector2 Position, float Radius)>();
        foreach (var (_, position, asteroid) in em.GetEntitiesWithComponents<Position, Asteroid>())
            rocks.Add((position.Value, asteroid.Radius));

        return rocks;
    }

    // Impact points sit on the surface, so the only slack is float error in the ray solve.
    private const float SurfaceTolerance = 0.1f;

    private static bool AnyRockAt(List<(Vector2 Position, float Radius)> rocks, Vector2 point)
    {
        foreach (var (rockPosition, radius) in rocks)
        {
            Vector2 offset = rockPosition - point;
            float reach = radius + SurfaceTolerance;
            if (offset.X * offset.X + offset.Y * offset.Y <= reach * reach) return true;
        }

        return false;
    }

    [Fact]
    public void SilencedScenario_RemovesPlayerTurrets_ButNotEnemyOnes()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7);
        int enemyShipsBefore = Count<EnemyShip>(em);

        ScenarioWorld.Apply(em, player, Scenarios.Find("baseline-idle")!);

        Assert.False(em.GetEntitiesWithComponents<TurretSlot, Turret>().TryFirst(out _));
        Assert.Equal(enemyShipsBefore, Count<EnemyShip>(em));
        Assert.True(Count<Turret>(em) > 0, "enemy turrets must survive silencing");
    }

    [Fact]
    public void HullDamage_SetsHealthRatioWithoutChangingMax()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Heavy, WorldView.DefaultViewportSize, seed: 7);
        ScenarioWorld.Apply(em, player, Scenarios.Find("hull-damage")!);

        var health = em.GetComponent<Health>(player);
        Assert.Equal(20, health.Max);
        Assert.Equal(2, health.Current);
    }

    [Fact]
    public void Scenarios_AreNamedAndUnique()
    {
        Assert.Equal(9, Scenarios.All.Length);
        Assert.Equal(Scenarios.All.Length, Scenarios.All.Select(s => s.Name).Distinct().Count());
        Assert.Null(Scenarios.Find("nope"));
    }

    // The missile worlds price guidance and cross-fire (plans/MISSILE_PERF.md), so the seeded counts
    // and the two factions must be exactly what the spec says: racks silent, both sides present, the
    // armed hulls really armed, and a player that outlasts the detonations.
    [Fact]
    public void MissileWorlds_SeedExactCountsOnBothSides_WithArmedHulls_AndSilentRacks()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7, testAsteroid: true);
        ScenarioWorld.Apply(em, player, Scenarios.Find("missile-384")!);

        Assert.Equal(384, Count<Missile>(em));

        int enemyGuided = 0;
        foreach (var (_, _, ammo) in em.GetEntitiesWithComponents<Missile, Ammo>())
            if (ammo.IsEnemy) enemyGuided++;
        Assert.Equal(192, enemyGuided);

        // Every seeded round has a live lock: guidance steering against a missing entity would measure
        // "fly on until the fuse dies", not steering.
        foreach (var (entity, missile) in em.GetEntitiesWithComponents<Missile>())
            Assert.True(em.TryGetComponent<Position>(missile.LockedTarget, out _), $"missile {entity} is locked on a dead entity");

        // The 8 scenario hulls are recognisable by the health ScenarioWorld gives them — a factory
        // hull at any tier is far lighter — and each must really be armed and holding its post. No
        // hull anywhere carries a rack, so the seeded count is the missile count for the window.
        int armedHulls = 0;
        foreach (var (entity, health) in em.GetEntitiesWithComponents<Health>())
        {
            if (health.Max != ScenarioHullHealth) continue;
            armedHulls++;
            Assert.True(em.TryGetComponent<Turret>(entity, out _), $"scenario hull {entity} lost its gun");
            Assert.True(em.TryGetComponent<EnemyShip>(entity, out var ship) && ship.FiringRange > 0f,
                $"scenario hull {entity} is not holding its post");
        }
        Assert.Equal(MissileWorldHulls, armedHulls);
        Assert.False(em.GetEntitiesWithComponents<MissileWeapon>().TryFirst(out _));

        Assert.True(em.GetComponent<Health>(player).Current > ShipType.Shadow.MaxHealth,
            "the missile-world player must outlast the detonations, or the window measures a wreck");
    }

    private const int InitialShips = 6;      // SpawningStats.InitialWorld.EnemyShipCount
    private const int CrowdingHulls = 36;    // ScenarioWorld.CrowdingRingShipCounts: 6 + 12 + 18
    private const int MissileWorldHulls = 8; // ScenarioWorld.MissileWorldHullTypes x MissileWorldHullsPerType
    private const int ScenarioHullHealth = 100_000; // ScenarioWorld.ScenarioHullHealth

    // The two hull worlds price how a hull collides (plans/NON_CIRCLE_COLLISION.md), so they are only
    // worth trusting if they really put unarmed hulls in the world: a hull that shoots back, or a ring
    // that silently placed nobody, would quietly measure something else.
    [Fact]
    public void HullWorlds_PlaceUnarmedHullsAndKeepThePlayerStanding()
    {
        var (streamEm, streamPlayer, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7, testAsteroid: true);
        ScenarioWorld.Apply(streamEm, streamPlayer, Scenarios.Find("hull-stream")!);

        var (crowdEm, crowdPlayer, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7, testAsteroid: true);
        ScenarioWorld.Apply(crowdEm, crowdPlayer, Scenarios.Find("hull-crowding")!);

        Assert.Equal(InitialShips + 1, Count<EnemyShip>(streamEm));
        Assert.Equal(InitialShips + CrowdingHulls, Count<EnemyShip>(crowdEm));

        int unarmed = 0;
        foreach (var (_, ship) in crowdEm.GetEntitiesWithComponents<EnemyShip>())
        {
            if (ship.FiringRange > 0f) continue; // a spawned hull, armed by EnemyShipFactory
            unarmed++;
        }
        Assert.Equal(CrowdingHulls, unarmed);

        Assert.True(crowdEm.GetComponent<Health>(crowdPlayer).Current > ShipType.Shadow.MaxHealth,
            "the crowding player must outlast the grind, or the window measures a wreck");
    }

    private static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
