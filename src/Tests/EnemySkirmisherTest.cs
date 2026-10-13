using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Skirmisher: a data-only EnemyShipType variant (light, fast pursuer). It reuses the chase AI,
// the factory and the tier path of the other types, so these tests pin down the data contract,
// the weighted spawn pick and the shared tier scaling.
public class EnemySkirmisherTest
{
    [Fact]
    public void Skirmisher_IsAFastLightPursuer()
    {
        var skirmisher = EnemyShipType.Skirmisher;
        var standard = EnemyShipType.Default;

        Assert.True(skirmisher.Speed > standard.Speed, "skirmisher must outpace the standard ship");
        Assert.True(skirmisher.Acceleration > standard.Acceleration, "skirmisher must accelerate harder");
        Assert.True(skirmisher.TurnRate > standard.TurnRate, "skirmisher must turn sharper");
        Assert.True(skirmisher.Radius < standard.Radius, "skirmisher must be the smaller hull");
        Assert.True(skirmisher.Health < standard.Health, "skirmisher must be frailer than the standard ship");
        Assert.True(skirmisher.FiringRange < standard.FiringRange, "skirmisher must close in closer before shooting");
    }

    [Fact]
    public void Skirmisher_IsRegisteredInAll_AndResolvesByGraphicsId()
    {
        Assert.Contains(EnemyShipType.Skirmisher, EnemyShipType.All);
        Assert.Equal(
            EnemyShipType.Skirmisher,
            EnemyShipType.FromGraphicsId(EnemyShipType.Skirmisher.GraphicsId));
    }

    [Fact]
    public void AllTypes_HaveUniqueGraphicsIdsAndTextureKeys()
    {
        Assert.Equal(EnemyShipType.All.Length, EnemyShipType.All.Select(t => t.GraphicsId).Distinct().Count());
        Assert.Equal(EnemyShipType.All.Length, EnemyShipType.All.Select(t => t.TextureKey).Distinct().Count());
    }

    [Fact]
    public void CreateComponents_CarriesSkirmisherStatsIntoEnemyShipAndTurret()
    {
        var (ship, health, turret) = Spawn(EnemyShipType.Skirmisher, 0f);
        var type = EnemyShipType.Skirmisher;

        Assert.Equal(type.GraphicsId, ship.GraphicsId);
        Assert.Equal(type.Radius, ship.Radius, precision: 3);
        Assert.Equal(type.Speed, ship.Speed, precision: 3);
        Assert.Equal(type.TurnRate, ship.TurnRate, precision: 3);
        Assert.Equal(type.Acceleration, ship.Acceleration, precision: 3);
        Assert.Equal(type.FiringRange, ship.FiringRange, precision: 3);

        Assert.Equal(type.Health, health);
        Assert.Equal(type.TurretFireRate, turret.Weapon.FireRate, precision: 3);
        Assert.Equal(type.TurretAmmoSpeed, turret.Weapon.AmmoSpeed, precision: 3);
        Assert.Equal(type.FiringRange, turret.Range, precision: 3);
        Assert.True(turret.IsEnemy);
    }

    [Fact]
    public void AddComponents_AppliesTiersToTheSkirmisher()
    {
        // The GameInitializer path (AddComponents) must tier the new type like CreateComponents does.
        var em = new EntityManager();
        var entity = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, entity, Vector2.Zero, Vector2.Zero, 0f, 0f, EnemyShipType.Skirmisher, elapsedTime: 120f);

        int tier = EnemyShipStats.TierFor(120f);
        int expected = (int)MathF.Round(EnemyShipType.Skirmisher.Health * EnemyShipStats.HpMultiplier(tier), MidpointRounding.AwayFromZero);

