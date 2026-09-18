using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class DamageEffectTest
{
    private const float Tick = 1f / 60f;

    // World with a camera at origin and the default viewport, so ships near the origin are visible.
    private static (EntityManager em, WorldView view) CreateWorld()
    {
        var em = new EntityManager();
        var camera = em.CreateEntity();
        em.AddComponent(camera, new Camera(Vector2.Zero, Vector2.Zero));
        return (em, new WorldView(em));
    }

    private static Entity AddDamagedEnemy(EntityManager em, Vector2 pos, int currentHp, int maxHp)
    {
        var entity = TargetingTest.AddEnemyShip(em, pos);
        em.AddComponent(entity, new Health(currentHp, maxHp));
        return entity;
    }

    private static Entity AddPlayer(EntityManager em, Vector2 pos, int currentHp, int maxHp = 10)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(pos));
        em.AddComponent(entity, new Velocity(Vector2.Zero));
        em.AddComponent(entity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: maxHp, Radius: 18f));
        em.AddComponent(entity, new Health(currentHp, maxHp));
        return entity;
    }

    private static void RunTicks(EntityManager em, WorldView view, int ticks)
    {
        var system = new DamageEffectSystem();
        for (int i = 0; i < ticks; i++)
        {
            var commands = new CommandBuffer();
            system.Update(view, Tick, commands);
            commands.Apply(em);
            em.AddElapsedTime(Tick);
        }
    }

    private static int CountSmoke(EntityManager em) => em.GetEntitiesWithComponents<SmokePuff>().Count();
    private static int CountSparks(EntityManager em) => em.GetEntitiesWithComponents<DamageSpark>().Count();

    [Fact]
    public void FullHpShipsEmitNothing()
    {
        var (em, view) = CreateWorld();
        AddPlayer(em, Vector2.Zero, 10);
        AddDamagedEnemy(em, new Vector2(300f, -300f), 5, 5);

        RunTicks(em, view, 60);

        Assert.Equal(0, CountSmoke(em));
        Assert.Equal(0, CountSparks(em));
    }

    [Fact]
    public void LightlyDamagedShipEmitsSmokeButNoSparks()
    {
        var (em, view) = CreateWorld();
        AddDamagedEnemy(em, new Vector2(300f, -300f), 5, 10); // ratio 0.5: below 2/3, above 1/3

        RunTicks(em, view, 600); // 10 s at the expected 2 puffs/s -> P(no puff) ~ e^-20

        Assert.True(CountSmoke(em) > 0, "Expected smoke puffs from a light-damaged ship");
        Assert.True(CountSparks(em) == 0, "Light tier must not emit sparks");
    }

    [Fact]
    public void HeavilyDamagedShipEmitsSmokeAndSparks()
    {
        var (em, view) = CreateWorld();
        AddDamagedEnemy(em, new Vector2(300f, -300f), 2, 10); // ratio 0.2: below 1/3

        RunTicks(em, view, 600);

        Assert.True(CountSmoke(em) > 0, "Expected smoke puffs from a heavy-damaged ship");
        Assert.True(CountSparks(em) > 0, "Expected sparks from a heavy-damaged ship (P(miss) ~ e^-30)");
    }

    [Fact]
    public void OffScreenAndDeadShipsEmitNothing()
    {
        var (em, view) = CreateWorld();
        AddDamagedEnemy(em, new Vector2(5000f, 0f), 1, 10); // far outside viewport + margin
        var dead = AddDamagedEnemy(em, new Vector2(300f, -300f), 1, 10);
        em.AddComponent(dead, new Dead());

        RunTicks(em, view, 120);

        Assert.Equal(0, CountSmoke(em));
        Assert.Equal(0, CountSparks(em));
    }

    [Fact]
    public void CandidatesOrderedByPriorityThenDistance()
    {
        var (em, view) = CreateWorld();
        em.AddElapsedTime(10f); // world time for mark freshness

        var player = AddPlayer(em, new Vector2(100f, 0f), 5, 10);
        var manual = AddDamagedEnemy(em, new Vector2(200f, 0f), 5, 10);
        var freshMarked = AddDamagedEnemy(em, new Vector2(300f, 0f), 5, 10);
        em.AddComponent(freshMarked, new AutoTargetMark(9.5f)); // age 0.5 s -> fresh
        var staleMarked = AddDamagedEnemy(em, new Vector2(400f, 0f), 5, 10);
        em.AddComponent(staleMarked, new AutoTargetMark(8f));   // age 2 s -> other tier
        var nearOther = AddDamagedEnemy(em, new Vector2(500f, 0f), 5, 10);
        var farOther = AddDamagedEnemy(em, new Vector2(600f, 0f), 5, 10);

        em.AddComponent(player, new PrimaryTarget(manual));

        var ordered = DamageEffectSystem.OrderCandidates(view);

        Assert.Equal([player, manual, freshMarked, staleMarked, nearOther, farOther], ordered.Select(c => c.Entity).ToList());
    }

    [Fact]
    public void PerTickBudgetCapsSpawns()
    {
        var (em, view) = CreateWorld();
        for (int i = 0; i < 5; i++)
            AddDamagedEnemy(em, new Vector2(100f + i * 50f, -300f), 1, 10); // all heavy

        var commands = new CommandBuffer();
        new DamageEffectSystem().Update(view, 1f, commands); // dt = 1 s -> every roll succeeds (rate x dt >= 1)
        commands.Apply(em);

        Assert.True(CountSmoke(em) == 1, $"Smoke budget is one puff per tick, got {CountSmoke(em)}");
        Assert.True(CountSparks(em) == 1, $"Spark budget is one spark per tick, got {CountSparks(em)}");
    }

    [Fact]
    public void SmokePuffsExpireAfterOneSecond()
    {
        var (em, view) = CreateWorld();
        var puff = em.CreateEntity();
        em.AddComponent(puff, new Position(Vector2.Zero));
        em.AddComponent(puff, new Velocity(Vector2.Zero));
        em.AddComponent(puff, new SmokePuff(1f, 1f, 6f));

        var effectSystem = new EffectSystem();

        var commands = new CommandBuffer();
        effectSystem.Update(view, 0.6f, commands); // lifetime -> 0.4 s
        commands.Apply(em);
        Assert.True(em.HasComponent<SmokePuff>(puff), "Puff must survive within its lifetime");

        commands = new CommandBuffer();
        effectSystem.Update(view, 0.5f, commands); // lifetime -> -0.1 s
        commands.Apply(em);
        Assert.False(em.HasComponent<SmokePuff>(puff), "Puff must be destroyed after one second");
    }
}
