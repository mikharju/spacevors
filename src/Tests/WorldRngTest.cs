using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

public class WorldRngTest
{
    [Fact]
    public void SameSeed_SameSpawns()
    {
        var runA = RunSpawnSimulation();
        var runB = RunSpawnSimulation();

        Assert.True(runA.Count > 1, "Expected at least one spawned entity");
        Assert.Equal(runA, runB);
    }

    private static List<Vector2> RunSpawnSimulation()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(new Vector2(10f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        var shipSpawner = new EnemyShipSpawnSystem();
        var mineSpawner = new MineRespawnSystem();

        const float deltaTime = 2f;
        for (int i = 0; i < 30; i++)
        {
            var commands = new CommandBuffer();
            shipSpawner.Update(view, deltaTime, commands);
            mineSpawner.Update(view, deltaTime, commands);
            commands.Apply(em);
            em.AddElapsedTime(deltaTime);
        }

        return view.GetEntitiesWithComponents<Position>()
            .ToList()
            .Select(t => t.Value1.Value)
            .OrderBy(p => p.X).ThenBy(p => p.Y)
            .ToList();
    }
}