        Assert.Equal(expected, em.GetComponent<Health>(entity).Current);
        Assert.Equal(EnemyShipType.Skirmisher.TurretFireRate * EnemyShipStats.FireRateMultiplier(tier),
            em.GetComponent<Turret>(entity).Weapon.FireRate, precision: 3);
    }

    [Fact]
    public void Skirmisher_StaysFrailerThanStandard_AtEveryTier()
    {
        int maxTier = EnemyShipStats.TierFor(7200f);
        for (int tier = 0; tier <= maxTier; tier++)
        {
            float elapsed = tier * EnemyShipStats.TierDuration;
            int skirmisherHealth = Spawn(EnemyShipType.Skirmisher, elapsed).Health;
            int standardHealth = Spawn(EnemyShipType.Default, elapsed).Health;

            Assert.Equal(ExpectedHealth(EnemyShipType.Skirmisher, tier), skirmisherHealth);
            Assert.True(skirmisherHealth < standardHealth, $"tier {tier}: skirmisher {skirmisherHealth} vs standard {standardHealth}");
        }
    }

    [Fact]
    public void PickRandomType_PicksTheSkirmisher_DeterministicallyForOneSeed()
    {
        var first = PickMany(new Random(1234));
        var again = PickMany(new Random(1234));

        Assert.Equal(first, again);
        Assert.Contains(EnemyShipType.Skirmisher, first);
    }

    [Fact]
    public void PickRandomType_GivesTheSkirmisherItsWeightedShare()
    {
        var picks = PickMany(new Random(2024));

        float totalWeight = 0f;
        foreach (var type in EnemyShipType.All) totalWeight += type.SpawnWeight;

        foreach (var type in EnemyShipType.All)
        {
            int count = picks.Count(p => p == type);
            float expected = Picks * type.SpawnWeight / totalWeight;
            Assert.InRange(count / expected, 0.9f, 1.1f);
        }
    }

    [Fact]
    public void PickRandomType_ConsumesExactlyOneRngDraw()
    {
        // Spawn interval draws follow the type pick on the same world RNG, so the pick must stay
        // one draw per call no matter how many types exist (deterministic spawn streams).
        var rng = new CountingRandom(5);
        EnemyShipFactory.PickRandomType(rng);

        Assert.Equal(1, rng.Draws);
    }

    [Fact]
    public void EnemyShipSpawnSystem_SpawnsSkirmisherOffScreenWithItsStats()
    {
        byte skirmisherId = EnemyShipType.Skirmisher.GraphicsId;
        bool found = false;

        for (int seed = 0; seed < 20 && !found; seed++)
        {
            var em = new EntityManager(seed);
            var view = new WorldView(em);
            AddPlayer(em, new Vector2(100f, 0f));

            var commands = new CommandBuffer();
            new EnemyShipSpawnSystem().Update(view, 10f, commands); // expires the 9 s initial delay
            commands.Apply(em);

            foreach (var (entity, ship, pos) in em.GetEntitiesWithComponents<EnemyShip, Position>())
            {
                if (ship.GraphicsId != skirmisherId) continue;

                found = true;
                Assert.Equal(EnemyShipType.Skirmisher.Radius, ship.Radius, precision: 3);
                Assert.Equal(EnemyShipType.Skirmisher.Health, em.GetComponent<Health>(entity).Current);
                Assert.True(pos.Value.Magnitude > WorldView.DefaultViewportSize.Y * 0.5f,
                    $"skirmisher spawned inside the screen at {pos.Value}");
            }
        }

        Assert.True(found, "no seed in 0..19 spawned a skirmisher");
    }

    private const int Picks = 20000;

    private static List<EnemyShipType> PickMany(Random rng)
    {
        var picks = new List<EnemyShipType>(Picks);
        for (int i = 0; i < Picks; i++) picks.Add(EnemyShipFactory.PickRandomType(rng));
        return picks;
    }

    private static (EnemyShip Ship, int Health, Turret Turret) Spawn(EnemyShipType type, float elapsed)
    {
        var em = new EntityManager();
        var entity = em.CreateEntity();
        foreach (var component in EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type, elapsed))
            component.Apply(em, entity);

        return (em.GetComponent<EnemyShip>(entity), em.GetComponent<Health>(entity).Current, em.GetComponent<Turret>(entity));
    }

    private static int ExpectedHealth(EnemyShipType type, int tier) =>
        (int)MathF.Round(type.Health * EnemyShipStats.HpMultiplier(tier), MidpointRounding.AwayFromZero);

    private static void AddPlayer(EntityManager em, Vector2 velocity)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(velocity));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }

    private sealed class CountingRandom : Random
    {
        public CountingRandom(int seed) : base(seed) { }

        public int Draws { get; private set; }

        public override double NextDouble()
        {
            Draws++;
            return base.NextDouble();
        }
    }
}
