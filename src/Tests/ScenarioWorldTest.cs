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
        var rocks = new List<(Vector2 Position, float Radius)>();
        foreach (var (_, position, asteroid) in em.GetEntitiesWithComponents<Position, Asteroid>())
            rocks.Add((position.Value, asteroid.Radius));

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
    public void SilencedScenario_RemovesPlayerTurrets_ButNotEnemyOnes()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7);
        int enemyShipsBefore = Count<EnemyShip>(em);

        ScenarioWorld.Apply(em, player, Scenarios.Find("baseline-idle")!);

        Assert.False(em.GetEntitiesWithComponents<Turret, TurretOffset>().TryFirst(out _));
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
        Assert.Equal(5, Scenarios.All.Length);
        Assert.Equal(Scenarios.All.Length, Scenarios.All.Select(s => s.Name).Distinct().Count());
        Assert.Null(Scenarios.Find("nope"));
    }

    private static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
