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
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7);
        ScenarioWorld.Apply(em, player, Scenarios.Find("ammo-spray")!);

        Assert.True(em.TryGetStorage(out ComponentStorage<Ammo> ammo));
        Assert.Equal(7000, ammo.Count);

        // A bullet starting inside a rock is destroyed on the first tick, which is a transient rather
        // than the steady-state cost the scenario is meant to measure. Resampling cannot always find a
        // clear spot, so the invariant is "a negligible fraction", not "none".
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

        Assert.True(inside < 70, $"{inside} seeded bullets start inside an asteroid");
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
        Assert.Equal(4, Scenarios.All.Length);
        Assert.Equal(Scenarios.All.Length, Scenarios.All.Select(s => s.Name).Distinct().Count());
        Assert.Null(Scenarios.Find("nope"));
    }

    private static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
