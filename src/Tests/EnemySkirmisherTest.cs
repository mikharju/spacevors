using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class EnemySkirmisherTest
{
    [Fact]
    public void Skirmisher_IsFastLightPursuer_WeakerThanStandard()
    {
        var skirmisher = EnemyShipType.Skirmisher;
        var standard = EnemyShipType.Default;

        Assert.True(skirmisher.Speed > EnemyShipType.Interceptor.Speed, "skirmisher should be the fastest ship");
        Assert.True(skirmisher.Radius < standard.Radius, "skirmisher should be smaller than Standard");
        Assert.True(skirmisher.Health < standard.Health, "skirmisher should have less hp than Standard");
        Assert.True(skirmisher.Acceleration > standard.Acceleration, "skirmisher should accelerate harder than Standard");
    }

    [Fact]
    public void Skirmisher_IsRegisteredForGraphicsLookup()
    {
        Assert.Equal(EnemyShipType.Skirmisher, EnemyShipType.FromGraphicsId(EnemyShipType.Skirmisher.GraphicsId));
        Assert.Contains(EnemyShipType.Skirmisher, EnemyShipType.All);
    }

    [Fact]
    public void PickRandomType_WithSkirmisher_SameSeed_SameSequence()
    {
        var picksA = Enumerable.Range(0, 100).Select(_ => EnemyShipFactory.PickRandomType(new Random(123))).ToList();
        var picksB = Enumerable.Range(0, 100).Select(_ => EnemyShipFactory.PickRandomType(new Random(123))).ToList();

        Assert.Equal(picksA, picksB);
    }

    [Fact]
    public void PickRandomType_EqualWeights_EveryTypeIncludingSkirmisherAppearsRoughlyQuarterOfTheTime()
    {
        var rng = new Random(7);
        const int draws = 4000;

        var counts = EnemyShipType.All.ToDictionary(t => t, _ => 0);
        for (int i = 0; i < draws; i++)
            counts[EnemyShipFactory.PickRandomType(rng)]++;

        foreach (var (type, count) in counts)
        {
            Assert.True(count > 0, $"{type.TextureKey} never picked");
            float share = (float)count / draws;
            Assert.True(share is > 0.2f and < 0.3f, $"{type.TextureKey} share {share} is not near the equal-weight 25%");
        }
    }

    [Fact]
    public void CreateComponents_AtTimeZero_UsesSkirmisherBaseStats()
    {
        var type = EnemyShipType.Skirmisher;
        var components = EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type);

        var ship = ComponentOf<EnemyShip>(components);
        var health = ComponentOf<Health>(components);
        var turret = ComponentOf<Turret>(components);

        Assert.Equal(type.Radius, ship.Radius);
        Assert.Equal(type.Speed, ship.Speed);
        Assert.Equal(type.Acceleration, ship.Acceleration);
        Assert.Equal(type.Health, health.Max);
        Assert.Equal(type.TurretFireRate, turret.Weapon.FireRate, precision: 3);
    }

    [Fact]
    public void SpawnSystem_SkirmishersSpawnThroughFactory_WithTieredHealth()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(new Vector2(100f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        var spawner = new EnemyShipSpawnSystem();
        for (int i = 0; i < 400; i++)
        {
            var commands = new CommandBuffer();
            spawner.Update(view, 2f, commands);
            commands.Apply(em);
            em.AddElapsedTime(2f);
        }

        var skirmishers = em.GetEntitiesWithComponents<EnemyShip, Health>()
            .ToList()
            .Where(t => t.Value1.Speed == EnemyShipType.Skirmisher.Speed)
            .ToList();

        Assert.NotEmpty(skirmishers);

        // Health must match the tier curve applied at spawn time for the new type.
        var validHealths = Enumerable.Range(0, EnemyShipStats.TierFor(7200f) + 1)
            .Select(tier => (int)MathF.Round(EnemyShipType.Skirmisher.Health * EnemyShipStats.HpMultiplier(tier), MidpointRounding.AwayFromZero))
            .ToHashSet();

        foreach (var (_, _, health) in skirmishers)
            Assert.True(validHealths.Contains(health.Max), $"skirmisher hp {health.Max} is not a tiered value");
    }

    private static T ComponentOf<T>(IInitialComponent[] components) where T : notnull =>
        components.OfType<InitialComponent<T>>().Single().Component;
}
